using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class ItemRepository(
    SqliteConnection conn,
    SqliteTransaction? tx,
    ChangeRecorder? rec = null
) : IItemRepository
{
    const string Upsert = """
        INSERT INTO todo_item (id, workspace_id, parent_id, is_container, title, notes, status, is_someday, planned_for, due_date,
            estimate_minutes, priority, waiting_on, drop_reason, time_of_day, board_column, manual_rank, recurrence_rule_id,
            occurrence_date, completed_on, created_at, created_tz, completed_at, dropped_at, deleted_at)
        VALUES ($id, $ws, $parent, $container, $title, $notes, $status, $someday, $planned, $due,
            $estimate, $priority, $waiting, $drop, $tod, $column, $rank, $rule,
            $occurrence, $completedOn, $createdAt, $createdTz, $completedAt, $droppedAt, $deletedAt)
        ON CONFLICT(id) DO UPDATE SET
            workspace_id=$ws, parent_id=$parent, is_container=$container, title=$title, notes=$notes, status=$status,
            is_someday=$someday, planned_for=$planned, due_date=$due, estimate_minutes=$estimate, priority=$priority,
            waiting_on=$waiting, drop_reason=$drop, time_of_day=$tod, board_column=$column, manual_rank=$rank,
            recurrence_rule_id=$rule, occurrence_date=$occurrence, completed_on=$completedOn,
            created_at=$createdAt, created_tz=$createdTz, completed_at=$completedAt, dropped_at=$droppedAt, deleted_at=$deletedAt
        """;

    public async Task<TodoItem?> GetAsync(Guid id)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT * FROM todo_item WHERE id = $id",
            ("$id", id.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public Task<IReadOnlyList<TodoItem>> ListAsync(Guid workspaceId) =>
        QueryAsync(
            "SELECT * FROM todo_item WHERE workspace_id = $ws AND deleted_at IS NULL",
            workspaceId
        );

    public Task<IReadOnlyList<TodoItem>> ListAllAsync(Guid workspaceId) =>
        QueryAsync("SELECT * FROM todo_item WHERE workspace_id = $ws", workspaceId);

    public Task<IReadOnlyList<TodoItem>> ListForTodayAsync(Guid workspaceId, DateOnly today) =>
        QueryAsync(
            "SELECT * FROM todo_item WHERE workspace_id = $ws AND deleted_at IS NULL "
                + "AND (status IN ('Open', 'Waiting') OR completed_on = $today)",
            workspaceId,
            ("$today", Sql.Val(today))
        );

    async Task<IReadOnlyList<TodoItem>> QueryAsync(
        string sql,
        Guid workspaceId,
        params (string Name, object Value)[] extra
    )
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            sql,
            extra.Prepend(("$ws", (object)workspaceId.ToString())).ToArray()
        );
        using var r = await cmd.ExecuteReaderAsync();
        var items = new List<TodoItem>();
        while (await r.ReadAsync())
            items.Add(Map(r));
        return items;
    }

    public async Task UpsertAsync(TodoItem i)
    {
        var before = rec?.Active == true ? await GetAsync(i.Id) : null;
        using var cmd = Sql.Cmd(
            conn,
            tx,
            Upsert,
            ("$id", i.Id.ToString()),
            ("$ws", i.WorkspaceId.ToString()),
            ("$parent", Sql.Val(i.ParentId)),
            ("$container", i.IsContainer ? 1 : 0),
            ("$title", i.Title),
            ("$notes", Sql.Val(i.Notes)),
            ("$status", i.Status.ToString()),
            ("$someday", i.IsSomeday ? 1 : 0),
            ("$planned", Sql.Val(i.PlannedFor)),
            ("$due", Sql.Val(i.DueDate)),
            ("$estimate", Sql.Val(i.EstimateMinutes)),
            ("$priority", i.Priority),
            ("$waiting", Sql.Val(i.WaitingOn)),
            ("$drop", Sql.Val(i.DropReason)),
            ("$tod", Sql.Val(i.TimeOfDay)),
            ("$column", Sql.Val(i.BoardColumn)),
            ("$rank", i.ManualRank),
            ("$rule", Sql.Val(i.RecurrenceRuleId)),
            ("$occurrence", Sql.Val(i.OccurrenceDate)),
            ("$completedOn", Sql.Val(i.CompletedOn)),
            ("$createdAt", Sql.Val(i.CreatedAt)),
            ("$createdTz", i.CreatedTz),
            ("$completedAt", Sql.Val(i.CompletedAt)),
            ("$droppedAt", Sql.Val(i.DroppedAt)),
            ("$deletedAt", Sql.Val(i.DeletedAt))
        );
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true)
            await rec.RecordAsync(
                EntityTypes.TodoItem,
                i.Id,
                i.WorkspaceId,
                before is null ? null : SyncRows.ToRow(before),
                SyncRows.ToRow(i)
            );
    }

    static TodoItem Map(SqliteDataReader r) =>
        new()
        {
            Id = r.Guid("id")!.Value,
            WorkspaceId = r.Guid("workspace_id")!.Value,
            ParentId = r.Guid("parent_id"),
            IsContainer = r.Bool("is_container"),
            Title = r.Str("title")!,
            Notes = r.Str("notes"),
            Status = r.Enum<ItemStatus>("status")!.Value,
            IsSomeday = r.Bool("is_someday"),
            PlannedFor = r.Date("planned_for"),
            DueDate = r.Date("due_date"),
            EstimateMinutes = r.Int("estimate_minutes"),
            Priority = r.Int("priority")!.Value,
            WaitingOn = r.Str("waiting_on"),
            DropReason = r.Enum<DropReason>("drop_reason"),
            TimeOfDay = r.Enum<TimeOfDay>("time_of_day"),
            BoardColumn = r.Str("board_column"),
            ManualRank = r.Str("manual_rank")!,
            RecurrenceRuleId = r.Guid("recurrence_rule_id"),
            OccurrenceDate = r.Date("occurrence_date"),
            CompletedOn = r.Date("completed_on"),
            CreatedAt = r.Instant("created_at")!.Value,
            CreatedTz = r.Str("created_tz")!,
            CompletedAt = r.Instant("completed_at"),
            DroppedAt = r.Instant("dropped_at"),
            DeletedAt = r.Instant("deleted_at"),
        };
}
