# Noto — Design Review (2026-10-07)

Review of `01`–`10` for **feasibility**, **requirements** and **correctness**. Severity: 🔴 blocks a correct v1 · 🟠 will cause rework if not fixed before the relevant phase · 🟡 cleanup.

`07-ui-ux-design.md` was rewritten as part of this review (v2).

## Resolution Status (2026-10-07)

All findings below have been applied to the docs. The original findings are kept unchanged underneath for traceability.

| Finding | Resolution | Where |
| ------- | ---------- | ----- |
| C1 Age definitions | Age/carry/defers derived from state + events using logical dates (tz + day boundary). Stored `age_days` removed. Off-by-one fixed. | 04 §3–4.1, 02 UC-01/03 |
| C2 Escalation scales | Single pressure table (gentle/honest/relentless) applied to **carry** | 03 › Pressure |
| C3 Rollover algorithm | Rollover is a query. Day change writes nothing. Snapshots replaced by derived local caches. Deterministic recurrence ids. | 04 §2.5, §4.3, §5 |
| C4 Sync data loss | Op-log, per-field LWW with HLC, server `seq` cursor, pagination, tombstones, snapshot bootstrap, defined per-workspace toggle behavior | 05 › Sync, 06 › Sync |
| C5 Model inconsistencies | `MODE_CONFIG` replaced by workspace fields + `layout_settings`; recurrence cycle removed; credentials out of the DB; previews keyed by URL and local-only; `User` server-only; streaks derived | 04 §1–2, §6 |
| C6 Modes vs data | Modes = presets over Layout × Order × Pressure; switching guarantees per layout; board columns mapped to state | 03 |
| C7 Roadmap errors | Roadmap rewritten: single numbering, macOS-only MVP, recurrence with Habit layout, search in the core loop, digest moved to Sync tier, keyboard map fixed in 07 §12 | 08, 03, 09 |
| C8 Cleanup | .NET 10, compose fixed (required secrets, no `version:`), Avalonia a11y terms, AA palette, Slack `conversations.replies` + no "resolved" state, Figma `/design/` URLs, FluentAssertions licensing noted | 05, 06, 07, 10 |
| R1 Decisions & planning | `planned_for`, Waiting, Someday, Unscheduled, Dropped + reason, Deferred events; Morning Review, Shutdown, capacity | 04, 02 UC-09…16, 07 §4 |
| R2 Missing requirements | NFR table, time-zone/DST edge cases, subtasks (UC-16), import (UC-15), notifications, sync re-enable behavior, quick capture as platform work | 02, 05 ADR-09, 08 Phase 6 |
| R3 Undecided items | Web = sync client (no CRUD endpoints); previews never synced; Teams removed from projections | 06, 10, 09 |
| F1 OAuth / credentials | **Owner decision:** native clients keep tokens in the OS keyring and call providers directly; embedded native OAuth client secrets accepted as a known trade-off (RFC 8252 public clients; PKCE, minimal scopes, rotation; risk register). PAT paths are always available. Slack/Notion/Atlassian org constraints documented. | 10, 08 › Risks |
| F2 Web connected apps | Backend **Connected Apps Gateway**: OAuth broker + allowlisted, read-only, SSRF-guarded fetch proxy; tokens not stored server-side | 06 › Gateway, 10 › Web, 05 ADR-06 |
| F3 Stack | Clients use Microsoft.Data.Sqlite + SQL migrations (no EF Core); server keeps EF Core; WASM SQLite/OPFS spike with fallback | 05 ADR-08, 08 Phase 9 |
| F4 Schedule | Reordered (loop → dogfood → native → connected apps → sync → web → layouts → platforms), working days, +20% contingency, milestones | 08 |
| F5 Business | Sunsama/Akiflow/Marvin added; differentiation restated; conversion 3–4%, DAU/MAU > 35%; Sync tier includes hosted gateway; Teams future-only | 09 |

---

## Verdict

The concept is sound, and the docs are unusually thorough for a pre-code project. The **local-first, single-user, macOS MVP is feasible**. Three things need fixing before Phase 1 starts:

