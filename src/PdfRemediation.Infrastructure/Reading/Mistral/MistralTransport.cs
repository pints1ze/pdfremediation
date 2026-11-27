using System.Text.Json;

namespace PdfRemediation.Infrastructure.Reading.Mistral;

/// <summary>One page's response body exactly as the provider sent it, and how it was obtained.</summary>
public sealed record MistralExchange(string Body, string? RequestId, int Tries, bool Replayed);

/// <summary>How the reader gets a response for a page: from the API (recording it) or from a recording.</summary>
public interface IMistralTransport
{
    Task<MistralExchange> SendAsync(ReaderPage page, CancellationToken cancellationToken);
}

/// <summary>
/// The on-disk layout of recorded responses: one folder per PDF, holding <c>page-0001.json</c>
/// (the raw body) and <c>page-0001.meta.json</c> (request ID, HTTP tries, model and when it was recorded).
/// </summary>
public static class MistralRecording
{
    public static string BodyPath(string folder, int pageNumber) => Path.Combine(folder, $"page-{pageNumber:0000}.json");

    public static string MetaPath(string folder, int pageNumber) => Path.Combine(folder, $"page-{pageNumber:0000}.meta.json");
}

/// <summary>Replays recorded responses. It has no HTTP client, so a replay can't reach the network.</summary>
public sealed class MistralReplayTransport(string recordingFolder) : IMistralTransport
{
    public async Task<MistralExchange> SendAsync(ReaderPage page, CancellationToken cancellationToken)
    {
        var bodyPath = MistralRecording.BodyPath(recordingFolder, page.PageNumber);
        if (!File.Exists(bodyPath))
        {
            throw new ReaderException("READER_RECORDING_MISSING", $"No recorded response for page {page.PageNumber} at {bodyPath}.");
        }

        var body = await File.ReadAllTextAsync(bodyPath, cancellationToken);
        string? requestId = null;
        var tries = 1;
        var metaPath = MistralRecording.MetaPath(recordingFolder, page.PageNumber);
        if (File.Exists(metaPath))
        {
            try
            {
                using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath, cancellationToken));
                requestId = meta.RootElement.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                tries = meta.RootElement.TryGetProperty("tries", out var t) && t.TryGetInt32(out var n) ? n : 1;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new ReaderException("READER_RECORDING_MALFORMED", $"The recording metadata at {metaPath} is not valid: {ex.Message}");
            }
        }

        return new MistralExchange(body, requestId, tries, Replayed: true);
    }
}
