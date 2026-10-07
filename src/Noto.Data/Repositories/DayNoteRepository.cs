using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class DayNoteRepository(
    SqliteConnection conn,
    SqliteTransaction? tx,
    ChangeRecorder? rec = null
) : IDayNoteRepository
{
    public async Task<DayNote?> GetAsync(Guid workspaceId, DateOnly day, DayNoteKind kind)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT * FROM day_note WHERE workspace_id = $ws AND day = $day AND kind = $kind",
            ("$ws", workspaceId.ToString()),
            ("$day", Sql.Val(day)),
            ("$kind", kind.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public async Task UpsertAsync(DayNote note)
    {
        var before = rec?.Active == true ? await rec.LoadAsync(EntityTypes.DayNote, note.Id) : null;
        using var cmd = Sql.Cmd(
            conn,
            tx,
            """
            INSERT INTO day_note (id, workspace_id, day, kind, text) VALUES ($id, $ws, $day, $kind, $text)
            ON CONFLICT(id) DO UPDATE SET text=$text
            """,
            ("$id", note.Id.ToString()),
            ("$ws", note.WorkspaceId.ToString()),
            ("$day", Sql.Val(note.Day)),
            ("$kind", note.Kind.ToString()),
            ("$text", note.Text)
        );
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true)
            await rec.RecordRowStoreAsync(EntityTypes.DayNote, note.Id, before);
    }

    public async Task<IReadOnlyList<DayNote>> ListAsync(Guid workspaceId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT * FROM day_note WHERE workspace_id = $ws ORDER BY day",
            ("$ws", workspaceId.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        var notes = new List<DayNote>();
        while (await r.ReadAsync())
            notes.Add(Map(r));
        return notes;
    }

    static DayNote Map(SqliteDataReader r) =>
        new()
        {
            Id = r.Guid("id")!.Value,
            WorkspaceId = r.Guid("workspace_id")!.Value,
            Day = r.Date("day")!.Value,
            Kind = r.Enum<DayNoteKind>("kind")!.Value,
            Text = r.Str("text")!,
        };
}
