using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Time;

namespace Noto.App.Services;

// Workspace-level writes (settings, Now pointer, creation). Writes go through the bus so sync records them.
// Item changes go through ActionRunner instead.
public sealed class WorkspaceActions(IUnitOfWork uow, ICommandBus bus, IClock clock)
{
    // Workspace.Color holds an accent name; the theme resolves it per light/dark (ThemeTokens.Accents).
    public static readonly string[] AccentPalette = [.. Noto.App.Themes.ThemeTokens.Accents.Keys];

    public Task<IReadOnlyList<Workspace>> ListAsync() => uow.RunAsync(s => s.Workspaces.ListAsync());

    public Task<Workspace?> GetAsync(Guid id) => uow.RunAsync(s => s.Workspaces.GetAsync(id));

    public async Task<Workspace> CreateAsync(string name, string icon, Preset preset, int colorIndex)
    {
        var existing = await ListAsync();
        var ws = new Workspace
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Icon = icon,
            Color = AccentPalette[colorIndex % AccentPalette.Length],
            TimeZone = clock.DeviceTimeZone.Id,
            SortRank = existing.Count,
            CreatedAt = clock.UtcNow,
        };
        preset.ApplyTo(ws);
        await bus.SaveWorkspaceAsync(ws);
        return ws;
    }

    public async Task<Workspace> CreateFromTemplateAsync(Noto.Core.Workspaces.WorkspaceTemplate template, int colorIndex)
    {
        var ws = await CreateAsync(template.Name, template.Icon, template.Preset, colorIndex);
        if (template.Focus is null) return ws;
        await UpdateAsync(ws.Id, w => w.FocusHoursJson = template.Focus.ToJson());
        return (await GetAsync(ws.Id))!;
    }

    public async Task UpdateAsync(Guid id, Action<Workspace> edit)
    {
        var ws = await GetAsync(id) ?? throw new InvalidOperationException("Workspace not found");
        edit(ws);
        ws.Preset = BuiltInPresets.Label(ws);
        await bus.SaveWorkspaceAsync(ws);
    }

    public Task SetNowAsync(Guid workspaceId, Guid? itemId) => UpdateAsync(workspaceId, ws => ws.NowItemId = itemId);
}
