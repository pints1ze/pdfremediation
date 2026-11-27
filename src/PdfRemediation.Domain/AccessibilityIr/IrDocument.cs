namespace PdfRemediation.Domain.AccessibilityIr;

/// <summary>
/// The Accessibility IR v1 for one Document. Only <see cref="IrParser"/> creates it,
/// so holding one means the IR has already passed validation.
/// The structure tree always has an implicit Document root; page Elements sit under it in reading order.
/// </summary>
public sealed class IrDocument
{
    internal IrDocument(string title, string language, IReadOnlyList<IrPage> pages)
    {
        Title = title;
        Language = language;
        Pages = pages;
    }

    public string Title { get; }

    /// <summary>BCP 47 language tag for the Document, e.g. <c>en-GB</c>.</summary>
    public string Language { get; }

    /// <summary>Pages in page-number order.</summary>
    public IReadOnlyList<IrPage> Pages { get; }
}

public sealed class IrPage
{
    internal IrPage(int pageNumber, double width, double height, IReadOnlyList<IrElement> elements)
    {
        PageNumber = pageNumber;
        Width = width;
        Height = height;
        Elements = elements;
    }

    /// <summary>1-based page number.</summary>
    public int PageNumber { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>Elements in reading order.</summary>
    public IReadOnlyList<IrElement> Elements { get; }
}

public sealed class IrElement
{
    internal IrElement(string id, ElementType type, BoundingBox bbox, int readingOrder, double? confidence, string? text, IReadOnlyList<string> sourceObjects)
    {
        Id = id;
        Type = type;
        Bbox = bbox;
        ReadingOrder = readingOrder;
        Confidence = confidence;
        Text = text;
        SourceObjects = sourceObjects;
    }

    public string Id { get; }

    public ElementType Type { get; }

    public BoundingBox Bbox { get; }

    /// <summary>Position in the page's reading order; unique within the page.</summary>
    public int ReadingOrder { get; }

    public double? Confidence { get; }

    public string? Text { get; }

    /// <summary>IDs of the Source objects this Element describes.</summary>
    public IReadOnlyList<string> SourceObjects { get; }
}

public enum ElementType
{
    H1,
    H2,
    H3,
    H4,
    H5,
    H6,
    P,
    Artifact,
}

/// <summary>A rectangle in PDF points with a top-left origin, as readers report it.</summary>
public readonly record struct BoundingBox(double Left, double Top, double Right, double Bottom);
