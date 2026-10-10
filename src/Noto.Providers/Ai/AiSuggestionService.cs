using Noto.Core.Ai;

namespace Noto.Providers.Ai;

// Ties a registry to one connection: build the prompt, call the provider, parse the reply. This is the
// only ISuggestionService implementation the app ships; tests use a fake instead.
public sealed class AiSuggestionService(AiProviderRegistry registry, AiConnection connection)
    : ISuggestionService
{
    public bool IsEnabled =>
        connection.Mode != AiMode.Off && registry.For(connection.Mode) is not null;

    public async Task<FieldSuggestion?> SuggestAsync(
        SuggestionRequest request,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.Text)
            || registry.For(connection.Mode) is not { } provider
        )
            return null;

        var reply = await provider.CompleteAsync(connection, SuggestionPrompt.For(request), ct);
        return SuggestionParser.Parse(reply, request.Today);
    }
}
