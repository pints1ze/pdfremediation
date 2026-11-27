using PdfRemediation.Infrastructure.SourceObjects;
using PdfRemediation.Tests.Support;

namespace PdfRemediation.Tests.SourceObjects;

public class SourceObjectIndexTests
{
    // The fixture's single content stream draws 38 words with one Tj each, then one stroked rule.
    // See test/fixtures/make-fixtures.cs for what is drawn where.
    private static readonly SourceObjectIndex Index = SourceObjectIndex.Build(Fixtures.Bytes("headings-and-paragraphs.pdf"));

    [Fact]
    public void Every_painting_operator_gets_a_source_object()
    {
        Assert.Equal(38, Index.All.Count(o => o.Kind == SourceObjectKind.Text));
        Assert.Equal(1, Index.All.Count(o => o.Kind == SourceObjectKind.Vector));
        Assert.All(Index.All, o => Assert.Equal(1, o.PageNumber));
    }

    [Fact]
    public void A_source_object_id_names_its_page_stream_and_operator_byte_range()
    {
        // "BT\n/F1 24 Tf\n72 700 Td\n" is 23 bytes; "<01020304050603> Tj" follows and is 19 bytes long.
        var first = Index.All[0];

        Assert.Equal("p1:s0:23-42", first.Id);
        Assert.Equal("Harbour", first.Text);
        Assert.Same(first, Index.Find("p1:s0:23-42"));
        Assert.Null(Index.Find("p1:s0:24-42"));
    }

    [Fact]
    public void Text_geometry_is_where_the_fixture_drew_it()
    {
        // "Harbour" is drawn at 24pt with its baseline at y=700 on a 792pt page: 92pt from the top in top-left coordinates.
        // It has no descenders, so the glyphs sit on the baseline and rise less than the font size above it.
        var bounds = Index.All[0].Bounds!.Value;

        Assert.InRange(bounds.Left, 72, 76); // the origin plus the glyph's left side bearing
        Assert.InRange(bounds.Bottom, 91, 93);
        Assert.InRange(bounds.Top, 92 - 24, 92 - 10);
        Assert.InRange(bounds.Right, 120, 200);
    }

    [Fact]
    public void Vector_geometry_is_where_the_fixture_drew_it()
    {
        // The rule runs from (72, 60) to (540, 60): 732pt from the top.
        var rule = Assert.Single(Index.All, o => o.Kind == SourceObjectKind.Vector);
        var bounds = rule.Bounds!.Value;

        Assert.Equal(72, bounds.Left, 0.01);
        Assert.Equal(540, bounds.Right, 0.01);
        Assert.Equal(732, bounds.Top, 0.5);
        Assert.Equal(732, bounds.Bottom, 0.5);
    }
}
