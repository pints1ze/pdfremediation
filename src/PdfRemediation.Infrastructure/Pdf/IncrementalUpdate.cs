using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;

namespace PdfRemediation.Infrastructure.Pdf;

/// <summary>
/// Appends new and replaced objects to a PDF as an incremental update, leaving the original bytes untouched.
/// </summary>
internal sealed class IncrementalUpdate
{
    private readonly PdfFile file;
    private readonly SortedDictionary<long, (IndirectReference Reference, IToken Token)> objects = new();
    private long nextObjectNumber;

    public IncrementalUpdate(PdfFile file)
    {
        if (file.UsesCrossReferenceStreams)
        {
            throw new PdfProcessingException("UNSUPPORTED_XREF_STREAM", "PDFs whose cross-reference data is in streams are not supported yet.");
        }

        this.file = file;
        Info = file.Trailer.Info;
        nextObjectNumber = file.Trailer.Size;
    }

    /// <summary>The document information dictionary the updated trailer points to; the original's unless replaced.</summary>
    public IToken? Info { get; set; }

    public IndirectReference Allocate() => new(nextObjectNumber++, 0);

    public void Set(IndirectReference reference, IToken token) => objects[reference.ObjectNumber] = (reference, token);

    public byte[] Build()
    {
        using var output = new MemoryStream();
        output.Write(file.Bytes);
        if (file.Bytes.Length > 0 && file.Bytes[^1] is not ((byte)'\n' or (byte)'\r'))
        {
            output.WriteByte((byte)'\n');
        }

        var offsets = new SortedDictionary<long, (long Offset, int Generation)>();
        foreach (var (number, (reference, token)) in objects)
        {
            offsets[number] = (output.Position, reference.Generation);
            Write(output, $"{number} {reference.Generation} obj\n");
            TokenWriter.Instance.WriteToken(token, output);
            Write(output, "\nendobj\n");
        }

        var xrefOffset = output.Position;
        Write(output, "xref\n");
        foreach (var run in ConsecutiveRuns(offsets.Keys))
        {
            Write(output, $"{run[0]} {run.Count}\n");
            foreach (var number in run)
            {
                var (offset, generation) = offsets[number];
                Write(output, $"{offset:D10} {generation:D5} n \n");
            }
        }

        var trailer = new Dictionary<NameToken, IToken>
        {
            [NameToken.Size] = new NumericToken(Math.Max(nextObjectNumber, file.Trailer.Size)),
            [NameToken.Root] = new IndirectReferenceToken(file.CatalogReference),
            [NameToken.Prev] = new NumericToken(LastStartXref(file.Bytes)),
        };
        if (Info is { } info)
        {
            trailer[NameToken.Info] = info;
        }

        if (file.Trailer.Identifier is { Count: > 0 } identifier)
        {
            trailer[NameToken.Id] = new ArrayToken(identifier.Cast<IToken>().ToArray());
        }

        Write(output, "trailer\n");
        TokenWriter.Instance.WriteToken(new DictionaryToken(trailer), output);
        Write(output, $"\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static IEnumerable<List<long>> ConsecutiveRuns(IEnumerable<long> numbers)
    {
        var run = new List<long>();
        foreach (var number in numbers)
        {
            if (run.Count > 0 && number != run[^1] + 1)
            {
                yield return run;
                run = [];
            }

            run.Add(number);
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    /// <summary>The offset the original file's last <c>startxref</c> points to, which the update's trailer chains back to.</summary>
    private static long LastStartXref(byte[] bytes)
    {
        var tail = bytes.AsSpan(Math.Max(0, bytes.Length - 2048));
        var at = tail.LastIndexOf("startxref"u8);
        if (at < 0)
        {
            throw new PdfProcessingException("PDF_UNREADABLE", "The PDF has no startxref.");
        }

        var digits = Encoding.ASCII.GetString(tail[(at + "startxref".Length)..]).TrimStart().TakeWhile(char.IsAsciiDigit);
        return long.Parse(string.Concat(digits));
    }

    private static void Write(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
}
