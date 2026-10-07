using Microsoft.Data.Sqlite;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Sync;

namespace Noto.Data.Repositories;

sealed class WorkspaceRepository(SqliteConnection conn, SqliteTransaction? tx, ChangeRecorder? rec = null) : IWorkspaceRepository
{
    const string Upsert = """
        INSERT INTO workspace (id, name, icon, color, time_zone, tz_follows_device, day_boundary, focus_hours, capacity_unit,
            daily_capacity, preset, layout, sort_order_mode, pressure, pressure_overrides, layout_settings, now_item_id,
            sync_enabled, sort_rank, created_at, archived_at, deleted_at)
        VALUES ($id, $name, $icon, $color, $tz, $follows, $boundary, $focus, $unit,
            $capacity, $preset, $layout, $order, $pressure, $overrides, $layoutSettings, $now,
            $sync, $rank, $createdAt, $archivedAt, $deletedAt)
        ON CONFLICT(id) DO UPDATE SET
            name=$name, icon=$icon, color=$color, time_zone=$tz, tz_follows_device=$follows, day_boundary=$boundary,
            focus_hours=$focus, capacity_unit=$unit, daily_capacity=$capacity, preset=$preset, layout=$layout,
            sort_order_mode=$order, pressure=$pressure, pressure_overrides=$overrides, layout_settings=$layoutSettings,
            now_item_id=$now, sync_enabled=$sync, sort_rank=$rank, created_at=$createdAt, archived_at=$archivedAt, deleted_at=$deletedAt
        """;

    public async Task<Workspace?> GetAsync(Guid id)
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT * FROM workspace WHERE id = $id", ("$id", id.ToString()));
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    public async Task<IReadOnlyList<Workspace>> ListAsync()
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT * FROM workspace WHERE deleted_at IS NULL ORDER BY sort_rank");
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Workspace>();
        while (await r.ReadAsync()) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<Workspace>> ListDeletedAsync()
    {
        using var cmd = Sql.Cmd(conn, tx, "SELECT * FROM workspace WHERE deleted_at IS NOT NULL ORDER BY deleted_at DESC");
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Workspace>();
        while (await r.ReadAsync()) list.Add(Map(r));
        return list;
    }

    public async Task UpsertAsync(Workspace w)
    {
        var before = rec?.Active == true ? await GetAsync(w.Id) : null;
        using var cmd = Sql.Cmd(conn, tx, Upsert,
            ("$id", w.Id.ToString()), ("$name", w.Name), ("$icon", w.Icon), ("$color", w.Color), ("$tz", w.TimeZone),
            ("$follows", w.TzFollowsDevice ? 1 : 0), ("$boundary", w.DayBoundary.ToString("HH:mm")), ("$focus", Sql.Val(w.FocusHoursJson)),
            ("$unit", w.CapacityUnit.ToString()), ("$capacity", w.DailyCapacity), ("$preset", w.Preset),
            ("$layout", w.Layout.ToString()), ("$order", w.SortOrderMode.ToString()), ("$pressure", w.Pressure.ToString()),
            ("$overrides", Sql.Val(w.PressureOverridesJson)), ("$layoutSettings", Sql.Val(w.LayoutSettingsJson)),
            ("$now", Sql.Val(w.NowItemId)), ("$sync", w.SyncEnabled ? 1 : 0), ("$rank", w.SortRank),
            ("$createdAt", Sql.Val(w.CreatedAt)), ("$archivedAt", Sql.Val(w.ArchivedAt)), ("$deletedAt", Sql.Val(w.DeletedAt)));
        await cmd.ExecuteNonQueryAsync();
        // sync_enabled is a per-device toggle: ops flow only if it was on before and after this write.
        if (rec?.Active == true)
            await rec.RecordAsync(EntityTypes.Workspace, w.Id, w.Id, before is null ? null : SyncRows.ToRow(before), SyncRows.ToRow(w), before?.SyncEnabled == true && w.SyncEnabled);
    }

    static Workspace Map(SqliteDataReader r) => new()
    {
        Id = r.Guid("id")!.Value,
        Name = r.Str("name")!,
        Icon = r.Str("icon")!,
        Color = r.Str("color")!,
        TimeZone = r.Str("time_zone")!,
        TzFollowsDevice = r.Bool("tz_follows_device"),
        DayBoundary = TimeOnly.ParseExact(r.Str("day_boundary")!, "HH:mm"),
        FocusHoursJson = r.Str("focus_hours"),
        CapacityUnit = r.Enum<CapacityUnit>("capacity_unit")!.Value,
        DailyCapacity = r.Int("daily_capacity")!.Value,
        Preset = r.Str("preset")!,
        Layout = r.Enum<Layout>("layout")!.Value,
        SortOrderMode = r.Enum<SortOrderMode>("sort_order_mode")!.Value,
        Pressure = r.Enum<Pressure>("pressure")!.Value,
        PressureOverridesJson = r.Str("pressure_overrides"),
        LayoutSettingsJson = r.Str("layout_settings"),
        NowItemId = r.Guid("now_item_id"),
        SyncEnabled = r.Bool("sync_enabled"),
        SortRank = r.Int("sort_rank")!.Value,
        CreatedAt = r.Instant("created_at")!.Value,
        ArchivedAt = r.Instant("archived_at"),
        DeletedAt = r.Instant("deleted_at"),
    };
}
