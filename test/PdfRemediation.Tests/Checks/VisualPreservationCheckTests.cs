using PdfRemediation.Infrastructure.Checks;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace PdfRemediation.Tests.Checks;

public class VisualPreservationCheckTests
{
    [Fact]
    public void Identical_pdfs_have_no_differing_pixels()
    {
        var blank = Letter(drawSquare: false);

        var result = new VisualPreservationCheck(dpi: 72).Compare(blank, blank, diffImageFolder: null);

        Assert.True(result.Passed);
        var page = Assert.Single(result.Pages);
        Assert.Equal(0, page.MismatchedPixels);
        Assert.Equal(0.0, page.MismatchRatio);
        Assert.Null(page.DiffImagePath);
    }

    [Fact]
    public void A_visible_change_is_counted_and_gets_a_diff_image()
    {
        var folder = Directory.CreateTempSubdirectory("visual-diff-").FullName;

        var result = new VisualPreservationCheck(dpi: 72).Compare(Letter(drawSquare: false), Letter(drawSquare: true), folder);

        Assert.False(result.Passed);
        var page = Assert.Single(result.Pages);
        // A US Letter page at 72 dpi renders at 612 x 792 pixels; the square covers about 100 x 100 of them.
        Assert.Equal(612L * 792, page.TotalPixels);
        Assert.InRange(page.MismatchedPixels, 10_000, 10_500);
        Assert.Equal((double)page.MismatchedPixels / page.TotalPixels, page.MismatchRatio);
        Assert.True(File.Exists(page.DiffImagePath));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(page.DiffImagePath!)[..4]); // PNG signature
    }

    [Fact]
    public void A_different_page_count_fails()
    {
        var onePage = Letter(drawSquare: false);
        var builder = new PdfDocumentBuilder();
        builder.AddPage(612, 792);
        builder.AddPage(612, 792);

        var result = new VisualPreservationCheck(dpi: 72).Compare(onePage, builder.Build(), diffImageFolder: null);

        Assert.False(result.Passed);
    }

    private static byte[] Letter(bool drawSquare)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        if (drawSquare)
        {
            page.DrawRectangle(new PdfPoint(100, 100), 100, 100, lineWidth: 0, fill: true);
        }

        return builder.Build();
    }
}
