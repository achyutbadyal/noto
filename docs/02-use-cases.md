# Noto — Use Cases & User Stories

Terms (**age**, **carry**, **defers**, **logical day**, item states) are defined in [04-domain-model.md](04-domain-model.md).

## Personas

### 🧑‍💻 Dev (Primary)

A software engineer tracking standup items, code reviews and sprint commitments. Wants to see at a glance what keeps slipping, and wants PRs and tickets in todos to show live status.

### 🧑‍🎓 Student

Tracks assignments, study goals and personal habits. Wants different workspaces for courses and personal life, and deadlines that are visible on a timeline.

### 🧑‍💼 Manager

Tracks follow-ups with reports, meeting action items and project milestones. Needs **Waiting on** to know what's been delegated and for how long.

### 🏃 Side-Project Hustler

Runs side projects alongside a day job. Needs workspace isolation and focus hours, so weekend ideas don't clutter Monday's work view and work doesn't leak into evenings.

---

## Core Use Cases

### UC-01: Daily Work Cycle

**Actor:** Dev · **Trigger:** First open after the workspace's day boundary

1. User opens Noto. Today shows the current logical day.
2. If items were carried over, a banner shows "5 carried · Review". The Morning Review (UC-09) opens automatically under `honest`/`relentless` pressure.
3. User adds new items for today. The capacity bar shows planned time against capacity (UC-10).
4. During the day, the user completes items. Done items move to "Done today".
5. Optionally, at the end of the day, Shutdown (UC-11) decides what to do with what's left.

**Acceptance criteria:**

- The day boundary is configurable per workspace (default 00:00 local; e.g. 04:00 for night owls) and evaluated in the workspace's time zone.
- Completed items stay visible in the logical day they were credited to, forever (time travel, UC-12).
- Carry for an unfinished item increases by exactly 1 per logical day passed while it was planned and Open. Rows show it as `↻N` once it's ≥ 1.

---

### UC-02: Workspace Management

**Actor:** Any · **Trigger:** User wants to separate concerns

1. User creates a workspace (or picks a template: Work, Personal, Health, Side Projects).
2. Each workspace has its own items, mode, color, capacity, time settings and sync toggle.
3. User switches via the sidebar or ⌘1–⌘9. ⌘0 opens the Today (all) lens.
4. Optional focus hours hide a workspace from Today (all) and quiet its badges outside those hours.

**Acceptance criteria:**

- No data appears in another workspace's views, except in the explicit Today (all) lens.
- Each workspace can have its own color.
- Workspace switching takes < 100ms.
- Deletion is soft, with a 30-day recovery period.

---

### UC-03: Accountability Through Visibility

**Actor:** Any · **Trigger:** Items slip past their planned day

1. An item is created on Monday, planned for Monday.
2. On Tuesday it's still open: carry ↻1, pressure state per the workspace's pressure level.
3. As carry grows, the row escalates (warm → hot → stale) and eventually shows a `stuck?` chip (UC-13).
4. The user can list items by carry ("show items carried > 5") and see trends in Insights.

**Acceptance criteria:**

- Carry is always visible on the row when ≥ 1. Age and defers are visible in the inspector and tooltip.
- Escalation thresholds come from the workspace's pressure level ([03 › Pressure](03-modes-and-workspaces.md#pressure)) and can be overridden per workspace.
- Signals never rely on color alone (glyph + number + position).
- Insights show rollover-rate trend, median carry at completion and stuck-reason mix over time.

---

### UC-04: Mode Switching

**Actor:** Any · **Trigger:** User wants a different lens

1. User picks a preset (Sprint, Zen, Deadline, Habit, Kanban, Accountability) or changes layout, order or pressure directly.
2. The UI adapts immediately.

**Acceptance criteria:**

- The switch is instant and **writes nothing to items**.
- Each layout remembers its own settings per workspace.
- Items lacking a layout's natural field appear in a labeled lane ("No date", "Not habits").

---

### UC-05: Cross-Device Sync (Optional)

**Actor:** Any · **Trigger:** User works on multiple devices

1. User signs in to a Noto backend (hosted or self-hosted) and enables sync per workspace.
2. Changes appear on other devices within seconds while they're online.
3. Offline changes queue locally and sync on reconnect.

**Acceptance criteria:**

- Native apps work fully offline. Sync is additive.
- Concurrent edits to **different fields** of the same item both survive (e.g. completed on phone + renamed on laptop).
- Concurrent edits to the **same text field**: one wins deterministically, and the other is kept in a conflict log viewable from the item. No silent loss.
- Device clock skew doesn't decide outcomes (hybrid logical clocks).
- Per-workspace sync can be disabled and re-enabled. Re-enabling merges field by field without overwriting either side wholesale.
- Credentials and link previews are never synced.

---

### UC-06: Recurring Items & Habits

**Actor:** Dev, Manager, Health-focused user · **Trigger:** Items that repeat

