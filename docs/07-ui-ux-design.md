# Noto — UI/UX Design Specification (v2)

> v2 replaces the original spec. It keeps Noto's core idea (todos age and roll over) and changes how that idea reaches the user. Section 14 maps each UI concept to its definition in the domain model and modes docs.

---

## 0. The Problem This Design Solves

Every todo app can show a list. Noto's premise is that unfinished items roll over and age visibly. Taken alone, that premise fails in a predictable way:

> **Rollover without decisions turns into a guilt pile.** Today's list holds everything you didn't finish, so it grows every day, the red badges stop meaning anything, and the user declares "todo bankruptcy" and leaves.

Apps today handle stale tasks in one of three ways:

- **Ignore them.** Todoist and Things let overdue items sit quietly or pile into an "Overdue" bucket.
- **Move them silently.** Planners like Sunsama roll unfinished tasks forward and show a count, but nothing asks _why_ the task didn't happen.
- **Gamify them.** Habitica turns staleness into a game.

None of them turn the moment an item gets carried over into a **decision**, and none of them learn from those decisions. That is the design gap Noto fills:

> **Noto's promise: nothing rots silently. Every carried item gets a decision, every decision is one keystroke, and Noto learns from your decisions so it can help you plan more honestly.**

Everything below serves that loop:

```
   Plan honestly ──► Do ──► Close the day ──► Carry-over decisions ──► Learn
        ▲                                                               │
        └───────────── insights feed tomorrow's plan ◄──────────────────┘
```

---

## 1. Design Principles

| #   | Principle                                          | What it means in practice                                                                                                         |
| --- | -------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Decisions, not reminders**                       | A carried item never just reappears. It shows up with one-keystroke choices: keep, defer, someday, split, wait, drop.             |
| 2   | **Honest, not harsh**                              | Pressure is visible and the user can tune it. No skulls, no "Wall of Shame", no pulsing badges. Show facts, then offer a way out. |
| 3   | **The plan should be finishable**                  | Today shows capacity next to commitments. Overcommitting is visible _before_ the day starts, not as rollover the next morning.    |
| 4   | **Keyboard-first, pointer-complete, touch-native** | Every action has a key. Every key action is also reachable by pointer. Every pointer action has a gesture on mobile.              |
| 5   | **Links are live**                                 | A todo that points at a PR or ticket reflects that object's state and can react to it. A link is more than a preview card.        |
| 6   | **Density you choose**                             | Compact, Comfortable and Spacious densities. Rich content (previews, history) shows on focus, not on every row.                   |
| 7   | **Undo instead of confirm**                        | No "Are you sure?" dialogs. Destructive actions run immediately and show an undo toast (⌘Z works for 10 minutes).                 |
| 8   | **Never color alone**                              | Every color signal also has text, a shape or a position. The palette passes WCAG AA in both themes (section 11).                  |

---

## 2. Vocabulary the UI Uses

The original docs used "age" for two different things. The UI needs three distinct, visible numbers:

| Term       | Definition                                                                                | Drives                                    |
| ---------- | ----------------------------------------------------------------------------------------- | ----------------------------------------- |
| **Age**    | Days since the item was created.                                                          | Information only (detail panel, tooltip). |
| **Carry**  | Number of day boundaries the item crossed while it was planned for that day and not done. | **Pressure** (escalation color and copy). |
| **Defers** | Number of times the user explicitly moved the item to a later date.                       | Insights ("deferred 4×"), stuck prompts.  |

**Why this split matters:** an item created two weeks ago and deliberately planned for today should not look rotten on its first day. Pressure comes from _broken intentions_ (carries), not from time passing. Defers are honest (the user chose them), but they still count, so deferring can't be used to escape accountability forever.

Row display rule: show **carry** when it is ≥ 1 (e.g. `↻3`), otherwise show nothing. Age shows in the detail panel and tooltip.

---

## 3. App Shell (Desktop, macOS-first)

