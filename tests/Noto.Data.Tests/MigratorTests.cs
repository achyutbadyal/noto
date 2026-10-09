using Microsoft.Data.Sqlite;

namespace Noto.Data.Tests;

public class MigratorTests
{
    [Fact]
    public void Applies_from_empty_and_is_idempotent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        Migrator.Apply(conn);
        Migrator.Apply(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('workspace','todo_item','item_event')";
        ((long)cmd.ExecuteScalar()!).ShouldBe(3);
    }

    [Fact]
    public void A_script_numbered_below_the_newest_applied_one_still_runs()
    {
        // The regression: a database already at version 100 never received 0041 under a MAX()-based check,
        // so the app crashed reading a column that was missing.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Migrator.Apply(conn);
        Run(conn, "ALTER TABLE app_connection DROP COLUMN api_base_url");
        Run(conn, "DELETE FROM schema_version WHERE version = 41");

        Migrator.Apply(conn);

        Run(conn, "SELECT api_base_url FROM app_connection");
        Scalar(conn, "SELECT COUNT(*) FROM schema_version WHERE version = 41").ShouldBe(1);
    }

    static void Run(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
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
