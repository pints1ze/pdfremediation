using System.IO.Compression;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.CrossReference;
using UglyToad.PdfPig.Tokens;

namespace PdfRemediation.Infrastructure.Pdf;

/// <summary>A page's object reference, its raw dictionary and its decoded content streams.</summary>
internal sealed record PdfPageObject(int PageNumber, IndirectReference Reference, DictionaryToken Dictionary, IReadOnlyList<ContentStream> ContentStreams);

/// <summary>One content stream of a page: where it lives, its dictionary, and its decoded bytes.</summary>
internal sealed record ContentStream(int Index, IndirectReference Reference, DictionaryToken Dictionary, byte[] Decoded)
{
    /// <summary>This stream with marked content inserted, re-encoded with its original filter.</summary>
    public StreamToken WithMarkedContent(IEnumerable<MarkedContentWrap> wraps) => Reencode(MarkedContentSplice.Apply(Decoded, wraps));

    private StreamToken Reencode(byte[] decoded)
    {
        var encoded = IsFlate(Dictionary) ? Deflate(decoded) : decoded;
        return new StreamToken(Dictionary.With(NameToken.Length, new NumericToken(encoded.Length)), encoded);
    }

    internal static byte[] Decode(DictionaryToken dictionary, ReadOnlyMemory<byte> raw)
    {
        if (dictionary.TryGet(NameToken.DecodeParms, out var parms) && parms is not NullToken)
        {
            throw new PdfProcessingException("UNSUPPORTED_STREAM_FILTER", "Content streams with DecodeParms are not supported yet.");
        }

        if (!IsFlate(dictionary))
        {
            return raw.ToArray();
        }

        using var input = new ZLibStream(new MemoryStream(raw.ToArray()), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static bool IsFlate(DictionaryToken dictionary)
    {
        if (!dictionary.TryGet(NameToken.Filter, out var filter) || filter is NullToken)
        {
            return false;
        }

        var names = filter switch
        {
            NameToken name => [name.Data],
            ArrayToken array => array.Data.Select(t => (t as NameToken)?.Data).ToArray(),
            _ => [null],
        };

        return names switch
        {
            [] => false,
            ["FlateDecode"] => true,
            _ => throw new PdfProcessingException("UNSUPPORTED_STREAM_FILTER", $"Content stream filter {filter} is not supported yet; only FlateDecode or no filter."),
        };
    }

    private static byte[] Deflate(byte[] decoded)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(decoded);
        }

        return output.ToArray();
    }
}

/// <summary>
/// Raw, object-level access to a PDF: the catalog, the page tree and decoded content streams,
/// with the references needed to replace objects in an incremental update.
/// </summary>
internal sealed class PdfFile : IDisposable
{
    private readonly PdfDocument document;

    private PdfFile(byte[] bytes, PdfDocument document, IReadOnlyList<PdfPageObject> pages)
    {
        Bytes = bytes;
        this.document = document;
        Pages = pages;
    }

    public byte[] Bytes { get; }

    public IReadOnlyList<PdfPageObject> Pages { get; }

    public IndirectReference CatalogReference => document.Structure.Trailer.Root;

    public DictionaryToken Catalog => document.Structure.Catalog.CatalogDictionary;

    public TrailerDictionary Trailer => document.Structure.Trailer;

    public bool UsesCrossReferenceStreams => document.Structure.CrossReferenceTable.Parts.Any(p => p.Type == CrossReferenceType.Stream);

    public static PdfFile Open(byte[] bytes)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes);
        }
        catch (Exception ex) when (ex is not PdfProcessingException)
        {
            throw new PdfProcessingException("PDF_UNREADABLE", $"PdfPig could not open the PDF: {ex.Message}");
        }

        try
        {
            if (document.IsEncrypted)
            {
                throw new PdfProcessingException("PDF_ENCRYPTED", "Encrypted PDFs are not supported.");
            }

            var pages = new List<PdfPageObject>();
            CollectPages(document, document.Structure.Catalog.CatalogDictionary.Data["Pages"], pages);
            return new PdfFile(bytes, document, pages);
        }
        catch (Exception ex) when (ex is InvalidCastException or KeyNotFoundException or InvalidDataException)
        {
            document.Dispose();
            throw new PdfProcessingException("PDF_UNREADABLE", $"The PDF's page tree or content streams could not be read: {ex.Message}");
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>Follows an indirect reference (or returns a direct token unchanged).</summary>
    public IToken Resolve(IToken token) => Resolve(document, token);

    public void Dispose() => document.Dispose();

    private static IToken Resolve(PdfDocument document, IToken token) =>
        token is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data).Data : token;

    private static void CollectPages(PdfDocument document, IToken node, List<PdfPageObject> pages)
    {
        if (node is not IndirectReferenceToken reference || Resolve(document, node) is not DictionaryToken dictionary)
        {
            throw new PdfProcessingException("PDF_UNREADABLE", "The page tree has a node that is not an indirect dictionary.");
        }

        if (dictionary.TryGet(NameToken.Kids, out var kids))
        {
            foreach (var kid in ((ArrayToken)Resolve(document, kids)).Data)
            {
                CollectPages(document, kid, pages);
            }

            return;
        }

        var streams = new List<ContentStream>();
        if (dictionary.TryGet(NameToken.Contents, out var contents))
        {
            var references = contents switch
            {
                IndirectReferenceToken single when Resolve(document, single) is StreamToken => [single],
                _ => ((ArrayToken)Resolve(document, contents)).Data.Cast<IndirectReferenceToken>().ToArray(),
            };

            foreach (var streamReference in references)
            {
                var stream = (StreamToken)Resolve(document, streamReference);
                streams.Add(new ContentStream(streams.Count, streamReference.Data, stream.StreamDictionary, ContentStream.Decode(stream.StreamDictionary, stream.Data)));
            }
        }

        pages.Add(new PdfPageObject(pages.Count + 1, reference.Data, dictionary, streams));
    }
}
