using System.Globalization;

namespace PdfRemediation.Benchmark;

/// <summary>Everything one benchmark run is configured with, resolved from the command line.</summary>
public sealed record BenchmarkOptions(
    IReadOnlyList<string> Arguments,
    string Corpus,
    string Out,
    string Writer,
    string IrSource,
    string Mode,
    string Recordings,
    double TimeoutSeconds,
    int Dpi,
    string ComposeFile)
{
    public const string Usage = """
        Usage: pdf-benchmark --corpus <folder> --out <folder> [options]

        Runs each PDF in the corpus folder through tag writer, veraPDF and pixel diff,
        and writes a timestamped run folder with per-file results and a summary.

        Options:
          --corpus <folder>    Folder of corpus PDFs. Each <name>.pdf needs <name>.ir.json beside it.
          --out <folder>       Folder to create the run folder in.
          --writer <ours>      Tag writer to use (default: ours).
          --ir <source>        Where Accessibility IR comes from (default: files):
                                 files    the <name>.ir.json beside each PDF
                                 mistral  the Mistral OCR 4.1 reader
          --mode <mode>        How the reader runs (default: replay):
                                 replay   use recorded responses; makes no network calls
                                 live     call the API and record each response; needs the
                                          MISTRAL_API_KEY environment variable or the
                                          Mistral:ApiKey user secret
          --recordings <folder>  Where reader responses are recorded and replayed from
                               (default: <corpus>/recordings).
          --timeout <seconds>  Time allowed per file before it fails as TIMEOUT (default: 600).
          --dpi <n>            Render resolution for the pixel diff (default: 150).
          --compose <file>     Compose file with the pinned veraPDF service (default: compose.yaml in
                               the current folder or the nearest parent folder that has one).
        """;

    private static readonly string[] Writers = ["ours"];
    private static readonly string[] IrSources = ["files", "mistral"];
    private static readonly string[] Modes = ["replay", "live"];

    /// <summary>Parses the command line, or returns why it couldn't.</summary>
    public static (BenchmarkOptions? Options, string? Error) Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                return (null, $"Expected '--option value' but got '{args[i]}'.");
            }

            values[args[i][2..]] = args[i + 1];
        }

        var unknown = values.Keys.Except(["corpus", "out", "writer", "ir", "mode", "recordings", "timeout", "dpi", "compose"]).FirstOrDefault();
        if (unknown is not null)
        {
            return (null, $"Unknown option --{unknown}.");
        }

        if (!values.TryGetValue("corpus", out var corpus) || !values.TryGetValue("out", out var output))
        {
            return (null, "--corpus and --out are required.");
        }

        if (!Directory.Exists(corpus))
        {
            return (null, $"Corpus folder '{corpus}' does not exist.");
        }

        var writer = values.GetValueOrDefault("writer", "ours");
        if (!Writers.Contains(writer))
        {
            return (null, $"Writer '{writer}' is not available; choose one of: {string.Join(", ", Writers)}.");
        }

        var irSource = values.GetValueOrDefault("ir", "files");
        if (!IrSources.Contains(irSource))
        {
            return (null, $"IR source '{irSource}' is not available; choose one of: {string.Join(", ", IrSources)}.");
        }

        var mode = values.GetValueOrDefault("mode", "replay");
        if (!Modes.Contains(mode))
        {
            return (null, $"Mode '{mode}' is not available; choose one of: {string.Join(", ", Modes)}.");
        }

        if (irSource == "files" && (values.ContainsKey("mode") || values.ContainsKey("recordings")))
        {
            return (null, "--mode and --recordings apply only to a reader, such as --ir mistral.");
        }

        var recordings = values.GetValueOrDefault("recordings", Path.Combine(corpus, "recordings"));

        if (!double.TryParse(values.GetValueOrDefault("timeout", "600"), NumberStyles.Float, CultureInfo.InvariantCulture, out var timeout) || timeout <= 0)
        {
            return (null, "--timeout must be a number of seconds greater than zero.");
        }

        if (!int.TryParse(values.GetValueOrDefault("dpi", "150"), NumberStyles.None, CultureInfo.InvariantCulture, out var dpi) || dpi <= 0)
        {
            return (null, "--dpi must be a whole number greater than zero.");
        }

        var compose = values.TryGetValue("compose", out var composeFile) ? composeFile : FindComposeFile();
        if (compose is null || !File.Exists(compose))
        {
            return (null, "Could not find compose.yaml; pass --compose <file>.");
        }

        return (new BenchmarkOptions(args, Path.GetFullPath(corpus), Path.GetFullPath(output), writer, irSource, mode, Path.GetFullPath(recordings), timeout, dpi, Path.GetFullPath(compose)), null);
    }

    private static string? FindComposeFile()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "compose.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
