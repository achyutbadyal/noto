using Noto.Core.Sync;

namespace Noto.Sync.Tests;

public class HlcTests
{
    static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void Round_trips_through_text()
    {
        var hlc = new Hlc(1791375900123, 7, A);
        Hlc.Parse(hlc.ToString()).ShouldBe(hlc);
        hlc.ToString().ShouldBe($"1791375900123:7:{A}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1:2")]
    [InlineData("x:1:00000000-0000-0000-0000-00000000000a")]
    [InlineData("-1:1:00000000-0000-0000-0000-00000000000a")]
    [InlineData("1:1:not-a-guid")]
    public void Rejects_malformed_text(string text) => Hlc.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void Orders_by_ms_then_counter_then_device()
    {
        (new Hlc(2, 0, A) > new Hlc(1, 9, B)).ShouldBeTrue();
        (new Hlc(1, 2, A) > new Hlc(1, 1, B)).ShouldBeTrue();
        (new Hlc(1, 1, B) > new Hlc(1, 1, A)).ShouldBeTrue();
        new Hlc(1, 1, A).CompareTo(new Hlc(1, 1, A)).ShouldBe(0);
    }

    [Fact]
    public void Next_is_strictly_monotonic_even_when_the_wall_clock_stalls_or_goes_back()
    {
        long wall = 1000;
        var clock = new HybridClock(() => wall, A);

        var first = clock.Next();
        var second = clock.Next();
        wall = 500; // clock moved backwards
        var third = clock.Next();

        (second > first).ShouldBeTrue();
        (third > second).ShouldBeTrue();
        third.Ms.ShouldBe(1000);
    }

    [Fact]
    public void Counter_resets_when_the_wall_clock_advances()
    {
        long wall = 1000;
        var clock = new HybridClock(() => wall, A);
        clock.Next();
        clock.Next();
        wall = 2000;
        clock.Next().ShouldBe(new Hlc(2000, 0, A));
    }

    [Fact]
    public void Receive_makes_the_next_local_stamp_greater_than_anything_seen()
    {
        long wall = 1000;
        var slow = new HybridClock(() => wall, A);

        var remote = new Hlc(5000, 3, B);
        slow.Receive(remote);

        (slow.Next() > remote).ShouldBeTrue();
    }

    [Theory]
    [InlineData(1000, 1000, 2)] // same ms: counters merge
    [InlineData(1000, 900, 2)] // remote behind local
    [InlineData(1000, 3000, 2)] // remote ahead
    public void Receive_never_lets_the_clock_go_backwards(
        long localMs,
        long remoteMs,
        int remoteCounter
    )
    {
        var clock = new HybridClock(() => localMs, A);
        var before = clock.Next();
        var remote = new Hlc(remoteMs, remoteCounter, B);

        clock.Receive(remote);
        var after = clock.Next();

        (after > before).ShouldBeTrue();
        (after > remote).ShouldBeTrue();
    }

    [Fact]
    public void A_device_with_a_slow_clock_wins_again_once_it_has_heard_from_peers()
    {
        long fastWall = 10_000,
            slowWall = 1_000;
        var fast = new HybridClock(() => fastWall, B);
        var slow = new HybridClock(() => slowWall, A);

        var fastEdit = fast.Next();
        (slow.Next() < fastEdit).ShouldBeTrue(); // before syncing, the slow device loses

        slow.Receive(fastEdit);
        (slow.Next() > fastEdit).ShouldBeTrue(); // after, its edits are ordered after what it has seen
    }

    [Fact]
    public void Restore_prevents_reissuing_stamps_after_a_restart()
    {
        long wall = 1000;
        var first = new HybridClock(() => wall, A);
        first.Next();
        first.Next();
        var saved = first.Last;

        var restarted = new HybridClock(() => wall, A);
        restarted.Restore(saved);

        (restarted.Next() > Hlc.Parse(saved)).ShouldBeTrue();
    }

    [Fact]
    public void Missing_clock_sorts_below_everything()
    {
        Hlc.Compare(null, new Hlc(0, 0, A).ToString()).ShouldBeLessThan(0);
        Hlc.Compare(new Hlc(0, 0, A).ToString(), null).ShouldBeGreaterThan(0);
        Hlc.Compare(null, null).ShouldBe(0);
    }
}
