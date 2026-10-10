namespace Noto.Providers.Ai;

// How the model is reached. The form does not branch on this; only the provider registry does.
//
//   Off           — no AI anywhere. The default, and the only mode a fresh install starts in.
//   AppleOnDevice — Apple Intelligence on Apple silicon, through the Noto helper (nothing leaves the Mac).
//   Local         — a server on this machine or the LAN: Ollama, LM Studio, llama.cpp, vLLM…
//   Cloud         — a hosted OpenAI-compatible API: OpenAI, OpenRouter, Groq, Together, DeepSeek…
//
// Local and Cloud both speak the OpenAI chat-completions shape, so one provider serves both; they are
// separate modes only because the words, the defaults and the privacy story differ.
public enum AiMode
{
    Off,
    AppleOnDevice,
    Local,
    Cloud,
}

// Everything a provider needs to make one call. `ApiKey` is read from the keyring on demand and never
// stored next to the rest of the settings (docs/05 › Security).
public sealed record AiConnection(AiMode Mode, string? Endpoint, string? Model, string? ApiKey);

// A single turn: the instructions and the user's raw text.
public sealed record AiPrompt(string System, string User);

// One way of reaching a model. A provider is usable when `UnavailableReason` is null; otherwise the
// settings pane shows the reason instead of letting the user pick a dead mode (docs/07 §19: degrade
// visibly rather than fail silently).
public interface IAiProvider
{
    IReadOnlyList<AiMode> Modes { get; }

    string DisplayName { get; }

    // Null when this build and machine can use the provider; otherwise a sentence for the settings UI.
    string? UnavailableReason { get; }

    Task<string> CompleteAsync(AiConnection connection, AiPrompt prompt, CancellationToken ct);
}

// Picks the provider for a mode. Kept separate from the providers so the service never names a
// concrete type and a new mode is one registration.
public sealed class AiProviderRegistry(IEnumerable<IAiProvider> providers)
{
    readonly IReadOnlyList<IAiProvider> _providers = providers.ToList();

    public IReadOnlyList<IAiProvider> Providers => _providers;

    // Null when no provider claims the mode or the one that does cannot run here.
    public IAiProvider? For(AiMode mode) =>
        _providers.FirstOrDefault(p => p.Modes.Contains(mode) && p.UnavailableReason is null);

    // Why a mode is not selectable, for the settings pane. Null when it is.
    public string? ReasonUnavailable(AiMode mode)
    {
        if (mode == AiMode.Off)
            return null;
        var provider = _providers.FirstOrDefault(p => p.Modes.Contains(mode));
        return provider is null
            ? $"No provider is registered for {mode}."
            : provider.UnavailableReason;
    }
}
