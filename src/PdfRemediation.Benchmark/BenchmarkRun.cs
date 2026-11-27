using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.Checks;
using PdfRemediation.Infrastructure.Pdf;
using PdfRemediation.Infrastructure.Reading;
using PdfRemediation.Infrastructure.Reading.Mistral;
using PdfRemediation.Infrastructure.SourceObjects;
using PdfRemediation.Infrastructure.TagWriting;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace PdfRemediation.Benchmark;

/// <summary>
/// How one corpus file did. <see cref="Category"/> is the stable code of the first stage that failed.
/// The reader fields are set when a reader ran, live or replayed: its estimated cost and the pages it billed.
/// </summary>
public sealed record FileResult(string Name, string Status, string? Category, string? Message, int? Pages, double Seconds, double? ReaderCostUsd, int? ReaderPages);

public sealed record SecondsPerPage(double Mean, double P95);

public sealed record RunSummary(
    string RunId,
    int Files,
    int Passed,
    int Failed,
    IReadOnlyDictionary<string, int> FailuresByCategory,
    int Pages,
    SecondsPerPage? SecondsPerPage,
    double? ReaderCostPerPage,
    IReadOnlyList<FileResult> Results);

/// <summary>
/// One benchmark run over a corpus folder. Each file goes through IR → writer → veraPDF → pixel diff
/// under its own timeout; a file that fails or times out is recorded and the run moves on.
/// </summary>
internal sealed class BenchmarkRun(BenchmarkOptions options, MistralOptions mistral, string? mistralApiKey, TextWriter log)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITagWriter writer = new PdfPigTagWriter();
    private readonly VeraPdfOptions veraPdfOptions = VeraPdfOptions.DockerCompose(options.ComposeFile);

    // Each HTTP request has its own timeout (INT-01), so the client itself never times out.
    private readonly Lazy<HttpClient> http = new(() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    public async Task<RunSummary> ExecuteAsync()
    {
        var runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var runFolder = Path.Combine(options.Out, runId);
        for (var suffix = 2; Directory.Exists(runFolder); suffix++)
        {
            runFolder = Path.Combine(options.Out, $"{runId}-{suffix}");
        }

        Directory.CreateDirectory(runFolder);
        var pdfs = Directory.GetFiles(options.Corpus, "*.pdf").Order(StringComparer.Ordinal).ToList();
        await WriteJson(Path.Combine(runFolder, "config.json"), Config(pdfs));

        var validator = new VeraPdfValidator(veraPdfOptions);
        var visual = new VisualPreservationCheck(options.Dpi);
        var results = new List<FileResult>();
        foreach (var pdf in pdfs)
        {
            var name = Path.GetFileNameWithoutExtension(pdf);
            var folder = Path.Combine(runFolder, "files", name);
            Directory.CreateDirectory(folder);

            var result = await RunFile(pdf, name, folder, validator, visual);
            await WriteJson(Path.Combine(folder, "result.json"), result);
            await log.WriteLineAsync($"{name}: {result.Status}{(result.Category is null ? "" : $" ({result.Category})")}");
            results.Add(result);
        }

        var summary = Summarize(Path.GetFileName(runFolder), results);
        await WriteJson(Path.Combine(runFolder, "summary.json"), summary);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "summary.md"), Markdown(summary));
        await log.WriteLineAsync($"{summary.Passed} of {summary.Files} files passed. Results: {runFolder}");
        return summary;
    }

    private async Task<FileResult> RunFile(string pdfPath, string name, string folder, VeraPdfValidator validator, VisualPreservationCheck visual)
    {
        var stopwatch = Stopwatch.StartNew();
        int? pages = null;
        double? readerCost = null;
        int? readerPages = null;
        FileResult Failed(string category, string message) => new(name, "failed", category, message, pages, stopwatch.Elapsed.TotalSeconds, readerCost, readerPages);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            IrDocument ir;
            byte[] original;
            if (options.IrSource == "files")
            {
                var irPath = Path.ChangeExtension(pdfPath, ".ir.json");
                if (!File.Exists(irPath))
                {
                    return Failed("IR_MISSING", $"No IR file at {Path.GetFileName(irPath)}.");
                }

                var parsed = IrParser.Parse(await File.ReadAllTextAsync(irPath, timeout.Token));
                if (parsed.Ir is null)
                {
                    await WriteJson(Path.Combine(folder, "ir-errors.json"), parsed.Errors);
                    return Failed("IR_INVALID", $"{parsed.Errors.Count} IR validation errors; see ir-errors.json.");
                }

                ir = parsed.Ir;
                original = await File.ReadAllBytesAsync(pdfPath, timeout.Token);
            }
            else
            {
                original = await File.ReadAllBytesAsync(pdfPath, timeout.Token);
                var read = await ReadWithMistral(original, name, folder, timeout.Token);
                readerCost = read.Calls.Sum(c => c.EstimatedCostUsd);
                readerPages = read.Calls.Sum(c => c.PagesProcessed);
                if (read.Ir is null)
                {
                    var error = read.Errors.FirstOrDefault();
                    return Failed(error?.Code ?? "READER_FAILED", error?.Message ?? "The reader produced no IR; see reader.json.");
                }

                ir = read.Ir;
            }

            var written = await Task.Run(() => writer.Write(original, ir), timeout.Token).WaitAsync(timeout.Token);
            await WriteJson(Path.Combine(folder, "diagnostics.json"), written.Diagnostics);
            if (written.TaggedPdf is not { } tagged)
            {
                var error = written.Diagnostics.Errors.FirstOrDefault();
                return Failed(error?.Code ?? "WRITER_FAILED", error?.Message ?? "The writer produced no PDF.");
            }

            await File.WriteAllBytesAsync(Path.Combine(folder, "tagged.pdf"), tagged, timeout.Token);

            var veraPdf = await validator.ValidateAsync(tagged, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(folder, "verapdf.raw.json"), veraPdf.RawReport, timeout.Token);
            await WriteJson(Path.Combine(folder, "verapdf.json"), veraPdf.Report);

            var diff = await Task.Run(() => visual.Compare(original, tagged, Path.Combine(folder, "diff")), timeout.Token).WaitAsync(timeout.Token);
            await WriteJson(Path.Combine(folder, "visual.json"), diff);

            // Only files that got through every stage count towards seconds per page.
            pages = PageCount(original);
            if (written.Diagnostics.Errors is [var first, ..])
            {
                return Failed(first.Code, $"{written.Diagnostics.ElementsSkipped} Elements skipped; see diagnostics.json.");
            }

            if (!veraPdf.Report.Passed)
            {
                return Failed("VERAPDF_FAILED", $"{veraPdf.Report.FailureCount} PDF/UA-1 rules failed; see verapdf.json.");
            }

            if (!diff.Passed)
            {
                return Failed("VISUAL_DIFFERENCE", diff.Failure ?? "Pixels differ; see visual.json.");
            }

            return new FileResult(name, "passed", null, null, pages, stopwatch.Elapsed.TotalSeconds, readerCost, readerPages);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Failed("TIMEOUT", $"Did not finish within {options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }
        catch (PdfProcessingException ex)
        {
            return Failed(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return Failed("UNEXPECTED_ERROR", ex.ToString());
        }
    }

    /// <summary>
    /// Runs the Mistral reader over one file. Live mode renders every page and records each response under
    /// <c>&lt;recordings&gt;/mistral/&lt;name&gt;/</c>; replay mode reads those recordings and sends nothing.
    /// </summary>
    private async Task<ReaderResult> ReadWithMistral(byte[] original, string name, string folder, CancellationToken token)
    {
        var live = options.Mode == "live";
        var input = await Task.Run(() => ReaderInputFor(original, name, render: live), token).WaitAsync(token);
        var recordings = Path.Combine(options.Recordings, "mistral", name);
        IMistralTransport transport = live
            ? new MistralLiveTransport(http.Value, mistralApiKey!, mistral, recordings)
            : new MistralReplayTransport(recordings);

        var result = await new MistralOcrReader(transport, mistral.CostPerPageUsd).ReadAsync(input, token);
        await WriteJson(Path.Combine(folder, "reader.json"), new { result.Errors, result.Warnings, result.Calls });
        if (result.IrJson is { } irJson)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "ir.json"), irJson, token);
        }

        return result;
    }

    /// <summary>
    /// The reader's input. The IR title is the PDF's Info title, or the file name; its language is the catalog's
    /// /Lang, or en-US. Replay never sends a render, so it skips rendering.
    /// </summary>
    private ReaderInput ReaderInputFor(byte[] pdf, string name, bool render)
    {
        var index = SourceObjectIndex.Build(pdf);
        using var document = PdfDocument.Open(pdf);
        var title = string.IsNullOrWhiteSpace(document.Information.Title) ? name : document.Information.Title;
        var language = document.Structure.Catalog.CatalogDictionary.TryGet<StringToken>(NameToken.Lang, out var lang) && !string.IsNullOrWhiteSpace(lang.Data)
            ? lang.Data
            : "en-US";
        var pages = render
            ? PageRenderer.Render(pdf, options.Dpi)
            : document.GetPages().Select(p => new ReaderPage(p.Number, p.Width, p.Height, [])).ToList();
        return new ReaderInput(title, language, pages, index);
    }

    private static int PageCount(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return document.NumberOfPages;
    }

    private object Config(IReadOnlyList<string> pdfs) => new
    {
        benchmarkVersion = typeof(BenchmarkRun).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        arguments = options.Arguments,
        corpus = options.Corpus,
        output = options.Out,
        writer = options.Writer,
        irSource = options.IrSource,
        mode = options.IrSource == "files" ? null : options.Mode,
        recordings = options.IrSource == "files" ? null : options.Recordings,
        mistral = options.IrSource != "mistral" ? null : new
        {
            endpoint = mistral.Endpoint.ToString(),
            model = mistral.Model,
            costPerPageUsd = mistral.CostPerPageUsd,
            requestTimeoutSeconds = mistral.RequestTimeout.TotalSeconds,
            retryDelaysSeconds = mistral.RetryDelays.Select(d => d.TotalSeconds),
        },
        timeoutSeconds = options.TimeoutSeconds,
        dpi = options.Dpi,
        veraPdf = new
        {
            executable = veraPdfOptions.Executable,
            arguments = veraPdfOptions.Arguments,
            flavour = veraPdfOptions.Flavour,
            composeFile = options.ComposeFile,
            composeSha256 = Sha256(File.ReadAllBytes(options.ComposeFile)),
        },
        startedAt = DateTimeOffset.UtcNow,
        files = pdfs.Select(pdf =>
        {
            var ir = Path.ChangeExtension(pdf, ".ir.json");
            return new
            {
                name = Path.GetFileNameWithoutExtension(pdf),
                pdfSha256 = Sha256(File.ReadAllBytes(pdf)),
                irSha256 = File.Exists(ir) ? Sha256(File.ReadAllBytes(ir)) : null,
            };
        }),
    };

    /// <summary>
    /// Seconds per page come from files that ran the whole pipeline: each of a file's pages counts as
    /// that file's time divided by its page count. p95 uses the nearest-rank method.
    /// </summary>
    private static RunSummary Summarize(string runId, List<FileResult> results)
    {
        var measured = results.Where(r => r.Pages is > 0).ToList();
        var perPage = measured.SelectMany(r => Enumerable.Repeat(r.Seconds / r.Pages!.Value, r.Pages!.Value)).Order().ToList();
        SecondsPerPage? secondsPerPage = perPage.Count == 0
            ? null
            : new SecondsPerPage(perPage.Average(), perPage[(int)Math.Ceiling(0.95 * perPage.Count) - 1]);

        var failures = results.Where(r => r.Category is not null)
            .GroupBy(r => r.Category!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

        // Reader cost per page is over every page the reader billed, whether or not the file then passed.
        var readerPages = results.Sum(r => r.ReaderPages ?? 0);
        double? readerCostPerPage = readerPages == 0 ? null : results.Sum(r => r.ReaderCostUsd ?? 0) / readerPages;

        var passed = results.Count(r => r.Status == "passed");
        return new RunSummary(runId, results.Count, passed, results.Count - passed, failures, measured.Sum(r => r.Pages!.Value), secondsPerPage, readerCostPerPage, results);
    }

    private static string Markdown(RunSummary summary)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Benchmark run {summary.RunId}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"{summary.Passed} of {summary.Files} files passed.");
        md.AppendLine();
        if (summary.SecondsPerPage is { } spp)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"Seconds per page over {summary.Pages} pages: mean {spp.Mean:0.000}, p95 {spp.P95:0.000}.");
            md.AppendLine();
        }

        if (summary.ReaderCostPerPage is { } cost)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"Reader cost per page: ${cost:0.0000} (estimated from the configured rate).");
            md.AppendLine();
        }

        if (summary.FailuresByCategory.Count > 0)
        {
            md.AppendLine("| Failure category | Files |");
            md.AppendLine("|---|---|");
            foreach (var (category, count) in summary.FailuresByCategory)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {category} | {count} |");
            }

            md.AppendLine();
        }

        md.AppendLine("| File | Status | Category | Pages | Seconds |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var r in summary.Results)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {r.Name} | {r.Status} | {r.Category ?? ""} | {r.Pages?.ToString(CultureInfo.InvariantCulture) ?? ""} | {r.Seconds:0.000} |");
        }

        return md.ToString();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static Task WriteJson<T>(string path, T value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json));
}