1. **The core loop is incomplete as a product.** Items roll over, but nothing asks the user to decide anything about them. The model also has no "planned for" date, so users can't defer or plan ahead. Without these, the "accountability" feature degrades into an ever-growing red list. (Addressed in UI v2; the model changes are below.)
2. **Rollover and sync are designed as device-side mutations with whole-entity last-write-wins.** That breaks as soon as there are two devices. Making rollover and snapshots *derived* fixes most of it cheaply.
3. **Connected Apps can't be built as specified** on WASM, and not without a server for the OAuth providers that require a client secret. That contradicts "credentials never leave the device / backend optional". It's also scheduled before sync and before the web build, even though web is P1.

---

## 1. Correctness

### 🔴 C1 — Age is defined three different ways

- `UC-03`: created on Day 1, "On Day 2 … age badge '2 days'". `04 › Age Calculation Rules`: "Created yesterday … 1 day". That's an off-by-one between the two docs.
- `04 › TodoItem`: `AgeDays => DateOnly.FromDateTime(DateTime.Now) - OriginalDate`.
  - It ignores the workspace `day_boundary`. At 2 AM with a 4 AM boundary, the age is already one day too high.
  - It doesn't freeze at completion or cancellation, contradicting the rules table in the same doc.
  - It uses local `DateTime.Now`, so it depends on the time zone (it shifts when traveling).
- The ERD **also** stores `age_days` as a column. A stored copy and a computed property will diverge.

**Fix:** store nothing. Compute `logical_date(ts, workspace)` = the local date of `ts - day_boundary` in the workspace's IANA time zone. Age = `logical_date(end ?? now) - logical_date(created_at)`, where `end` is `completed_at`/`dropped_at`. Use `DateTimeOffset` (or UTC + tz id) everywhere.

### 🔴 C2 — Three conflicting escalation scales

| Source                        | Amber | Orange | Red  | Extreme   |
| ----------------------------- | ----- | ------ | ---- | --------- |
| 03 Sprint                     | 3–4   | —      | 5+   | —         |
| 03 Accountability             | 3     | —      | 7    | 14 (skull) |
| 07 v1 color table             | 3–4   | 5–6    | 7–13 | 14+       |
| 07 v1 tokens (`accent-amber`) | 3–6   | —      | 7+   | —         |

UC-03 says escalation is "configurable per workspace", but no doc says where that configuration lives. **Fix:** one `pressure` setting with thresholds (UI v2 §9), stored on the workspace/mode config.

### 🔴 C3 — Rollover algorithm

`04 › Rollover Algorithm`:

- It writes a `RolledOver` history event for **every active item, every day**. 50 open items for a year means about 18k events of noise, and each one is a sync payload.
- It only finalizes `yesterday`. The edge case in `02` ("doesn't open for 3 days") leaves two days unfinalized and without snapshots, so stats and streaks for those days are wrong.
- "Idempotent" is claimed, but nothing enforces it. There's no unique `(workspace_id, snapshot_date)` constraint and no guard on event emission.
- **Multi-device:** both devices run rollover independently, producing duplicate snapshots and events that sync as "conflicts".
- `if item.OriginalDate < today → rolled over`: an item created *for* a future date (deferred) would be counted as rolled over. There's no `planned_for` field to distinguish the two cases (see R1).

**Fix:** make rollover a **query**, not a write. Today's view = `status = Active AND planned_for <= today` (+ completed today). Carry count = the number of logical days between `planned_for` and today (or end), minus days spent Waiting. Snapshots become a **derived cache**, recomputed deterministically from items and history for any date range. Then they never need to sync, they self-heal after gaps, and multi-device rollover disappears as a problem.

### 🔴 C4 — Sync design loses data

`05 › Sync Rules`, `06 › POST /sync`:

- **Whole-entity LWW by `UpdatedAt` from client clocks.** Completing an item on the phone while renaming it on the laptop means one edit is lost. Client clock skew decides the winner. UC-05's "no data loss" is not met. **Fix:** per-field LWW with a hybrid logical clock (HLC), or an op-log of field-level changes. That's still simple, and it's the "CRDT-inspired" approach `05` gestures at.
- **`last_sync_at` timestamp cursor.** Changes committed on the server with an earlier timestamp than the cursor get skipped. **Fix:** a server-assigned monotonic sequence number as the cursor.
- Hard `Delete` with no tombstone semantics, and no sync-response pagination (first sync on a new device could be huge).
- `TodoItem.Version` ("optimistic concurrency") is defined but never used by the LWW rules.
- **Per-workspace sync toggle vs user-level sync log.** Turning sync off and back on for a workspace has no defined backfill behavior.

