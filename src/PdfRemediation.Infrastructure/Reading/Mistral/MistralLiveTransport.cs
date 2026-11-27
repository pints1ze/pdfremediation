using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PdfRemediation.Infrastructure.Reading.Mistral;

/// <summary>
/// Everything about the Mistral OCR call except the API key, which never goes in a record that might be logged.
/// Retries and timeouts follow PRD INT-01: at most two retries, 2 and 8 seconds apart, 120 seconds per request.
/// </summary>
public sealed record MistralOptions(Uri Endpoint, string Model, double CostPerPageUsd, TimeSpan RequestTimeout, IReadOnlyList<TimeSpan> RetryDelays)
{
    public static MistralOptions Default { get; } = new(
        new Uri("https://api.mistral.ai/v1/ocr"),
        "mistral-ocr-4-1",
        CostPerPageUsd: 0.004,
        RequestTimeout: TimeSpan.FromSeconds(120),
        RetryDelays: [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)]);
}

/// <summary>Calls the Mistral OCR API with one page render, retrying transient failures, and records each response.</summary>
public sealed class MistralLiveTransport(
    HttpClient http,
    string apiKey,
    MistralOptions options,
    string recordingFolder,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IMistralTransport
{
    private static readonly string[] RequestIdHeaders = ["x-request-id", "mistral-correlation-id", "request-id"];

    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    public async Task<MistralExchange> SendAsync(ReaderPage page, CancellationToken cancellationToken)
    {
        var request = RequestBody(page);
        string lastFailure = "";
        for (var attempt = 1; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(options.RequestTimeout);
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
                {
                    Content = new StringContent(request, Encoding.UTF8, "application/json"),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var response = await http.SendAsync(message, attemptTimeout.Token);
                var body = await response.Content.ReadAsStringAsync(attemptTimeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    var requestId = RequestId(response);
                    await Record(page, body, requestId, attempt, cancellationToken);
                    return new MistralExchange(body, requestId, attempt, Replayed: false);
                }

                var failure = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}";
                if (!IsTransient(response.StatusCode))
                {
                    throw new ReaderException("READER_REQUEST_FAILED", $"Mistral OCR rejected page {page.PageNumber}: {failure}");
                }

                lastFailure = failure;
            }
            catch (HttpRequestException ex)
            {
                lastFailure = ex.Message;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = $"no response within {options.RequestTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds";
            }

            if (attempt > options.RetryDelays.Count)
            {
                throw new ReaderException("READER_UNAVAILABLE", $"Mistral OCR failed page {page.PageNumber} after {attempt} attempts; last: {lastFailure}");
            }

            await delay(options.RetryDelays[attempt - 1], cancellationToken);
        }
    }

    private string RequestBody(ReaderPage page) => new JsonObject
    {
        ["model"] = options.Model,
        ["document"] = new JsonObject
        {
            ["type"] = "image_url",
            ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(page.Png),
        },
        ["include_blocks"] = true,
        ["confidence_scores_granularity"] = "block",
        ["include_image_base64"] = false,
    }.ToJsonString();

    private async Task Record(ReaderPage page, string body, string? requestId, int tries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(recordingFolder);
        await File.WriteAllTextAsync(MistralRecording.BodyPath(recordingFolder, page.PageNumber), body, cancellationToken);
        var meta = new JsonObject
        {
            ["requestId"] = requestId,
            ["tries"] = tries,
            ["model"] = options.Model,
            ["endpoint"] = options.Endpoint.ToString(),
            ["recordedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
        await File.WriteAllTextAsync(MistralRecording.MetaPath(recordingFolder, page.PageNumber), meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string? RequestId(HttpResponseMessage response) =>
        RequestIdHeaders.Select(h => response.Headers.TryGetValues(h, out var values) ? values.FirstOrDefault() : null).FirstOrDefault(v => v is not null);

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static string Truncate(string body) => body.Length <= 500 ? body : body[..500] + "…";
}
