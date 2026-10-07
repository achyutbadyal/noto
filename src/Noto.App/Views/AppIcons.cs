using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Noto.App.Views;

// Centralized monochrome vector icon set. Icons are 24x24 outline paths that inherit the current
// foreground, so they render black/white/accent with the theme and never as coloured OS emoji.
// Use them in XAML as <PathIcon Data="{v:Icon Home}" /> or bind a name through IconConverters.Geometry.
public static class AppIcons
{
    static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- app sections ---
        ["today"] =
            "M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10zm0 2a3 3 0 1 1 0 6 3 3 0 0 1 0-6zM11 1h2v3h-2zM11 20h2v3h-2zM1 11h3v2H1zM20 11h3v2h-3zM4.2 5.6 5.6 4.2l2.1 2.1L6.3 7.7zM16.3 16.3l1.4-1.4 2.1 2.1-1.4 1.4zM16.3 7.7l2.1-2.1 1.4 1.4-2.1 2.1zM4.2 18.4l2.1-2.1 1.4 1.4-2.1 2.1zM4 19h16v2H4z",
        ["backlog"] =
            "M4 4h16a1 1 0 0 1 1 1v4H3V5a1 1 0 0 1 1-1zm-1 7h6a2 2 0 0 0 4 0h6v7a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z",
        ["review"] =
            "M12 6a5 5 0 1 0 0 10 5 5 0 0 0 0-10zm0 2a3 3 0 1 1 0 6 3 3 0 0 1 0-6zM11 1h2v3h-2zM11 13h2v3h-2zM1 8h3v2H1zM20 8h3v2h-3zM4.2 2.6 5.6 1.2l2.1 2.1L6.3 4.7zM16.3 13.3l1.4-1.4 2.1 2.1-1.4 1.4zM4 19h16v2H4z",
        ["insights"] = "M4 20h3v-8H4zm6.5 0h3V6h-3zM17 20h3V8h-3z",
        ["weekly"] =
            "M19 4h-1V2h-2v2H8V2H6v2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zm0 16H5V10h14zM7 12h3v3H7z",
        ["settings"] =
            "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.48.48 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z",
        ["sidebar"] = "M3 4h18v16H3zm2 2v12h5V6zm7 0v12h7V6z",
        ["todayall"] =
            "M12 2 2 7l10 5 10-5zM2 12l10 5 10-5-2.5-1.25L12 14.5 4.5 10.75zM2 17l10 5 10-5-2.5-1.25L12 19.5 4.5 15.75z",

        // --- workspaces / presets ---
        ["work"] =
            "M20 6h-4V4c0-1.11-.89-2-2-2h-4c-1.11 0-2 .89-2 2v2H4c-1.11 0-1.99.89-1.99 2L2 19c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V8c0-1.11-.89-2-2-2zm-6 0h-4V4h4v2z",
        ["personal"] = "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z",
        ["health"] =
            "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z",
        ["side"] =
            "M9.4 16.6 4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0 4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z",
        ["sprint"] = "M4 6h2v2H4zm4 0h12v2H8zM4 11h2v2H4zm4 0h12v2H8zM4 16h2v2H4zm4 0h12v2H8z",
        ["zen"] = "M12 2 9.5 8.5 3 11l6.5 2.5L12 20l2.5-6.5L21 11l-6.5-2.5z",
        ["deadline"] =
            "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm0 18a8 8 0 1 1 8-8 8 8 0 0 1-8 8zm0-14a6 6 0 1 0 6 6 6 6 0 0 0-6-6zm0 10a4 4 0 1 1 4-4 4 4 0 0 1-4 4zm0-6a2 2 0 1 0 2 2 2 2 0 0 0-2-2z",
        ["habit"] = "M7 7h10v3l4-4-4-4v3H5v6h2zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2z",
        ["kanban"] =
            "M16 3l5 5-1.4 1.4-1.1-1.1-4.2 4.2.5 3.6-1.4 1.4-3.5-3.5-4.6 4.6-1.4-1.4 4.6-4.6-3.5-3.5 1.4-1.4 3.6.5 4.2-4.2-1.1-1.1z",
        ["accountability"] =
            "M13.5.67s.74 2.65.74 4.8c0 2.06-1.35 3.73-3.41 3.73-2.07 0-3.63-1.67-3.63-3.73l.03-.36C5.21 7.51 4 10.62 4 14c0 4.42 3.58 8 8 8s8-3.58 8-8C20 8.61 17.41 3.8 13.5.67zM12 20c-3.31 0-6-2.69-6-6 0-1.53.75-3.68 1.94-5.06.33 1.93 1.9 3.39 3.86 3.39 2.15 0 3.9-1.75 3.9-3.9 0-.49-.1-.95-.27-1.38C16.89 8.78 18 11.39 18 14c0 3.31-2.69 6-6 6z",
        ["folder"] =
            "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z",
        ["focus"] =
            "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10 10-4.5 10-10S17.5 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm.5-13H11v6l5.2 3.1.8-1.3-4.5-2.7V7z",