### 🟠 C5 — Domain model inconsistencies (`04`)

- `WORKSPACE }|--|| MODE_CONFIG` reads "many workspaces share one mode config". It should be 1:N, one config per (workspace, mode), so each mode remembers its settings.
- `RECURRENCE_RULE.template_todo_id` and `TODO_ITEM.recurrence_rule_id` create a cycle. Instances need `(rule_id, occurrence_date)` with a **deterministic id** (e.g. a UUIDv5 of both). Otherwise two devices generating "on demand" create duplicate instances.
- `APP_CREDENTIAL` is a table in the ERD with an FK from `LINK_PREVIEW`, but `05 ADR-06`/`10` say credentials live in a separate encrypted file, "never in the main DB".
- `LINK_PREVIEW.todo_id`: the same URL in multiple todos is fetched and cached once per todo. Key the cache by normalized URL, with a separate join table.
- Priority `0–4` in the model vs API filter `1,2,3,4` (0 = "none" isn't filterable). `TodoStatus.Archived` overlaps with "completed items are archived but visible".
- Habit `streak_current`/`streak_longest` are listed as stored fields (`03`). They're derivable, and storing them adds another sync-conflict source.
- `USER` entity with email in a "no account required" local DB. Make it optional / server-only.

### 🟠 C6 — "Modes change behavior, not data" is contradicted by the modes themselves

- Deadline makes `due_date` **required**. Switching an existing workspace into Deadline leaves invalid items.
- Habit makes "all items implicitly recurring". Kanban has "no daily rollover". Switching Sprint → Kanban → Sprint has undefined rollover/age state.
- Kanban "Done" column vs `Completed` status: two sources of truth.
- Zen's "Someday" bucket is data, not a lens. Habit's "time-of-day order" needs a field that doesn't exist.
- ADR-05 says "adding new modes doesn't require code changes", but board, timeline and heatmap layouts are code.

UI v2 §9 resolves this by splitting modes into **Layout × Order × Pressure** presets and defining the switching behavior.

### 🟠 C7 — Roadmap document errors (`08`)

- **"Phase 5" appears twice** (Sync & Backend at line ~185 and Connected Apps), and Sync & Backend is specified twice (once as Phase 5, once as Phase 6). The Gantt chart has Connected Apps as Phase 5.
- The MVP says "Desktop (Windows + macOS + Linux)". `01` says macOS P0 and Windows/Linux P2.
- **Phase 3 Habit mode** ("recurring-by-default") depends on the **Phase 8** recurrence engine.
- Phase 4 "all keyboard shortcuts" includes Search (⌘F), which is built in Phase 8.
- Accountability mode's "weekly email digest" (`03`) needs a server and email. It's sold as a paid Sync feature in `09` but described as a free mode feature in `03`.
- The `v1` keyboard map collided with OS shortcuts: ⌘M (minimize), ⌘S, ⌥Space / Alt+Space (Windows window menu), and ⌘←/→/⌫ inside text fields. Fixed in UI v2 §12.

### 🟡 C8 — Smaller items

- `01`/`05` target `net8.0`. .NET 8 support ends Nov 2026; target **.NET 10 (LTS)**. `netstandard2.1` in `Noto.Core` is unnecessary since there's no consumer that needs it.
- `docker-compose.yml` uses the obsolete `version:` key. The default `POSTGRES_PASSWORD=password` and `JWT_SECRET` should be required env vars with no defaults.
- `07 v1` accessibility used web terms (`aria-label`, `prefers-reduced-motion`). Avalonia uses `AutomationProperties` and OS settings.
- `07 v1` palette contrast: `text-tertiary #5E5E72` on `#1A1A24` = 2.7:1, the 14+ badge `#8B2020` = 1.9:1, `accent-red` = 3.9:1. All fail WCAG AA.
- Snapshot "automatic archival after 90 days" (`05`) contradicts "time travel to any previous day" (`04`) and monthly stats. Moot if snapshots are derived (C3).
- `10`: Slack has **no "thread resolved" concept** in its API. Threads come from `conversations.replies`, not `conversations.history`. Figma URLs are now `/design/…`, not only `/file/…`.

---

## 2. Requirements

### 🔴 R1 — Missing: deciding about carried items, and planning ahead

The model has `original_date` but no **`planned_for`**, so:

- There's no defer / "not today" (Zen's "Someday" is the only escape hatch, and only in one mode).
- The "tomorrow →" navigation in the `07 v1` wireframe has nothing to show.
- Rollover can't tell "procrastinated" from "deliberately scheduled".

