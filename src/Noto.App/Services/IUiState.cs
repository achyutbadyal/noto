namespace Noto.App.Services;

// Small local key/value store for view state (dismissed review, theme, density). Never synced.
public interface IUiState
{
    string? Get(string key);
    void Set(string key, string? value);
}

public sealed class InMemoryUiState : IUiState
{
    readonly Dictionary<string, string> _values = [];

    public string? Get(string key) => _values.GetValueOrDefault(key);

    public void Set(string key, string? value)
    {
        if (value is null) _values.Remove(key);
        else _values[key] = value;
    }
}
