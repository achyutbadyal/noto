namespace Noto.Core.Models;

public enum ItemStatus
{
    Open,
    Waiting,
    Done,
    Dropped,
}

public enum DropReason
{
    NotNeeded,
    SomeoneElseDidIt,
    NotWorthIt,
    Other,
}

public enum TimeOfDay
{
    Morning,
    Midday,
    Afternoon,
    Evening,
}

public enum Layout
{
    List,
    Board,
    Timeline,
    HabitGrid,
}

public enum SortOrderMode
{
    Manual,
    PriorityCarry,
    DueDate,
    CarryDesc,
    TimeOfDay,
}

public enum Pressure
{
    Gentle,
    Honest,
    Relentless,
}

public enum CapacityUnit
{
    Minutes,
    Items,
}

public enum ItemEventType
{
    Created,
    TitleChanged,
    NotesChanged,
    PriorityChanged,
    EstimateChanged,
    Planned,
    SomedayChanged,
    WaitingStarted,
    WaitingEnded,
    Completed,
    Reopened,
    Dropped,
    Restored,
    StuckReasonGiven,
    BrokenDown,
    FocusStarted,
    FocusStopped,
    ColumnChanged,
    DueDateChanged,
    MovedWorkspace,
    LinkStateChanged,
    Deleted,
    TimeOfDayChanged,
    BreakDownUndone, // compensating event for BrokenDown: {planned_for}
}
