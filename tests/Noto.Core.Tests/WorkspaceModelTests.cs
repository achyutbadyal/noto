using Noto.Core.Workspaces;

namespace Noto.Core.Tests;

public class FocusHoursTests
{
    static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso + "Z");
    readonly FocusHours _work = FocusHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(18, 0));

    [Theory]
    [InlineData("2026-10-07T09:00:00", true)]  // Wed
    [InlineData("2026-10-07T17:59:00", true)]
    [InlineData("2026-10-07T18:00:00", false)]
    [InlineData("2026-10-07T08:59:00", false)]
    [InlineData("2026-10-10T12:00:00", false)] // Sat
    public void Active_only_on_listed_days_within_hours(string instant, bool active) =>
        _work.IsActive(At(instant), Utc).ShouldBe(active);

    [Fact]
    public void Uses_the_given_zone()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        _work.IsActive(At("2026-10-07T01:00:00"), tokyo).ShouldBeTrue(); // 10:00 JST
        _work.IsActive(At("2026-10-07T10:00:00"), tokyo).ShouldBeFalse(); // 19:00 JST
    }

    [Fact]
    public void Json_round_trips()
    {
        var back = FocusHours.FromJson(_work.ToJson())!;
        back.Days.ShouldBe(_work.Days);
        (back.Start, back.End).ShouldBe((_work.Start, _work.End));
        FocusHours.FromJson(null).ShouldBeNull();
    }

    [Fact]
    public void Workspaces_without_focus_hours_are_always_active()
    {
        var ws = Make.Workspace();
        FocusHours.IsActive(ws, new FakeClock(At("2026-10-10T03:00:00"))).ShouldBeTrue();

        ws.FocusHoursJson = _work.ToJson();
        FocusHours.IsActive(ws, new FakeClock(At("2026-10-10T03:00:00"))).ShouldBeFalse();
    }
}