```
┌──────┬──────────────────────────────────────────────────────┬─────────────────────────┐
│ ●●●  │  Today · Wed Oct 7          ◀ ▶      ⌘K Ask or jump… │  Inspector (⌘I)         │
│      │  ▁▂▅▇▃▆█  ←── day strip (last 14 days)                │                         │
│ ⌂    ├──────────────────────────────────────────────────────┤  Deploy v2.3 to staging │
│ Today│  Capacity  ██████████████░░░░  5h 10m / 6h  ✓ fits   │  Work · Sprint          │
│      │                                                      │                         │
│ WORK │  ▸ NOW                                               │  Age 9d · Carry ↻4      │
│ ▌Work│    ◉  Deploy v2.3 to staging     ↻4   ~1h   ⏱ 23:10   │  Defers 1               │
│  Pers│                                                      │                         │
│  Hlth│  ▸ PLANNED (6)                                       │  ⚠ Stuck? Pick a reason  │
│  Side│    ○  Review PR #482  ⟨GH · 2 approvals · CI ✓⟩  ↻1   │  [Too big] [Blocked]    │
│      │    ○  Write API tests                    ~2h         │  [Unclear] [Not needed] │
│ ───  │    ○  1:1 notes for Sam                  ~15m        │                         │
│ ◷    │    …                                                 │  Life of this item      │
│Review│                                                      │  Sep 28  created        │
│ ◎    │  ▸ WAITING ON (2)                                    │  Sep 29  carried        │
│Insght│    ◌  API keys from Priya    since Mon · nudge?      │  Oct 1   deferred → 3   │
│      │    ◌  PROJ-1234 ⟨Jira · In Review⟩  auto-unblocks    │  Oct 3   carried ×3     │
│      │                                                      │  Oct 7   focused        │
│      │  ▸ DONE TODAY (3)                          collapsed │                         │
│ ⚙    │                                                      │  Links                  │
│      │  + Add to today…   (n)                               │  ⟨GH PR #511 · Merged⟩  │
└──────┴──────────────────────────────────────────────────────┴─────────────────────────┘
  56px                     fluid (min 480)                         320px (toggle ⌘I)
```

### 3.1 Regions

| Region        | Behavior                                                                                                                                                                                                                                                            |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Sidebar**   | 56px icon rail collapsed, 232px expanded (⌘B). Top: **Today** (all workspaces, opt-in) and the workspace list (⌘1–⌘9). Bottom: Review, Insights, Settings. Each workspace shows its color bar and a count of items _needing a decision_, not a count of open items. |
| **Header**    | Date, day navigation, the **day strip** (completion sparkline for the last 14 days; click a bar to go to that day), and the command bar.                                                                                                                            |
| **Main**      | The current layout (List, Board, Calendar or Habit grid). The default List groups items into **Now / Planned / Waiting on / Done today**.                                                                                                                           |
| **Inspector** | Details for the focused item: the three numbers, the stuck prompt, the "life of this item" timeline, links, subtasks and notes. Hidden by default under 1100px wide; opens as a sheet instead.                                                                      |

### 3.2 Why sections instead of one sorted list

The old design showed one list ordered by the mode's sort rule. Sections answer the questions people actually ask:

- **Now** — what am I doing? At most one item (it was "Focus Item" in Sprint mode; it now exists in every mode).
- **Planned** — what did I commit to today?
- **Waiting on** — what am I not able to do yet? Items here **don't build carry**. Being blocked isn't procrastinating, and honest accountability needs that distinction.
- **Done today** — proof of progress. Collapsed, with a count.

Carried items that haven't been decided yet sit at the top of Planned behind a single banner (section 4.1). They never mix silently into the list.

Unscheduled items (no planned day) and Someday items live in the workspace's **Backlog** view (`g b`, or the tab next to Today). Neither builds carry, so a workspace can hold a large backlog without Today turning red.

### 3.3 The cross-workspace "Today" lens

Strict workspace isolation stays the default. But a person has one day, not four, so **Today (all)** is an opt-in lens:

- Rows show a 3px workspace color bar on the left edge.
- Capacity is summed across workspaces.
- It respects **focus hours**: a workspace can have active hours (Work: Mon–Fri 09:00–18:00). Outside those hours, its items drop out of Today (all), its badge counts go quiet, and quick capture defaults to another workspace. This is "context separation" in time, not just in data, which the original docs promised but never designed.

---

## 4. The Daily Loop

### 4.1 Morning: Carry-over Review

This is Noto's signature screen. It appears the first time the user opens a workspace after its day boundary, **if** there are carried items. (They can dismiss it; the banner stays.)

```
┌─────────────────────────────────────────────────────────────────┐
│  Good morning. 5 items carried over from yesterday.     1 / 5   │
│  ─────────────────────────────────────────────────────────────  │
│                                                                 │
│     Deploy v2.3 to staging                                      │
│     Work · created 9 days ago · carried ↻4 · deferred once      │
│     ⟨GH PR #511 · Merged 2h ago⟩                                │
│                                                                 │
│     Noto noticed: the linked PR merged. Is this done?           │
│                                                                 │
│   [T] Today   [D] Defer…   [S] Someday   [B] Break down         │
│   [W] Waiting on…   [X] Drop   [✓] Already done                 │
│                                                                 │
│  ─────────────────────────────────────────────────────────────  │
│  Today so far: 3h 40m of 6h   ███████████░░░░░░                 │
│  [Keep all for today]  [Skip review]                    Esc     │
└─────────────────────────────────────────────────────────────────┘
```

