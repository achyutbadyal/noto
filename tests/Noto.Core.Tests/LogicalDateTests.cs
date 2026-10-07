using Noto.Core.Time;

namespace Noto.Core.Tests;

public class LogicalDateTests
{
    static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso + "Z");

    [Theory]
    [InlineData("2026-10-07T03:59:00", "2026-10-06")]
    [InlineData("2026-10-07T04:00:00", "2026-10-07")]
    public void Day_boundary_shifts_the_logical_date(string instant, string expected)
    {
        var date = LogicalDate.Of(Utc(instant), TimeZoneInfo.Utc, new TimeOnly(4, 0));
        date.ShouldBe(DateOnly.Parse(expected));
    }

    [Fact]
    public void Uses_the_zone_not_utc()
    {
        // 20:00Z is already the next morning in Tokyo.
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        LogicalDate
            .Of(Utc("2026-10-07T20:00:00"), tokyo, TimeOnly.MinValue)
            .ShouldBe(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public void Boundary_inside_a_dst_gap_starts_the_day_at_the_next_valid_instant()
    {
        // New York springs forward 02:00 → 03:00 on 2026-03-08; a 02:30 boundary doesn't exist that day.
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var boundary = new TimeOnly(2, 30);

        LogicalDate.Of(Utc("2026-03-08T06:59:00"), ny, boundary).ShouldBe(new DateOnly(2026, 3, 7));
        LogicalDate.Of(Utc("2026-03-08T07:00:00"), ny, boundary).ShouldBe(new DateOnly(2026, 3, 8));
    }

    [Fact]
    public void Dst_fall_back_does_not_repeat_the_day()
    {
        // 2026-11-01 01:30 happens twice in New York; both map to the same logical date.
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var first = LogicalDate.Of(Utc("2026-11-01T05:30:00"), ny, TimeOnly.MinValue);
        var second = LogicalDate.Of(Utc("2026-11-01T06:30:00"), ny, TimeOnly.MinValue);

        first.ShouldBe(second);
    }

    [Fact]
    public void Recorded_zone_keeps_history_stable_when_traveling()
    {
        var instant = Utc("2026-10-07T20:00:00");

        LogicalDate
            .Of(instant, "America/Los_Angeles", TimeOnly.MinValue)
            .ShouldBe(new DateOnly(2026, 10, 7));
        LogicalDate
            .Of(instant, "Asia/Tokyo", TimeOnly.MinValue)
            .ShouldBe(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public void Today_follows_device_zone_when_enabled()
    {
        var ws = Make.Workspace(tz: "Asia/Tokyo");
        var clock = new FakeClock(Utc("2026-10-07T20:00:00"), tz: "America/Los_Angeles");

        LogicalDate.Today(ws, clock).ShouldBe(new DateOnly(2026, 10, 8));
        ws.TzFollowsDevice = true;
        LogicalDate.Today(ws, clock).ShouldBe(new DateOnly(2026, 10, 7));
    }
}
