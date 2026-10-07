# Noto

Design docs live in `docs/` (start with `05-architecture.md`, `04-domain-model.md`, `08-implementation-roadmap.md`).

## Tooling

All tooling is managed by mise (`mise.toml`). Never install tools globally; run through mise:

- `mise run build` / `mise run test` / `mise run fmt` — see `mise tasks` for the full list
- `mise exec -- dotnet <args>` for anything else

.NET _tools_ (CSharpier, the formatter) are pinned in `.config/dotnet-tools.json` and restored by
`mise run restore`; `mise run fmt:check` verifies formatting and `mise run lint` checks whitespace/style/analyzers.

## Layout

- `src/Noto.Core` — no UI, no I/O: models, time, commands, interfaces
- `src/Noto.Data` — SQLite repositories + embedded SQL migrations (`Migrations/NNNN_name.sql`)
- `tests/` — one test project per src project; `Noto.Core.Tests` holds shared helpers (`FakeClock`, `Make`)

Projects are added phase by phase per the roadmap; don't scaffold later phases early.

## Sync wiring

Create one `HybridClock` per device and pass it to both `SqliteUnitOfWork(connectionString, hlc)` and `CommandBus(..., hlc)`.
The unit of work's repositories record ops for every synced write (commands and services alike); without the clock nothing is recorded.
New synced entity types: add field list to `SyncRows`, a codec in `Noto.Sync/EntityCodecs.cs`, and a recording hook in the repository.

## Source generators (read this before debugging "missing member" errors)

`Noto.App` depends on two source generators. If your editor reports hundreds of `CS0103` / `CS1061` /
`CS0759` ("does not exist in the current context", "no defining declaration found for partial method")
while `mise run build` succeeds with zero warnings, your language server is not running them:

- **CommunityToolkit.Mvvm** — `[ObservableProperty]` fields become properties, `[RelayCommand]` methods
  become `XxxCommand`, and `partial void OnXxxChanged(...)` hooks are supplied by the generator.
- **Avalonia.Generators** — `x:Name` fields and `InitializeComponent` for every `.axaml`.

Confirm the generators are fine from the CLI: `mise exec -- dotnet build src/Noto.App -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=/tmp/gen`
should emit ~97 files. Then restart the language server; if that does not fix it, delete every `obj/`
and `bin/` and reload. `global.json` pins the SDK so the editor and the CLI agree on which one to use.
