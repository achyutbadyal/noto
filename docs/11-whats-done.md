# Noto — What's Done

Status as of 2026-10-07. Build is clean (warnings are errors) and every test project passes. Acceptance criteria that are tested are ticked in [08 › Implementation Roadmap](08-implementation-roadmap.md); this file holds what the checkboxes can't: what is unverified, deviations and gaps.

```
mise run build    # dotnet build Noto.sln
mise run test     # dotnet test Noto.sln
```

Everything below was verified by automated tests unless it is listed under [Not verified](#not-verified) or [Not done](#not-done).

## Summary by roadmap phase

| Phase                      | State                                 | Notes                                                                                                                                          |
| -------------------------- | ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| 1 Foundation               | Done                                  | Models, I1–I7 invariants, time semantics, SQLite + migrations, command bus with events and undo                                                |
| 2 Derivations              | Done                                  | Carry/age/defers, day stats, caches + invalidation. Perf budgets met                                                                           |
| 3 Core loop (macOS)        | Built, visually checked headless only | Today, Review, Shutdown, Backlog, inspector, ⌘K, FTS5 search, undo toasts, a11y labels                                                         |
| 4 Presets and insights     | Done                                  | Presets, order, pressure, stuck prompt, insights v1, pace forecast, day strip, time travel                                                     |
| 5 Dogfood → MVP            | **Not done**                          | Needs two weeks of real use                                                                                                                    |
| 6 macOS native             | Built, **not exercised**              | Hotkey, menubar, notifications, capture-with-context compile and have unit tests; never fired on a real desktop. Import/export done and tested |
| 7 Connected apps (desktop) | Done against fixtures                 | Nine providers, previews, live links, auth flows. Never run against real services                                                              |
| 8 Sync and backend         | Done                                  | HLC op-log, per-field LWW, convergence harness, server, auth. Postgres and Docker untested                                                     |
| 9 Web                      | Server half done, **client not done** | Gateway, SSRF guard, CSP are in. No WASM host, no SQLite/OPFS spike                                                                            |
| 10 Layouts and recurrence  | Done                                  | Recurrence, habits, board, timeline, weekly review: logic and Avalonia views                                                                   |
| 11 More providers          | Done against fixtures                 | Slack, GitLab, Notion, Confluence, Figma                                                                                                       |
| 12 Platforms               | Windows/Linux written, unverified on real OSes | Windows (Credential Manager, RegisterHotKey, toasts) and Linux (secret-tool, X11 XGrabKey, notify-send) platform layers; zip/tarball packaging; CI matrix. No iOS/Android hosts |

## What exists

### `Noto.Core` (no UI, no I/O)

- **Models and invariants:** `TodoItem`, `Workspace`, `ItemEvent`, `RecurrenceRule`, `Tag`, `DayNote`, plus I1–I7 checks.
- **Time:** injectable clock; logical dates from instant + IANA zone + day boundary (DST gaps and repeats, history keeps its recorded zone).
- **Commands:** one `CommandBus` is the only item writer. Each command writes state, event and sync op in one transaction. Create, plan (plan / keep today / defer / unschedule), someday, wait, complete (with "already done" credit), reopen, drop, restore, rename, break down, field setters, stuck reason, focus, delete. All undoable.
- **Derivations:** age / carry / defers by replaying events (segment arithmetic, not per-day loops), day stats and the exact item sets behind them (time travel), Today query, pressure thresholds, six built-in presets, five order strategies.
- **Insights:** stuck prompt, completion streak, pace forecast, every doc 04 §4.4 insight (hidden below 10 samples), container rules, capacity bar.
- **Recurrence and habits:** RRULE subset with deterministic instance ids, carry vs skip, flex streaks, heatmap.
- **Layouts:** board (columns, WIP, stuck-in-column), timeline, weekly review data.
- **Workspaces:** create/rename/reorder/archive/soft-delete with recovery, Now item, presets, focus hours, Today (all) lens.
- **Import/export:** Todoist (CSV, JSON), Things, Reminders `.ics`, TickTick, Markdown checklists; JSON and CSV export (no credentials or previews).
- **Links and sync primitives:** URL normalization, HLC, op model, per-field LWW rule.

### `Noto.Data`

SQLite via `Microsoft.Data.Sqlite` with hand-written SQL, embedded numbered migrations (`0001`–`0100`), repositories, derived caches (local-only), FTS5 search with triggers, and op recording inside the repositories so services and commands are both captured.

### `Noto.Sync`

Client sync with seq cursor and pagination, batching merger with conflict log for text fields, per-workspace enable / disable / re-enable, snapshot bootstrap, scheduler, `HttpSyncTransport`.

### `Noto.Server`

Minimal API + EF Core 10 (SQLite and Postgres providers). Argon2id passwords, 15-minute JWTs with rotating refresh tokens (reuse detection) bound to devices, `/sync`, snapshot, workspace delete, account export and delete. Connected Apps Gateway: OAuth broker, allowlisted fetch proxy, read-only GraphQL guard, SSRF-guarded OpenGraph (public IPs only, redirects re-checked, 1 MB / 5 s caps). Rate limits, strict CSP, required secrets with no defaults, tombstone GC, Dockerfile.

### `Noto.Providers`

GitHub (batched GraphQL), Jira, Linear, Slack (admin-approval flow), GitLab, Notion, Confluence, Figma, OpenGraph fallback, custom JSONPath apps. Cache-first preview service with visible-first queue, rate gate, backoff, change dot. Live-link rules (merged PR → "Done?" suggestion; Waiting item returns when its ticket leaves a blocked status). PAT and OAuth (PKCE, loopback, refresh). Tokens never reach SQLite, exports or logs (tested, including raw DB/WAL bytes).

### `Noto.App` / `Noto.Desktop` / `Noto.Platform`

Avalonia 11.3 with CommunityToolkit.Mvvm. Shell, sidebar, Today, Morning Review (keyboard-only), Shutdown, Backlog, inspector, command bar with search, undo toasts, time travel, Insights, Settings, Today (all), Weekly Review, Board, Timeline, Habit grid, quick capture, onboarding. VoiceOver label strings, theme tokens in one C# source shared with the contrast tests. Platform abstractions with macOS implementations (Keychain, Carbon hotkey, notifications, capture context); Windows/Linux stubs that explain what is unavailable.

## Acceptance criteria that are tested

| Criterion (docs/08)                                          | Result                                                                                          |
| ------------------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| Every row of the carry/age table in doc 04 §4.1              | One test per row                                                                                |
| Seeded 365-day history: Today < 50 ms, a year of stats < 1 s | Today ≈ 31 ms, year ≈ 310 ms (Release). Debug builds use a 4× allowance                         |
| Keyboard-only 5-item Morning Review                          | ≤ 8 key presses, at view-model level                                                            |
| Every decision undoable                                      | Tested for each decision                                                                        |
| VoiceOver row strings                                        | String format tested; VoiceOver itself not run                                                  |
| Both themes pass the contrast table                          | Computed from the tokens; documented dark `text-3` hover exception verified                     |
| 3 replicas, 10k random ops, partitions, clock skew converge  | Identical state, matches an independent oracle, no different-field edit lost                    |
| No credentials or previews in any op                         | Asserted; server rejects unknown entity types and fields                                        |
| App works fully offline with sync enabled                    | Tested (ops queue, push on reconnect, failed sync changes nothing)                              |
| Tokens only in the keyring                                   | Tested against SQLite tables, DB/WAL bytes, exports and logs; real Keychain round trip on macOS |
| Merged PR → "Done?"; Waiting auto-returns on Jira status     | Tested with fixtures                                                                            |

## Library choices

Hand-rolled code was replaced by libraries where they fit:

| Need                       | Library                                                                           |
| -------------------------- | --------------------------------------------------------------------------------- |
| UI, MVVM                   | Avalonia, CommunityToolkit.Mvvm                                                   |
| Deterministic UUIDv5       | UUIDNext (random ids use built-in v7)                                             |
| CSV                        | CsvHelper                                                                         |
| Markdown checklists        | Markdig                                                                           |
| iCalendar, RRULE expansion | Ical.Net                                                                          |
| JSONPath                   | JsonPath.Net                                                                      |
| Password hashing           | Konscious Argon2                                                                  |
| Auth, API, ORM (server)    | ASP.NET Core JWT bearer, EF Core 10                                               |
| SQLite                     | Microsoft.Data.Sqlite + SQLitePCLRaw 2.1.13 (2.1.11 has a high-severity advisory) |

Kept hand-written, deliberately:

- **SQL migrator and repositories.** ADR-08: EF Core and reflection-based mappers are a poor fit for iOS and WASM trimming.
- **HLC.** No maintained, AOT-friendly package found that fits.
- **Fractional rank** for manual order lives in the hand-written layer for the same reason.

Behavior changes from the library swap: monthly `BYMONTHDAY=31` now skips short months (RFC 5545) instead of clamping, and in Markdown a bare line right after a list item continues that item.

## Deviations from the design docs

- Pace forecast prefers, but does not require, the same weekday (with 14 days of history a strict filter leaves about 2 samples).
- Flex streak: an unfinished period that can still hit its target is neutral.
- `DayNote` gained a `Kind` (`Note`, `WeeklyOutcomes`) so the weekly top-3 doesn't clash with the shutdown note.
- Board: items planned for a future day sit in Backlog. Timeline: past-planned items with no due date go to "No date".
- Live-link state hash includes the normalized `LinkState`, otherwise Blocked → In Progress would never fire the Waiting auto-return. A brand-new link starts as viewed.
- Server: added `GET /v1/sync/workspaces`; snapshot rows carry their own `field_clocks`; op clocks more than 24 h ahead are rejected; OAuth redeem takes an optional `code_verifier`.
- Undo reverts only the fields a command changed, so edits merged from other devices survive.
- Shutdown "tomorrow" records a defer so the carry isn't erased (our reading of the spec).
- New event types `TimeOfDayChanged` and `BreakDownUndone`; plan kinds use snake_case (`keep_today`, `undo_defer`).

## Not verified

Compiled and unit-tested, but never run for real:

- **Any real third-party service.** OAuth endpoints and scopes for all providers, GraphQL / REST payload shapes, Slack's admin-approval error codes: written from documentation and memory, tested against hand-written fixtures. The Notion token exchange (HTTP basic auth) is probably wrong.
- **macOS native features on a real desktop.** Tray icon, global hotkey firing, focus window and capture panel on screen, osascript notifications and capture-with-context (they need permission prompts), VoiceOver.
- **UI interaction.** Layout was checked with headless Skia screenshots (Today dark/light, review, command bar, settings, insights, help, board, timeline, habits, Today (all), weekly review). Pointer interactions (row clicks, flyouts) are untested; the board has keyboard moves only, no drag.
- **Postgres and Docker.** The Postgres provider is wired and its connection-string conversion tested, but no instance was run. The single-file binary was built and smoke-tested; the Docker image was not.

## Not done

- **Phase 5, dogfooding.** The MVP gate needs two weeks of daily use, and the risk register's premise (the loop sticks) is still untested.
- **Phase 9 client.** `Noto.Browser` WASM host, the SQLite-in-WASM / OPFS spike with its go/no-go, client-side keyring (Web Crypto + IndexedDB), web CSP polish.
- **Phase 12.** iOS and Android hosts, widgets, share targets. Windows/Linux native code compiles and its pure logic is tested, but it has only been run on macOS: the CI matrix (windows-latest, ubuntu-latest under Xvfb) is its first real exercise. Not done there: Wayland global hotkey (xdg-desktop-portal GlobalShortcuts), capture-with-context, Windows secrets over 2560 bytes.

## Known gaps and follow-ups

- No Redo (the bus has none). Rows aren't virtualized, so a very large backlog will be slow. The inspector edits plain Markdown with no rendering.
- `#tags` are parsed and shown as chips but not saved (no tag command on the bus).
- Habit grid: only today's cell can be ticked.
- `--capture` starts the app with the panel open; there is no single-instance IPC.
- Link chips and cards are not wired into the UI; Auto-prompting the Weekly Review is not wired.
- Today (all) assumes all workspaces share one logical day; capacity is not summed across workspaces.
- Importers can throw raw `JsonException` / `CsvHelperException`; Core should wrap them (the Settings view catches them).
- No hard purge of soft-deleted workspaces after 30 days (only the recovery check). Local tombstones are purged on request only.
- Server: no EF migrations (`EnsureCreated`), ingest serialized per process (multiple instances need a DB-level lock), conflict log is best-effort (a device whose pushed edit is overwritten gets no entry).
- Workspace tab accent colors do not repaint until the next refresh after a theme switch.
- Sync wiring is easy to get wrong: one shared `HybridClock` must go to both `SqliteUnitOfWork` and `CommandBus`, otherwise nothing is recorded (documented in `CLAUDE.md`).

## Suggested next steps

1. Run the macOS app and use it daily (Phase 5). That is the cheapest way to find what the tests cannot.
2. Do the Phase 9 SQLite/OPFS spike in a real browser before investing in the web client.
3. Run each provider against a real account, starting with GitHub and Linear (PAT-friendly).
4. Run the app on real Windows and Linux machines (Wayland and X11), then bring the mobile hosts up.
