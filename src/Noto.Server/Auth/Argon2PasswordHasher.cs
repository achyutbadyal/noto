using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Noto.Server.Config;

namespace Noto.Server.Auth;

// Argon2id, stored as "argon2id$m=..,t=..,p=..$<salt>$<hash>" so parameters can be raised later.
public sealed class Argon2PasswordHasher(ServerConfig config)
{
    const int SaltBytes = 16,
        HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var s = config.Argon2;
        return $"argon2id$m={s.MemoryKb},t={s.Iterations},p={s.Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(Compute(password, salt, s))}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "argon2id")
            return false;

        var p = parts[1]
            .Split(',')
            .Select(kv => kv.Split('='))
            .ToDictionary(kv => kv[0], kv => int.Parse(kv[1]));
        var settings = new Argon2Settings(p["m"], p["t"], p["p"]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Compute(password, Convert.FromBase64String(parts[2]), settings);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    // Burns the same time as a real check so unknown emails can't be told apart by latency.
    public void DummyVerify(string password) =>
        Compute(password, new byte[SaltBytes], config.Argon2);

    static byte[] Compute(string password, byte[] salt, Argon2Settings s)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = s.MemoryKb,
            Iterations = s.Iterations,
            DegreeOfParallelism = s.Parallelism,
        };
        return argon.GetBytes(HashBytes);
    }
}
