using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class RuleRepository(
    SqliteConnection conn,
    SqliteTransaction? tx,
    ChangeRecorder? rec = null
) : IRecurrenceRuleRepository
{
    public async Task<RecurrenceRule?> GetAsync(Guid id)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT * FROM recurrence_rule WHERE id = $id",
            ("$id", id.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public async Task<IReadOnlyList<RecurrenceRule>> ListAsync(Guid workspaceId)
    {
        using var cmd = Sql.Cmd(
            conn,
            tx,
            "SELECT * FROM recurrence_rule WHERE workspace_id = $ws AND deleted_at IS NULL",
            ("$ws", workspaceId.ToString())
        );
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<RecurrenceRule>();
        while (await r.ReadAsync())
            list.Add(Map(r));
        return list;
    }

    public async Task UpsertAsync(RecurrenceRule x)
    {
        var before =
            rec?.Active == true ? await rec.LoadAsync(EntityTypes.RecurrenceRule, x.Id) : null;
        using var cmd = Sql.Cmd(
            conn,
            tx,
            """
            INSERT INTO recurrence_rule (id, workspace_id, rrule, template, missed_behavior, target_count, target_period, start_date, end_date, deleted_at)
            VALUES ($id, $ws, $rrule, $template, $missed, $count, $period, $start, $end, $deleted)
            ON CONFLICT(id) DO UPDATE SET rrule=$rrule, template=$template, missed_behavior=$missed, target_count=$count,
                target_period=$period, start_date=$start, end_date=$end, deleted_at=$deleted
            """,
            ("$id", x.Id.ToString()),
            ("$ws", x.WorkspaceId.ToString()),
            ("$rrule", x.RRule),
            ("$template", x.TemplateJson),
            ("$missed", x.MissedBehavior.ToString()),
            ("$count", Sql.Val(x.TargetCount)),
            ("$period", Sql.Val(x.TargetPeriod)),
            ("$start", Sql.Val(x.StartDate)),
            ("$end", Sql.Val(x.EndDate)),
            ("$deleted", Sql.Val(x.DeletedAt))
        );
        await cmd.ExecuteNonQueryAsync();
        if (rec?.Active == true)
            await rec.RecordRowStoreAsync(EntityTypes.RecurrenceRule, x.Id, before);
    }

    static RecurrenceRule Map(SqliteDataReader r) =>
        new()
        {
            Id = r.Guid("id")!.Value,
            WorkspaceId = r.Guid("workspace_id")!.Value,
            RRule = r.Str("rrule")!,
            TemplateJson = r.Str("template")!,
            MissedBehavior = r.Enum<MissedBehavior>("missed_behavior")!.Value,
            TargetCount = r.Int("target_count"),
            TargetPeriod = r.Enum<TargetPeriod>("target_period"),
            StartDate = r.Date("start_date")!.Value,
            EndDate = r.Date("end_date"),
            DeletedAt = r.Instant("deleted_at"),
        };
}