This is the single biggest product gap. If every unfinished item lands on today forever, Today becomes a backlog, and accountability turns into noise. **Add:** `planned_for`, `Waiting` status, `Someday` flag, `Dropped` + reason, `Deferred` events, and derived carry/defer counts (UI v2 §2, §4, §14).

### 🟠 R2 — Requirements without acceptance criteria or owners

- **Quick capture "< 2s" and "works when the app isn't focused".** Avalonia has no global hotkey or menubar-capture API. This is native interop on each OS (Carbon `RegisterEventHotKey`, Win32 `RegisterHotKey`; Linux/Wayland has no general solution). It's scheduled as a 2-day UI task.
- **"Workspace switching < 100ms".** OK, but there's no NFR for cold start, DB size or memory. Set a budget: cold start < 1s on Apple Silicon, 10k items without jank.
- **"Sync can be disabled/enabled per workspace"** has no defined behavior for re-enable (backfill? conflict policy?).
- **Notifications** (Phase 8) have no requirements beyond "reminders, OS notifications, digest".
- **Subtasks:** `parent_id` exists, but no use case, UI or rollover rule covers them. Does a parent carry when a subtask carries?
- **Time zones / travel / DST:** no requirement at all, despite day boundaries being the core mechanic.
- **Data portability:** export is "JSON/CSV", but there's no **import** from Todoist, Things or Reminders. That import is the main adoption path for a todo app.

### 🟠 R3 — Undecided, but load-bearing

- Is the web (WASM) build **local-only** (data in browser storage, which the browser can evict) or **server-backed**? `06` adds CRUD endpoints "for web-only users", which creates a second write path that bypasses the sync engine. Pick one. Recommendation: WASM is just another sync client; drop the CRUD endpoints; keep the read-only stats endpoints only if a non-app consumer exists.
- **Do link previews sync?** They're "cached in main SQLite" (`10`), which syncs. Preview content comes from *private* Slack, Jira and GitHub data fetched with the user's credentials. Syncing it sends private third-party content to the Noto server, which contradicts "privacy-first". Recommendation: device-local, never synced (each device refetches).
- **Teams tier** (`09`, $8/user) has no domain model: no sharing, permissions or multi-user conflict model. Per-entity LWW is the wrong basis for collaboration. Treat it as a post-v1 research item and remove it from the revenue projections until it's designed.

---

## 3. Feasibility

### 🔴 F1 — Connected Apps OAuth needs a server

- **Slack and Atlassian (Jira/Confluence Cloud) OAuth require a `client_secret` for the code exchange.** GitHub OAuth Apps traditionally do too. A secret shipped inside a desktop or WASM binary is public. Options:
  - (a) a tiny token-exchange relay run by Noto. Tokens pass through it, which contradicts "never leave the device", so the wording needs to change.
  - (b) **PATs / API tokens only for v1.** GitHub fine-grained PAT, Atlassian API token, Linear personal API key, GitLab PAT. This is fully local, works today, and fits the developer persona.
  - (c) device flow where supported (GitHub supports it).

  **Recommendation:** ship (b) + (c) first, and add the OAuth relay with Sync.
- **Slack specifically:** reading threads needs a Slack app installed into the user's workspace. Many company workspaces require admin approval for third-party apps, which will block a large share of the "Dev/Manager" persona. Plan for this, or start with GitHub/Jira/Linear.
- **Notion:** integrations only see pages explicitly shared with them, so arbitrary pasted Notion URLs will mostly fail.

### 🔴 F2 — Connected Apps on WASM

Browsers block cross-origin calls to Slack, Jira and most APIs (CORS), and OpenGraph scraping of arbitrary sites is impossible from a page. WASM previews **require a proxy server**. That puts tokens and fetched content on the server, which contradicts F1's privacy model. The "Web Crypto + passphrase" credential store doesn't solve this. **Decide:** connected apps are desktop/mobile-only, or web requires Sync and a declared server-side fetch.

