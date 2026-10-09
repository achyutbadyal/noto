using System.Text.Json;

namespace Noto.Sync;

// The Noto server's error bodies are problem details: { "title": "...", "code": "EMAIL_TAKEN", ... }.
public static class ProblemDetails
{
    public sealed record Problem(string? Code, string? Title);

    public static Problem Read(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new Problem(
                root.TryGetProperty("code", out var c) ? c.GetString() : null,
                root.TryGetProperty("title", out var t) ? t.GetString() : null
            );
        }
        catch (JsonException)
        {
            return new Problem(null, null);
        }
    }
}
