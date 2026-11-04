using System.Text.Json;

namespace App.Web.Mcp;

/// <summary>
/// Accepts native JSON arrays where the tool schema declares a string.
/// Runs in the CallTool filter before argument binding, converting arrays to
/// the string form each tool already validates.
/// </summary>
internal static class McpArgumentNormalization
{
    private const string TagsExpected = "comma-separated string or array of strings (max 500 chars)";
    private const string ChecklistExpected = "JSON string or array of {text, isDone} (max 8000 chars)";

    public static void Normalize(string toolName, IDictionary<string, JsonElement> args)
    {
        if (string.Equals(toolName, "batch_create_items", StringComparison.Ordinal))
        {
            NormalizeBatchItems(args);
            return;
        }

        if (IsTagsTool(toolName))
        {
            NormalizeTagsField(args, "tags");
        }

        if (IsChecklistTool(toolName, out var checklistParam))
        {
            NormalizeChecklistField(args, checklistParam);
        }
    }

    private static bool IsTagsTool(string toolName) =>
        string.Equals(toolName, "create_item", StringComparison.Ordinal)
        || string.Equals(toolName, "update_daily", StringComparison.Ordinal)
        || string.Equals(toolName, "update_habit", StringComparison.Ordinal)
        || string.Equals(toolName, "update_todo", StringComparison.Ordinal);

    private static bool IsChecklistTool(string toolName, out string paramName)
    {
        if (string.Equals(toolName, "set_checklist", StringComparison.Ordinal))
        {
            paramName = "json";
            return true;
        }

        if (string.Equals(toolName, "create_item", StringComparison.Ordinal)
            || string.Equals(toolName, "update_daily", StringComparison.Ordinal)
            || string.Equals(toolName, "update_habit", StringComparison.Ordinal)
            || string.Equals(toolName, "update_todo", StringComparison.Ordinal))
        {
            paramName = "checklistJson";
            return true;
        }

        paramName = "";
        return false;
    }

    private static void NormalizeBatchItems(IDictionary<string, JsonElement> args)
    {
        var key = FindKey(args, "items");
        if (key is null || args[key].ValueKind != JsonValueKind.Array)
        {
            return;
        }

        // Single pass: validate each element (bad shapes throw naming the field)
        // and rebuild the array when at least one element needed normalization.
        var array = new System.Text.Json.Nodes.JsonArray();
        var changed = false;
        var index = 0;
        foreach (var element in args[key].EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                array.Add(System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText()));
                index++;
                continue;
            }

            var node = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText())!;
            changed |= NormalizeTagsNode(node, "Tags", $"items[{index}].tags");
            changed |= NormalizeTagsNode(node, "tags", $"items[{index}].tags");
            changed |= NormalizeChecklistNode(node, "ChecklistJson", $"items[{index}].checklistJson");
            changed |= NormalizeChecklistNode(node, "checklistJson", $"items[{index}].checklistJson");
            array.Add(node);
            index++;
        }

        if (changed)
        {
            args[key] = JsonSerializer.SerializeToElement(array);
        }
    }

    private static void NormalizeTagsField(IDictionary<string, JsonElement> args, string paramName)
    {
        var key = FindKey(args, paramName);
        if (key is null)
        {
            return;
        }

        args[key] = NormalizeTagsElement(args[key], paramName);
    }

    private static void NormalizeChecklistField(IDictionary<string, JsonElement> args, string paramName)
    {
        var key = FindKey(args, paramName);
        if (key is null)
        {
            return;
        }

        args[key] = NormalizeChecklistElement(args[key], paramName);
    }

    private static bool NormalizeTagsNode(System.Text.Json.Nodes.JsonObject node, string property, string field)
    {
        if (!node.TryGetPropertyValue(property, out var value) || value is null)
        {
            return false;
        }

        var element = JsonSerializer.SerializeToElement(value);
        var normalized = NormalizeTagsElement(element, field);
        if (normalized.ValueKind == JsonValueKind.String && element.ValueKind == JsonValueKind.Array)
        {
            node[property] = normalized.GetString();
            return true;
        }

        return false;
    }

    private static bool NormalizeChecklistNode(System.Text.Json.Nodes.JsonObject node, string property, string field)
    {
        if (!node.TryGetPropertyValue(property, out var value) || value is null)
        {
            return false;
        }

        var element = JsonSerializer.SerializeToElement(value);
        var normalized = NormalizeChecklistElement(element, field);
        if (normalized.ValueKind == JsonValueKind.String && element.ValueKind == JsonValueKind.Array)
        {
            node[property] = normalized.GetString();
            return true;
        }

        return false;
    }

    private static JsonElement NormalizeTagsElement(JsonElement element, string field)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            case JsonValueKind.String:
                return element;
            case JsonValueKind.Array:
                {
                    var parts = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                        {
                            throw new McpFieldException(field,
                                $"expected {TagsExpected}, got array containing JSON {item.ValueKind.ToString().ToLowerInvariant()}.");
                        }

                        var text = (item.GetString() ?? "").Trim();
                        if (text.Length > 0)
                        {
                            parts.Add(text);
                        }
                    }

                    return JsonSerializer.SerializeToElement(string.Join(",", parts));
                }

            default:
                throw new McpFieldException(field,
                    $"expected {TagsExpected}, got JSON {element.ValueKind.ToString().ToLowerInvariant()}.");
        }
    }

    private static JsonElement NormalizeChecklistElement(JsonElement element, string field)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            case JsonValueKind.String:
                return element;
            case JsonValueKind.Array:
                {
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            throw new McpFieldException(field,
                                $"expected {ChecklistExpected}, got array containing JSON {item.ValueKind.ToString().ToLowerInvariant()} at index {index}.");
                        }

                        if (!TryGetText(item, out _))
                        {
                            throw new McpFieldException(field,
                                $"expected {ChecklistExpected}, got object without a 'text' string at index {index}.");
                        }

                        index++;
                    }

                    return JsonSerializer.SerializeToElement(element.GetRawText());
                }

            default:
                throw new McpFieldException(field,
                    $"expected {ChecklistExpected}, got JSON {element.ValueKind.ToString().ToLowerInvariant()}.");
        }
    }

    private static bool TryGetText(JsonElement item, out string? text)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, "text", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                text = property.Value.GetString();
                return !string.IsNullOrWhiteSpace(text);
            }
        }

        text = null;
        return false;
    }

    private static string? FindKey(IDictionary<string, JsonElement> args, string name) =>
        args.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
}
