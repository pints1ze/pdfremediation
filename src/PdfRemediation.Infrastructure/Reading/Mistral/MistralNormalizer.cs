using System.Text.Json;
using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.SourceObjects;

namespace PdfRemediation.Infrastructure.Reading.Mistral;

/// <summary>One Element normalized from a block, before it is written as IR JSON.</summary>
internal sealed record NormalizedElement(string Id, ElementType Type, BoundingBox Bbox, int ReadingOrder, double? Confidence, string Text, IReadOnlyList<string> SourceObjects);

internal sealed record NormalizedPage(IReadOnlyList<NormalizedElement> Elements, IReadOnlyList<ReaderDiagnostic> Warnings, int PagesProcessed);

/// <summary>
/// Turns one page's OCR response into IR Elements. Block types map to Element types, block order is reading order,
/// the block-type confidence becomes the Element's confidence, and boxes are scaled from response pixels to page points.
/// Each Source object goes to the block that covers most of it, so no Source object is claimed twice.
/// </summary>
internal static class MistralNormalizer
{
    /// <summary>A Source object belongs to a block only if the block covers at least this share of it.</summary>
    private const double MinimumOverlap = 0.5;

    private static readonly Dictionary<string, ElementType> Mapped = new(StringComparer.Ordinal)
    {
        ["title"] = ElementType.H1, // The level comes from the Markdown heading marker in the content; see Heading.
        ["text"] = ElementType.P,
        ["list"] = ElementType.P, // IR v1 has no list Elements yet.
        ["caption"] = ElementType.P,
        ["code"] = ElementType.P,
        ["references"] = ElementType.P,
        ["aside_text"] = ElementType.P,
        ["equation"] = ElementType.P,
        ["header"] = ElementType.Artifact,
        ["footer"] = ElementType.Artifact,
    };

    /// <summary>Block types Mistral documents that IR v1 has no Element type for yet.</summary>
    private static readonly HashSet<string> Unmapped = new(StringComparer.Ordinal) { "image", "table", "signature" };

    public static NormalizedPage Normalize(string body, ReaderPage page, IEnumerable<SourceObject> sourceObjects)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw Malformed("", $"The response is not JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            {
                throw Malformed("pages", "The response has no pages array.");
            }

            var pagesProcessed = root.TryGetProperty("usage_info", out var usage) && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("pages_processed", out var processed) && processed.ValueKind == JsonValueKind.Number && processed.TryGetInt32(out var count) && count >= 0
                ? count
                : throw Malformed("usage_info.pages_processed", "The response has no usage_info.pages_processed.");

            if (pages.GetArrayLength() == 0)
            {
                throw new ReaderException("READER_RESPONSE_PARTIAL", "The response has no pages.");
            }

