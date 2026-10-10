using CommunityToolkit.Mvvm.ComponentModel;
using Noto.Core.Ai;
using Noto.Platform.Abstractions;
using Noto.Providers.Ai;

namespace Noto.App.Services;

// Device-level AI configuration, and the one object the form asks for a suggestion. Non-secret settings
// live in IUiState; the API key lives in the OS keyring and is read only when a call is made, so it never
// reaches SQLite, exports or logs (docs/05 › Security).
//
// Implements ISuggestionService so the form depends on a single thing whether AI is on or off — switching
// it off just flips IsEnabled to false and every affordance disappears (docs/07 §19: AI only when asked).
public sealed partial class AiOptions : ObservableObject, ISuggestionService
{
    public const string KeyService = "app.noto.ai";
    const string KeyAccount = "api-key";

    readonly IUiState _state;
    readonly IKeyring _keyring;

    public AiOptions(IUiState state, IKeyring keyring, HttpClient http)
    {
        _state = state;
        _keyring = keyring;
        Registry = new AiProviderRegistry([
            new OpenAiCompatibleProvider(http),
            new AppleOnDeviceProvider(),
        ]);

        _mode = ParseMode(_state.Get("ai-mode"));
        _endpoint = _state.Get("ai-endpoint") ?? "";
        _model = _state.Get("ai-model") ?? "";
        _hasKey = _state.Get("ai-has-key") == "true";
    }

    public AiProviderRegistry Registry { get; }

    // Off unless the user has opted in; the app never reaches for a model on its own.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    AiMode _mode;

    [ObservableProperty]
    string _endpoint;

    [ObservableProperty]
    string _model;

    // A flag, not the secret: the keyring is async and IsEnabled must be readable synchronously.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    bool _hasKey;

    public bool IsEnabled =>
        Mode switch
        {
            AiMode.Off => false,
            // A hosted API is useless without a key; a local server or the on-device model needs none.
            AiMode.Cloud => HasKey && Registry.For(AiMode.Cloud) is not null,
            _ => Registry.For(Mode) is not null,
        };

    public Task<FieldSuggestion?> SuggestAsync(
        SuggestionRequest request,
        CancellationToken ct = default
    )
    {
        if (!IsEnabled)
            return Task.FromResult<FieldSuggestion?>(null);
        return SuggestCoreAsync(request, ct);
    }

    async Task<FieldSuggestion?> SuggestCoreAsync(SuggestionRequest request, CancellationToken ct)
    {
        var key = await _keyring.GetAsync(KeyService, KeyAccount);
        var service = new AiSuggestionService(
            Registry,
            new AiConnection(Mode, Endpoint, Model, key)
        );
        return await service.SuggestAsync(request, ct);
    }

    // Persists the choice and, when a key is supplied, the key itself. Passing null for `apiKey` leaves
    // the stored key untouched (the form only sends it when the user edits the field).
    public async Task SaveAsync(AiMode mode, string endpoint, string model, string? apiKey)
    {
        Mode = mode;
        Endpoint = endpoint.Trim();
        Model = model.Trim();

        _state.Set("ai-mode", Mode.ToString());
        _state.Set("ai-endpoint", Endpoint.Length == 0 ? null : Endpoint);
        _state.Set("ai-model", Model.Length == 0 ? null : Model);

        if (apiKey is not null)
        {
            var trimmed = apiKey.Trim();
            if (trimmed.Length == 0)
                await _keyring.DeleteAsync(KeyService, KeyAccount);
            else
                await _keyring.SetAsync(KeyService, KeyAccount, trimmed);

            HasKey = trimmed.Length > 0;
            _state.Set("ai-has-key", HasKey ? "true" : null);
        }
    }

    public static string DefaultEndpoint(AiMode mode) =>
        OpenAiCompatibleProvider.DefaultEndpoint(mode);

    public static string Label(AiMode mode) =>
        mode switch
        {
            AiMode.AppleOnDevice => "Apple Intelligence (on device)",
            AiMode.Local => "Local server",
            AiMode.Cloud => "Cloud API",
            _ => "Off",
        };

    public static string Description(AiMode mode) =>
        mode switch
        {
            AiMode.AppleOnDevice => "Runs on this Mac. Nothing is sent anywhere.",
            AiMode.Local => "Your own server — Ollama, LM Studio, llama.cpp, vLLM.",
            AiMode.Cloud => "A hosted OpenAI-compatible API, using your own key.",
            _ => "No AI anywhere. The sparkle never appears.",
        };

    static AiMode ParseMode(string? text) =>
        Enum.TryParse<AiMode>(text, out var mode) ? mode : AiMode.Off;
}
