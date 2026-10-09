using Noto.Platform.Abstractions;
using Noto.Platform.Internal;

namespace Noto.Platform.Linux;

// The Secret Service (GNOME Keyring, KWallet, KeePassXC) through libsecret's `secret-tool`. The secret travels on
// stdin and never appears in the argument list. Items are keyed by the attributes service + account.
public sealed class SecretToolKeyring(string executable) : IKeyring
{
    public Capability Capability => Capability.Supported;

    public static IKeyring Create() =>
        ProcessRunner.FindOnPath("secret-tool") is { } path
            ? new SecretToolKeyring(path)
            : new InMemoryKeyring(
                "No keyring found: install libsecret-tools (secret-tool) and a Secret Service such as GNOME Keyring. Secrets are kept in memory only."
            );

    public async Task SetAsync(string service, string account, string secret)
    {
        var result = await ProcessRunner.RunAsync(
            executable,
            ["store", "--label=Noto", .. Attributes(service, account)],
            stdin: secret
        );
        if (!result.Succeeded)
            throw new InvalidOperationException(Describe("store", result));
    }

    public async Task<string?> GetAsync(string service, string account)
    {
        var result = await ProcessRunner.RunAsync(
            executable,
            ["lookup", .. Attributes(service, account)]
        );
        // `lookup` exits 1 with no output when nothing matches. Anything else is a real failure.
        if (result.Succeeded)
            return result.Output.Length == 0 ? null : result.Output;
        if (result.ExitCode == 1 && result.Error.Length == 0)
            return null;
        throw new InvalidOperationException(Describe("lookup", result));
    }

    public async Task DeleteAsync(string service, string account)
    {
        var result = await ProcessRunner.RunAsync(
            executable,
            ["clear", .. Attributes(service, account)]
        );
        if (!result.Succeeded && result.Error.Length > 0)
            throw new InvalidOperationException(Describe("clear", result));
    }

    internal static string[] Attributes(string service, string account) =>
        ["service", service, "account", account];

    static string Describe(string verb, ProcessResult result) =>
        $"secret-tool {verb} failed: {result.Error.Trim()}";
}
