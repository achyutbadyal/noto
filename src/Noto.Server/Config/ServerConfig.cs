namespace Noto.Server.Config;

public enum DbProvider { Sqlite, Postgres }

public sealed record RateLimits(
    int AuthPerMinute = 10, int SyncPerMinute = 60, int SnapshotPerMinute = 10,
    int GatewayFetchPerMinute = 300, int GatewayFetchPerProviderPerMinute = 60,
    int OpenGraphPerMinute = 60, int AccountPerMinute = 5, int GatewayOAuthPerMinute = 30);

public sealed record Argon2Settings(int MemoryKb = 65536, int Iterations = 3, int Parallelism = 2);

public sealed class ServerConfigException(string message) : Exception(message);

public sealed record ServerConfig(
    string JwtSigningKey,
    Uri PublicUrl,
    DbProvider Db,
    string ConnectionString,
    string DataDir,
    IReadOnlySet<string> AllowPrivateHosts,
    IReadOnlyList<string> CorsOrigins,
    RateLimits Limits,
    Argon2Settings Argon2)
{
    // Secrets are required and have no defaults: the server refuses to start without them (docs/06 › Self-Hosting).
    public static ServerConfig Load(IConfiguration c)
    {
        var missing = new List<string>();
        string? Required(string key)
        {
            var v = c[key];
            if (string.IsNullOrWhiteSpace(v)) missing.Add(key);
            return v;
        }

        var key = Required("JWT_SIGNING_KEY");
        var publicUrl = Required("PUBLIC_URL");
        if (missing.Count > 0) throw new ServerConfigException($"Missing required settings: {string.Join(", ", missing)}");
        if (key!.Length < 32) throw new ServerConfigException("JWT_SIGNING_KEY must be at least 32 characters");
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new ServerConfigException("PUBLIC_URL must be an absolute http(s) URL");

        var databaseUrl = c["DATABASE_URL"];
        var provider = (c["DB"] ?? (databaseUrl is null ? "sqlite" : "postgres")).ToLowerInvariant() switch
        {
            "sqlite" => DbProvider.Sqlite,
            "postgres" or "postgresql" => DbProvider.Postgres,
            var other => throw new ServerConfigException($"Unknown DB '{other}' (sqlite | postgres)"),
        };

        var dataDir = c["DATA_DIR"] ?? "./noto-data";
        string connection;
        if (provider == DbProvider.Postgres)
        {
            if (string.IsNullOrWhiteSpace(databaseUrl)) throw new ServerConfigException("DATABASE_URL is required for DB=postgres");
            connection = PostgresConnectionString(databaseUrl);
        }
        else connection = $"Data Source={Path.Combine(dataDir, "noto.db")}";

        return new ServerConfig(
            key, url, provider, connection, dataDir,
            Split(c["GATEWAY_ALLOW_PRIVATE_HOSTS"]).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Split(c["CORS_ORIGINS"]).ToList(),
            new RateLimits(
                Int(c, "RATE_AUTH_PER_MIN", 10), Int(c, "RATE_SYNC_PER_MIN", 60), Int(c, "RATE_SNAPSHOT_PER_MIN", 10),
                Int(c, "RATE_GATEWAY_FETCH_PER_MIN", 300), Int(c, "RATE_GATEWAY_FETCH_PROVIDER_PER_MIN", 60),
                Int(c, "RATE_OPENGRAPH_PER_MIN", 60), Int(c, "RATE_ACCOUNT_PER_MIN", 5), Int(c, "RATE_GATEWAY_OAUTH_PER_MIN", 30)),
            new Argon2Settings(Int(c, "ARGON2_MEMORY_KB", 65536), Int(c, "ARGON2_ITERATIONS", 3), Int(c, "ARGON2_PARALLELISM", 2)));
    }

    // postgresql://user:pass@host:5432/db → Npgsql connection string
    static string PostgresConnectionString(string url)
    {
        if (!url.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)) return url; // already a connection string
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "")}";
    }

    static IEnumerable<string> Split(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static int Int(IConfiguration c, string key, int fallback) => int.TryParse(c[key], out var n) ? n : fallback;
}
