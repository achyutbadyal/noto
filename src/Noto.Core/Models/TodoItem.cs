namespace Noto.Core.Models;

public sealed class TodoItem
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsContainer { get; set; }

    public string Title { get; set; } = "";
    public string? Notes { get; set; }

    public ItemStatus Status { get; set; }
    public bool IsSomeday { get; set; }
    public DateOnly? PlannedFor { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? WaitingOn { get; set; }
    public DropReason? DropReason { get; set; }

    public int? EstimateMinutes { get; set; }
    public int Priority { get; set; }
    public TimeOfDay? TimeOfDay { get; set; }
    public string? BoardColumn { get; set; }
    public string ManualRank { get; set; } = "";

    public Guid? RecurrenceRuleId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedTz { get; init; } = "";
    public DateOnly? CompletedOn { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DroppedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public TodoItem Clone() => (TodoItem)MemberwiseClone();
}
