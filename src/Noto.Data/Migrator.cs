using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Noto.Data;

// Applies embedded `NNNN_name.sql` scripts in order, each in its own transaction.
public static class Migrator
{
    public static void Apply(SqliteConnection conn)
    {
        Exec(conn, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY)");
        var current = Scalar(conn, "SELECT COALESCE(MAX(version), 0) FROM schema_version");

        var asm = Assembly.GetExecutingAssembly();
        var scripts = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql"))
            .Select(n => (Name: n, Version: ParseVersion(n)))
            .Where(s => s.Version > current)
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

    static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
