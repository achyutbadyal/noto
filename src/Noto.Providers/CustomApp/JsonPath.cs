using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;

namespace Noto.Providers.CustomApp;

// Field mapping for custom apps via the JsonPath.Net library (RFC 9535).
public static class JsonPathSelector
{
    public static JsonElement? Select(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !JsonPath.TryParse(path.Trim(), out var parsed)) return null;

        var result = parsed.Evaluate(JsonNode.Parse(root.GetRawText()));
        if (result.Matches is not { Count: > 0 } || result.Matches[0].Value is not { } node) return null;
        return JsonSerializer.SerializeToElement(node);
    }

    public static string? SelectString(JsonElement root, string? path) => Select(root, path) is { } e
        ? e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()
        : null;
}
