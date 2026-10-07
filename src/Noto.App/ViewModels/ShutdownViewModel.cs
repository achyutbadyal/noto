using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;

namespace Noto.App.ViewModels;

public enum ShutdownStep { Done, NotDone, Note }

// Evening shutdown in three steps: acknowledge what got done, decide the rest, leave a note (docs/07 §4.3).
public sealed partial class ShutdownViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly Guid _workspaceId;
    DateOnly _today;

    public ShutdownViewModel(AppServices services, Guid workspaceId)
    {
        _services = services;
        _workspaceId = workspaceId;
        Review = new ReviewViewModel(services, workspaceId, ReviewMode.Shutdown);
        Review.Closed += () => Step = ShutdownStep.Note;
    }

    public ReviewViewModel Review { get; }
    public ObservableCollection<ItemRowViewModel> DoneRows { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StepTitle), nameof(StepNumber))] ShutdownStep _step;
    [ObservableProperty] string _note = "";

    public event Action? Finished;

    public string StepTitle => Step switch
    {
        ShutdownStep.Done => $"Done today ({DoneRows.Count})",
        ShutdownStep.NotDone => "Not done",
        _ => "Anything to remember for tomorrow?",
    };

    public int StepNumber => (int)Step + 1;

    public async Task LoadAsync()
    {
        var snap = await _services.Reader.LoadAsync(_workspaceId);
        _today = snap.Today;
        var byId = snap.Items.ToDictionary(i => i.Id);

        DoneRows.Clear();
        foreach (var item in snap.Today_.DoneToday) DoneRows.Add(ItemRowFactory.Create(item, snap, false, byId));
        await Review.LoadAsync();
        Note = await _services.DayNotes.GetAsync(_workspaceId, _today) ?? "";
        Step = ShutdownStep.Done;
        OnPropertyChanged(nameof(StepTitle));
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        switch (Step)
        {
            case ShutdownStep.Done:
                // Nothing left to decide: skip straight to the note.
                Step = Review.Entries.Count == 0 ? ShutdownStep.Note : ShutdownStep.NotDone;
                break;
            case ShutdownStep.NotDone:
                Step = ShutdownStep.Note;
                break;
            default:
                await FinishAsync();
                break;
        }
    }

    [RelayCommand]
    public async Task FinishAsync()
    {
        if (Note.Trim().Length > 0) await _services.DayNotes.SetAsync(_workspaceId, _today, Note.Trim());
        Finished?.Invoke();
    }

    public async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        switch (Step)
        {
            case ShutdownStep.Done:
                if (chord.Key is "Enter" or "ArrowRight") { await NextAsync(); return true; }
                if (chord.Key == "Escape") { Finished?.Invoke(); return true; }
                return false;
            case ShutdownStep.NotDone:
                if (Review.IsComplete && chord.Key == "Enter") { await NextAsync(); return true; }
                return await Review.HandleKeyAsync(chord);
            default:
                if (chord.Key == "Escape") { Finished?.Invoke(); return true; }
                return false; // typing the note
        }
    }
}