1. User makes an item recurring (daily, weekdays, MWF, weekly, monthly, every N days).
2. User chooses **task-like** (a missed occurrence carries) or **habit-like** (a missed day is recorded as missed, and nothing carries). Habit-like items can have a flex target, e.g. "5 per week".
3. Noto creates today's instance when the day is viewed.

**Acceptance criteria:**

- No future instances are created in advance.
- Two devices generate the same instance id for the same rule and date (no duplicates after sync).
- After days away, a task-like rule creates only the most recent missed instance. Earlier misses are reported, not materialized.
- Editing the rule affects instances not yet generated, not existing ones.
- Each instance starts with age 0 and carry 0.

---

### UC-07: Quick Capture

**Actor:** Any · **Trigger:** A thought needs capturing now

1. User presses the global hotkey (default ⌃⌥Space, configurable) or uses the menubar item.
2. A small floating input appears over the current app.
3. User types, with optional tokens (`tomorrow ~30m !2 #tag /workspace`), and presses Enter.
4. On macOS, if the frontmost app is a browser or Slack, an "attach current page" chip offers its URL.

**Acceptance criteria:**

- < 2s from hotkey to item saved.
- Works when the main window isn't focused or is closed (menubar app keeps running).
- The default workspace follows focus hours, falling back to a configured default. The default plan date is today (configurable: today / unscheduled).
- Where a global hotkey isn't available (Wayland without a portal, web), the UI says so and offers an alternative.

---

### UC-08: Connected Apps & Live Links

**Actor:** Dev, Manager · **Trigger:** A todo contains a URL from a connected app

1. User connects an app in Settings › Connected Apps via OAuth or a personal token.
2. User pastes a GitHub PR / Jira / Linear / Slack URL into a todo.
3. A one-line chip shows live status. The inspector shows the full card.
4. When the linked object changes meaningfully, the chip shows a dot. When it's merged or closed, Noto suggests completing the todo. A Waiting item linked to a ticket that gets unblocked returns to Today.

**Acceptance criteria:**

- Native: credentials stored in the OS keyring; provider APIs called directly from the device.
- Web: OAuth and API calls go through the Noto backend's Connected Apps Gateway, which doesn't store tokens. Without a backend, links are plain.
- Credentials are never synced or exported. Previews are device-local.
- Chip renders within 2s from cache, or 5s on first fetch.
- If the app isn't connected, the link is plain and clickable, with a one-time "Connect X" hint.
- Expired tokens refresh automatically. If refresh fails, the chip shows "🔑 reconnect".
- Multiple URLs in one item each get a chip (at most 2 shown in the row, all of them in the inspector).
- Custom apps: token/API key + URL pattern + OpenGraph or JSONPath mapping, optionally with state mapping.
- Automatic completion from link state is opt-in, reversible and recorded in history.

---

### UC-09: Morning Review (Carry-over Decisions)

**Actor:** Any · **Trigger:** First open of a workspace after its day boundary with ≥ 1 item needing a decision

1. Noto shows carried items one at a time, with age, carry, defers and any live-link evidence.
2. For each, the user presses one key: **T** today, **D** defer (natural-language date), **S** someday, **B** break down, **W** waiting on, **X** drop (optional reason), **✓** already done (credited to yesterday).
3. The capacity bar updates live as items are kept.

**Acceptance criteria:**

- Each decision is one keystroke (plus a date or reason where applicable). ⌘Z undoes it.
- Median review time for 5 items is < 30s (measured with opt-in telemetry).
- "Keep all for today" exists and shows the capacity consequence.
- Under `relentless` pressure, the review can't be skipped as a whole.
- Defers never reset carry. They stop carry from growing until the new date.

---

### UC-10: Honest Planning (Capacity & Defer)

**Actor:** Any · **Trigger:** Planning today or a future day

1. Items take optional estimates (`~15m`, `~2h`, `~s/m/l`). Missing estimates count as the user's historical median, marked with `*`.
2. Today shows a capacity bar. When overcommitted, Noto suggests which items to defer.
3. Items can be planned for any future day, or left unscheduled in the Backlog.
4. With 14+ days of history, a forecast shows "On days like this you usually finish 5 of 8".

**Acceptance criteria:**

- Capacity is per workspace, in minutes or items. Today (all) sums across workspaces.
- Future days show their planned items. Unscheduled and Someday items never accrue carry.

---

### UC-11: Shutdown

**Actor:** Any · **Trigger:** End of focus hours, menubar action, or ⌘⇧D

1. Review what got done today.
2. Decide on each unfinished item (same keys as UC-09).
3. Optionally leave a one-line note for tomorrow. It appears at the top of tomorrow's Morning Review.

**Acceptance criteria:** items decided in Shutdown don't appear in the next Morning Review.

---

### UC-12: Time Travel & Weekly Review

**Actor:** Any · **Trigger:** "What happened Tuesday?" / end of week

1. The day strip and `[` `]` navigate to any past day: what was planned, done, carried and decided, read-only.
2. Weekly Review walks through wins, a Someday sweep, stuck patterns, estimate accuracy and next week's top 3 outcomes.

