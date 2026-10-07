namespace Noto.Core.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    TimeZoneInfo DeviceTimeZone { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeZoneInfo DeviceTimeZone => TimeZoneInfo.Local;
}
