namespace Noto.Core.Models;

public sealed class Workspace
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";

    public string TimeZone { get; set; } = "UTC";
    public bool TzFollowsDevice { get; set; } = true;
    public TimeOnly DayBoundary { get; set; } = TimeOnly.MinValue;
    public string? FocusHoursJson { get; set; }

    public CapacityUnit CapacityUnit { get; set; } = CapacityUnit.Minutes;
    public int DailyCapacity { get; set; } = 360;

    public string Preset { get; set; } = "zen";
    public Layout Layout { get; set; }
    public SortOrderMode SortOrderMode { get; set; }
    public Pressure Pressure { get; set; } = Pressure.Honest;
    public string? PressureOverridesJson { get; set; }
    public string? LayoutSettingsJson { get; set; }

    public Guid? NowItemId { get; set; }
    public bool SyncEnabled { get; set; }
    public int SortRank { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
