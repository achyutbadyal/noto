using Noto.App.Logic;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

// Keyboard routing for the shell: global chords, the `g` prefix, prompts, then the current page.
public sealed partial class ShellViewModel
{
    bool _pendingG;

    // Returns true when the key was consumed. `textInputFocused` lets single-key shortcuts stay out of text boxes.
    public async Task<bool> HandleKeyAsync(KeyChord chord, bool textInputFocused = false)
    {
        if (CommandBar.IsOpen)
            return await CommandBar.HandleKeyAsync(chord);
        if (IsHelpOpen)
        {
            if (chord.Key is "Escape" or "?")
                IsHelpOpen = false;
            return true;
        }

        // The detailed create panel is modal, so Escape must close it wherever focus happens to be
        // (a dropdown inside it swallows the key otherwise).
        if (
            chord.Key == "Escape"
            && Content is TodayViewModel { Add.IsDetailedOpen: true } detailed
        )
        {
            detailed.Add.Dismiss(keepTitle: true);
            return true;
        }

        if (Page == AppPage.Onboarding)
            return false;

        if (chord.Command && KeyMap.Resolve(KeyScope.Global, chord) is { } global)
        {
            // Inside a text box ⌘Z/⌘A belong to the box.
            if (!(textInputFocused && global.Action is AppAction.Undo or AppAction.SelectAll))
            {
                await ExecuteGlobalAsync(global);
                return true;
            }
        }

        if (await RoutePromptKeyAsync(chord))
            return true;
        if (textInputFocused)
            return false;

        // `g` then `b` / `t`: go to Backlog / Today.
        if (_pendingG)
        {
            _pendingG = false;
            if (chord.Key == "b")
            {
                await GoAsync(AppPage.Backlog);
                return true;
            }
            if (chord.Key == "t")
            {
                await GoAsync(AppPage.Today);
                return true;
            }
        }
        if (
            chord is { Key: "g", Command: false, Shift: false }
            && Page is AppPage.Today or AppPage.Backlog or AppPage.TodayAll
        )
        {
            _pendingG = true;
            return true;
        }

        if (Page is AppPage.Today or AppPage.Backlog or AppPage.DayLog or AppPage.TodayAll)
        {
            switch (KeyMap.Resolve(KeyScope.List, chord)?.Action)
            {
                case AppAction.PrevDay:
                    await ShiftDayAsync(-1);
                    return true;
                case AppAction.NextDay:
                    await ShiftDayAsync(1);
                    return true;
                case AppAction.Help:
                    IsHelpOpen = true;
                    return true;
                case AppAction.Search:
                    await CommandBar.OpenAsync();
                    return true;
            }
        }

        return Content switch
        {
            ReviewViewModel review => await review.HandleKeyAsync(chord),
            ShutdownViewModel shutdown => await shutdown.HandleKeyAsync(chord),
            WeeklyReviewViewModel weekly => await weekly.HandleKeyAsync(chord),
            ItemListViewModel list => await list.HandleKeyAsync(chord),
            _ => false,
        };
    }

    // An open prompt or title editor receives Enter/Escape (and drop-reason digits) even while a text box has focus.
    async Task<bool> RoutePromptKeyAsync(KeyChord chord)
    {
        if (Inspector.Decisions.Prompt is not null)
            return await Inspector.Decisions.HandlePromptKeyAsync(chord);
        return Content switch
        {
            ItemListViewModel { Decisions.Prompt: not null }
            or ItemListViewModel { IsEditingTitle: true } => await (
                (ItemListViewModel)Content
            ).HandleKeyAsync(chord),
            ReviewViewModel { Decisions.Prompt: not null } review => await review.HandleKeyAsync(
                chord
            ),
            ShutdownViewModel { Review.Decisions.Prompt: not null } shutdown =>
                await shutdown.HandleKeyAsync(chord),
            _ => false,
        };
    }

    async Task ExecuteGlobalAsync(Binding binding)
    {
        switch (binding.Action)
        {
            case AppAction.CommandBar:
                await CommandBar.OpenAsync();
                break;
            case AppAction.ToggleInspector:
                ToggleInspector();
                break;
            case AppAction.ToggleSidebar:
                ToggleSidebar();
                break;
            case AppAction.JumpToday:
                await GoAsync(AppPage.Today);
                break;
            case AppAction.WorkspaceN:
                if (binding.Arg - 1 < Workspaces.Count)
                    await SelectWorkspaceAsync(Workspaces[binding.Arg - 1].Id);
                break;
            case AppAction.TodayAll:
                await GoAsync(AppPage.TodayAll);
                break;
            case AppAction.ModeSwitcher:
                await GoAsync(AppPage.Settings);
                break;
            case AppAction.StartReview:
                await StartReviewAsync();
                break;
            case AppAction.Shutdown:
                await GoAsync(AppPage.Shutdown);
                break;
            case AppAction.Undo:
                await UndoAsync();
                break;
            case AppAction.CycleTheme:
                Appearance.CycleTheme();
                Toast.Show($"Appearance: {Appearance.ThemeLabel}", canUndo: false);
                break;
            case AppAction.SelectAll when Content is ItemListViewModel list:
                list.SelectAll();
                break;
        }
    }
}
