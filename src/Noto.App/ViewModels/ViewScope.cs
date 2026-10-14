namespace Noto.App.ViewModels;

// How many days the home screen spans. Day is the workspace's own layout (list, board, timeline);
// Week and Month swap in the calendar over the same items. It is a way of looking, not a layout,
// so it is per workspace view state, never synced, and never touches an item.
public enum ViewScope
{
    Day,
    Week,
    Month,
}
