using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Links;

namespace Noto.Data.Repositories;

sealed class ConnectionRepository(SqliteConnection conn, SqliteTransaction? tx) : IConnectionRepository
{
    public async Task<AppConnection?> GetAsync(Guid id)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT * FROM app_connection WHERE id = $id", ("$id", id.ToString()));
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public async Task<IReadOnlyList<AppConnection>> ListAsync()
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT * FROM app_connection ORDER BY connected_at");
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<AppConnection>();
        while (await r.ReadAsync()) list.Add(Map(r));
        return list;
    }

    public async Task UpsertAsync(AppConnection c)
    {
        using var cmd = Sql.Cmd(conn, tx, """
            INSERT INTO app_connection (id, provider_id, auth_method, display_label, instance_url, scopes, status, connected_at, last_used_at)
            VALUES ($id, $provider, $auth, $label, $instance, $scopes, $status, $connected, $lastUsed)
            ON CONFLICT(id) DO UPDATE SET display_label=$label, instance_url=$instance, scopes=$scopes, status=$status, last_used_at=$lastUsed
            """,
            ("$id", c.Id.ToString()), ("$provider", c.ProviderId), ("$auth", c.AuthMethod.ToString()),
            ("$label", c.DisplayLabel), ("$instance", Sql.Val(c.InstanceUrl)), ("$scopes", string.Join(' ', c.Scopes)),
            ("$status", c.Status.ToString()), ("$connected", Sql.Val(c.ConnectedAt)), ("$lastUsed", Sql.Val(c.LastUsedAt)));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        using var cmd = Sql.Cmd(conn, tx, "DELETE FROM app_connection WHERE id = $id", ("$id", id.ToString()));
        await cmd.ExecuteNonQueryAsync();
    }

    static AppConnection Map(SqliteDataReader r) => new()
    {
        Id = r.Guid("id")!.Value,
        ProviderId = r.Str("provider_id")!,
        AuthMethod = r.Enum<AuthMethod>("auth_method")!.Value,
        DisplayLabel = r.Str("display_label")!,
        InstanceUrl = r.Str("instance_url"),
        Scopes = r.Str("scopes")!.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        Status = r.Enum<ConnectionStatus>("status")!.Value,
        ConnectedAt = r.Instant("connected_at")!.Value,
        LastUsedAt = r.Instant("last_used_at"),
    };
}
