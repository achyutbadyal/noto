using System.Text.Json;

namespace Noto.Server.Gateway;

// The gateway may only run GraphQL *queries*. Any top-level mutation or subscription is refused.
public static class GraphQlGuard
{
    public static bool IsReadOnlyRequest(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false; // batching is not supported
            return doc.RootElement.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String && IsReadOnly(q.GetString()!);
        }
        catch (JsonException) { return false; }
    }

    // Walks the document skipping strings and comments; keywords only count at brace depth 0.
    public static bool IsReadOnly(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var depth = 0;
        var sawOperation = false;
        var inFragmentHeader = false;

        for (var i = 0; i < query.Length;)
        {
            var c = query[i];
            if (c == '#') { while (i < query.Length && query[i] != '\n') i++; continue; }
            if (c == '"')
            {
                if (string.CompareOrdinal(query, i, "\"\"\"", 0, 3) == 0)
                {
                    var end = query.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    if (end < 0) return false;
                    i = end + 3;
                }
                else
                {
                    i++;
                    while (i < query.Length && query[i] != '"') i += query[i] == '\\' ? 2 : 1;
                    if (i >= query.Length) return false;
                    i++;
                }
                continue;
            }
            if (c is '{' or '(' or '[')
            {
                if (c == '{' && depth == 0)
                {
                    if (!inFragmentHeader) sawOperation = true; // anonymous query shorthand
                    inFragmentHeader = false;
                }
                depth++; i++; continue;
            }
            if (c is '}' or ')' or ']') { depth--; i++; if (depth < 0) return false; continue; }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < query.Length && (char.IsLetterOrDigit(query[i]) || query[i] == '_')) i++;
                if (depth == 0)
                {
                    var word = query[start..i];
                    if (word is "mutation" or "subscription") return false;
                    if (word == "query") sawOperation = true;
                    if (word == "fragment") inFragmentHeader = true;
                }
                continue;
            }
            i++;
        }
        return depth == 0 && sawOperation;
    }
}
