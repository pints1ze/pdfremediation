using System.Text.Json.Nodes;
using PdfRemediation.Domain.AccessibilityIr;
using PdfRemediation.Infrastructure.Reading;
using PdfRemediation.Infrastructure.Reading.Mistral;
using PdfRemediation.Infrastructure.SourceObjects;
using PdfRemediation.Tests.Support;

namespace PdfRemediation.Tests.Reading;

/// <summary>Seam 3: the Mistral OCR reader, fed recorded responses. No test here touches the network.</summary>
public class MistralOcrReaderTests
{
    private static readonly byte[] Pdf = Fixtures.Bytes("headings-and-paragraphs.pdf");
    private static readonly SourceObjectIndex Index = SourceObjectIndex.Build(Pdf);
    private static readonly string Recording = Fixtures.Text(Path.Combine("mistral-synthetic", "headings-and-paragraphs", "page-0001.json"));

    [Fact]
    public async Task A_replayed_response_becomes_IR_with_block_types_reading_order_and_block_confidence()
    {
        var result = await Read(Recording);

        Assert.Empty(result.Errors);
        var elements = Assert.Single(result.Ir!.Pages).Elements;
        Assert.Equal([ElementType.H1, ElementType.P, ElementType.H1, ElementType.P, ElementType.Artifact], elements.Select(e => e.Type));
        Assert.Equal([1, 2, 3, 4, 5], elements.Select(e => e.ReadingOrder));
        Assert.Equal([0.93, 0.97, 0.88, 0.96, 0.79], elements.Select(e => e.Confidence!.Value));
        Assert.Equal("Opening hours", elements[2].Text);
        Assert.Equal(("Harbour Library", "en-GB"), (result.Ir.Title, result.Ir.Language));
    }

    [Fact]
    public async Task Each_Element_links_to_the_Source_objects_its_block_overlaps()
    {
        var result = await Read(Recording);

        // The hand-written IR for the same page is the independent answer: same Elements, same Source objects.
        var expected = IrParser.Parse(Fixtures.Text("headings-and-paragraphs.ir.json")).Ir!.Pages[0].Elements;
        var actual = result.Ir!.Pages[0].Elements;
        Assert.Equal(expected.Select(e => e.SourceObjects), actual.Select(e => e.SourceObjects));
    }

    [Fact]
    public async Task Block_boxes_are_converted_from_response_pixels_to_page_points()
    {
        var result = await Read(Recording);

        // 146,148 → 712,208 px on a 1275 × 1650 px render of a 612 × 792 pt page.
        var bbox = result.Ir!.Pages[0].Elements[0].Bbox;
        Assert.Equal(70.08, bbox.Left, 2);
        Assert.Equal(71.04, bbox.Top, 2);
        Assert.Equal(341.76, bbox.Right, 2);
        Assert.Equal(99.84, bbox.Bottom, 2);
    }

    [Fact]
    public async Task A_block_type_IR_v1_cannot_express_is_left_out_with_a_warning()
    {
        var result = await Read(Recording);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(("READER_BLOCK_TYPE_UNMAPPED", 1), (warning.Code, warning.PageNumber));
        Assert.Contains("image", warning.Message);
    }

    [Fact]
    public async Task A_block_over_no_Source_object_is_left_out_with_a_warning()
    {
        var response = JsonNode.Parse(Recording)!;
        var blocks = response["pages"]![0]!["blocks"]!.AsArray();
        blocks.Insert(2, JsonNode.Parse("""{"type":"text","content":"ghost","top_left_x":900,"top_left_y":900,"bottom_right_x":1000,"bottom_right_y":950,"confidence_scores":{"block_type_confidence_score":0.5}}"""));

        var result = await Read(response.ToJsonString());

        Assert.Empty(result.Errors);
        Assert.Equal(5, result.Ir!.Pages[0].Elements.Count);
        Assert.Contains(result.Warnings, w => w.Code == "READER_ELEMENT_UNLINKED" && w.Message.Contains("ghost"));
    }

    [Fact]
    public async Task Each_call_records_its_estimated_cost_from_the_configured_rate()
    {
        var result = await Read(Recording, costPerPageUsd: 0.004);

        var call = Assert.Single(result.Calls);
        Assert.Equal((1, 1, true), (call.PageNumber, call.PagesProcessed, call.Replayed));
        Assert.Equal(0.004, call.EstimatedCostUsd, 6);
    }