### 🟠 F3 — Stack choices

- **EF Core on WASM and iOS (AOT).** EF Core's AOT/trimming support is still partial (precompiled queries are experimental), and iOS forbids JIT. ADR-02 picks CommunityToolkit.Mvvm *for* AOT-friendliness, then pairs it with the least AOT-friendly piece of the stack. For a ~10-table schema, consider `Microsoft.Data.Sqlite` + hand-written SQL (or Dapper/AOT-friendly mapping) in a repository layer. Keep migrations as numbered SQL scripts. This also makes the WASM SQLite story (OPFS-backed wasm SQLite) less risky.
- **Avalonia for a macOS-first app.** It's workable, but it won't feel native: menus, text input, scrolling physics and VoiceOver support all need deliberate work. Menubar, global hotkey, share extension and widgets are all native code regardless. Budget a "macOS polish" phase explicitly. (This review doesn't recommend changing frameworks: cross-platform from one codebase is a stated goal.)
- **Avalonia WASM on mobile browsers.** IME/text input and performance are weak spots. Don't count it as "covers all other platforms" (`01`).

### 🟠 F4 — Schedule

- The Gantt chart is **fully sequential, about 156 working days** (~7.5 months full-time solo), with no buffer, design time, beta or bug-fix phase, using calendar dates that include weekends.
- 29 days of Connected Apps come **before** Sync and **before** WASM, although web is P1 and Connected Apps depend on decisions only Sync can answer (F1/F2).
- Six modes in 20 days ("each mode is essentially a mini-app", per the roadmap's own risk register).

**Suggested order:**
1. Foundation + derived rollover + the Today/Review/Shutdown loop (macOS).
2. Sprint and Zen presets.
3. **Dogfood 2 weeks.**
4. Quick capture (native).
5. GitHub/Jira/Linear via PAT with live links.
6. Sync (HLC op-log) + WASM client.
7. Board/Timeline/Habit layouts.
8. OAuth relay, mobile.

### 🟡 F5 — Business (`09`)

- **Missing competitors:** Sunsama (daily planning ritual with task rollover, plus Jira/GitHub/Linear/Slack integrations), Akiflow, Amazing Marvin (procrastination/stale-task features), Things (Logbook). The moat claim "no other TODO app makes item age a first-class citizen" is weak against Sunsama. The defensible difference is **decisions + reasons + insights, local-first, at a fraction of Sunsama's price** (UI v2 §0, §5, §8).
- **Assumptions:** 5% free→paid is the top of the typical freemium range, and DAU/MAU > 60% is well above what productivity apps usually achieve. Treat these as stretch numbers, not conservative ones.
- **Open-core conflict:** a free self-hostable sync server competes directly with the $4 Sync tier. That's fine (Bitwarden-style), but the paid tier then needs a reason beyond sync, such as hosted OAuth relay, email digest, backups and web access.

---

## 4. Action List (before Phase 1)

| #   | Action                                                                               | Docs     |
| --- | ------------------------------------------------------------------------------------ | -------- |
| 1   | Add `planned_for`, `Waiting`, `Someday`, `Dropped`+reason, estimates; derive age/carry; remove stored `age_days` | 03, 04 |
| 2   | Rewrite rollover as a query; make snapshots a derived, non-synced cache              | 04, 05   |
| 3   | Replace whole-entity LWW with per-field HLC; use a server sequence cursor; add tombstones | 05, 06 |
| 4   | Define time zone / day-boundary semantics (`DateTimeOffset`, IANA tz per workspace)  | 04       |
| 5   | Restructure modes as Layout × Order × Pressure presets; one escalation config        | 03       |
| 6   | Decide WASM data model (sync client, not CRUD); drop CRUD endpoints or justify them  | 06       |
| 7   | Connected Apps v1 = PAT/device-flow, desktop only, previews never synced; OAuth relay later | 10 |
| 8   | Fix roadmap numbering/duplication, dependency order, add buffer and dogfood phases   | 08       |
| 9   | Target .NET 10; reconsider EF Core for AOT targets                                   | 05       |
| 10  | Add competitors and recalibrate assumptions; remove Teams revenue until designed     | 09       |
