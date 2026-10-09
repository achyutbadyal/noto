using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Noto.Data;

// Applies embedded `NNNN_name.sql` scripts in order, each in its own transaction. Every script whose version
// is not yet recorded runs, even one numbered below the newest applied version, so a script added later
// into an existing number range still reaches databases that are already past it.
public static class Migrator
{
    public static void Apply(SqliteConnection conn)
    {
        Exec(conn, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY)");
        var applied = Applied(conn);

        var asm = Assembly.GetExecutingAssembly();
        var scripts = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql"))
            .Select(n => (Name: n, Version: ParseVersion(n)))
            .Where(s => !applied.Contains(s.Version))
            .OrderBy(s => s.Version);

        foreach (var (name, version) in scripts)
        {
            using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
            using var tx = conn.BeginTransaction();
            Exec(conn, reader.ReadToEnd(), tx);
            Exec(conn, $"INSERT INTO schema_version (version) VALUES ({version})", tx);
            tx.Commit();
        }
    }

    // Resource names look like "Noto.Data.Migrations.0001_init.sql".
    static long ParseVersion(string resource)
    {
        var file = resource.Split('.')[^2];
        return long.Parse(file.Split('_')[0]);
    }

    static void Exec(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static HashSet<long> Applied(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version";
        using var reader = cmd.ExecuteReader();
        var versions = new HashSet<long>();
        while (reader.Read())
            versions.Add(reader.GetInt64(0));
        return versions;
    }
}
