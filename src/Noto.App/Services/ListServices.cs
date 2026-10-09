using Noto.Core.Time;

namespace Noto.App.Services;

// What every flat item screen (Today, Backlog, Board, Timeline…) needs: read the workspace, run decisions, time the Now item.
public sealed record ListServices(
    WorkspaceReader Reader,
    ActionRunner Runner,
    FocusSession Focus,
    IClock Clock
);