    [Fact]
    public async Task The_IR_JSON_parses_back_to_the_same_IR()
    {
        var result = await Read(Recording);

        var reparsed = IrParser.Parse(result.IrJson!).Ir!;
        Assert.Equal(result.Ir!.Pages[0].Elements.Select(e => (e.Id, e.Type, e.Bbox)), reparsed.Pages[0].Elements.Select(e => (e.Id, e.Type, e.Bbox)));
    }

    [Theory]
    [InlineData("READER_RESPONSE_MALFORMED", "this is not JSON")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"model":"mistral-ocr-4-1"}""")]
    [InlineData("READER_RESPONSE_PARTIAL", """{"pages":[],"usage_info":{"pages_processed":1}}""")]
    [InlineData("READER_RESPONSE_PARTIAL", """{"pages":[{"index":0,"dimensions":{"dpi":150,"height":1650,"width":1275},"blocks":null}],"usage_info":{"pages_processed":1}}""")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"pages":[{"index":0,"blocks":[]}],"usage_info":{"pages_processed":1}}""")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"pages":[{"index":0,"dimensions":{"dpi":150,"height":1650,"width":1275},"blocks":[{"type":"text","content":"x","top_left_y":1,"bottom_right_x":9,"bottom_right_y":9}]}],"usage_info":{"pages_processed":1}}""")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"pages":[{"index":0,"dimensions":{"dpi":150,"height":1650,"width":1275},"blocks":[{"type":"text","content":"x","top_left_x":9,"top_left_y":1,"bottom_right_x":2,"bottom_right_y":9}]}],"usage_info":{"pages_processed":1}}""")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"pages":[{"index":0,"dimensions":{"dpi":150,"height":1650,"width":1275},"blocks":[{"type":"text","content":"x","top_left_x":1,"top_left_y":1,"bottom_right_x":9,"bottom_right_y":9,"confidence_scores":{"block_type_confidence_score":1.5}}]}],"usage_info":{"pages_processed":1}}""")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"pages":[{"index":0,"dimensions":{"dpi":150,"height":1650,"width":1275},"blocks":[]}]}""")]
    [InlineData("READER_RESPONSE_MALFORMED", """{"pages":[{"index":0,"dimensions":{"dpi":150,"height":1650,"width":1275},"blocks":[]}],"usage_info":{"pages_processed":1.5}}""")]
    public async Task A_malformed_or_partial_response_gives_a_stable_error_and_no_IR(string code, string response)
    {
        var result = await Read(response);

        Assert.Null(result.Ir);
        var error = Assert.Single(result.Errors);
        Assert.Equal((code, 1), (error.Code, error.PageNumber));
    }

    [Fact]
    public async Task Replaying_a_page_that_was_never_recorded_gives_a_stable_error()
    {
        var folder = Directory.CreateTempSubdirectory("mistral-replay-").FullName;
        var reader = new MistralOcrReader(new MistralReplayTransport(folder), costPerPageUsd: 0.004);

        var result = await reader.ReadAsync(Input(), CancellationToken.None);

        Assert.Null(result.Ir);
        Assert.Equal("READER_RECORDING_MISSING", Assert.Single(result.Errors).Code);
        Assert.Empty(result.Calls);
    }

    [Fact]
    public async Task A_recording_with_unreadable_metadata_gives_a_stable_error()
    {
        var folder = Directory.CreateTempSubdirectory("mistral-replay-").FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "page-0001.json"), Recording);
        await File.WriteAllTextAsync(Path.Combine(folder, "page-0001.meta.json"), "{ not json");
        var reader = new MistralOcrReader(new MistralReplayTransport(folder), costPerPageUsd: 0.004);

        var result = await reader.ReadAsync(Input(), CancellationToken.None);

        Assert.Equal("READER_RECORDING_MALFORMED", Assert.Single(result.Errors).Code);
    }

    private static async Task<ReaderResult> Read(string response, double costPerPageUsd = 0.004)
    {
        var folder = Directory.CreateTempSubdirectory("mistral-replay-").FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "page-0001.json"), response);
        var reader = new MistralOcrReader(new MistralReplayTransport(folder), costPerPageUsd);
        return await reader.ReadAsync(Input(), CancellationToken.None);
    }

    /// <summary>Replay never sends the render, so the page image can be empty.</summary>
    private static ReaderInput Input() => new("Harbour Library", "en-GB", [new ReaderPage(1, 612, 792, [])], Index);
}
