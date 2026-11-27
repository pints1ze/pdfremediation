namespace PdfRemediation.Benchmark;

public static class BenchmarkCommand
{
    public const string Usage = """
        Usage: pdf-benchmark --corpus <folder> --out <folder> [options]

        Runs each PDF in the corpus folder through the remediation pipeline.

        Options:
          --corpus <folder>          Folder of corpus PDFs to run.
          --out <folder>             Folder to write benchmark output into.
          --reader <mistral|paddle>  Reader to use.
          --writer <ours|aspose>     Tag writer to use.
          --mode <live|replay>       Call providers live, or replay recorded responses.
        """;

    public static int Run(string[] args, TextWriter output)
    {
        output.WriteLine(Usage);
        return 1;
    }
}
