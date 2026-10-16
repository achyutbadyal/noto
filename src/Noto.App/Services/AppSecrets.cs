using Noto.Core.Interfaces;
using Noto.Providers.Auth;

namespace Noto.App.Services;

// Every secret name this app owns, in one place: the two fixed ones and one per provider connection.
//
// The keyring sweep needs the whole list — it cannot discover names in a keychain that has no portable
// "list everything" — and only this layer knows both halves, because the names belong to the classes that
// write them (`AccountService`, `AiOptions`, `CredentialStore`) and the connection ids live in the
// database (docs/05 › ADR-06).
public static class AppSecrets
{
    // The names that are always ours, whatever the database holds.
    public static readonly (string Service, string Account)[] Fixed =
    [
        AccountService.SessionSecret,
        AiOptions.KeySecret,
    ];

    public static async Task<IReadOnlyList<(string Service, string Account)>> AllAsync(
        IUnitOfWork uow
    )
    {
        var connections = await uow.RunAsync(s => s.Connections.ListAsync());
        return [.. Fixed, .. connections.Select(c => CredentialStore.SecretFor(c.Id))];
    }
}