        // --- item status ---
        ["planned"] = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zm0 2a6 6 0 1 1 0 12 6 6 0 0 1 0-12z",
        ["now"] =
            "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zm0 2a6 6 0 1 1 0 12 6 6 0 0 1 0-12zM12 8.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7z",
        ["waiting"] =
            "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10 10-4.5 10-10S17.5 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm.5-13H11v6l5.2 3.1.8-1.3-4.5-2.7V7z",
        ["done"] =
            "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16zm-1.2 10.2L8.4 11.8 7 13.2l3.8 3.8L17 10.8l-1.4-1.4z",
        ["dropped"] =
            "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 2a8 8 0 0 1 6.32 3.1L6.1 18.32A8 8 0 0 1 12 4zm0 16a8 8 0 0 1-6.32-3.1L17.9 5.68A8 8 0 0 1 12 20z",
        ["carry"] = "M12 5V2L7 6l5 4V7a5 5 0 1 1-5 5H5a7 7 0 1 0 7-7z",
        ["stuck"] = "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z",

        // --- controls ---
        ["search"] =
            "M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z",
        ["plus"] = "M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z",
        ["close"] =
            "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
        ["help"] =
            "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16zm-.9 12.6h1.8V18h-1.8zm1.8-1.5h-1.8v-.5c0-.9.5-1.4 1.2-1.9.6-.4 1-.7 1-1.3 0-.7-.6-1.2-1.4-1.2-.8 0-1.4.4-1.5 1.2H9.6c.1-1.6 1.3-2.7 3-2.7 1.7 0 2.9 1 2.9 2.5 0 1.1-.6 1.7-1.4 2.2-.7.4-1.2.7-1.2 1.4z",
        ["chevron-left"] = "M15.4 7.4 14 6l-6 6 6 6 1.4-1.4L10.8 12z",
        ["chevron-right"] = "M9.4 6 8 7.4 12.6 12 8 16.6 9.4 18l6-6z",
        ["chevron-down"] = "M7.4 8.6 6 10l6 6 6-6-1.4-1.4L12 13.2z",
        ["check"] = "M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z",
        ["arrow-right"] = "M12 4l-1.4 1.4L16.2 11H4v2h12.2l-5.6 5.6L12 20l8-8z",
        ["sparkle"] = "M12 2 9.5 8.5 3 11l6.5 2.5L12 20l2.5-6.5L21 11l-6.5-2.5z",
        ["moon"] =
            "M12.3 2a10 10 0 0 0-1.9 20 10 10 0 0 0 10-10 10.2 10.2 0 0 0-1.1.06 8 8 0 0 1-8.06-8.06C11.3 3.3 11.7 2.6 12.3 2z",
        ["timer"] =
            "M19.03 7.39l1.42-1.42c-.45-.51-.94-.99-1.47-1.42l-1.42 1.42C16.07 4.74 14.12 4 12 4c-5.52 0-10 4.48-10 10s4.48 10 10 10 10-4.48 10-10c0-2.12-.74-4.07-1.97-5.61zM12 22c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm1-13h-2v6l4.25 2.52.77-1.28-3.52-2.09V9zM11 1h2v2h-2z",
    };

    // Legacy emoji / older names map to the semantic set so existing databases keep rendering cleanly.
    static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["🏢"] = "work",
        ["🏠"] = "personal",
        ["💪"] = "health",
        ["🛠"] = "side",
        ["📋"] = "sprint",
        ["🧘"] = "zen",
        ["🎯"] = "deadline",
        ["🔁"] = "habit",
        ["📌"] = "kanban",
        ["🔥"] = "accountability",
        ["Work"] = "work",
        ["Personal"] = "personal",
        ["Health"] = "health",
        ["Side Projects"] = "side",
        ["Sprint"] = "sprint",
        ["Zen"] = "zen",
        ["Deadline"] = "deadline",
        ["Habit"] = "habit",
        ["Kanban"] = "kanban",
        ["Accountability"] = "accountability",
    };

    static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Resolves a semantic name (or a legacy emoji / preset name) to a shared, theme-aware geometry.
    public static Geometry Get(string? name)
    {
        var key = Resolve(name);
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var geometry = StreamGeometry.Parse(Paths[key]);
        Cache[key] = geometry;
        return geometry;
    }

    static string Resolve(string? name)
    {
        if (name is not null)
        {
            if (Paths.ContainsKey(name))
                return name;
            if (Aliases.TryGetValue(name, out var alias))
                return alias;
        }
        return "folder";
    }
}

// <PathIcon Data="{v:Icon Home}" /> — resolves a name from the icon set above.
public sealed class IconExtension : MarkupExtension
{
    public IconExtension() { }

    public IconExtension(string name) => Name = name;

    [ConstructorArgument("name")]
    public string Name { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => AppIcons.Get(Name);
}
