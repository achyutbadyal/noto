using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Commands;

namespace Noto.App.ViewModels;

// The inline "+ Add" input: understands the capture tokens and previews them as chips.
public sealed partial class AddItemViewModel(
    AppServices services, Guid workspaceId, bool plannedForToday, Func<string, Guid?>? resolveWorkspace, Func<DateOnly> today)
    : ObservableObject
{
    [ObservableProperty] string _text = "";
    [ObservableProperty] string? _error;
    [ObservableProperty] IReadOnlyList<string> _chips = [];

    public event Action? Added;

    public Guid? LastCreatedId { get; private set; }

    partial void OnTextChanged(string value)
    {
        Error = null;
        Chips = TokenParser.Parse(value, today()).Tokens.Select(t => t.Text).ToList();
    }

    // Backspace at the end of a recognized token removes the whole token.
    public void Backspace() => Text = TokenParser.RemoveTrailingToken(Text, today());

    [RelayCommand]
    async Task AddAsync() => await SubmitAsync();

    public async Task<bool> SubmitAsync()
    {
        var parsed = TokenParser.Parse(Text, today());
        if (parsed.Title.Length == 0) { Error = "Add a title"; return false; }

        var target = workspaceId;
        if (parsed.WorkspaceName is { } name)
        {
            if (resolveWorkspace?.Invoke(name) is not { } found) { Error = $"No workspace called /{name}"; return false; }
            target = found;
        }

        var id = Guid.CreateVersion7();
        var planned = parsed.PlannedFor ?? (plannedForToday ? today() : null);
        var commands = new List<ItemCommand> { new CreateItem(id, target, parsed.Title, planned, false, parsed.EstimateMinutes) };
        if (parsed.Priority > 0) commands.Add(new SetPriority(id, parsed.Priority));
        if (parsed.WaitingOn is { } on) commands.Add(new StartWaiting(id, on));

        await services.Runner.RunAllAsync(commands, $"Added “{parsed.Title}”");
        LastCreatedId = id;
        Text = "";
        Added?.Invoke();
        return true;
    }
}