| Key | Decision     | Effect                                                                                                                                    |
| --- | ------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| T   | Today        | Plan for today. Carry stays (it was already counted).                                                                                     |
| D   | Defer…       | Inline date picker with natural language ("fri", "next week", "+3"). Defers +1. Carry stops growing until the new date (it never resets). |
| S   | Someday      | Moves to the workspace's Someday list. No pressure, no carry. Shown in Weekly Review.                                                     |
| B   | Break down   | Splits the item into 2–5 subtasks inline. The parent becomes a container; the first subtask is planned for today.                         |
| W   | Waiting on…  | Asks "on whom/what?" (person, or a link). Moves to Waiting on. Carry pauses.                                                              |
| X   | Drop         | Optional one-tap reason (No longer needed / Someone else did it / Not worth it). Kept in history and counted in stats.                    |
| ✓   | Already done | Marks complete with yesterday's date (so yesterday's stats are correct).                                                                  |

Design rules:

- **One item at a time, one keystroke each.** Target: a 5-item review takes under 30 seconds. Arrow keys move between items; ⌘Z undoes the last decision.
- **The capacity meter updates live** as items are kept, so the user sees the day filling up while deciding.
- **"Keep all for today"** is available but shows the capacity consequence ("this puts you at 9h of 6h").
- **Noto suggests**, using evidence: linked PR merged → "Already done?"; carried ≥ 3 and estimated over 2h → "Break down?"; linked ticket reassigned → "Drop or delegate?". A suggestion is shown as one highlighted key. It never acts by itself in the review.

### 4.2 During the day: Planning honestly

