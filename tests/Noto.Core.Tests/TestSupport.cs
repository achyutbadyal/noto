using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Tests;

public sealed class FakeClock(DateTimeOffset now, string tz = "UTC") : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
    public TimeZoneInfo DeviceTimeZone { get; set; } = TimeZoneInfo.FindSystemTimeZoneById(tz);

    public void Advance(TimeSpan by) => UtcNow += by;
}

public static class Make
{
    public static Workspace Workspace(string tz = "UTC", TimeOnly? boundary = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = "Work",
        TimeZone = tz,
        TzFollowsDevice = false,
        DayBoundary = boundary ?? TimeOnly.MinValue,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    public static TodoItem Item(Guid? workspaceId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId ?? Guid.CreateVersion7(),
        Title = "Task",
        CreatedAt = DateTimeOffset.UnixEpoch,
        CreatedTz = "UTC",
    };
}
