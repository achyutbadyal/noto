using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Export;
using Noto.Core.Import;
using Noto.Core.Insights;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Recurrence;
using Noto.Core.Time;
using Noto.Core.Workspaces;
using Noto.Platform.Abstractions;

namespace Noto.App.Services;

// Composition root for the view-models: built once by the host (or by tests).
public sealed class AppServices
{
    public AppServices(
        IUnitOfWork uow,
        ICommandBus bus,
        IClock clock,
        ISearchIndex search,
        PlatformServices platform,
        IUiState? uiState = null,
        IDayNotes? dayNotes = null
    )
    {
        Uow = uow;
        Bus = bus;
        Clock = clock;
        Search = search;
        Platform = platform;
        UiState = uiState ?? new InMemoryUiState();
        DayNoteService = new DayNoteService(uow);
        DayNotes = dayNotes ?? new StoredDayNotes(DayNoteService);

        Derive = new DerivationService(uow, clock);
        Reader = new WorkspaceReader(uow, Derive, clock);
        Undo = new UndoService(bus, clock);
        Runner = new ActionRunner(bus, Undo, new ContainerService(uow, bus));
        Workspaces = new WorkspaceActions(uow, bus, clock);
        Focus = new FocusSession(bus, Workspaces, clock);
        Links = new LinkIndexer(uow, clock);
        Recurrence = new RecurrenceService(uow, clock);
        Export = new ExportService(uow, clock);
        Import = new ImportService(bus, uow, clock);
        TodayAll = new TodayAllService(uow, clock, Derive);
    }

    public IUnitOfWork Uow { get; }
    public ICommandBus Bus { get; }
    public IClock Clock { get; }
    public ISearchIndex Search { get; }
    public PlatformServices Platform { get; }
    public IUiState UiState { get; }
    public IDayNotes DayNotes { get; }
    public DerivationService Derive { get; }
    public WorkspaceReader Reader { get; }
    public UndoService Undo { get; }
    public ActionRunner Runner { get; }
    public WorkspaceActions Workspaces { get; }
    public FocusSession Focus { get; }
    public LinkIndexer Links { get; }
    public RecurrenceService Recurrence { get; }
    public DayNoteService DayNoteService { get; }
    public ExportService Export { get; }
    public ImportService Import { get; }
    public TodayAllService TodayAll { get; }
}
