using System.Text.Json.Nodes;
using PdfRemediation.Benchmark;
using PdfRemediation.Tests.Support;

namespace PdfRemediation.Tests.Benchmark;

/// <summary>Seam 1: the benchmark command, run over a corpus folder exactly as a developer would run it.</summary>
public class BenchmarkCommandTests
{
    [Fact]
    public async Task Running_with_no_arguments_prints_usage_and_fails()
    {
        var output = new StringWriter();

        var exitCode = await BenchmarkCommand.RunAsync([], output);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Usage: pdf-benchmark", output.ToString());
    }

    [Theory]
    [InlineData("--corpus", "{corpus}", "--out", "{out}", "--writer", "aspose")]
    [InlineData("--corpus", "{corpus}", "--out", "{out}", "--colour", "blue")]
    [InlineData("--corpus", "{missing}", "--out", "{out}")]
    [InlineData("--corpus", "{corpus}", "--out", "{out}", "--timeout", "soon")]
    public async Task Bad_arguments_print_usage_and_fail(params string[] args)
    {
        var corpus = Corpus.Create();
        var output = new StringWriter();

        var exitCode = await BenchmarkCommand.RunAsync(Corpus.Fill(args, corpus), output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage: pdf-benchmark", output.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(corpus.Out));
    }

    [Fact]
    [Trait("Category", Docker.Category)]
    public async Task A_run_writes_per_file_outputs_and_both_summaries_and_keeps_going_past_bad_files()
    {
        var corpus = Corpus.Create();
        corpus.Add("headings-and-paragraphs", Fixtures.Bytes("headings-and-paragraphs.pdf"), Fixtures.Text("headings-and-paragraphs.ir.json"));
        corpus.Add("corrupt", "this is not a PDF"u8.ToArray(), Fixtures.Text("headings-and-paragraphs.ir.json"));
        corpus.Add("no-ir", Fixtures.Bytes("headings-and-paragraphs.pdf"), ir: null);

        var exitCode = await BenchmarkCommand.RunAsync(corpus.Args(), new StringWriter());

        Assert.Equal(2, exitCode); // the run finished, but not every file passed
        var run = Assert.Single(Directory.GetDirectories(corpus.Out));

        var summary = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "summary.json")))!;
        Assert.Equal(3, (int)summary["files"]!);
        Assert.Equal(1, (int)summary["passed"]!);
        Assert.Equal(2, (int)summary["failed"]!);
        Assert.Equal(1, (int)summary["failuresByCategory"]!["PDF_UNREADABLE"]!);
        Assert.Equal(1, (int)summary["failuresByCategory"]!["IR_MISSING"]!);
        Assert.Equal(1, (int)summary["pages"]!); // only the file that could be read has a page count
        Assert.True((double)summary["secondsPerPage"]!["mean"]! > 0);
        Assert.True((double)summary["secondsPerPage"]!["p95"]! >= (double)summary["secondsPerPage"]!["mean"]!);
        Assert.Contains("1 of 3 files passed", File.ReadAllText(Path.Combine(run, "summary.md")));

        var passed = Path.Combine(run, "files", "headings-and-paragraphs");
        Assert.Equal("PDF-", File.ReadAllText(Path.Combine(passed, "tagged.pdf"))[1..5]);
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(Path.Combine(passed, "verapdf.raw.json")))!["report"]);
        var veraPdf = JsonNode.Parse(File.ReadAllText(Path.Combine(passed, "verapdf.json")))!;
        Assert.Equal(("PDF/UA-1", true), ((string)veraPdf["profile"]!, (bool)veraPdf["passed"]!));
        var visual = JsonNode.Parse(File.ReadAllText(Path.Combine(passed, "visual.json")))!;
        Assert.Equal(0, (long)visual["pages"]![0]!["mismatchedPixels"]!);
        var diagnostics = JsonNode.Parse(File.ReadAllText(Path.Combine(passed, "diagnostics.json")))!;
        Assert.Equal((5, 0), ((int)diagnostics["elementsApplied"]!, (int)diagnostics["elementsSkipped"]!));

        var corrupt = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "files", "corrupt", "result.json")))!;
        Assert.Equal(("failed", "PDF_UNREADABLE"), ((string)corrupt["status"]!, (string)corrupt["category"]!));
    }

    [Fact]
    public async Task The_run_folder_records_the_exact_config_used()
    {
        var corpus = Corpus.Create();
        corpus.Add("headings-and-paragraphs", Fixtures.Bytes("headings-and-paragraphs.pdf"), Fixtures.Text("headings-and-paragraphs.ir.json"));
        string[] args = [.. corpus.Args(), "--dpi", "96", "--timeout", "0.001"];

        await BenchmarkCommand.RunAsync(args, new StringWriter());

        var run = Assert.Single(Directory.GetDirectories(corpus.Out));
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "config.json")))!;
        Assert.Equal(args, config["arguments"]!.AsArray().Select(a => (string)a!));
        Assert.Equal(("ours", "files", 96, 0.001), ((string)config["writer"]!, (string)config["irSource"]!, (int)config["dpi"]!, (double)config["timeoutSeconds"]!));
        Assert.Equal(Path.GetFullPath(corpus.Folder), (string)config["corpus"]!);
        Assert.Equal("ua1", (string)config["veraPdf"]!["flavour"]!);
        var file = Assert.Single(config["files"]!.AsArray())!;
        // SHA-256 of the committed fixture, so a rerun can prove it used the same bytes.
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Fixtures.Bytes("headings-and-paragraphs.pdf"))), (string)file["pdfSha256"]!);
    }

    [Fact]
    public async Task A_file_that_times_out_is_reported_as_failed_and_the_run_completes()
    {
        var corpus = Corpus.Create();
        corpus.Add("first", Fixtures.Bytes("headings-and-paragraphs.pdf"), Fixtures.Text("headings-and-paragraphs.ir.json"));
        corpus.Add("second", Fixtures.Bytes("headings-and-paragraphs.pdf"), Fixtures.Text("headings-and-paragraphs.ir.json"));

        // One millisecond is far less than tagging, validating and rendering a file takes.
        var exitCode = await BenchmarkCommand.RunAsync([.. corpus.Args(), "--timeout", "0.001"], new StringWriter());

        Assert.Equal(2, exitCode);
        var run = Assert.Single(Directory.GetDirectories(corpus.Out));
        var summary = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "summary.json")))!;
        Assert.Equal((2, 0), ((int)summary["files"]!, (int)summary["passed"]!));
        Assert.Equal(2, (int)summary["failuresByCategory"]!["TIMEOUT"]!);
    }

    /// <summary>A throwaway corpus folder and output folder for one test.</summary>
    private sealed record Corpus(string Folder, string Out)
    {
        public static Corpus Create()
        {
            var root = Directory.CreateTempSubdirectory("benchmark-test-").FullName;
            var corpus = new Corpus(Path.Combine(root, "corpus"), Path.Combine(root, "out"));
            Directory.CreateDirectory(corpus.Folder);
            Directory.CreateDirectory(corpus.Out);
            return corpus;
        }

        public void Add(string name, byte[] pdf, string? ir)
        {
            File.WriteAllBytes(Path.Combine(Folder, $"{name}.pdf"), pdf);
            if (ir is not null)
            {
                File.WriteAllText(Path.Combine(Folder, $"{name}.ir.json"), ir);
            }
        }

        public string[] Args() =>
            ["--corpus", Folder, "--out", Out, "--compose", Path.Combine(Fixtures.RepoRoot, "compose.yaml")];

        public static string[] Fill(string[] args, Corpus corpus) =>
            args.Select(a => a.Replace("{corpus}", corpus.Folder).Replace("{out}", corpus.Out).Replace("{missing}", Path.Combine(corpus.Folder, "nope"))).ToArray();
    }
}
