using System.Text.Json;
using System.Text.Json.Serialization;
using Noto.Core.Interfaces;

namespace Noto.Providers.Auth;

[JsonSerializable(typeof(Credential))]
internal sealed partial class AuthJson : JsonSerializerContext;

public interface ICredentialSource
{
    Task<Credential?> GetAsync(CancellationToken ct);
}

public sealed class StaticCredentialSource(Credential? credential) : ICredentialSource
{
    public Task<Credential?> GetAsync(CancellationToken ct) => Task.FromResult(credential);
}

public class AuthRequiredException(string message) : Exception(message);

// One keyring item per connection: service `app.noto.connections`, account `<connection id>`.
public sealed class CredentialStore(IKeyring keyring)
{
    public const string Service = "app.noto.connections";

    public async Task<Credential?> LoadAsync(Guid connectionId) =>
        await keyring.GetAsync(Service, connectionId.ToString()) is { } json
            ? JsonSerializer.Deserialize(json, AuthJson.Default.Credential)
            : null;

    public Task SaveAsync(Guid connectionId, Credential credential) =>
        keyring.SetAsync(Service, connectionId.ToString(), JsonSerializer.Serialize(credential, AuthJson.Default.Credential));

    public Task DeleteAsync(Guid connectionId) => keyring.DeleteAsync(Service, connectionId.ToString());
}

public sealed class InMemoryKeyring : IKeyring
{
    readonly Dictionary<(string, string), string> _items = [];

    public Task<string?> GetAsync(string service, string account) =>
        Task.FromResult(_items.GetValueOrDefault((service, account)));

    public Task SetAsync(string service, string account, string secret)
    {
        _items[(service, account)] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string service, string account)
    {
        _items.Remove((service, account));
        return Task.CompletedTask;
    }
}
