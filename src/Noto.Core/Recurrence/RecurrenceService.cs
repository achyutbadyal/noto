using Noto.Core.Commands;
using Noto.Core.Interfaces;
using Noto.Core.Models;
using Noto.Core.Time;

namespace Noto.Core.Recurrence;

public sealed class RecurrenceService(IUnitOfWork uow, IClock clock)
{
    public Task<RecurrenceRule> CreateRuleAsync(
        Guid workspaceId,
        string rrule,
        RuleTemplate template,
        DateOnly start,
        MissedBehavior missed = MissedBehavior.Carry,
        int? targetCount = null,
        TargetPeriod? targetPeriod = null,
        DateOnly? end = null
    ) =>
        uow.RunAsync(async store =>
        {
            _ = RRule.Parse(rrule);
            if (string.IsNullOrWhiteSpace(template.Title))
                throw new CommandException("Title is required");
            if ((targetCount is null) != (targetPeriod is null))
                throw new CommandException("Target count and period go together");
            if (end < start)
                throw new CommandException("End date is before start date");
            if (await store.Workspaces.GetAsync(workspaceId) is null)
                throw new CommandException("Workspace not found");

            var rule = new RecurrenceRule
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                RRule = rrule,
                Template = template,
                MissedBehavior = missed,
                TargetCount = targetCount,
                TargetPeriod = targetPeriod,
                StartDate = start,
                EndDate = end,
            };
            await store.Rules.UpsertAsync(rule);
            return rule;
        });

    // Editing the template only affects instances not generated yet.
    public Task UpdateAsync(Guid ruleId, Action<RecurrenceRule> change) =>
        uow.RunAsync(async store =>
        {
            var rule =
                await store.Rules.GetAsync(ruleId) ?? throw new CommandException("Rule not found");
            change(rule);
            _ = RRule.Parse(rule.RRule);
            await store.Rules.UpsertAsync(rule);
            return 0;
        });

    public Task DeleteAsync(Guid ruleId) => UpdateAsync(ruleId, r => r.DeletedAt = clock.UtcNow);

    // Creates any instances due today for the workspace. Safe to call repeatedly and from several devices.
    public Task<IReadOnlyList<TodoItem>> GenerateDueAsync(Guid workspaceId) =>
        uow.RunAsync(async store =>
        {
            var ws =
                await store.Workspaces.GetAsync(workspaceId)
                ?? throw new CommandException("Workspace not found");
            var tz = LogicalDate.EffectiveZone(ws, clock);
            var today = LogicalDate.Of(clock.UtcNow, tz, ws.DayBoundary);
            var created = new List<TodoItem>();

            foreach (var rule in await store.Rules.ListAsync(workspaceId))
            {
                var existing = new Dictionary<DateOnly, bool>();
                var dates = RecurrenceEngine.DatesToGenerate(
                    rule,
                    today,
                    d =>
                        existing[d] =
                            store
                                .Items.GetAsync(RecurrenceEngine.InstanceId(rule, d))
                                .GetAwaiter()
                                .GetResult()
                                is not null
                );

                foreach (var day in dates)
                {
                    var item = RecurrenceEngine.BuildInstance(rule, day, ws, tz);
                    var violations = ItemInvariants.Check(item);
                    if (violations.Count > 0)
                        throw new InvariantViolationException(violations);

                    await store.Items.UpsertAsync(item);
                    await store.Events.AppendAsync(RecurrenceEngine.CreatedEvent(item));
                    if (rule.Template.TagIds is { Count: > 0 } tags)
                        await store.Tags.SetItemTagsAsync(item.Id, tags);
                    await store.Caches.InvalidateDayStatsFromAsync(workspaceId, day);
                    created.Add(item);
                }
            }
            return (IReadOnlyList<TodoItem>)created;
        });

    // Materialises the instance for one occurrence date if it does not exist yet, so a habit day can be
    // ticked retroactively (docs/07 §9.1). Returns null when the rule is not scheduled that day — a
    // weekday habit must not gain an instance on a Saturday.
    public Task<TodoItem?> EnsureOccurrenceAsync(Guid ruleId, DateOnly day) =>
        uow.RunAsync(async store =>
        {
            var rule = await store.Rules.GetAsync(ruleId);
            if (rule is null || !RecurrenceEngine.Scheduled(rule, day))
                return null;

            var existing = await store.Items.GetAsync(RecurrenceEngine.InstanceId(rule, day));
            if (existing is not null)
                return existing;

            var ws = await store.Workspaces.GetAsync(rule.WorkspaceId);
            if (ws is null)
                return null;
            var tz = LogicalDate.EffectiveZone(ws, clock);

            var item = RecurrenceEngine.BuildInstance(rule, day, ws, tz);
            var violations = ItemInvariants.Check(item);
            if (violations.Count > 0)
                throw new InvariantViolationException(violations);

            await store.Items.UpsertAsync(item);
            await store.Events.AppendAsync(RecurrenceEngine.CreatedEvent(item));
            if (rule.Template.TagIds is { Count: > 0 } tags)
                await store.Tags.SetItemTagsAsync(item.Id, tags);
            await store.Caches.InvalidateDayStatsFromAsync(rule.WorkspaceId, day);
            return item;
        });
}
