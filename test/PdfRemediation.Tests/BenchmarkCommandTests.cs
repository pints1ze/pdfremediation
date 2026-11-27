using PdfRemediation.Benchmark;

namespace PdfRemediation.Tests;

public class BenchmarkCommandTests
{
    [Fact]
    public void Running_with_no_arguments_prints_usage_and_fails()
    {
        var output = new StringWriter();

        var exitCode = BenchmarkCommand.Run([], output);

        Assert.NotEqual(0, exitCode);
        Assert.StartsWith("Usage: pdf-benchmark", output.ToString());
    }
}
