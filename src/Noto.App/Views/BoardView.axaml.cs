using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noto.App.ViewModels;

namespace Noto.App.Views;

// Board drag-and-drop (see ItemDragger). Dragging a card onto a column runs exactly the same command the keyboard move
// does, so the WIP check and the undo toast behave identically (docs/07 §9.1).
public partial class BoardView : UserControl
{
    BoardViewModel? _vm;

    public BoardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as BoardViewModel;
        _ = new ItemDragger(
            this,
            section => section.ColumnId is not null,
            async (row, target) =>
            {
                if (_vm is not null && target.ColumnId is { } columnId)
                    await _vm.MoveToColumnAsync(row.Item, columnId);
            }
        );
    }
}
