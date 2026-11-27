using System.Text.Json.Nodes;
using PdfRemediation.Domain.AccessibilityIr;

namespace PdfRemediation.Tests.AccessibilityIr;

public class IrValidationTests
{
    private const string ValidIr = """
        {
          "schemaVersion": "1.0",
          "title": "Harbour Library Annual Report",
          "language": "en-GB",
          "pages": [
            {
              "pageNumber": 1,
              "width": 612,
              "height": 792,
              "elements": [
                { "id": "e1", "type": "H1", "bbox": [72, 70, 400, 100], "readingOrder": 1, "sourceObjects": ["p1:s0:10-20"] },
                { "id": "e2", "type": "P", "bbox": [72, 120, 540, 160], "readingOrder": 2, "sourceObjects": ["p1:s0:30-40", "p1:s0:41-50"] }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Valid_ir_parses_into_the_model()
    {
        var result = IrParser.Parse(ValidIr);

        Assert.Empty(result.Errors);
        var ir = Assert.IsType<IrDocument>(result.Ir);
        Assert.Equal("Harbour Library Annual Report", ir.Title);
        Assert.Equal("en-GB", ir.Language);
        var page = Assert.Single(ir.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Collection(page.Elements,
            e =>
            {
                Assert.Equal("e1", e.Id);
                Assert.Equal(ElementType.H1, e.Type);
                Assert.Equal(new BoundingBox(72, 70, 400, 100), e.Bbox);
                Assert.Equal(["p1:s0:10-20"], e.SourceObjects);
            },
            e =>
            {
                Assert.Equal(ElementType.P, e.Type);
                Assert.Equal(2, e.ReadingOrder);
            });
    }

    public static TheoryData<string, Action<JsonNode>, string, string> InvalidExamples => new()
    {
        { "missing element id", ir => Element(ir, 0).AsObject().Remove("id"), "IR_ELEMENT_ID_MISSING", "pages[0].elements[0].id" },
        { "blank element id", ir => Element(ir, 0)["id"] = " ", "IR_ELEMENT_ID_MISSING", "pages[0].elements[0].id" },
        { "duplicate element id", ir => Element(ir, 1)["id"] = "e1", "IR_ELEMENT_ID_DUPLICATE", "pages[0].elements[1].id" },
        { "bbox with three numbers", ir => Element(ir, 0)["bbox"] = new JsonArray(72, 70, 400), "IR_BBOX_INVALID", "pages[0].elements[0].bbox" },
        { "bbox with zero width", ir => Element(ir, 0)["bbox"] = new JsonArray(72, 70, 72, 100), "IR_BBOX_INVALID", "pages[0].elements[0].bbox" },
        { "bbox upside down", ir => Element(ir, 0)["bbox"] = new JsonArray(72, 100, 400, 70), "IR_BBOX_INVALID", "pages[0].elements[0].bbox" },
        { "bbox off the page", ir => Element(ir, 0)["bbox"] = new JsonArray(72, 70, 700, 100), "IR_BBOX_INVALID", "pages[0].elements[0].bbox" },
        { "duplicate reading order", ir => Element(ir, 1)["readingOrder"] = 1, "IR_READING_ORDER_DUPLICATE", "pages[0].elements[1].readingOrder" },
        { "unsupported element type", ir => Element(ir, 0)["type"] = "Blockquote", "IR_ELEMENT_TYPE_UNSUPPORTED", "pages[0].elements[0].type" },
        { "no source objects", ir => Element(ir, 0)["sourceObjects"] = new JsonArray(), "IR_SOURCE_OBJECTS_MISSING", "pages[0].elements[0].sourceObjects" },
        { "missing title", ir => ir.AsObject().Remove("title"), "IR_FIELD_MISSING", "title" },
        { "missing language", ir => ir.AsObject().Remove("language"), "IR_FIELD_MISSING", "language" },
        { "missing reading order", ir => Element(ir, 0).AsObject().Remove("readingOrder"), "IR_FIELD_MISSING", "pages[0].elements[0].readingOrder" },
        { "unknown schema version", ir => ir["schemaVersion"] = "2.0", "IR_SCHEMA_VERSION_UNSUPPORTED", "schemaVersion" },
        { "reading order as text", ir => Element(ir, 0)["readingOrder"] = "first", "IR_FIELD_INVALID", "pages[0].elements[0].readingOrder" },
        { "reading order below one", ir => Element(ir, 0)["readingOrder"] = 0, "IR_FIELD_INVALID", "pages[0].elements[0].readingOrder" },
        { "confidence above one", ir => Element(ir, 0)["confidence"] = 1.5, "IR_FIELD_INVALID", "pages[0].elements[0].confidence" },
        { "blank source object id", ir => Element(ir, 1)["sourceObjects"]!.AsArray().Add(" "), "IR_FIELD_INVALID", "pages[0].elements[1].sourceObjects[2]" },
        { "duplicate page",ir => ir["pages"]!.AsArray().Add(ir["pages"]![0]!.DeepClone()), "IR_PAGE_DUPLICATE", "pages[1].pageNumber" },
    };

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Invalid_ir_is_rejected_with_a_stable_error(string example, Action<JsonNode> breakIt, string code, string path)
    {
        var ir = JsonNode.Parse(ValidIr)!;
        breakIt(ir);

        var result = IrParser.Parse(ir.ToJsonString());

        Assert.Null(result.Ir);
        var error = Assert.Single(result.Errors, e => e.Code == code);
        Assert.True(error.Path == path, $"{example}: expected path {path}, got {error.Path}");
    }

    [Fact]
    public void Malformed_json_is_rejected_with_a_stable_error()
    {
        var result = IrParser.Parse("{ \"schemaVersion\": ");

        Assert.Null(result.Ir);
        Assert.Equal("IR_MALFORMED_JSON", Assert.Single(result.Errors).Code);
    }

    private static JsonNode Element(JsonNode ir, int index) => ir["pages"]![0]!["elements"]![index]!;
}