**Acceptance criteria:**

- Any past day can be reconstructed exactly from items and their history. Nothing is purged.
- Insights appear only with a sample size ≥ 10.

---

### UC-13: Stuck Items

**Actor:** Any · **Trigger:** An item reaches the workspace's stuck threshold (carry or defers)

1. The row shows `stuck?`, and the inspector asks why: too big / blocked / unclear / don't want to / not needed.
2. Each answer offers a concrete fix (break down, waiting on, rewrite as next action, make it Now with a timer, drop).

**Acceptance criteria:** the reason is recorded as an event and aggregated in Insights.

---

### UC-14: Waiting On

**Actor:** Manager, Dev · **Trigger:** An item is blocked on someone or something

1. Press **W**, enter a person or paste a link.
2. The item moves to "Waiting on" and stops accruing carry.
3. It returns to Planned manually, or automatically when its linked ticket or PR is unblocked.

**Acceptance criteria:** Insights show median waiting time per person.

---

### UC-15: Import & Export

**Actor:** New user switching apps; any user

1. Import from Todoist, Things, Apple Reminders, TickTick (their export formats), or a Markdown checklist.
2. Export everything as JSON (and items as CSV) from Settings.

**Acceptance criteria:**

- Imported open items start with carry 0 (history from another app isn't held against the user).
- Exports exclude credentials and link previews.

---

### UC-16: Subtasks (Break Down)

**Actor:** Any · **Trigger:** An item is too big

1. Press **B** (or answer "too big"). Type 2–5 subtasks inline.
2. The original becomes a container showing `n/m` progress. The first subtask takes its planned day.

**Acceptance criteria:**

- One level of nesting only.
- The container never appears in Today itself, and its carry history is kept.
- Completing the last subtask completes the container (with undo).

---

## Non-Functional Requirements

| Area          | Requirement                                                                                                                                                                        |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Performance   | Cold start < 1s to interactive Today (Apple Silicon). Workspace switch < 100ms. 10k items / 200k events per workspace without jank.                                                |
| Offline       | Every feature except connected-app refresh and sync works offline.                                                                                                                 |
| Time          | Correct across time zones, travel and DST ([04 › Time Semantics](04-domain-model.md#3-time-semantics)).                                                                            |
| Accessibility | WCAG 2.1 AA contrast in both themes. Full keyboard operation. Screen reader labels on every row. OS reduce-motion respected. Body text scalable from 13 to 18pt.                   |
| Privacy       | No telemetry without opt-in. Credentials never leave native devices. Link previews never synced.                                                                                   |
| Data safety   | All writes are transactional. Undo for 10 minutes. Soft delete for workspaces (30 days).                                                                                           |
| Notifications | Off by default except: morning nudge if decisions are pending, Now timer done, explicitly subscribed live-link events ([07 › Notifications](07-ui-ux-design.md#17-notifications)). |

---

## Edge Cases & Considerations

| Scenario                                       | Behavior                                                                                                                        |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| User doesn't open Noto for 3 days              | Carry includes all 3 days. Stats for those days are computed on demand. Morning Review covers everything that needs a decision. |
| Item completed on a later day than planned     | Credited to the day it was completed (or to yesterday via "Already done"). Creation date and carry are preserved.               |
| Workspace deleted                              | Soft-delete, 30-day recovery.                                                                                                   |
| Day boundary passes while the app is open      | Banner "It's a new day. N items to decide". The list doesn't reflow under the user.                                             |
| User flies across time zones                   | "Today" follows the device zone (default). Past events keep their recorded zone, so history doesn't shift.                      |
| DST change at the day boundary                 | Skipped hour → the day starts at the next valid instant. Repeated hour → the first occurrence.                                  |
| Same item edited on two offline devices        | Per-field merge. Same-field text conflicts are kept in the conflict log.                                                        |
| Two devices open on a new day at the same time | No conflict: day change writes nothing, and recurrence instances have deterministic ids.                                        |
| 100+ items in a day                            | Virtualized list. The capacity bar makes the overcommitment obvious and suggests deferrals.                                     |
| URL pasted but app not connected               | Plain link, plus a one-time "Connect [App] for live status" hint.                                                               |
| OAuth token expires mid-session                | Background refresh. If it fails, the chip shows 🔑 reconnect.                                                                   |
| Linked resource deleted or inaccessible        | Chip shows unavailable, with a "remove link" action.                                                                            |
| Multiple URLs from different apps in one item  | Each gets a chip, fetched in parallel (batched per provider).                                                                   |
| Self-hosted app URL (e.g. on-prem Jira)        | Native: per-connection `instance_url`. Web: only via a self-hosted gateway with that host allowlisted.                          |
| Provider rate limit hit                        | Exponential backoff with jitter. Cached data served. Retry queued.                                                              |
| Slack workspace requires admin approval        | Connect flow explains this and offers a copyable approval-request message.                                                      |
