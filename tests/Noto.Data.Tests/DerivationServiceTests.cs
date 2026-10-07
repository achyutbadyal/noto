using System.Diagnostics;
using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Tests;

namespace Noto.Data.Tests;

public sealed class DerivationServiceTests : IDisposable
{
    readonly SqliteUnitOfWork _uow = SqliteUnitOfWork.InMemory();
    readonly FakeClock _clock = new(DateTimeOffset.Parse("2026-10-05T10:00:00Z"));
    readonly Workspace _ws = Make.Workspace();
    readonly CommandBus _bus;
    readonly DerivationService _derive;

    public DerivationServiceTests()
    {
        _bus = new CommandBus(_uow, _clock, Guid.CreateVersion7());
        _derive = new DerivationService(_uow, _clock);
        _uow.RunAsync(async s =>
            {
                await s.Workspaces.UpsertAsync(_ws);
                return 0;
            })
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose() => _uow.Dispose();

    [Fact]
    public async Task Carry_grows_with_days_without_any_write_at_day_change()
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(new CreateItem(id, _ws.Id, "Deploy", new DateOnly(2026, 10, 5)));
        _clock.Advance(TimeSpan.FromDays(3));

        var today = await _derive.GetTodayAsync(_ws.Id);

        var item = today.Planned.ShouldHaveSingleItem();
        (await _derive.GetMetricsAsync(_ws.Id, [item]))[id].Carry.ShouldBe(3);
        today.NeedsDecision(new DateOnly(2026, 10, 8)).ShouldBe(1);
    }

    [Fact]
    public async Task Keep_today_stops_the_decision_prompt_but_keeps_accrued_carry()
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(new CreateItem(id, _ws.Id, "Deploy", new DateOnly(2026, 10, 5)));
        _clock.Advance(TimeSpan.FromDays(2));
        await _bus.SendAsync(new PlanItem(id, null, PlanKind.KeepToday));

        var today = await _derive.GetTodayAsync(_ws.Id);

        today.NeedsDecision(new DateOnly(2026, 10, 7)).ShouldBe(0);
        (await _derive.GetMetricsAsync(_ws.Id, today.Planned.ToList()))[id].Carry.ShouldBe(2);
    }

    [Fact]
    public async Task Metrics_cache_is_invalidated_by_commands()
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(new CreateItem(id, _ws.Id, "x", new DateOnly(2026, 10, 5)));
        _clock.Advance(TimeSpan.FromDays(2));
        await _derive.GetTodayAsync(_ws.Id); // primes the cache with carry 2

        await _bus.SendAsync(new PlanItem(id, new DateOnly(2026, 10, 9), PlanKind.Defer));

        var item = (await _uow.RunAsync(s => s.Items.GetAsync(id)))!;
        (await _derive.GetMetricsAsync(_ws.Id, [item]))[id].Defers.ShouldBe(1);
    }

    [Fact]
    public async Task Past_day_stats_are_cached_and_dropped_when_history_changes()
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(new CreateItem(id, _ws.Id, "x", new DateOnly(2026, 10, 5)));
        _clock.Advance(TimeSpan.FromDays(2));
        var day = new DateOnly(2026, 10, 6);

        var first = (await _derive.GetDayStatsAsync(_ws.Id, day, day)).Single();
        first.OpenAtEnd.ShouldBe(1);
        (await _uow.RunAsync(s => s.Caches.GetDayStatsAsync(_ws.Id, day, day))).Count.ShouldBe(1);

        // Crediting the item to the 5th ("Already done") removes it from the 6th's leftovers.
        await _bus.SendAsync(new CompleteItem(id, new DateOnly(2026, 10, 5)));
        (await _uow.RunAsync(s => s.Caches.GetDayStatsAsync(_ws.Id, day, day))).Count.ShouldBe(0);
        (await _derive.GetDayStatsAsync(_ws.Id, day, day)).Single().OpenAtEnd.ShouldBe(0);
    }

    [Fact]
    public async Task Today_stats_are_never_cached()
    {
        var today = new DateOnly(2026, 10, 5);
        await _derive.GetDayStatsAsync(_ws.Id, today, today);
        (await _uow.RunAsync(s => s.Caches.GetDayStatsAsync(_ws.Id, today, today))).ShouldBeEmpty();
    }

    // Phase 2 acceptance: a seeded 365-day history computes Today < 50ms and a year of stats < 1s.
    [Fact]
    public async Task Meets_performance_budgets_on_a_year_of_history()
    {
        var start = new DateOnly(2025, 10, 5);
        var rng = new Random(42);
        await Seed(start, days: 365, perDay: 8, rng);
        _clock.UtcNow = new DateTimeOffset(
            new DateOnly(2026, 10, 5).ToDateTime(new TimeOnly(10, 0)),
            TimeSpan.Zero
        );

        var sw = Stopwatch.StartNew();
        var view = await _derive.GetTodayAsync(_ws.Id);
        var todayMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var year = await _derive.GetDayStatsAsync(_ws.Id, start, new DateOnly(2026, 10, 4));
        var yearMs = sw.ElapsedMilliseconds;

        year.Count.ShouldBe(365);
        view.Planned.Count.ShouldBeGreaterThan(0);
#if DEBUG
        const int scale = 4; // budgets are for optimized builds; Debug runs ~3-4x slower
#else
        const int scale = 1;
#endif
        todayMs.ShouldBeLessThan(50 * scale);
        yearMs.ShouldBeLessThan(1000 * scale);
    }

    async Task Seed(DateOnly start, int days, int perDay, Random rng)
    {
        var device = Guid.CreateVersion7();
        await _uow.RunAsync(async s =>
        {
            for (var d = 0; d < days; d++)
            for (var n = 0; n < perDay; n++)
            {
                var day = start.AddDays(d);
                var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
                var item = new TodoItem
                {
                    Id = Guid.CreateVersion7(),
                    WorkspaceId = _ws.Id,
                    Title = $"t{d}-{n}",
                    PlannedFor = day,
                    CreatedAt = at,
                    CreatedTz = "UTC",
                    ManualRank = "a",
                };
                var events = new List<ItemEvent>
                {
                    Event(
                        item,
                        ItemEventType.Created,
                        at,
                        new()
                        {
                            ["planned_for"] = day.ToString("yyyy-MM-dd"),
                            ["is_someday"] = false,
                        }
                    ),
                };

                if (rng.NextDouble() < 0.9) // most get done within a few days
                {
                    var doneOn = day.AddDays(rng.Next(0, 4));
                    var doneAt = new DateTimeOffset(
                        doneOn.ToDateTime(new TimeOnly(15, 0)),
                        TimeSpan.Zero
                    );
                    item.Status = ItemStatus.Done;
                    item.CompletedOn = doneOn;
                    item.CompletedAt = doneAt;
                    events.Add(
                        Event(
                            item,
                            ItemEventType.Completed,
                            doneAt,
                            new() { ["completed_on"] = doneOn.ToString("yyyy-MM-dd") }
                        )
                    );
                }

                await s.Items.UpsertAsync(item);
                foreach (var e in events)
                    await s.Events.AppendAsync(e);
            }
            return 0;

            ItemEvent Event(
                TodoItem i,
                ItemEventType t,
                DateTimeOffset at,
                System.Text.Json.Nodes.JsonObject data
            ) =>
                new()
                {
                    Id = Guid.CreateVersion7(),
                    ItemId = i.Id,
                    WorkspaceId = i.WorkspaceId,
                    Type = t,
                    Data = data,
                    OccurredAt = at,
                    Tz = "UTC",
                    DeviceId = device,
                };
        });
    }
}
