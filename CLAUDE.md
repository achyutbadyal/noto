# Noto

Design docs live in `docs/` (start with `05-architecture.md`, `04-domain-model.md`, `08-implementation-roadmap.md`).

## Tooling
All tooling is managed by mise (`mise.toml`). Never install tools globally; run through mise:
- `mise run build` / `mise run test`
- `mise exec -- dotnet <args>` for anything else

## Layout
- `src/Noto.Core` — no UI, no I/O: models, time, commands, interfaces
- `src/Noto.Data` — SQLite repositories + embedded SQL migrations (`Migrations/NNNN_name.sql`)
- `tests/` — one test project per src project; `Noto.Core.Tests` holds shared helpers (`FakeClock`, `Make`)

Projects are added phase by phase per the roadmap; don't scaffold later phases early.

## Sync wiring
Create one `HybridClock` per device and pass it to both `SqliteUnitOfWork(connectionString, hlc)` and `CommandBus(..., hlc)`.
The unit of work's repositories record ops for every synced write (commands and services alike); without the clock nothing is recorded.
New synced entity types: add field list to `SyncRows`, a codec in `Noto.Sync/EntityCodecs.cs`, and a recording hook in the repository.
