using System.Globalization;
using System.Text.Json.Nodes;
using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.Checks;
using PdfRemediation.Infrastructure.TagWriting;
using PdfRemediation.Tests.Support;

namespace PdfRemediation.Tests.TagWriting;

/// <summary>Seam 2: a fixture PDF plus hand-written IR in, a tagged PDF plus diagnostics out.</summary>
public class TagWriterTests
{
    private static readonly byte[] Original = Fixtures.Bytes("headings-and-paragraphs.pdf");
    private static readonly TagWriterResult Result = new PdfPigTagWriter().Write(Original, LoadIr("headings-and-paragraphs.ir.json"));

    [Fact]
    public void Every_element_is_applied()
    {
        Assert.Empty(Result.Diagnostics.Errors);
        Assert.Equal(5, Result.Diagnostics.ElementsApplied);
        Assert.Equal(0, Result.Diagnostics.ElementsSkipped);
    }

    [Fact]
    public void The_original_bytes_are_an_unchanged_prefix_of_the_output()
    {
        var output = Assert.IsType<byte[]>(Result.TaggedPdf);

        Assert.True(output.Length > Original.Length);
        Assert.True(output.AsSpan(0, Original.Length).SequenceEqual(Original));
    }

    [Fact]
    public void The_structure_tree_reads_back_with_each_element_in_reading_order()
    {
        var tree = TaggedPdfReader.Read(Result.TaggedPdf!);

        Assert.Equal("Document", tree.RootType);
        Assert.Equal(
            [
                ("H1", "Harbour Library Annual Report"),
                ("P", "The library served more readers this year than in any year before.Opening hours were extended on weekday evenings."),
                ("H2", "Opening hours"),
                ("P", "The library is open from nine until eight, Monday to Saturday."),
            ],
            tree.Elements.Select(e => (e.Type, e.Text)));
        Assert.All(tree.Elements, e => Assert.Equal(1, e.PageNumber));
    }

    [Fact]
    public void Each_mcid_is_used_once_and_the_parent_tree_maps_it_back_to_its_element()
    {
        var tree = TaggedPdfReader.Read(Result.TaggedPdf!);

        // 36 words are tagged: 4 + 19 + 2 + 11.
        Assert.Equal(Enumerable.Range(0, 36), tree.Elements.SelectMany(e => e.Mcids).Order());
        Assert.True(tree.ParentTreeMatches);
    }

    [Fact]
    public void Content_no_element_tags_is_marked_as_an_artifact()
    {
        var tree = TaggedPdfReader.Read(Result.TaggedPdf!);

        // The footer is an Artifact Element in the IR; the rule above it belongs to no Element.
        Assert.Equal(["<path>", "Page", " 1"], tree.ArtifactContent);
        Assert.Empty(tree.UnmarkedText);
    }

    [Fact]
    public void Decoded_content_streams_differ_from_the_input_only_by_inserted_marked_content()
    {
        var before = ContentStreams.Decoded(Original);
        var after = ContentStreams.Decoded(Result.TaggedPdf!);

        Assert.Equal(before.Count, after.Count);
        for (var page = 0; page < before.Count; page++)
        {
            Assert.Equal(before[page].Count, after[page].Count);
            for (var stream = 0; stream < before[page].Count; stream++)
            {
                Assert.True(ContentStreams.IsSubsequence(before[page][stream], after[page][stream]), $"Page {page + 1} stream {stream} lost or changed original bytes.");
            }
        }

        Assert.Equal(ContentStreams.OperationsWithoutMarkedContent(Original), ContentStreams.OperationsWithoutMarkedContent(Result.TaggedPdf!));
    }

    [Fact]
    public void The_output_renders_with_zero_differing_pixels_on_every_page()
    {
        var result = new VisualPreservationCheck(dpi: 150).Compare(Original, Result.TaggedPdf!, diffImageFolder: null);

        Assert.True(result.Passed);
        Assert.All(result.Pages, page => Assert.Equal("0.0000", page.MismatchRatio.ToString("0.0000", CultureInfo.InvariantCulture)));
    }

    [Fact]
    [Trait("Category", Docker.Category)]
    public async Task The_output_passes_veraPDF_pdf_ua_1()
    {
        var report = await Docker.VeraPdf.ValidateAsync(Result.TaggedPdf!);

        Assert.Empty(report.Failures);
        Assert.True(report.Passed);
    }

    [Fact]
    public void The_title_and_language_come_from_the_ir()
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(Result.TaggedPdf!);

        Assert.Equal("Harbour Library Annual Report", document.Information.Title);
        Assert.True(document.TryGetXmpMetadata(out var xmp));
        var dcTitle = xmp.GetXDocument().Descendants(System.Xml.Linq.XName.Get("title", "http://purl.org/dc/elements/1.1/")).Single();
        Assert.Equal("Harbour Library Annual Report", dcTitle.Value);
        var lang = (UglyToad.PdfPig.Tokens.StringToken)document.Structure.Catalog.CatalogDictionary.Data["Lang"];
        Assert.Equal("en-GB", lang.Data);
    }

    [Theory]
    [InlineData("p1:s0:9999-10010", "SOURCE_OBJECT_NOT_FOUND")]
    [InlineData("p2:s0:23-42", "SOURCE_OBJECT_NOT_FOUND")] // names a page the Element isn't on
    [InlineData("p1:s0:23-42", "SOURCE_OBJECT_CLAIMED_TWICE")] // already part of the H1
    public void An_element_whose_source_objects_cannot_be_used_is_skipped_with_a_stable_error(string sourceObject, string code)
    {
        var json = JsonNode.Parse(Fixtures.Text("headings-and-paragraphs.ir.json"))!;
        json["pages"]![0]!["elements"]![2]!["sourceObjects"] = new JsonArray(sourceObject);
        var ir = IrParser.Parse(json.ToJsonString()).Ir!;

        var result = new PdfPigTagWriter().Write(Original, ir);

        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal((code, "e3", 1), (error.Code, error.ElementId, error.PageNumber));
        Assert.Equal((4, 1), (result.Diagnostics.ElementsApplied, result.Diagnostics.ElementsSkipped));
        var tree = TaggedPdfReader.Read(result.TaggedPdf!);
        Assert.Equal(["H1", "P", "P"], tree.Elements.Select(e => e.Type));
        Assert.Contains(" hours", tree.ArtifactContent); // the skipped heading's words fall back to Artifacts
    }

    internal static IrDocument LoadIr(string fixture)
    {
        var parsed = IrParser.Parse(Fixtures.Text(fixture));
        Assert.Empty(parsed.Errors);
        return parsed.Ir!;
    }
}
