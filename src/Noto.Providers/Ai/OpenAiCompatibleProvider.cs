using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Noto.Core.Ai;

namespace Noto.Providers.Ai;

// Any endpoint that speaks the OpenAI chat-completions shape. That is one provider for both hosted
// APIs (OpenAI, OpenRouter, Groq, Together, DeepSeek…) and servers on the user's own machine
// (Ollama, LM Studio, llama.cpp, vLLM), which is why "bring your own provider" needs no per-vendor code.
public sealed class OpenAiCompatibleProvider(HttpClient http) : IAiProvider
{
    public IReadOnlyList<AiMode> Modes => [AiMode.Cloud, AiMode.Local];

    public string DisplayName => "OpenAI-compatible API";

    // Always usable: the endpoint and model are supplied by the user, so there is nothing to detect.
    public string? UnavailableReason => null;

    public static string DefaultEndpoint(AiMode mode) =>
        mode == AiMode.Local ? "http://localhost:11434/v1" : "https://api.openai.com/v1";

    public async Task<string> CompleteAsync(
        AiConnection connection,
        AiPrompt prompt,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(connection.Model))
            throw new AiSuggestionException("No model is set for AI suggestions.");

        var endpoint = string.IsNullOrWhiteSpace(connection.Endpoint)
            ? DefaultEndpoint(connection.Mode)
            : connection.Endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(endpoint + "/chat/completions", UriKind.Absolute, out var url))
            throw new AiSuggestionException($"'{endpoint}' isn't a valid address.");

        var body = new JsonObject
        {
            ["model"] = connection.Model,
            ["temperature"] = 0,
            // Widely supported and it removes most fence/prose noise before the parser sees it.
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = prompt.System },
                new JsonObject { ["role"] = "user", ["content"] = prompt.User }
            ),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                connection.ApiKey
            );

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new AiSuggestionException($"Couldn't reach {url.Host}.", e);
        }
        // A timeout surfaces as a cancellation that the caller didn't ask for.
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new AiSuggestionException("The model took too long to answer.", e);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new AiSuggestionException(Describe(response.StatusCode, payload));
            return Content(payload);
        }
    }

    // The assistant's text. Handles the string form and the multi-part form some servers return.
    static string Content(string payload)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(payload);
            root = document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new AiSuggestionException("The model returned a reply that wasn't JSON.", e);
        }

        if (
            !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
        )
            throw new AiSuggestionException("The model returned no message.");

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";
        if (content.ValueKind == JsonValueKind.Array)
            return string.Concat(
                content
                    .EnumerateArray()
                    .Select(part =>
                        part.ValueKind == JsonValueKind.Object
                        && part.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String
                            ? text.GetString()
                            : null
                    )
            );
        throw new AiSuggestionException("The model returned an unexpected reply shape.");
    }

    static string Describe(HttpStatusCode status, string payload)
    {
        // Surface the provider's own message when it sends one; it is the useful part.
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (
                document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
            )
                return message.GetString()!;
        }
        catch (JsonException)
        { /* fall through to the status line */
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "The API key was rejected.",
            HttpStatusCode.Forbidden => "The API key isn't allowed to use this model.",
            HttpStatusCode.NotFound => "The endpoint or model wasn't found.",
            HttpStatusCode.TooManyRequests => "The provider is rate limiting. Try again shortly.",
            _ => $"The provider returned {(int)status}.",
        };
    }
}
