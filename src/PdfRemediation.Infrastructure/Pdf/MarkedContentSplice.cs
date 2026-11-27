using System.Text;

namespace PdfRemediation.Infrastructure.Pdf;

/// <summary>Marked-content operators to wrap around one byte range of a decoded content stream.</summary>
internal readonly record struct MarkedContentWrap(int Start, int End, string Open)
{
    public static MarkedContentWrap Tagged(int start, int end, string tag, int mcid) =>
        new(start, end, $"/{tag} <</MCID {mcid}>> BDC\n");

    public static MarkedContentWrap Artifact(int start, int end) => new(start, end, "/Artifact BMC\n");
}

/// <summary>
/// Inserts marked-content operators into a decoded content stream. The original bytes are kept as they are;
/// the result differs from the input only by the inserted operators.
/// </summary>
internal static class MarkedContentSplice
{
    private static readonly byte[] Close = Encoding.ASCII.GetBytes("\nEMC");

    public static byte[] Apply(byte[] decoded, IEnumerable<MarkedContentWrap> wraps)
    {
        using var output = new MemoryStream(decoded.Length + 64);
        var position = 0;
        foreach (var wrap in wraps.OrderBy(w => w.Start))
        {
            if (wrap.Start < position || wrap.End > decoded.Length || wrap.End <= wrap.Start)
            {
                throw new InvalidOperationException($"Marked-content wraps overlap or fall outside the stream at {wrap.Start}-{wrap.End}.");
            }

            output.Write(decoded, position, wrap.Start - position);
            output.Write(Encoding.ASCII.GetBytes(wrap.Open));
            output.Write(decoded, wrap.Start, wrap.End - wrap.Start);
            output.Write(Close);
            position = wrap.End;
        }

        output.Write(decoded, position, decoded.Length - position);
        return output.ToArray();
    }
}
