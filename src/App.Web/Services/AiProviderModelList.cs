using System.Text.Json;

namespace App.Web.Services;

/// <summary>Reads model ids from the shapes OpenAI-compatible providers and Ollama return.</summary>
internal static class AiProviderModelList
{
    private static readonly string[] ChatSuffixes = ["/chat/completions", "/responses"];

    /// <summary>Turns a chat endpoint or base URL into the matching model-list URL.</summary>
    public static string BuildModelsUrl(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        var suffix = ChatSuffixes
            .FirstOrDefault(candidate => trimmed.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (suffix is not null)
        {
            trimmed = trimmed[..^suffix.Length];
        }

        return trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/models";
    }

    /// <summary>
    ///     Accepts <c>{"data":[{"id":...}]}</c>, <c>{"models":[{"name":...}]}</c>,
    ///     <c>{"models":[...ids]}</c>, and a bare array. Invalid JSON yields an empty list.
    /// </summary>
    public static IReadOnlyList<string> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var ids = new List<string>();
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                AddItems(root, ids);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    AddItems(data, ids);
                }
                else if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    AddItems(models, ids);
                }
            }

            return ids;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddItems(JsonElement array, List<string> ids)
    {
        foreach (var item in array.EnumerateArray())
        {
            var id = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => ReadString(item, "id")
                    ?? ReadString(item, "name")
                    ?? ReadString(item, "model"),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id.Trim());
            }
        }
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
