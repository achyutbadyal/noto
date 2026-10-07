using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Time;

namespace Noto.Core.Workspaces;

// Workspace lifecycle and settings. Switching presets/controls never touches items (docs/03).
public sealed class WorkspaceService(IUnitOfWork uow, IClock clock)
{
    public static readonly TimeSpan RecoveryWindow = TimeSpan.FromDays(30);

    public Task<Workspace> CreateAsync(WorkspaceTemplate template, TimeOnly? dayBoundary = null) =>
        CreateAsync(template.Name, template.Icon, template.Color, template.Preset, template.Focus, dayBoundary);

    public Task<Workspace> CreateAsync(
        string name, string icon, string color, Preset preset, FocusHours? focus = null, TimeOnly? dayBoundary = null) =>
        uow.RunAsync(async store =>
        {
            if (string.IsNullOrWhiteSpace(name)) throw new CommandException("Workspace name is required");
            var all = await store.Workspaces.ListAsync();
            var ws = new Workspace
            {
                Id = Guid.CreateVersion7(),
                Name = name.Trim(),
                Icon = icon,
                Color = color,
                TimeZone = clock.DeviceTimeZone.Id,
                DayBoundary = dayBoundary ?? TimeOnly.MinValue,
                FocusHoursJson = focus?.ToJson(),
                SortRank = all.Count == 0 ? 0 : all.Max(w => w.SortRank) + 1,
                CreatedAt = clock.UtcNow,
            };
            preset.ApplyTo(ws);
            await store.Workspaces.UpsertAsync(ws);
            return ws;
        });

    // Active = not archived and not deleted, in sidebar order.
    public Task<IReadOnlyList<Workspace>> ListAsync() =>
        uow.RunAsync(async s => (IReadOnlyList<Workspace>)(await s.Workspaces.ListAsync())
            .Where(w => w.ArchivedAt is null).OrderBy(w => w.SortRank).ToList());

    public Task<IReadOnlyList<Workspace>> ListArchivedAsync() =>
        uow.RunAsync(async s => (IReadOnlyList<Workspace>)(await s.Workspaces.ListAsync()).Where(w => w.ArchivedAt is not null).ToList());

    // Deleted workspaces still inside the recovery window.
    public Task<IReadOnlyList<Workspace>> ListRecoverableAsync() =>
        uow.RunAsync(async s => (IReadOnlyList<Workspace>)(await s.Workspaces.ListDeletedAsync())
            .Where(w => IsRecoverable(w, clock.UtcNow)).ToList());

    public static bool IsRecoverable(Workspace ws, DateTimeOffset now) =>
        ws.DeletedAt is { } deleted && now - deleted <= RecoveryWindow;

    public Task RenameAsync(Guid id, string name) => Edit(id, ws =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new CommandException("Workspace name is required");
        ws.Name = name.Trim();
    });

    public Task ArchiveAsync(Guid id) => Edit(id, ws => ws.ArchivedAt = clock.UtcNow);
    public Task UnarchiveAsync(Guid id) => Edit(id, ws => ws.ArchivedAt = null);
    public Task DeleteAsync(Guid id) => Edit(id, ws => ws.DeletedAt = clock.UtcNow);

    public Task RestoreAsync(Guid id) => Edit(id, ws =>
    {
        if (ws.DeletedAt is null) return;
        if (!IsRecoverable(ws, clock.UtcNow)) throw new CommandException("The 30-day recovery window has passed");
        ws.DeletedAt = null;
    });

    // `orderedIds` is the new sidebar order; workspaces not listed keep their relative order after them.
    public Task ReorderAsync(IReadOnlyList<Guid> orderedIds) => uow.RunAsync(async store =>
    {
        var all = (await store.Workspaces.ListAsync()).OrderBy(w => w.SortRank).ToList();
        var rest = all.Where(w => !orderedIds.Contains(w.Id));
        var ordered = orderedIds.Select(id => all.FirstOrDefault(w => w.Id == id) ?? throw new CommandException("Unknown workspace")).Concat(rest);
        var rank = 0;
        foreach (var ws in ordered)
        {
            ws.SortRank = rank++;
            await store.Workspaces.UpsertAsync(ws);
        }
        return 0;
    });

    // The single Now item; it must be an open item of this workspace.
    public Task SetNowAsync(Guid workspaceId, Guid? itemId) => uow.RunAsync(async store =>
    {
        var ws = await Require(store, workspaceId);
        if (itemId is { } id)
        {
            var item = await store.Items.GetAsync(id);
            if (item is null || item.WorkspaceId != workspaceId || item.Status != ItemStatus.Open || item.IsContainer || item.DeletedAt is not null)
                throw new CommandException("Now must be an open item in this workspace");
        }
        ws.NowItemId = itemId;
        await store.Workspaces.UpsertAsync(ws);
        return 0;
    });

    public Task ApplyPresetAsync(Guid id, Preset preset) => Edit(id, preset.ApplyTo);

    // Any control change turns the preset into "<preset> (custom)"; items are never written.
    public Task SetControlsAsync(Guid id, Layout? layout = null, SortOrderMode? order = null, Pressure? pressure = null,
        string? pressureOverridesJson = null) => Edit(id, ws =>
    {
        if (layout is { } l) ws.Layout = l;
        if (order is { } o) ws.SortOrderMode = o;
        if (pressure is { } p) ws.Pressure = p;
        if (pressureOverridesJson is not null) ws.PressureOverridesJson = pressureOverridesJson;
        ws.Preset = BuiltInPresets.Label(ws);
    });

    public Task SetFocusHoursAsync(Guid id, FocusHours? focus) => Edit(id, ws => ws.FocusHoursJson = focus?.ToJson());

    public Task SetCapacityAsync(Guid id, CapacityUnit unit, int capacity) => Edit(id, ws =>
    {
        if (capacity <= 0) throw new CommandException("Capacity must be positive");
        ws.CapacityUnit = unit;
        ws.DailyCapacity = capacity;
    });

    public Task SetTimeAsync(Guid id, string timeZone, bool followsDevice, TimeOnly dayBoundary) => Edit(id, ws =>
    {
        _ = LogicalDate.Zone(timeZone); // throws for unknown IANA ids
        ws.TimeZone = timeZone;
        ws.TzFollowsDevice = followsDevice;
        ws.DayBoundary = dayBoundary;
    });

    Task Edit(Guid id, Action<Workspace> change) => uow.RunAsync(async store =>
    {
        var ws = await Require(store, id);
        change(ws);
        await store.Workspaces.UpsertAsync(ws);
        return 0;
    });

    static async Task<Workspace> Require(IStore store, Guid id) =>
        await store.Workspaces.GetAsync(id) ?? throw new CommandException("Workspace not found");
}