**Capacity.** Each workspace has a daily capacity (default: 6h, or "N items" for people who don't estimate). Items take an optional size:

- Typed inline: `~15m`, `~2h`, or T-shirt sizes `~s ~m ~l` (configurable: 15m / 1h / 3h).
- Items without an estimate count as the user's **historical median** (computed from completed items). The UI marks it as an estimate (`~45m*`).

The capacity bar sits above Planned:

```
 Capacity  ██████████████████████▒▒▒▒  7h 20m / 6h   ⚠ 1h 20m over · suggest deferring 2
```

Clicking "suggest" proposes the two lowest-priority, least-carried items to defer. This is the main way Noto **prevents** rollover instead of only reporting it.

**Pace forecast.** Once there are 14+ days of history, the bar shows a personal forecast:

> "On days like this you usually finish 5 of 8. Expect 3 to carry."

That forecast comes from the user's own completion history (derived day stats, [04 › Day stats](04-domain-model.md#43-day-stats-replaces-stored-daysnapshot)). Nothing is sent anywhere.

### 4.3 Evening: Shutdown (optional, 3 steps)

Triggered from the menubar ("Shut down Work"), at the end of the workspace's focus hours, or with ⌘⇧D.

1. **Done today:** a list of what got done, to acknowledge progress.
2. **Not done:** each remaining item gets the same decision keys as the morning review. Deciding at night means the next morning starts clean.
3. **One line:** "Anything to remember for tomorrow?" (optional). It's saved as the day's note and shown at the top of tomorrow's Carry-over Review.

Users who do Shutdown skip most of the morning review. Users who don't still get the morning review. Both paths reach the same state.

---

## 5. Stuck Items: Intervention, Not Punishment

When an item hits the **stuck threshold** (default: carry ≥ 3 _or_ defers ≥ 3), its row gains a small `stuck?` chip, and the inspector shows the reason prompt:

```
 ⚠ This has been carried 4 times. What's in the way?
   [Too big]   [Blocked]   [Unclear what to do]   [Don't want to]   [Not needed]
```

Each reason leads to a concrete fix:

| Reason        | Fix Noto offers                                                                                                         |
| ------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Too big       | Break down (inline subtask editor), with the first subtask pre-focused.                                                 |
| Blocked       | Move to Waiting on, linked to a person or URL. If it's a connected-app link, it unblocks automatically (section 6.3).   |
| Unclear       | Prompts "What's the very next physical action?" and rewrites the title with the answer (the old title goes to history). |
| Don't want to | Offers to make it the **Now** item with a 25-minute timer, or to schedule it first thing tomorrow.                      |
| Not needed    | Drop, with the reason recorded.                                                                                         |

**Reasons become insight.** Weekly Review (section 8) aggregates them: _"Most of your stuck items this month were **too big**. Items estimated over 2h carried 3.8× on average. Items under 30m carried 0.4×."_ As far as we know, no mainstream todo app records **why** tasks stall and reports it back. That makes it a real differentiator, and it costs one tap.

---

## 6. Connected Apps: From Previews to Live Links

The original spec rendered a large preview card inside every row. At 10 linked items that destroys density, and the cards answer the wrong question. The user doesn't need to reread a Slack message in their todo list. They need to know whether **the thing changed and whether they still need to act**.

### 6.1 Link chip (in the row)

```
 ○  Review PR #482  ⟨ GH  #482 · 2✓ · CI ✓ · ready ⟩            ↻1  ~30m
 ○  Answer Sarah    ⟨ Slack  #design · 12 replies · 2 new ⟩           ~10m
 ○  Fix login bug   ⟨ Jira  PROJ-88 · In Review ⟩                ↻3
 ○  Read RFC        ⟨ docs.acme.com ⟩                                 ~20m
```

- A chip has one line: provider glyph, short identifier, and **at most three facts**, chosen per provider for actionability (PR: approvals, CI, mergeability; Slack: replies _since you last looked_; Jira: status).
- **Change dot:** a chip gets a dot when the linked object changed since the user last opened the item.
- States: loading (shimmer chip), auth needed (`⟨ Slack · 🔑 reconnect ⟩`), unavailable (struck-through chip with a "remove" action), not connected (plain link, plus a one-time hint "Connect Slack for live status").

### 6.2 Expanded card (inspector or ⌥-click)

The full preview card from `10-connected-apps.md` lives here. That keeps rows dense while still giving the detail on demand.

### 6.3 Link-driven state ("live links")

This is where Noto goes beyond showing a preview:

| Linked object event                  | Noto behavior                                                                                                                      |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| PR merged / issue closed             | Item shows **"Linked PR merged. Done?"** with a one-key confirm. Auto-complete is opt-in per workspace; it always comes with undo. |
| Ticket moved out of a blocked status | A Waiting-on item moves back to Planned with a toast: "PROJ-1234 unblocked. Back on today."                                        |
| Review requested from you            | (Opt-in) Suggest-capture: "You were asked to review #511. Add to today?"                                                           |
| Slack thread gets new replies        | Change dot. No notification by default.                                                                                            |

Rules: Noto suggests, the user confirms. Nothing is completed or created without an explicit opt-in. Every automatic change appears in the item's history as _"via GitHub"_.

### 6.4 Paste-to-task

Pasting a recognized URL into an empty add field creates a titled item ("Review: Add rich preview support (#482)") with the link attached. The title is editable, and the cursor lands at its end.

---

## 7. Capture

### 7.1 Inline add & the command bar (⌘K)

One input understands tokens. Each token is highlighted as it's recognized, and Backspace removes the whole token:

```
 Review PR #482 tomorrow ~30m !2 #backend @waiting:priya /work
                ────────  ──── ── ──────── ──────────── ─────
                 date     size pri  tag     waiting-on   workspace
```

The same bar (⌘K) is also navigation and commands: "go personal", "defer all stuck to monday", "show items carried > 5", "switch to board". Matches are ranked fuzzily; the keyboard shortcut for each command appears next to it so users learn the shortcuts.

### 7.2 Global quick capture

- **Global hotkey** ⌃⌥Space by default (configurable). A floating 560px panel shows the token input and a workspace picker (defaults by focus hours).
- **Capture with context (macOS):** if the frontmost app is a browser, Slack or an editor, show a **"＋ attach current page"** chip with the URL already filled in. One key (⌘L) attaches it. This needs Accessibility/Automation permission, requested only the first time the user tries it.
- **Menubar item:** shows the Now item and its timer, the count of items needing a decision, and quick add.
- Target: hotkey → item saved in under 2s, with no app switch.

### 7.3 Now & focus timer

Pressing `f` on any item makes it **Now**. An optional mini-window (⌘⇧N) stays on top: `Deploy v2.3 · 23:10 · ✓ done`. Time tracked here builds the user's estimate accuracy ("you estimate 1h, you take 1h40m").

---

## 8. Reflection: Weekly Review & Insights

### 8.1 Weekly Review (guided, ~5 minutes)

Prompted on a chosen day (default: Friday afternoon or Monday morning).

1. **Wins:** items completed, grouped by workspace, with the oldest item finally done highlighted ("Finally: _Migrate auth to OAuth_, after 23 days").
2. **Someday sweep:** each Someday item: promote, keep or drop. Items untouched for 60+ days are pre-selected for dropping.
3. **Stuck patterns:** the reason breakdown (section 5) and one concrete suggestion.
4. **Estimate check:** estimated vs actual time, per size bucket.
5. **Next week:** the top 3 outcomes (free text, optional). They're pinned to the header all week.

### 8.2 Insights (narrative first, charts second)

Each insight is a sentence backed by a small chart, never a bare number:

```
┌──────────────────────────────────────────────────────────────────┐
│  You finish small things fast and big things never.              │
│  Items ≤30m: done same day 81%   ████████████████▏               │
│  Items ≥2h:  done same day 12%   ██▍                              │
│  → Try breaking ≥2h items down during the morning review.        │
├──────────────────────────────────────────────────────────────────┤
│  Mondays are overloaded.                                         │
│  You plan 9.1 items on Mondays and finish 5.3.                   │
│  M ████████▊ T ██████ W █████▌ T ██████ F ████                   │
├──────────────────────────────────────────────────────────────────┤
│  Rollover rate is down: 34% → 21% over 4 weeks.          ▆▅▅▃▂   │
└──────────────────────────────────────────────────────────────────┘
```

Insight catalog for v1: size vs completion, weekday overload, rollover trend, stuck-reason mix, estimate accuracy, oldest open items, Waiting-on duration by person. Each insight is computed locally and needs a minimum sample size (n ≥ 10) before it appears, so Noto doesn't show noise.

### 8.3 Time travel

The day strip and `[` / `]` navigate days. Past days are a **read-only log** (what was planned, done, carried, decided), reconstructed from item state and history. Future days show items planned or deferred to them (`planned_for`), so planning ahead is possible.

---

## 9. Modes, Presented as Lenses

Modes are three independent controls (**Layout × Order × Pressure**), and the six named modes are presets over them. The presets, pressure thresholds and switching guarantees are specified in [03-modes-and-workspaces.md](03-modes-and-workspaces.md); this section covers only how they look.

The mode switcher (⌘⇧M, or click the mode name in the header) shows the preset and a one-line "what changes" preview, e.g. _"Accountability: sorts by carry, pins your 3 most-carried items, Morning Review can't be skipped."_ Users can tweak any control; the preset name then shows "Sprint (custom)".

Switching is instant and **never writes to items**. Items that lack the layout's natural field get a visible lane instead of being hidden ("No date", "Not habits (12)").

### 9.1 Layout sketches

**Board**

```
 Someday (4)   Backlog (8)    In progress (2/3)   Today (3)       Waiting (1)    Done 7d (11)
 ┌──────────┐  ┌──────────┐   ┌───────────────┐   ┌───────────┐   ┌──────────┐   ┌──────────┐
 │ Rewrite  │  │ Spike    │   │ Sync endpoint │   │ Fix CI    │   │ API keys │   │ ✓ Login  │
 │ in Rust? │  │ CRDT ~l  │   │ ⏸ 6d here     │   │ ↻1 ~1h    │   │ ← Priya  │   │ ✓ Docs   │
 └──────────┘  └──────────┘   └───────────────┘   └───────────┘   └──────────┘   └──────────┘
                              └ user column ┘
```

Someday, Backlog, Today, Waiting and Done are fixed columns mapped to item state. User columns (like "In progress") sit between Backlog and Today. Only Today accrues carry. The WIP limit shows as `2/3` in the column header. Over the limit, the header turns warm and a drop into that column asks to swap an item out.

**Timeline (Deadline)**

```
           Today   Thu    Fri  │ Mon    Tue    Wed
 Overdue ▌ Submit report (2d late)
         ├──■ Exam prep ch.4 ──────────┤ due Fri
                  ├── Slides ──┤ due Fri
 No date ▌ 3 items
```

**Habit grid**

```
                 M T W T F S S   streak
 Morning run     ■ ■ □ ■ ■ · ·   2  (best 11)
 Read 20 pages   ■ ■ ■ ■ ■ · ·   5
 No phone in bed ■ □ ■ □ ■ · ·   1
```

Missed days show as an empty square, not a red X. Habits use **"flex streaks"** (configurable, e.g. "5 of 7 days"), so one miss doesn't erase months of progress.

---

## 10. Item Row & Inspector Specification

### 10.1 Row anatomy (Comfortable density, 36px)

```
 ▌ ○  Deploy v2.3 to staging  ⟨GH #511 · merged •⟩  #deploy   ↻4  ~1h  !2   ⋯
 │ │  │                        │                     │         │   │    │    └ hover / focus only
 │ │  title (inline edit: e)   link chip(s), max 2    tags      │   size priority
 │ └ status glyph: ○ planned ◉ now ◌ waiting ✓ done ⊘ dropped   carry (pressure-colored)
 └ 3px workspace bar (Today-all lens only) or pressure bar (Honest/Relentless)
```

| Density     | Row height | Shows                                                            |
| ----------- | ---------- | ---------------------------------------------------------------- |
| Compact     | 28px       | glyph, title, carry. Chips collapse to a provider glyph.         |
| Comfortable | 36px       | everything above, at most 2 chips, tags truncated                |
| Spacious    | 52px       | plus a second line with notes excerpt or subtasks progress `2/5` |

**Pressure presentation** (never color alone):

| State | Carry glyph | Text color token | Row treatment                            |
| ----- | ----------- | ---------------- | ---------------------------------------- |
| Fresh | (none)      | —                | none                                     |
| Warm  | `↻2`        | `pressure-warm`  | none                                     |
| Hot   | `↻4`        | `pressure-hot`   | 3px left bar                             |
| Stale | `↻7 stuck?` | `pressure-stale` | 3px left bar plus the `stuck?` chip text |

No pulsing, no scaling, no full-row tints. Static signals at a consistent spot scan faster and don't distract during focused work.

### 10.2 Inspector

Sections, top to bottom: title and notes (Markdown, inline), **the three numbers** (Age · Carry · Defers), stuck prompt (if any), schedule (planned for, due, recurrence), links (expanded cards), subtasks, **Life of this item** (the TodoHistory timeline in plain language: "Carried 3 times", "Deferred to Mon", "Unblocked via Jira"), then a danger zone (drop, move workspace).

---

## 11. Visual System

### 11.1 Tokens

Both themes are first-class; the app follows the OS setting by default. All text/foreground pairs below were computed against WCAG 2.1 and pass AA (≥ 4.5:1) on `canvas`, `surface` and `raised`, unless noted.

| Token            | Dark                                         | Light     | Use                                                                                                                    |
| ---------------- | -------------------------------------------- | --------- | ---------------------------------------------------------------------------------------------------------------------- |
| `canvas`         | `#0E0F12`                                    | `#F7F7F5` | Window background                                                                                                      |
| `surface`        | `#16181D`                                    | `#FFFFFF` | Lists, panels                                                                                                          |
| `raised`         | `#1D2027`                                    | `#F0F0EC` | Popovers, inspector, chips                                                                                             |
| `hover`          | `#242833`                                    | `#EFEFEB` | Hover / selected row                                                                                                   |
| `border`         | `#2A2E38`                                    | `#E2E2DC` | Hairlines (decorative, no contrast requirement)                                                                        |
| `text-1`         | `#ECEEF2`                                    | `#17181C` | Primary text (14–18:1)                                                                                                 |
| `text-2`         | `#A3A9B6`                                    | `#525866` | Secondary text (6.2–8.1:1)                                                                                             |
| `text-3`         | `#868D9B`                                    | `#676D7A` | Hints, metadata (4.5–5.8:1). Exception: dark `text-3` on `hover` is 4.4:1, so hovered rows render metadata in `text-2` |
| `link`           | `#7AA7FF`                                    | `#2F5FD0` | Links, focus ring                                                                                                      |
| `done`           | `#6CC08B`                                    | `#2B7A4B` | Completion                                                                                                             |
| `pressure-warm`  | `#E0B252`                                    | `#8A5F00` | Carry, warm                                                                                                            |
| `pressure-hot`   | `#F08A4B`                                    | `#B34A12` | Carry, hot                                                                                                             |
| `pressure-stale` | `#FF6B6B`                                    | `#C22F2F` | Carry, stale                                                                                                           |
| `workspace-*`    | user-picked from 10 presets, tuned per theme |           | Sidebar bar, accent, Today-all row bar                                                                                 |

The workspace color is the **accent** for that workspace (selection, primary buttons, focus timer). The app has no global brand blue competing with it.

> Old-palette issues fixed: `text-tertiary #5E5E72` was 2.7:1 and the 14+-day badge `#8B2020` was 1.9:1 against `bg-secondary`. Both failed AA.

### 11.2 Type & spacing

- **Typeface:** system UI (SF Pro on macOS, Segoe UI Variable on Windows, Inter as the bundled fallback for Linux and WASM). **Tabular numerals** for every count, time and carry glyph, so columns don't jitter.
- **Scale:** 12 / 13 / 14 (body) / 16 / 20 / 28. The user setting scales the body from 13 to 18 and everything else follows.
- **Spacing:** 4px base grid. Row padding 8/12. Sections separated by 20px and an uppercase 11px label in `text-3`.
- **Radius:** 6px (chips, inputs), 10px (panels, popovers). **Elevation:** only popovers and the quick-capture panel have shadows.

### 11.3 Motion

| Trigger               | Animation                                                                                          | Duration      |
| --------------------- | -------------------------------------------------------------------------------------------------- | ------------- |
| Complete item         | Glyph fills; row holds 600ms (an undo window), then slides into Done                               | 150ms + 200ms |
| Review decision       | Card exits in the direction of the decision (←defer, ↑someday, →today)                             | 180ms         |
| Add item              | Expands from the input                                                                             | 150ms         |
| Section collapse      | Height ease-out                                                                                    | 160ms         |
| Layout switch         | Cross-fade (no morph: cheaper in Avalonia and less disorienting)                                   | 150ms         |
| Day change while open | Banner "It's a new day. 3 items to decide" slides in. The list doesn't reflow underneath the user. | 200ms         |

All motion is disabled under the OS reduce-motion setting (on macOS, `NSWorkspace.accessibilityDisplayShouldReduceMotion`), with state changes shown instantly.

---

## 12. Keyboard Map

Single-key shortcuts work when the list has focus (Linear/Superhuman style). Modifier shortcuts work everywhere. This map avoids the OS conflicts in the old spec: ⌘M (minimize), ⌘S (save), ⌥Space (types a non-breaking space on macOS), Alt+Space (window menu on Windows), and ⌘←/⌘→/⌘⌫ (text-editing keys inside inputs).

| Action                   | Key              | Action                  | Key        |
| ------------------------ | ---------------- | ----------------------- | ---------- |
| Command bar              | ⌘K               | Move down / up          | j / k, ↓ ↑ |
| Global quick capture     | ⌃⌥Space (config) | Complete                | x          |
| New item                 | n                | Edit title              | e, Enter   |
| Make Now (focus)         | f                | Defer…                  | d          |
| Today (plan for today)   | t                | Someday                 | s          |
| Waiting on…              | w                | Break down              | b          |
| Drop                     | ⌫ (list focus)   | Set estimate / priority | ~ / 1–4    |
| Prev / next day          | [ / ]            | Jump to today           | ⌘T         |
| Workspace 1–9            | ⌘1–⌘9            | Today (all)             | ⌘0         |
| Toggle sidebar/inspector | ⌘B / ⌘I          | Switch mode/preset      | ⌘⇧M        |
| Start review             | ⌘⇧R              | Shutdown                | ⌘⇧D        |
| Search                   | /                | Undo / Redo             | ⌘Z / ⌘⇧Z   |
| Backlog view             | g b              | Today view              | g t        |
| Multi-select             | ⇧j / ⇧k, ⌘A      | Shortcut help           | ?          |

Every decision key works on a **multi-selection**: select 6 items, press `d`, type "mon", and all six are deferred.

On Windows/Linux, ⌘ maps to Ctrl, and the global capture default becomes Ctrl+Alt+Space.

---

## 13. Mobile & Web

### 13.1 Mobile (P2, designed now so the model supports it)

```
┌─────────────────────────────┐
│ Work ▾          Oct 7   ⋯   │
│ ███████████░░░  4h / 6h     │
├─────────────────────────────┤
│ 5 carried · Review →        │  ← banner opens card stack
├─────────────────────────────┤
│ ◉ Deploy v2.3        ↻4 ~1h │
│ ○ Review PR #482  GH✓   ↻1  │
│ ○ Write API tests       ~2h │
│ ◌ API keys ← Priya          │
│ ✓ 3 done today            ▸ │
├─────────────────────────────┤
│   Today   Review   ＋   ◎   │
└─────────────────────────────┘
```

- **Carry-over review as a card stack.** Swipe → today, ← defer (opens a date sheet), ↑ someday, ↓ drop. Buttons are also shown, so gestures aren't the only way to act.
- **Row swipes:** right = complete, left = defer.
- Large central ＋ for capture. Share-sheet target: "Add to Noto" with the shared URL attached.
- Widgets: Now item with timer; "N items need a decision".

### 13.2 Web (WASM)

The same layouts as desktop, responsive at 480 / 768 / 1100px breakpoints. Things the web build can't do, labeled in the UI rather than hidden: global hotkey (replaced by a bookmarklet/extension later) and capture with context. Connected-app previews and live links work on web **through the Noto backend's Connected Apps Gateway**, because browsers can't call provider APIs directly ([10 › Web](10-connected-apps.md#web-connected-apps-gateway)). Without a signed-in backend, web shows plain links with a "Sign in to Noto Sync for live links" hint.

---

## 14. Where the Model Lives

The model changes this design needed have been applied. This table maps UI concepts to their definitions:

| UI concept                                                                 | Defined in                                                                                                                                             |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Age, Carry, Defers, pressure state, stuck                                  | [04 › Derived Metrics](04-domain-model.md#4-derived-metrics)                                                                                           |
| Planned / Needs decision / Unscheduled / Someday / Waiting                 | [04 › TodoItem states](04-domain-model.md#21-todoitem)                                                                                                 |
| Decisions (Today, Defer, Someday, Break down, Waiting, Drop, Already done) | `ItemEvent` types in [04](04-domain-model.md#24-itemevent-append-only-history)                                                                         |
| Capacity, focus hours, Now item                                            | [04 › Workspace](04-domain-model.md#23-workspace), [03 › Focus Hours](03-modes-and-workspaces.md#focus-hours)                                          |
| Day strip, time travel, insights, pace forecast                            | [04 › Day stats](04-domain-model.md#43-day-stats-replaces-stored-daysnapshot), [04 › Workspace stats](04-domain-model.md#44-workspace-stats--insights) |
| Presets, pressure thresholds, board columns                                | [03 › Mode System](03-modes-and-workspaces.md#mode-system)                                                                                             |
| Shutdown note                                                              | `DayNote` in [04](04-domain-model.md#1-entity-relationship-diagram)                                                                                    |
| Flex streaks                                                               | [04 › Habit stats](04-domain-model.md#45-habit-stats-rules-with-missed_behavior--skip)                                                                 |
| Link chips, change dot, live links                                         | [04 › Links](04-domain-model.md#6-links), [10-connected-apps.md](10-connected-apps.md)                                                                 |

---

## 15. Avalonia Implementation Notes

| Concern                           | Approach                                                                                                                                                                                               |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Virtualized sectioned list        | `ItemsRepeater` with a grouped source, or `ListBox` with `VirtualizingStackPanel` and section-header items. Keep row templates cheap: chips render from cached view models, never fetch during layout. |
| Single-key shortcuts              | `KeyBindings` on the list container, active only when focus isn't inside a `TextBox`.                                                                                                                  |
| Global hotkey and menubar (macOS) | Native interop: `RegisterEventHotKey` (Carbon) for the hotkey, and Avalonia `TrayIcon` + `NativeMenu` for the menubar. Windows: `RegisterHotKey`. Budget this as platform work, not UI work.           |
| Capture with context              | macOS Apple Events to browsers / Accessibility API. Requires entitlements and a usage description. Ship it after the core loop works.                                                                  |
| Floating quick-capture panel      | A borderless `Window` with `Topmost`, shown by the hotkey service, kept warm in memory for the < 2s target.                                                                                            |
| Accessibility                     | `AutomationProperties.Name` and `HelpText` on every row ("Deploy v2.3, planned, carried 4 times, estimate 1 hour"). Test with VoiceOver every release.                                                 |
| Reduce motion                     | Read the OS setting at startup and on change, and gate every `Transitions` block on it.                                                                                                                |
| Theming                           | Fluent theme with token overrides in `ThemeDictionaries` (Dark/Light). Workspace accent swapped at runtime via a dynamic resource.                                                                     |

---

## 16. Onboarding & Empty States

- **No four seeded workspaces.** First launch asks one question: _"What do you want Noto to keep honest?"_ (Work · Personal · Both). That creates one or two workspaces with the Sprint/Zen presets. More can be added later.
- **Import first:** Todoist, Things, Apple Reminders, TickTick (CSV/JSON) and a Markdown checklist. Imported open items start with carry 0. (We don't punish people for history from another app.)
- **Empty Today:** "Nothing planned. Pull from Someday (4) or add something." It shows a calm state, never an illustration of a sad mascot.
- **First carry-over** (day 2): a one-time coach mark explains the decision keys.

---

## 17. Notifications

Off by default except for these, each opt-out:

1. **Morning nudge** at the start of focus hours, _only if_ items need a decision.
2. **Now timer** finished.
3. **Live link** events the user explicitly subscribed to per item ("tell me when PR #511 merges").

No streak-guilt notifications, no "you haven't opened Noto in 3 days".

---

## 18. Measuring Whether the Design Works

| Metric (local, opt-in telemetry only)                                                   | Target                                                            |
| --------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| Median time to finish a Carry-over Review                                               | < 30s                                                             |
| Share of carried items that get an explicit decision (vs "Keep all")                    | > 60%                                                             |
| Median carry at completion                                                              | < 1.5                                                             |
| Share of items dropped _with a reason_ vs abandoned without one (old and never decided) | ↑ over time                                                       |
| Day-14 retention of users who did ≥ 3 reviews vs users who didn't                       | Reviews should correlate with retention; if not, revisit the loop |
| Capacity overcommit days per week                                                       | ↓ over first month                                                |

---

## 19. Deliberately Not Doing

- **Shame mechanics** (skulls, "Wall of Shame", pulsing red). They push users to quit, not to finish.
- **AI auto-planning that moves tasks around on its own.** Noto suggests with visible evidence. The user decides. (A local "suggest a plan for today" can come later, built on the same evidence.)
- **Inline full preview cards in rows.** They live in the inspector.
- **Streak-loss punishment.** Flex streaks and neutral "missed" states instead.
- **Collaboration UI.** Teams is a business-doc aspiration with no model behind it yet. It needs its own design pass.
