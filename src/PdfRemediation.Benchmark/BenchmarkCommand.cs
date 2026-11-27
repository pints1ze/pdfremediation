namespace PdfRemediation.Benchmark;

/// <summary>The benchmark command (seam 1): parses the command line and runs the corpus.</summary>
public static class BenchmarkCommand
{
    /// <summary>Returns 0 when every file passed, 2 when the run finished with failures, and 1 for bad arguments.</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var (options, error) = BenchmarkOptions.Parse(args);
        if (options is null)
        {
            if (args.Length > 0)
            {
                await output.WriteLineAsync(error);
                await output.WriteLineAsync();
            }

            await output.WriteLineAsync(BenchmarkOptions.Usage);
            return 1;
        }

        var summary = await new BenchmarkRun(options, output).ExecuteAsync();
        return summary.Failed == 0 ? 0 : 2;
    }
}
