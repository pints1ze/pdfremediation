using System.Runtime.Versioning;
using PdfRemediation.Infrastructure.Reading;
using PDFtoImage;
using SkiaSharp;

namespace PdfRemediation.Infrastructure.Pdf;

/// <summary>Renders every page to PNG with the pinned PDFium, the same renderer the Visual preservation check uses.</summary>
public static class PageRenderer
{
    public static IReadOnlyList<ReaderPage> Render(byte[] pdf, int dpi)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return RenderPages(pdf, dpi);
        }

        throw new PlatformNotSupportedException("PDFium rendering needs Windows, Linux or macOS.");
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static List<ReaderPage> RenderPages(byte[] pdf, int dpi)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var options = new RenderOptions(Dpi: dpi, WithAnnotations: true, BackgroundColor: SKColors.White);
        var pages = new List<ReaderPage>();
        for (var number = 1; number <= document.NumberOfPages; number++)
        {
            var page = document.GetPage(number);
            using var bitmap = Conversion.ToImage(pdf, number - 1, options: options);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            pages.Add(new ReaderPage(number, page.Width, page.Height, png.ToArray()));
        }

        return pages;
    }
}
