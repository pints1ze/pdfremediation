using System.Diagnostics;
using System.Text.Json;
using PdfRemediation.Infrastructure.Pdf;

namespace PdfRemediation.Infrastructure.Checks;

/// <summary>One failed veraPDF rule: its ID (<c>clause-test</c>), how many checks failed, and what it requires.</summary>
public sealed record ValidationFailure(string RuleId, int Count, string Message);

/// <summary>A veraPDF result normalized to the shape in PRD §10.4.</summary>
public sealed record ValidationReport(string Profile, bool Passed, int FailureCount, IReadOnlyList<ValidationFailure> Failures);

/// <summary>
/// How to start veraPDF. The command is configuration: <see cref="Executable"/> and <see cref="Arguments"/>
/// must start a veraPDF CLI that reads the PDF from stdin.
/// </summary>
public sealed record VeraPdfOptions(string Executable, IReadOnlyList<string> Arguments, string Flavour, TimeSpan Timeout)
{
    /// <summary>Runs the pinned <c>verapdf</c> service from the repo's compose file, with the PDF/UA-1 profile.</summary>
    public static VeraPdfOptions DockerCompose(string composeFile) =>
        new("docker", ["compose", "-f", composeFile, "run", "--rm", "-T", "verapdf"], "ua1", TimeSpan.FromMinutes(5));
}

/// <summary>The Validator: runs the veraPDF CLI on a PDF and normalizes its JSON report.</summary>
public sealed class VeraPdfValidator(VeraPdfOptions options)
{
    private static readonly Dictionary<string, string> ProfileNames = new()
    {
        ["ua1"] = "PDF/UA-1",
    };

    public async Task<ValidationReport> ValidateAsync(byte[] pdf, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(options.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in options.Arguments.Concat(["--flavour", options.Flavour, "--format", "json"]))
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        using var process = Process.Start(start)
            ?? throw new PdfProcessingException("VALIDATOR_UNAVAILABLE", $"Could not start '{options.Executable}'.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(pdf, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new PdfProcessingException("VALIDATOR_TIMEOUT", $"veraPDF did not finish within {options.Timeout}.");
        }

        var json = await stdout;
        try
        {
            return Normalize(json);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new PdfProcessingException("VALIDATOR_REPORT_UNREADABLE", $"veraPDF exited with {process.ExitCode} without a readable report: {await stderr}");
        }
    }

    private ValidationReport Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("report").GetProperty("jobs")[0].GetProperty("validationResult");
        var result = results.ValueKind == JsonValueKind.Array ? results[0] : results;

        var failures = result.GetProperty("details").GetProperty("ruleSummaries").EnumerateArray()
            .Where(rule => rule.GetProperty("status").GetString() == "failed")
            .Select(rule => new ValidationFailure(
                $"{rule.GetProperty("clause").GetString()}-{rule.GetProperty("testNumber").GetInt32()}",
                rule.GetProperty("failedChecks").GetInt32(),
                rule.GetProperty("description").GetString() ?? ""))
            .ToList();

        var profile = ProfileNames.GetValueOrDefault(options.Flavour, options.Flavour);
        return new ValidationReport(profile, result.GetProperty("compliant").GetBoolean() && failures.Count == 0, failures.Count, failures);
    }
}
