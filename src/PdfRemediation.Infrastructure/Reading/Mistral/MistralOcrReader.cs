using System.Text.Json;
using System.Text.Json.Nodes;
using PdfRemediation.Domain.AccessibilityIr;

namespace PdfRemediation.Infrastructure.Reading.Mistral;

/// <summary>
/// The Mistral OCR 4.1 reader adapter. It sends one page render per call and normalizes each response the same way
/// whether it came live or from a recording, so a replay gives the same IR as the live run it recorded.
/// </summary>
public sealed class MistralOcrReader(IMistralTransport transport, double costPerPageUsd) : IReaderAdapter
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public async Task<ReaderResult> ReadAsync(ReaderInput input, CancellationToken cancellationToken)
    {
        var errors = new List<ReaderDiagnostic>();
        var warnings = new List<ReaderDiagnostic>();
        var calls = new List<ReaderCall>();
        var pages = new JsonArray();
        foreach (var page in input.Pages)
        {
            MistralExchange exchange;
            try
            {
                exchange = await transport.SendAsync(page, cancellationToken);
            }
            catch (ReaderException ex)
            {
                errors.Add(new(ex.Code, page.PageNumber, ex.Message));
                continue;
            }

            try
            {
                var normalized = MistralNormalizer.Normalize(exchange.Body, page, input.SourceObjects.All.Where(o => o.PageNumber == page.PageNumber));
                calls.Add(Call(page, exchange, normalized.PagesProcessed));
                warnings.AddRange(normalized.Warnings);
                pages.Add(PageJson(page, normalized.Elements));
            }
            catch (ReaderException ex)
            {
                // The call was still made and charged; assume it covered the one page sent.
                calls.Add(Call(page, exchange, pagesProcessed: 1));
                errors.Add(new(ex.Code, page.PageNumber, ex.Message));
            }
        }

        if (errors.Count > 0)
        {
            return new ReaderResult(null, null, errors, warnings, calls);
        }

        var irJson = new JsonObject
        {
            ["schemaVersion"] = IrParser.SupportedSchemaVersion,
            ["title"] = input.Title,
            ["language"] = input.Language,
            ["pages"] = pages,
        }.ToJsonString(IndentedJson);

        var parsed = IrParser.Parse(irJson);
        if (parsed.Ir is null)
        {
            // Normalization should never produce invalid IR; if it does, say exactly where.
            errors.AddRange(parsed.Errors.Select(e => new ReaderDiagnostic("READER_IR_INVALID", null, $"{e.Code} at {e.Path}: {e.Message}")));
            return new ReaderResult(null, irJson, errors, warnings, calls);
        }

        return new ReaderResult(parsed.Ir, irJson, [], warnings, calls);
    }

    private ReaderCall Call(ReaderPage page, MistralExchange exchange, int pagesProcessed) =>
        new(page.PageNumber, exchange.RequestId, exchange.Tries, pagesProcessed, pagesProcessed * costPerPageUsd, exchange.Replayed);

    private static JsonObject PageJson(ReaderPage page, IReadOnlyList<NormalizedElement> elements) => new()
    {
        ["pageNumber"] = page.PageNumber,
        ["width"] = page.Width,
        ["height"] = page.Height,
        ["elements"] = new JsonArray(elements.Select(e => (JsonNode)new JsonObject
        {
            ["id"] = e.Id,
            ["type"] = e.Type.ToString(),
            ["bbox"] = new JsonArray(e.Bbox.Left, e.Bbox.Top, e.Bbox.Right, e.Bbox.Bottom),
            ["readingOrder"] = e.ReadingOrder,
            ["confidence"] = e.Confidence,
            ["text"] = e.Text,
            ["sourceObjects"] = new JsonArray(e.SourceObjects.Select(s => (JsonNode)s).ToArray()),
        }).ToArray()),
    };
}