            var warnings = new List<ReaderDiagnostic>();
            var blocks = ReadBlocks(pages[0], page, warnings);
            var elements = Link(blocks, page, sourceObjects, warnings);
            return new NormalizedPage(elements, warnings, pagesProcessed);
        }
    }

    private static List<NormalizedElement> ReadBlocks(JsonElement pageJson, ReaderPage page, List<ReaderDiagnostic> warnings)
    {
        if (!pageJson.TryGetProperty("dimensions", out var dimensions) || dimensions.ValueKind != JsonValueKind.Object)
        {
            throw Malformed("pages[0].dimensions", "The page has no dimensions.");
        }

        var width = Number(dimensions, "width", "pages[0].dimensions");
        var height = Number(dimensions, "height", "pages[0].dimensions");
        if (width <= 0 || height <= 0)
        {
            throw Malformed("pages[0].dimensions", "The page dimensions must be positive.");
        }

        if (!pageJson.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            throw new ReaderException("READER_RESPONSE_PARTIAL", "The page has no blocks, though include_blocks was requested.");
        }

        var scaleX = page.Width / width;
        var scaleY = page.Height / height;
        var elements = new List<NormalizedElement>();
        var index = 0;
        foreach (var block in blocks.EnumerateArray())
        {
            var path = $"pages[0].blocks[{index}]";
            index++;
            var type = block.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()!
                : throw Malformed($"{path}.type", "The block has no type.");

            var left = Number(block, "top_left_x", path);
            var top = Number(block, "top_left_y", path);
            var right = Number(block, "bottom_right_x", path);
            var bottom = Number(block, "bottom_right_y", path);
            if (right <= left || bottom <= top)
            {
                throw Malformed(path, $"The block's box ({left}, {top}) → ({right}, {bottom}) is empty or inverted.");
            }

            var confidence = BlockTypeConfidence(block, path);
            if (!Mapped.TryGetValue(type, out var elementType))
            {
                warnings.Add(Unmapped.Contains(type)
                    ? new("READER_BLOCK_TYPE_UNMAPPED", page.PageNumber, $"Block {index} is '{type}', which IR v1 has no Element type for; left out.")
                    : new("READER_BLOCK_TYPE_UNKNOWN", page.PageNumber, $"Block {index} has unknown type '{type}'; left out."));
                continue;
            }

            var bbox = new BoundingBox(
                Clamp(left * scaleX, page.Width),
                Clamp(top * scaleY, page.Height),
                Clamp(right * scaleX, page.Width),
                Clamp(bottom * scaleY, page.Height));
            // Content is Markdown, where a single line break is a soft wrap, so line breaks become spaces.
            var text = block.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? string.Join(' ', c.GetString()!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                : "";
            if (elementType == ElementType.H1)
            {
                (elementType, text) = Heading(text);
            }

            elements.Add(new NormalizedElement($"p{page.PageNumber}e{index}", elementType, bbox, 0, confidence, text, []));
        }

        return elements;
    }

    /// <summary>
    /// Block content is Markdown, so a title reads "## Opening hours": the number of leading '#' is the heading level
    /// (capped at H6) and the text is what follows. A title with no marker is an H1.
    /// </summary>
    private static (ElementType Type, string Text) Heading(string content)
    {
        var level = content.TakeWhile(ch => ch == '#').Count();
        if (level == 0 || level == content.Length || content[level] != ' ')
        {
            return (ElementType.H1, content);
        }

        return (ElementType.H1 + Math.Min(level, 6) - 1, content[(level + 1)..]);
    }

    private static List<NormalizedElement> Link(List<NormalizedElement> elements, ReaderPage page, IEnumerable<SourceObject> sourceObjects, List<ReaderDiagnostic> warnings)
    {
        var claims = elements.Select(_ => new List<string>()).ToList();
        foreach (var source in sourceObjects)
        {
            if (source.Bounds is not { } bounds)
            {
                continue;
            }

            var best = -1;
            var bestOverlap = MinimumOverlap;
            for (var i = 0; i < elements.Count; i++)
            {
                var overlap = Overlap(bounds, elements[i].Bbox);
                if (overlap >= bestOverlap && (best < 0 || overlap > bestOverlap))
                {
                    (best, bestOverlap) = (i, overlap);
                }
            }

            if (best >= 0)
            {
                claims[best].Add(source.Id);
            }
        }

        var linked = new List<NormalizedElement>();
        for (var i = 0; i < elements.Count; i++)
        {
            if (claims[i].Count == 0)
            {
                warnings.Add(new("READER_ELEMENT_UNLINKED", page.PageNumber, $"Element {elements[i].Id} ('{elements[i].Text}') covers no Source object; left out."));
                continue;
            }

            linked.Add(elements[i] with { ReadingOrder = linked.Count + 1, SourceObjects = claims[i] });
        }

        return linked;
    }

    /// <summary>
    /// The share of the Source object inside the block. A Source object with no area, like a horizontal rule,
    /// counts as inside when its centre is.
    /// </summary>
    private static double Overlap(BoundingBox source, BoundingBox block)
    {
        var area = (source.Right - source.Left) * (source.Bottom - source.Top);
        if (area <= 0)
        {
            var x = (source.Left + source.Right) / 2;
            var y = (source.Top + source.Bottom) / 2;
            return x >= block.Left && x <= block.Right && y >= block.Top && y <= block.Bottom ? 1 : 0;
        }

        var width = Math.Min(source.Right, block.Right) - Math.Max(source.Left, block.Left);
        var height = Math.Min(source.Bottom, block.Bottom) - Math.Max(source.Top, block.Top);
        return width <= 0 || height <= 0 ? 0 : width * height / area;
    }

    private static double? BlockTypeConfidence(JsonElement block, string path)
    {
        if (!block.TryGetProperty("confidence_scores", out var scores) || scores.ValueKind != JsonValueKind.Object
            || !scores.TryGetProperty("block_type_confidence_score", out var score) || score.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return score.ValueKind == JsonValueKind.Number && score.GetDouble() is >= 0 and <= 1 and var value
            ? value
            : throw Malformed($"{path}.confidence_scores.block_type_confidence_score", "Confidence must be a number from 0 to 1.");
    }

    private static double Number(JsonElement json, string name, string path) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : throw Malformed($"{path}.{name}", $"{name} is missing or not a number.");

    private static double Clamp(double value, double max) => Math.Round(Math.Clamp(value, 0, max), 2);

    private static ReaderException Malformed(string path, string message) =>
        new("READER_RESPONSE_MALFORMED", path.Length == 0 ? message : $"{path}: {message}");
}
