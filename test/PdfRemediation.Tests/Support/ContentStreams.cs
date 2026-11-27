using System.IO.Compression;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.MarkedContent;
using UglyToad.PdfPig.Tokens;

namespace PdfRemediation.Tests.Support;

/// <summary>Reads pages' decoded content streams and operations straight from a PDF, without the writer's code.</summary>
public static class ContentStreams
{
    /// <summary>Each page's content streams, decoded, in page order.</summary>
    public static IReadOnlyList<IReadOnlyList<byte[]>> Decoded(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        IToken Resolve(IToken token) => token is IndirectReferenceToken r ? document.Structure.GetObject(r.Data).Data : token;

        return document.GetPages().Select(page =>
        {
            var contents = Resolve(page.Dictionary.Data["Contents"]);
            var streams = contents is ArrayToken array ? array.Data.Select(Resolve).Cast<StreamToken>() : [(StreamToken)contents];
            return (IReadOnlyList<byte[]>)streams.Select(Inflate).ToList();
        }).ToList();
    }

    /// <summary>Each page's operations as PdfPig parses them, with marked-content operators left out.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> OperationsWithoutMarkedContent(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages()
            .Select(page => (IReadOnlyList<string>)page.Operations
                .Where(o => o is not (BeginMarkedContent or BeginMarkedContentWithProperties or EndMarkedContent))
                .Select(Describe)
                .ToList())
            .ToList();
    }

    /// <summary>True when <paramref name="inner"/> can be made from <paramref name="outer"/> by deleting bytes only.</summary>
    public static bool IsSubsequence(byte[] inner, byte[] outer)
    {
        var i = 0;
        foreach (var b in outer)
        {
            if (i < inner.Length && inner[i] == b)
            {
                i++;
            }
        }

        return i == inner.Length;
    }

    private static string Describe(IGraphicsStateOperation operation)
    {
        using var writer = new MemoryStream();
        operation.Write(writer);
        return System.Text.Encoding.Latin1.GetString(writer.ToArray()).Trim();
    }

    private static byte[] Inflate(StreamToken stream)
    {
        if (!stream.StreamDictionary.ContainsKey(NameToken.Filter))
        {
            return stream.Data.ToArray();
        }

        using var input = new ZLibStream(new MemoryStream(stream.Data.ToArray()), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
