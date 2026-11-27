using System.Globalization;
using Microsoft.Extensions.Configuration;
using PdfRemediation.Infrastructure.Reading.Mistral;

namespace PdfRemediation.Benchmark;

/// <summary>The benchmark command (seam 1): parses the command line and runs the corpus.</summary>
public static class BenchmarkCommand
{
    /// <summary>
    /// Returns 0 when every file passed, 2 when the run finished with failures, and 1 for bad arguments.
    /// Secrets and reader settings come from <paramref name="configuration"/>, which defaults to the user secrets
    /// and environment variables.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, TextWriter output, IConfiguration? configuration = null)
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

        configuration ??= new ConfigurationBuilder()
            .AddUserSecrets(typeof(BenchmarkCommand).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var apiKey = configuration["Mistral:ApiKey"] ?? configuration["MISTRAL_API_KEY"];
        if (options is { IrSource: "mistral", Mode: "live" } && string.IsNullOrWhiteSpace(apiKey))
        {
            await output.WriteLineAsync("A live Mistral run needs an API key: set the MISTRAL_API_KEY environment variable, or run "
                + "'dotnet user-secrets set Mistral:ApiKey <key> --project src/PdfRemediation.Benchmark'.");
            return 1;
        }

        var (mistral, configError) = MistralOptionsFrom(configuration);
        if (mistral is null)
        {
            await output.WriteLineAsync(configError);
            return 1;
        }

        var summary = await new BenchmarkRun(options, mistral, apiKey, output).ExecuteAsync();
        return summary.Failed == 0 ? 0 : 2;
    }

    /// <summary>The endpoint, model and rate are configuration, so a run can point elsewhere without a code change.</summary>
    private static (MistralOptions? Options, string? Error) MistralOptionsFrom(IConfiguration configuration)
    {
        var defaults = MistralOptions.Default;
        var endpoint = defaults.Endpoint;
        if (configuration["Mistral:Endpoint"] is { } configuredEndpoint && !Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out endpoint))
        {
            return (null, $"Mistral:Endpoint '{configuredEndpoint}' is not an absolute URL.");
        }

        var rate = defaults.CostPerPageUsd;
        if (configuration["Mistral:CostPerPageUsd"] is { } configuredRate
            && (!double.TryParse(configuredRate, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || rate < 0))
        {
            return (null, $"Mistral:CostPerPageUsd '{configuredRate}' is not a non-negative number of US dollars.");
        }

        return (defaults with { Endpoint = endpoint, Model = configuration["Mistral:Model"] ?? defaults.Model, CostPerPageUsd = rate }, null);
    }
}
