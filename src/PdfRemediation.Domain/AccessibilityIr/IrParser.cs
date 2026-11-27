using System.Text.Json;

namespace PdfRemediation.Domain.AccessibilityIr;

public sealed record IrValidationError(string Code, string Path, string Message);

/// <summary>Either a valid <see cref="IrDocument"/> or the stable validation errors that rejected it.</summary>
public sealed record IrParseResult(IrDocument? Ir, IReadOnlyList<IrValidationError> Errors);

/// <summary>Parses Accessibility IR v1 JSON and validates it before anything downstream can use it.</summary>
public static class IrParser
{
    public const string SupportedSchemaVersion = "1.0";

    public static IrParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new IrParseResult(null, [new IrValidationError("IR_MALFORMED_JSON", "", ex.Message)]);
        }

        using (document)
        {
            var errors = new List<IrValidationError>();
            var ir = ReadDocument(document.RootElement, errors);
            return errors.Count == 0 ? new IrParseResult(ir, []) : new IrParseResult(null, errors);
        }
    }

    private static IrDocument? ReadDocument(JsonElement root, List<IrValidationError> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new("IR_MALFORMED_JSON", "", "The IR must be a JSON object."));
            return null;
        }

        var version = RequiredString(root, "schemaVersion", "schemaVersion", errors);
        if (version is not null && version != SupportedSchemaVersion)
        {
            errors.Add(new("IR_SCHEMA_VERSION_UNSUPPORTED", "schemaVersion", $"Schema version '{version}' is not supported; expected '{SupportedSchemaVersion}'."));
        }

        var title = RequiredString(root, "title", "title", errors);
        var language = RequiredString(root, "language", "language", errors);

        var pages = new List<IrPage>();
        if (Required(root, "pages", "pages", JsonValueKind.Array, errors) is { } pagesJson)
        {
            var seenPages = new HashSet<int>();
            var index = 0;
            foreach (var pageJson in pagesJson.EnumerateArray())
            {
                var page = ReadPage(pageJson, $"pages[{index}]", errors);
                if (page is not null && !seenPages.Add(page.PageNumber))
                {
                    errors.Add(new("IR_PAGE_DUPLICATE", $"pages[{index}].pageNumber", $"Page {page.PageNumber} appears more than once."));
                }
                else if (page is not null)
                {
                    pages.Add(page);
                }

                index++;
            }
        }

        if (title is null || language is null)
        {
            return null;
        }

        return new IrDocument(title, language, pages.OrderBy(p => p.PageNumber).ToList());
    }

    private static IrPage? ReadPage(JsonElement json, string path, List<IrValidationError> errors)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new("IR_FIELD_INVALID", path, "A page must be a JSON object."));
            return null;
        }

        var pageNumber = RequiredInt(json, "pageNumber", $"{path}.pageNumber", errors);
        var width = RequiredDouble(json, "width", $"{path}.width", errors);
        var height = RequiredDouble(json, "height", $"{path}.height", errors);
        if (pageNumber is < 1)
        {
            errors.Add(new("IR_FIELD_INVALID", $"{path}.pageNumber", "pageNumber must be 1 or more."));
            pageNumber = null;
        }

        var elements = new List<IrElement>();
        if (Required(json, "elements", $"{path}.elements", JsonValueKind.Array, errors) is { } elementsJson)
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenOrders = new HashSet<int>();
            var index = 0;
            foreach (var elementJson in elementsJson.EnumerateArray())
            {
                var elementPath = $"{path}.elements[{index}]";
                var element = ReadElement(elementJson, elementPath, width, height, errors);
                if (element is not null)
                {
                    if (!seenIds.Add(element.Id))
                    {
                        errors.Add(new("IR_ELEMENT_ID_DUPLICATE", $"{elementPath}.id", $"Element ID '{element.Id}' is used more than once."));
                    }

                    if (!seenOrders.Add(element.ReadingOrder))
                    {
                        errors.Add(new("IR_READING_ORDER_DUPLICATE", $"{elementPath}.readingOrder", $"Reading order {element.ReadingOrder} is used more than once on this page."));
                    }

                    elements.Add(element);
                }

                index++;
            }
        }

        if (pageNumber is null || width is null || height is null)
        {
            return null;
        }

        return new IrPage(pageNumber.Value, width.Value, height.Value, elements.OrderBy(e => e.ReadingOrder).ToList());
    }

    private static IrElement? ReadElement(JsonElement json, string path, double? pageWidth, double? pageHeight, List<IrValidationError> errors)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new("IR_FIELD_INVALID", path, "An element must be a JSON object."));
            return null;
        }

        string? id = null;
        if (json.TryGetProperty("id", out var idJson) && idJson.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(idJson.GetString()))
        {
            id = idJson.GetString();
        }
        else
        {
            errors.Add(new("IR_ELEMENT_ID_MISSING", $"{path}.id", "Every element needs a non-blank string ID."));
        }

        ElementType? type = null;
        if (RequiredString(json, "type", $"{path}.type", errors) is { } typeName)
        {
            if (Enum.GetNames<ElementType>().Contains(typeName, StringComparer.Ordinal))
            {
                type = Enum.Parse<ElementType>(typeName);
            }
            else
            {
                errors.Add(new("IR_ELEMENT_TYPE_UNSUPPORTED", $"{path}.type", $"Element type '{typeName}' is not supported in IR v1."));
            }
        }

        var bbox = ReadBbox(json, $"{path}.bbox", pageWidth, pageHeight, errors);
        var readingOrder = RequiredInt(json, "readingOrder", $"{path}.readingOrder", errors);
        if (readingOrder is < 1)
        {
            errors.Add(new("IR_FIELD_INVALID", $"{path}.readingOrder", "readingOrder must be 1 or more."));
            readingOrder = null;
        }

        double? confidence = null;
        var confidenceValid = true;
        if (json.TryGetProperty("confidence", out var confidenceJson) && confidenceJson.ValueKind != JsonValueKind.Null)
        {
            confidenceValid = confidenceJson.ValueKind == JsonValueKind.Number && confidenceJson.GetDouble() is >= 0 and <= 1;
            if (confidenceValid)
            {
                confidence = confidenceJson.GetDouble();
            }
            else
            {
                errors.Add(new("IR_FIELD_INVALID", $"{path}.confidence", "confidence must be a number from 0 to 1."));
            }
        }

        string? text = json.TryGetProperty("text", out var textJson) && textJson.ValueKind == JsonValueKind.String ? textJson.GetString() : null;

        var sourceObjects = new List<string>();
        var sourcesValid = true;
        if (Required(json, "sourceObjects", $"{path}.sourceObjects", JsonValueKind.Array, errors) is { } sourcesJson)
        {
            var sourceIndex = 0;
            foreach (var source in sourcesJson.EnumerateArray())
            {
                if (source.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(source.GetString()))
                {
                    sourceObjects.Add(source.GetString()!);
                }
                else
                {
                    sourcesValid = false;
                    errors.Add(new("IR_FIELD_INVALID", $"{path}.sourceObjects[{sourceIndex}]", "A Source object reference must be a non-blank string."));
                }

                sourceIndex++;
            }

            if (sourceObjects.Count == 0 && sourcesValid)
            {
                errors.Add(new("IR_SOURCE_OBJECTS_MISSING", $"{path}.sourceObjects", "Every element must point to at least one Source object."));
            }
        }

        if (id is null || type is null || bbox is null || readingOrder is null || !confidenceValid || !sourcesValid || sourceObjects.Count == 0)
        {
            return null;
        }

        return new IrElement(id, type.Value, bbox.Value, readingOrder.Value, confidence, text, sourceObjects);
    }

    private static BoundingBox? ReadBbox(JsonElement json, string path, double? pageWidth, double? pageHeight, List<IrValidationError> errors)
    {
        if (Required(json, "bbox", path, JsonValueKind.Array, errors) is not { } bboxJson)
        {
            return null;
        }

        var values = bboxJson.EnumerateArray().ToList();
        if (values.Count != 4 || values.Any(v => v.ValueKind != JsonValueKind.Number))
        {
            errors.Add(new("IR_BBOX_INVALID", path, "bbox must be four numbers: [left, top, right, bottom]."));
            return null;
        }

        var box = new BoundingBox(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble(), values[3].GetDouble());
        var insidePage = box.Left >= 0 && box.Top >= 0
            && (pageWidth is null || box.Right <= pageWidth)
            && (pageHeight is null || box.Bottom <= pageHeight);
        if (!(box.Left < box.Right && box.Top < box.Bottom) || !insidePage)
        {
            errors.Add(new("IR_BBOX_INVALID", path, "bbox must have positive width and height and lie within the page."));
            return null;
        }

        return box;
    }

    private static JsonElement? Required(JsonElement json, string name, string path, JsonValueKind kind, List<IrValidationError> errors)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            errors.Add(new("IR_FIELD_MISSING", path, $"'{name}' is required."));
            return null;
        }

        if (value.ValueKind != kind)
        {
            errors.Add(new("IR_FIELD_INVALID", path, $"'{name}' must be a JSON {kind.ToString().ToLowerInvariant()}."));
            return null;
        }

        return value;
    }

    private static string? RequiredString(JsonElement json, string name, string path, List<IrValidationError> errors)
    {
        var value = Required(json, name, path, JsonValueKind.String, errors)?.GetString();
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new("IR_FIELD_MISSING", path, $"'{name}' must not be blank."));
            return null;
        }

        return value;
    }

    private static int? RequiredInt(JsonElement json, string name, string path, List<IrValidationError> errors)
    {
        if (Required(json, name, path, JsonValueKind.Number, errors) is not { } value)
        {
            return null;
        }

        if (value.TryGetInt32(out var number))
        {
            return number;
        }

        errors.Add(new("IR_FIELD_INVALID", path, $"'{name}' must be a whole number."));
        return null;
    }

    private static double? RequiredDouble(JsonElement json, string name, string path, List<IrValidationError> errors)
    {
        var value = Required(json, name, path, JsonValueKind.Number, errors)?.GetDouble();
        if (value is <= 0)
        {
            errors.Add(new("IR_FIELD_INVALID", path, $"'{name}' must be greater than zero."));
            return null;
        }

        return value;
    }
}
