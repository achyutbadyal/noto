using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.App.Services;

// The Now item and its countdown (docs/07 §7.3). Held in memory: a restart ends the timer, not the Now pointer.
public sealed class FocusSession(ICommandBus bus, WorkspaceActions workspaces, IClock clock)
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(25);

    DateTimeOffset _startedAt;

    public Guid? ItemId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Title { get; private set; } = "";
    public TimeSpan Duration { get; private set; } = DefaultDuration;

    public bool IsActive => ItemId is not null;
    public TimeSpan Elapsed => IsActive ? clock.UtcNow - _startedAt : TimeSpan.Zero;
    public TimeSpan Remaining => IsActive ? TimeSpan.FromTicks(Math.Max(0, (Duration - Elapsed).Ticks)) : TimeSpan.Zero;

    public event Action? Changed;

    public async Task ToggleAsync(Guid workspaceId, TodoItem item)
    {
        var wasThis = ItemId == item.Id;
        if (IsActive) await StopAsync();
        if (!wasThis) await StartAsync(workspaceId, item);
    }

    public async Task StartAsync(Guid workspaceId, TodoItem item, TimeSpan? duration = null)
    {
        ItemId = item.Id;
        WorkspaceId = workspaceId;
        Title = item.Title;
        Duration = duration ?? DefaultDuration;
        _startedAt = clock.UtcNow;
        await workspaces.SetNowAsync(workspaceId, item.Id);
        await bus.SendAsync(new StartFocus(item.Id));
        Changed?.Invoke();
    }

    public async Task StopAsync()
    {
        if (ItemId is not { } id) return;
        var minutes = (int)Math.Round(Elapsed.TotalMinutes);
        ItemId = null;
        await workspaces.SetNowAsync(WorkspaceId, null);
        await bus.SendAsync(new StopFocus(id, minutes));
        Changed?.Invoke();
    }

    // "23:10"
    public string RemainingText => $"{(int)Remaining.TotalMinutes:00}:{Remaining.Seconds:00}";
}
