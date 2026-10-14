using Avalonia.Input;

namespace Noto.App.Views;

// The drag payload shared by every layout that moves cards (Board columns, Calendar days): an item id.
// Avalonia only accepts a plain dotted identifier here — "noto/item" throws in the type initializer.
static class ItemDrag
{
    public static readonly DataFormat<string> Payload = DataFormat.CreateStringApplicationFormat(
        "noto.item"
    );
}
