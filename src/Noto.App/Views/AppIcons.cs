using Avalonia.Media;

namespace Noto.App.Views;

// Centralized monochrome vector icon paths for a modern, unified design.
// Zero colored OS emojis. All icons respond dynamically to Dark and Light theme tokens.
public static class AppIcons
{
    public const string Search = "M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z";
    public const string Clock = "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10 10-4.5 10-10S17.5 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm.5-13H11v6l5.2 3.1.8-1.3-4.5-2.7V7z";
    public const string Stopwatch = "M19.03 7.39l1.42-1.42c-.45-.51-.94-.99-1.47-1.42l-1.42 1.42C16.07 4.74 14.12 4 12 4c-5.52 0-10 4.48-10 10s4.48 10 10 10 10-4.48 10-10c0-2.12-.74-4.07-1.97-5.61zM12 22c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm1-13h-2v6l4.25 2.52.77-1.28-3.52-2.09V9zM11 1h2v2h-2z";
    public const string Target = "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm0 18a8 8 0 1 1 8-8 8 8 0 0 1-8 8zm0-14a6 6 0 1 0 6 6 6 6 0 0 0-6-6zm0 10a4 4 0 1 1 4-4 4 4 0 0 1-4 4zm0-6a2 2 0 1 0 2 2 2 2 0 0 0-2-2z";
    public const string Home = "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z";
    public const string CalendarGrid = "M19 4h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20a2 2 0 0 0 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 16H5V10h14v10zM5 8V6h14v2H5z";
    public const string Moon = "M12.3 2a10 10 0 0 0-1.9 20 10 10 0 0 0 10-10 10.2 10.2 0 0 0-1.1.06 8 8 0 0 1-8.06-8.06C11.3 3.3 11.7 2.6 12.3 2z";
    public const string Settings = "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.48.48 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z";
    public const string SidebarToggle = "M3 4h18v16H3V4zm2 2v12h5V6H5zm7 0v12h7V6h-7z";
    public const string Plus = "M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z";
    public const string Briefcase = "M20 6h-4V4c0-1.11-.89-2-2-2h-4c-1.11 0-2 .89-2 2v2H4c-1.11 0-1.99.89-1.99 2L2 19c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V8c0-1.11-.89-2-2-2zm-6 0h-4V4h4v2z";
    public const string Heart = "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z";
    public const string CodeTools = "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z";
    public const string Lightbulb = "M9 21c0 .55.45 1 1 1h4c.55 0 1-.45 1-1v-1H9v1zm3-19C8.14 2 5 5.14 5 9c0 2.38 1.19 4.47 3 5.74V17c0 .55.45 1 1 1h6c.55 0 1-.45 1-1v-2.26c1.81-1.27 3-3.36 3-5.74 0-3.86-3.14-7-7-7z";
    public const string Flame = "M13.5.67s.74 2.65.74 4.8c0 2.06-1.35 3.73-3.41 3.73-2.07 0-3.63-1.67-3.63-3.73l.03-.36C5.21 7.51 4 10.62 4 14c0 4.42 3.58 8 8 8s8-3.58 8-8C20 8.61 17.41 3.8 13.5.67zM12 20c-3.31 0-6-2.69-6-6 0-1.53.75-3.68 1.94-5.06.33 1.93 1.9 3.39 3.86 3.39 2.15 0 3.9-1.75 3.9-3.9 0-.49-.1-.95-.27-1.38C16.89 8.78 18 11.39 18 14c0 3.31-2.69 6-6 6z";
    public const string Warning = "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z";
    public const string Sparkle = "M12 2L9.5 8.5 3 11l6.5 2.5L12 20l2.5-6.5L21 11l-6.5-2.5L12 2z";
    public const string Folder = "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z";

    // Maps workspace icon representation (including legacy emoji strings) to a clean vector path.
    public static string ForWorkspace(string? icon) => icon switch
    {
        "🏢" or "work" or "Work" => Briefcase,
        "🏠" or "personal" or "Personal" => Home,
        "💪" or "health" or "Health" => Heart,
        "🛠" or "side" or "Side Projects" or "sideprojects" => CodeTools,
        "📋" or "sprint" or "Sprint" => CalendarGrid,
        "🧘" or "zen" or "Zen" => Sparkle,
        "🎯" or "deadline" or "Deadline" => Target,
        "🔁" or "habit" or "Habit" => Stopwatch,
        "📌" or "kanban" or "Kanban" => SidebarToggle,
        "🔥" or "accountability" or "Accountability" => Flame,
        _ => Folder,
    };
}
