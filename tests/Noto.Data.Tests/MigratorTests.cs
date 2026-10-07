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
}
