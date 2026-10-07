using System.Text.Json;
using Noto.App.Services;

namespace Noto.Desktop;

// View state as a small JSON file next to the database. Failures never block the app.
sealed class FileUiState : IUiState
{
    readonly string _path;
    readonly Dictionary<string, string> _values;

    public FileUiState(string path)
    {
        _path = path;
        try { _values = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), UiStateJson.Default.DictionaryStringString) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException) { _values = []; }
    }

    public string? Get(string key) => _values.GetValueOrDefault(key);

    public void Set(string key, string? value)
    {
        if (value is null) _values.Remove(key);
        else _values[key] = value;
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_values, UiStateJson.Default.DictionaryStringString)); }
        catch (IOException) { }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
sealed partial class UiStateJson : System.Text.Json.Serialization.JsonSerializerContext;
