using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace PdfRemediation.Infrastructure.Checks;

/// <summary>How one page of the output compares with the same page of the original.</summary>
public sealed record PageVisualDiff(int PageNumber, long MismatchedPixels, long TotalPixels, double MismatchRatio, string? DiffImagePath);

/// <summary>Visual preservation holds only when both PDFs have the same pages and no pixel differs on any of them.</summary>
public sealed record VisualPreservationResult(bool Passed, IReadOnlyList<PageVisualDiff> Pages, string? Failure);

/// <summary>
/// The Visual preservation check: renders both PDFs with the pinned PDFium (via PDFtoImage) at one DPI
/// and compares the pages byte for byte.
/// </summary>
public sealed class VisualPreservationCheck(int dpi)
{
    private static readonly SKColor DiffColor = new(255, 0, 0);

    public VisualPreservationResult Compare(byte[] original, byte[] output, string? diffImageFolder)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return Render(original, output, diffImageFolder);
        }

        throw new PlatformNotSupportedException("PDFium rendering needs Windows, Linux or macOS.");
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private VisualPreservationResult Render(byte[] original, byte[] output, string? diffImageFolder)
    {
        var originalPages = Conversion.GetPageCount(original);
        var outputPages = Conversion.GetPageCount(output);
        if (originalPages != outputPages)
        {
            return new VisualPreservationResult(false, [], $"The original has {originalPages} pages but the output has {outputPages}.");
        }

        var options = new RenderOptions(Dpi: dpi, WithAnnotations: true, BackgroundColor: SKColors.White);
        var pages = new List<PageVisualDiff>();
        for (var index = 0; index < originalPages; index++)
        {
            using var before = Conversion.ToImage(original, index, options: options);
            using var after = Conversion.ToImage(output, index, options: options);
            pages.Add(ComparePage(index + 1, before, after, diffImageFolder));
        }

        return new VisualPreservationResult(pages.All(p => p.MismatchedPixels == 0), pages, null);
    }

    private static PageVisualDiff ComparePage(int pageNumber, SKBitmap before, SKBitmap after, string? diffImageFolder)
    {
        long total = (long)before.Width * before.Height;
        if (before.Width != after.Width || before.Height != after.Height)
        {
            return new PageVisualDiff(pageNumber, total, total, 1.0, null);
        }

        var beforePixels = before.Pixels;
        var afterPixels = after.Pixels;
        var mismatched = new List<int>();
        for (var i = 0; i < beforePixels.Length; i++)
        {
            if (beforePixels[i] != afterPixels[i])
            {
                mismatched.Add(i);
            }
        }

        string? diffPath = null;
        if (mismatched.Count > 0 && diffImageFolder is not null)
        {
            diffPath = Path.Combine(diffImageFolder, $"page-{pageNumber:D4}-diff.png");
            WriteDiffImage(after, mismatched, diffPath);
        }

        return new PageVisualDiff(pageNumber, mismatched.Count, total, (double)mismatched.Count / total, diffPath);
    }

    /// <summary>Writes the output page, faded, with every differing pixel in red.</summary>
    private static void WriteDiffImage(SKBitmap after, List<int> mismatched, string path)
    {
        using var diff = new SKBitmap(after.Width, after.Height);
        var pixels = after.Pixels.Select(p => new SKColor(Fade(p.Red), Fade(p.Green), Fade(p.Blue))).ToArray();
        foreach (var i in mismatched)
        {
            pixels[i] = DiffColor;
        }

        diff.Pixels = pixels;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        diff.Encode(file, SKEncodedImageFormat.Png, 100);

        static byte Fade(byte channel) => (byte)(255 - (255 - channel) / 4);
    }
}
