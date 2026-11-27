using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace PdfRemediation.Infrastructure.SourceObjects;

public enum SourceObjectKind
{
    Text,
    Vector,
}

/// <summary>
/// One painting operation in the original PDF. Its ID names the page, the content stream on that page,
/// and the operation's byte range in the decoded stream: <c>p{page}:s{stream}:{start}-{end}</c>.
/// </summary>
public sealed record SourceObject(
    string Id,
    int PageNumber,
    int StreamIndex,
    int Start,
    int End,
    SourceObjectKind Kind,
    string? Text,
    BoundingBox? Bounds);

/// <summary>
/// Every Source object in a PDF, in content order, with the geometry it paints.
/// Text-showing and path-painting operators are indexed; images and Form XObjects are not yet.
/// </summary>
public sealed class SourceObjectIndex
{
    private const string ProbeTag = "PdfRemediationSource";

    private readonly Dictionary<string, SourceObject> byId;

    private SourceObjectIndex(IReadOnlyList<SourceObject> all)
    {
        All = all;
        byId = all.ToDictionary(o => o.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<SourceObject> All { get; }

    public SourceObject? Find(string id) => byId.GetValueOrDefault(id);

    public static SourceObjectIndex Build(byte[] pdf)
    {
        using var file = PdfFile.Open(pdf);
        var objects = Scan(file);
        return new SourceObjectIndex(AddGeometry(file, objects));
    }

    /// <summary>Finds every Source object's operator byte range, without geometry.</summary>
    internal static IReadOnlyList<SourceObject> Scan(PdfFile file)
    {
        var objects = new List<SourceObject>();
        foreach (var page in file.Pages)
        {
            foreach (var stream in page.ContentStreams)
            {
                int? pathStart = null;
                foreach (var operation in ContentStreamLexer.Lex(stream.Decoded))
                {
                    switch (operation.Operator)
                    {
                        case "m" or "l" or "c" or "v" or "y" or "h" or "re":
                            pathStart ??= operation.Start;
                            break;
                        case "W" or "W*":
                            break;
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            Add(SourceObjectKind.Vector, pathStart ?? operation.Start, operation.End);
                            pathStart = null;
                            break;
                        case "n":
                            pathStart = null;
                            break;
                        case "sh":
                            Add(SourceObjectKind.Vector, operation.Start, operation.End);
                            break;
                        case "Tj" or "TJ" or "'" or "\"":
                            Add(SourceObjectKind.Text, operation.Start, operation.End);
                            break;
                    }
                }

                void Add(SourceObjectKind kind, int start, int end) =>
                    objects.Add(new SourceObject($"p{page.PageNumber}:s{stream.Index}:{start}-{end}", page.PageNumber, stream.Index, start, end, kind, null, null));
            }
        }

        return objects;
    }

    /// <summary>
    /// Measures what each Source object paints by wrapping each one in its own marked-content sequence
    /// in a scratch copy, then letting PdfPig report the letters and paths inside each sequence.
    /// </summary>
    private static IReadOnlyList<SourceObject> AddGeometry(PdfFile file, IReadOnlyList<SourceObject> objects)
    {
        var update = new IncrementalUpdate(file);
        var probeIds = new Dictionary<(int Page, int Mcid), int>();
        foreach (var page in file.Pages)
        {
            var mcid = 0;
            foreach (var stream in page.ContentStreams)
            {
                var wraps = new List<MarkedContentWrap>();
                for (var i = 0; i < objects.Count; i++)
                {
                    var o = objects[i];
                    if (o.PageNumber == page.PageNumber && o.StreamIndex == stream.Index)
                    {
                        probeIds[(page.PageNumber, mcid)] = i;
                        wraps.Add(MarkedContentWrap.Tagged(o.Start, o.End, ProbeTag, mcid++));
                    }
                }

                update.Set(stream.Reference, stream.WithMarkedContent(wraps));
            }
        }

        var measured = objects.ToArray();
        using var probe = PdfDocument.Open(update.Build());
        foreach (var page in probe.GetPages())
        {
            var mediaBox = page.MediaBox.Bounds;
            foreach (var element in Flatten(page.GetMarkedContents()).Where(e => e.Tag == ProbeTag))
            {
                if (!probeIds.TryGetValue((page.Number, element.MarkedContentIdentifier), out var i))
                {
                    continue;
                }

                var rectangles = element.Letters.Select(l => l.BoundingBox)
                    .Concat(element.Paths.Select(p => p.GetBoundingRectangle()).OfType<PdfRectangle>())
                    .ToList();
                var text = measured[i].Kind == SourceObjectKind.Text ? string.Concat(element.Letters.Select(l => l.Value)) : null;
                measured[i] = measured[i] with { Text = text, Bounds = ToTopLeft(rectangles, mediaBox) };
            }
        }

        return measured;
    }

    private static IEnumerable<MarkedContentElement> Flatten(IEnumerable<MarkedContentElement> elements) =>
        elements.SelectMany(e => Flatten(e.Children).Prepend(e));

    private static BoundingBox? ToTopLeft(IReadOnlyList<PdfRectangle> rectangles, PdfRectangle mediaBox)
    {
        if (rectangles.Count == 0)
        {
            return null;
        }

        return new BoundingBox(
            rectangles.Min(r => r.Left) - mediaBox.Left,
            mediaBox.Top - rectangles.Max(r => r.Top),
            rectangles.Max(r => r.Right) - mediaBox.Left,
            mediaBox.Top - rectangles.Min(r => r.Bottom));
    }
}
