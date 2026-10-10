# Noto — UI/UX Design (v3, "Ledger")

> **v3 replaces the v2 spec.** The product idea is unchanged — todos age, roll over, and every carried
> item asks for a decision. What changed is the visual language and several screens: v2's palette was
> Apple system greys with the default framework blue, which made the app read as a default theme. v3
> gives it a point of view, **"Ledger"**: warm paper, ruled hairlines, ink-dark text, one accent ink per
> workspace, and pressure shown as ink density rather than alarm.
>
> §11 is the authoritative visual system. **§20 records what changed in the modernization pass, what
> shipped, and what is still owed.** Where this document and the code disagree, the code is right and
> this document is a bug.

---

## 0. The Problem This Design Solves

Every todo app can show a list. Noto's premise is that unfinished items roll over and age visibly.
Taken alone, that premise fails in a predictable way:

> **Rollover without decisions turns into a guilt pile.** Today's list holds everything you didn't
> finish, so it grows every day, the red badges stop meaning anything, and the user declares "todo
> bankruptcy" and leaves.

Apps today handle stale tasks in one of three ways:

- **Ignore them.** Todoist and Things let overdue items sit quietly or pile into an "Overdue" bucket.
- **Move them silently.** Planners like Sunsama roll unfinished tasks forward and show a count, but
  nothing asks _why_ the task didn't happen.
- **Gamify them.** Habitica turns staleness into a game.

None of them turn the moment an item gets carried over into a **decision**, and none of them learn from
those decisions. That is the design gap Noto fills:

> **Noto's promise: nothing rots silently. Every carried item gets a decision, every decision is one
> keystroke, and Noto learns from your decisions so it can help you plan more honestly.**

Everything below serves that loop:

```
   Plan honestly ──► Do ──► Close the day ──► Carry-over decisions ──► Learn
        ▲                                                               │
        └───────────── insights feed tomorrow's plan ◄──────────────────┘
```

### 0.1 Who this is for

Design context, stated explicitly because a design without it drifts toward the average. None of this is
derivable from the code.

| Question                          | Answer                                                                                                                                                                                                                                                                                                                                          |
| --------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Who uses Noto?**                | One knowledge worker with too many open loops across work and personal life. They have already tried Todoist / Things / Apple Reminders and felt either buried by silent rollover or patronised by streak games. Keyboard-comfortable, Mac-first, mildly allergic to being nagged. Secondary persona: the self-hoster who runs the sync server. |
| **What are they doing?**          | (1) **Morning, < 30s:** triage what rolled over and decide. (2) **During the day, glanceable:** see what is Now, capture without leaving the keyboard. (3) **Evening, optional:** close the day. (4) **Weekly, ~5 min:** see honest patterns. (5) **Occasionally:** plan on a board, read deadlines on a timeline.                              |
| **What should it feel like?**     | _Honest, not harsh._ Calm, precise, a little austere — a well-kept paper planner, or a precision instrument. Not a cheerleader, not a scold, not a game.                                                                                                                                                                                        |
| **What must it never feel like?** | A default framework theme. A dashboard. A form. A gamified habit tracker. Anything with a mascot.                                                                                                                                                                                                                                               |

**The test this design is held to.** Show a screenshot and say "an AI made this." If the answer is an
unqualified yes, the surface has no point of view and the design has failed, however good the model is.

---

## 1. Design Principles

| #   | Principle                                          | What it means in practice                                                                                                                                                                                |
| --- | -------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Decisions, not reminders**                       | A carried item never just reappears. It shows up with one-keystroke choices: keep, defer, someday, split, wait, drop.                                                                                    |
| 2   | **Honest, not harsh**                              | Pressure is visible and the user can tune it. No skulls, no "Wall of Shame", no pulsing badges, **and no red for a carried item** — red is reserved for genuine error. Show facts, then offer a way out. |
| 3   | **The plan should be finishable**                  | Today shows capacity next to commitments. Overcommitting is visible _before_ the day starts, not as rollover the next morning.                                                                           |
| 4   | **Keyboard-first, pointer-complete, touch-native** | Every action has a key. Every key action is also reachable by pointer. Every pointer action has a gesture on mobile.                                                                                     |
| 5   | **Links are live**                                 | A todo that points at a PR or ticket reflects that object's state and can react to it. A link is more than a preview card.                                                                               |
| 6   | **Density you choose**                             | Compact, Comfortable and Spacious densities. Rich content (previews, history) shows on focus, not on every row.                                                                                          |
| 7   | **Undo instead of confirm**                        | No "Are you sure?" dialogs. Destructive actions run immediately and show an undo toast (⌘Z works for 10 minutes).                                                                                        |
| 8   | **Never colour alone**                             | Every colour signal also has text, a shape or a position. The palette passes WCAG AA in both themes (§11.4).                                                                                             |

Three commitments make "Ledger" concrete, and each is a rule rather than a mood:

1. **Warm-neutral, not system-grey.** Surfaces are tinted (warm charcoal in dark, warm paper in light)
   with real luminance separation between chrome and content. v2's four surfaces were 3–6% apart, so the
   sidebar, list and inspector collapsed into one flat plane.
2. **Light-first, dark as a true companion.** The reference implementation is light — a planner is paper.
   Dark is derived from the same system, not an inversion, and both are first-class.
3. **One accent, owned by the workspace; no global blue.** The accent is reserved for _interactive and
   selected_. State (carry, done) has its own ramp; neutrals carry everything else. A single framework
   blue doing six jobs at once is the clearest tell of a default theme.

---

## 2. Vocabulary the UI Uses

The original docs used "age" for two different things. The UI needs three distinct, visible numbers:

| Term       | Definition                                                                                | Drives                                     |
| ---------- | ----------------------------------------------------------------------------------------- | ------------------------------------------ |
| **Age**    | Days since the item was created.                                                          | Information only (detail panel, tooltip).  |
| **Carry**  | Number of day boundaries the item crossed while it was planned for that day and not done. | **Pressure** (escalation colour and copy). |
| **Defers** | Number of times the user explicitly moved the item to a later date.                       | Insights ("deferred 4×"), stuck prompts.   |

**Why this split matters:** an item created two weeks ago and deliberately planned for today should not
look rotten on its first day. Pressure comes from _broken intentions_ (carries), not from time passing.
Defers are honest (the user chose them), but they still count, so deferring can't be used to escape
accountability forever.

Row display rule: show **carry** when it is ≥ 1 (e.g. `↻3`), otherwise show nothing. Age shows in the
detail panel and tooltip.

### 2.1 The in-app guide

This vocabulary is only useful if the app teaches it. Noto carries a **Guide** page (`AppPage.Help`) and
two lightweight affordances that point at it:

| Affordance    | Where                                                                                                     | Behaviour                                                    |
| ------------- | --------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| Short tooltip | Only where the label does not already say it                                                              | One sentence, never a paragraph. Depth belongs in the guide. |
| ⓘ button      | Next to a confusing label (mode controls, capacity, age/carry/defers, review, shutdown, weekly, insights) | Opens the Guide at the matching topic.                       |

The tooltip rule is strict, because noise trains people to ignore all of them:

- **If the label already says it, there is no tooltip.** A duration dropdown needs no "how long will this
  take"; an "Add" button needs no "add it".
- **Icon-only controls keep theirs** — there the tooltip _is_ the label, which also keeps the collapsed
  sidebar usable.
- **An ⓘ needs no tooltip**: the icon says "more", and its accessible name names the topic.
- What earns one: a concept that cannot be guessed from the label — what _carry_ counts, that priority 1
  is highest, what the status glyphs mean, that setting a waiting-on name moves the item to Waiting on,
  that a delete is undoable, that the day-strip bars are completions.

The guide is one page: a searchable topic rail plus a document pane of titled sections, each a short
paragraph and an optional label/description list. Its topics are `start`, `workspaces`, `modes`,
`layouts`, `order`, `pressure`, `carry`, `review`, `shutdown`, `weekly`, `today-all`, `capture`,
`shortcuts` and `connected`.

Three rules keep it honest as the app changes:

- **Ids are constants.** `HelpTopicIds` is the single source; XAML deep-links via `x:Static`, so a
  tooltip pointing at a topic that does not exist is a compile error, not a dead link.
- **The words match.** `ModeLabels` gives every mode enum its human phrase, and the settings dropdowns,
  the command bar and the guide all use it. A test asserts that every choice a dropdown offers is
  described in the guide using the same words.
- **It is reachable.** Sidebar → Guide, ⌘K → "Guide: …" (the topics are searchable), and a link from the
  `?` shortcuts overlay. Pressing `?` still opens the shortcuts sheet; the guide is the depth behind it.

Modes get the most space, because the three independent controls (layout × order × pressure) and the six
presets over them are the least guessable part of the model — see [03](03-modes-and-workspaces.md).

---

## 3. App Shell (Desktop, macOS-first)

```
┌──────┬──────────────────────────────────────────────────────┬─────────────────────────┐
│ ●●●  │  ▤  [Today|Backlog]        ⌘K Ask or jump…  ?  ▦    │  INSPECTOR              │
│      ├──────────────────────────────────────────────────────┤                         │
│ ⌂    │  Today · Wed Oct 7                            ◀ ▶    │  Deploy v2.3 to staging │
│ Today│  ▁▂▅▇▃▆█  ←── day strip (last 14 days)                │  Work · Sprint          │
│      │  Capacity  ██████████░░░░░░  3h 45m / 6h  ✓ fits     │                         │
│ WORK │                                                      │  Age 6d · Carry ↻6      │
│ ▌Work│  ▸ NOW (1)                                           │  Defers 0               │
│  Pers│    ◉  Deploy v2.3 to staging        Why? ↻6  ~1h !2   │                         │
│  Hlth│                                                      │  ⚠ Carried 6 times.     │
│  Side│  ▸ PLANNED (3)                                       │    What's in the way?   │
│      │    ○  Review PR #482   ⟨GH · 2✓ · CI ✓⟩  Why? ↻7 ~30m │  [Too big] [Blocked]    │
│ ───  │    ○  Write API tests                        ~2h      │  [Unclear] [Not wanted] │
│ ◷    │    ○  1:1 notes for Sam                      ~15m      │                         │
│Review│                                                      │  Life of this item      │
│ ◎    │  ▸ WAITING ON (1)                                    │  Sep 30  created        │
│Insght│    ◌  API keys from Priya                    ~1h*     │  Oct 1   carried ×6     │
│      │                                                      │                         │
│ ⚙    │  ▸ DONE TODAY (1)                          collapsed  │  Links                  │
│      │  + New task…            [estimate ▾] [Add] [More]    │  ⟨GH PR #511 · Merged⟩  │
└──────┴──────────────────────────────────────────────────────┴─────────────────────────┘
  56px                     fluid (min 480)                         320px (toggle ⌘I)
```

### 3.1 Regions

| Region        | Behaviour                                                                                                                                                                                                                                                                                             |
| ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Sidebar**   | 56px icon rail collapsed, 232px expanded (⌘B). Top: **Today** (all workspaces, opt-in) and the workspace list (⌘1–⌘9). Bottom: Review, Weekly review, Shutdown, Insights, Settings, Guide. Each workspace shows its accent tick and a count of items _needing a decision_, not a count of open items. |
| **Toolbar**   | Lives inside the native title bar. Holds only the sidebar toggle, the Today/Backlog segmented control, the command-bar trigger, help and the inspector toggle. **No page title** — that is the display header (§3.4).                                                                                 |
| **Header**    | The **display title** (the page name, or the date on a list page) with day navigation, then the **day strip** (completion sparkline for the last 14 days; click a bar to go to that day).                                                                                                             |
| **Main**      | The current layout (List, Board, Timeline or Habit grid). The default List groups items into **Now / Planned / Waiting on / Done today**.                                                                                                                                                             |
| **Inspector** | Details for the focused item: the three numbers, the stuck prompt, the "life of this item" timeline, links, subtasks and notes. Hidden by default under 1000px wide.                                                                                                                                  |

### 3.2 Why sections instead of one sorted list

The old design showed one list ordered by the mode's sort rule. Sections answer the questions people
actually ask:

- **Now** — what am I doing? At most one item (it was "Focus Item" in Sprint mode; it now exists in
  every mode).
- **Planned** — what did I commit to today?
- **Waiting on** — what am I not able to do yet? Items here **don't build carry**. Being blocked isn't
  procrastinating, and honest accountability needs that distinction.
- **Done today** — proof of progress. Collapsed, with a count.

Carried items that haven't been decided yet sit at the top of Planned behind a single banner (§4.1).
They never mix silently into the list.

Unscheduled items (no planned day) and Someday items live in the workspace's **Backlog** view
(`g b`, or the tab next to Today). Neither builds carry, so a workspace can hold a large backlog without
Today turning red.

### 3.3 The cross-workspace "Today" lens

Strict workspace isolation stays the default. But a person has one day, not four, so **Today (all)** is
an opt-in lens:

- Rows show a 3px workspace accent bar on the left edge.
- Capacity is summed across workspaces.
- It respects **focus hours**: a workspace can have active hours (Work: Mon–Fri 09:00–18:00). Outside
  those hours, its items drop out of Today (all), its badge counts go quiet, and quick capture defaults
  to another workspace. This is "context separation" in time, not just in data.

### 3.4 The display header

The page title is **not** in the toolbar. It lives in the content, as the largest thing on the page:

- List pages (Today, Backlog, Today-all, Day log) get a 30px serif title with day navigation beside it.
- Every other page gets the same treatment at 20px.

This exists to fix an inverted hierarchy. In v2 the centred toolbar title was the largest text on screen
while task titles were 13px, so chrome outranked content. The rule that follows from it: **do not
reintroduce a title in the toolbar.**

---

## 4. The Daily Loop

### 4.1 Morning: Carry-over Review

This is Noto's signature screen. It appears the first time the user opens a workspace after its day
boundary, **if** there are carried items. (They can dismiss it; the banner stays.)

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

- **One item at a time, one keystroke each.** Target: a 5-item review takes under 30 seconds. Arrow keys
  move between items; ⌘Z undoes the last decision.
- **The capacity meter updates live** as items are kept, so the user sees the day filling up while
  deciding.
- **"Keep all for today"** is available but shows the capacity consequence ("this puts you at 9h of 6h").
- **Noto suggests**, using evidence: linked PR merged → "Already done?"; carried ≥ 3 and estimated over
  2h → "Break down?"; linked ticket reassigned → "Drop or delegate?". A suggestion is shown as one
  highlighted key. It never acts by itself in the review.
- **A decision is a direction.** When the card leaves, it leaves _toward_ the decision — left for defer,
  up for someday, right for today. The motion is the confirmation, which is why there is no "saved"
  toast.

### 4.2 During the day: Planning honestly

**Capacity.** Each workspace has a daily capacity (default: 6h, or "N items" for people who don't
estimate). Items take an optional size:

- Typed inline: `~15m`, `~2h`, or T-shirt sizes `~s ~m ~l` (configurable: 15m / 1h / 3h).
- Items without an estimate count as the user's **historical median** (computed from completed items).
  The UI marks it as an estimate (`~45m*`).

The capacity bar sits above Planned:

```
 Capacity  ██████████████████████▒▒▒▒  7h 20m / 6h   ⚠ 1h 20m over · suggest deferring 2
```

Clicking "suggest" proposes the two lowest-priority, least-carried items to defer. This is the main way
Noto **prevents** rollover instead of only reporting it.

**Pace forecast.** Once there are 14+ days of history, the bar shows a personal forecast:

> "On days like this you usually finish 5 of 8. Expect 3 to carry."

That forecast comes from the user's own completion history (derived day stats,
[04 › Day stats](04-domain-model.md#43-day-stats-replaces-stored-daysnapshot)). Nothing is sent anywhere.

### 4.3 Evening: Shutdown (optional, 3 steps)

Triggered from the menubar ("Shut down Work"), at the end of the workspace's focus hours, or with ⌘⇧D.

1. **Done today:** a list of what got done, to acknowledge progress.
2. **Not done:** each remaining item gets the same decision keys as the morning review. Deciding at night
   means the next morning starts clean.
3. **One line:** "Anything to remember for tomorrow?" (optional). It's saved as the day's note and shown
   at the top of tomorrow's Carry-over Review.

Users who do Shutdown skip most of the morning review. Users who don't still get the morning review. Both
paths reach the same state.

---

## 5. Stuck Items: Intervention, Not Punishment

When an item hits the **stuck threshold** (default: carry ≥ 3 _or_ defers ≥ 3), its row gains a small
chip and the inspector shows the reason prompt:

```
 ⚠ This has been carried 4 times. What's in the way?
   [Too big]   [Blocked]   [Unclear what to do]   [Don't want to]   [Not needed]
```

**The row chip is a quiet ask, not an alarm.** It reads **"Why?"** in the warm end of the pressure ramp,
as a filled pill. v2 drew it as a red outlined chip labelled `stuck?`; red means error, and an outlined
red chip on an ordinary carried item contradicts principle 2. Nothing about a carried item is ever red.

Each reason leads to a concrete fix:

| Reason        | Fix Noto offers                                                                                                         |
| ------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Too big       | Break down (inline subtask editor), with the first subtask pre-focused.                                                 |
| Blocked       | Move to Waiting on, linked to a person or URL. If it's a connected-app link, it unblocks automatically (§6.3).          |
| Unclear       | Prompts "What's the very next physical action?" and rewrites the title with the answer (the old title goes to history). |
| Don't want to | Offers to make it the **Now** item with a 25-minute timer, or to schedule it first thing tomorrow.                      |
| Not needed    | Drop, with the reason recorded.                                                                                         |

**Reasons become insight.** Weekly Review (§8) aggregates them: _"Most of your stuck items this month were
**too big**. Items estimated over 2h carried 3.8× on average. Items under 30m carried 0.4×."_ As far as we
know, no mainstream todo app records **why** tasks stall and reports it back. That makes it a real
differentiator, and it costs one tap.

---

## 6. Connected Apps: From Previews to Live Links

The original spec rendered a large preview card inside every row. At 10 linked items that destroys
density, and the cards answer the wrong question. The user doesn't need to reread a Slack message in
their todo list. They need to know whether **the thing changed and whether they still need to act**.

### 6.1 Link chip (in the row)

```
 ○  Review PR #482  ⟨ GH  #482 · 2✓ · CI ✓ · ready ⟩            ↻1  ~30m
 ○  Answer Sarah    ⟨ Slack  #design · 12 replies · 2 new ⟩           ~10m
 ○  Fix login bug   ⟨ Jira  PROJ-88 · In Review ⟩                ↻3
 ○  Read RFC        ⟨ docs.acme.com ⟩                                 ~20m
```

- A chip has one line: provider glyph, short identifier, and **at most three facts**, chosen per provider
  for actionability (PR: approvals, CI, mergeability; Slack: replies _since you last looked_; Jira:
  status).
- **Change dot:** a chip gets a dot when the linked object changed since the user last opened the item.
- States: loading (shimmer chip), auth needed (`⟨ Slack · reconnect ⟩`), unavailable (struck-through chip
  with a "remove" action), not connected (plain link, plus a one-time hint "Connect Slack for live
  status").

### 6.2 Expanded card (inspector or ⌥-click)

The full preview card from `10-connected-apps.md` lives here. That keeps rows dense while still giving the
detail on demand.

### 6.3 Link-driven state ("live links")

This is where Noto goes beyond showing a preview:

| Linked object event                  | Noto behaviour                                                                                                                     |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| PR merged / issue closed             | Item shows **"Linked PR merged. Done?"** with a one-key confirm. Auto-complete is opt-in per workspace; it always comes with undo. |
| Ticket moved out of a blocked status | A Waiting-on item moves back to Planned with a toast: "PROJ-1234 unblocked. Back on today."                                        |
| Review requested from you            | (Opt-in) Suggest-capture: "You were asked to review #511. Add to today?"                                                           |
| Slack thread gets new replies        | Change dot. No notification by default.                                                                                            |

Rules: Noto suggests, the user confirms. Nothing is completed or created without an explicit opt-in.
Every automatic change appears in the item's history as _"via GitHub"_.

### 6.4 Paste-to-task

Pasting a recognised URL into an empty add field creates a titled item ("Review: Add rich preview support
(#482)") with the link attached. The title is editable, and the cursor lands at its end.

---

## 7. Capture

### 7.1 Inline add & the command bar (⌘K)

One input understands tokens. Each token is highlighted as it's recognised, and Backspace removes the
whole token:

```
 Review PR #482 tomorrow ~30m !2 #backend @waiting:priya /work
                ────────  ──── ── ──────── ──────────── ─────
                 date     size pri  tag     waiting-on   workspace
```

The same bar (⌘K) is also navigation and commands: "go personal", "defer all stuck to monday", "show
items carried > 5", "switch to board". Matches are ranked fuzzily; the keyboard shortcut for each command
appears next to it so users learn the shortcuts.

**Results are ranked in blocks, and the blocks are ordered by how much the user probably wants them:**

1. **Core commands** (go to…, start review, appearance, presets, pressure, undo, toggles).
2. **Items** (full-text search).
3. **Guide topics** — reference, not actions.
4. **"Add this"** — always last, because it is the fallback.

This ordering is a rule, not a tuning detail. v2 ranked everything in one fuzzy list, so typing `dep`
returned three `Guide: …` rows above the actual item named "Deploy". A fuzzy hit on documentation must
never outrank the user's own work.

### 7.2 Global quick capture

- **Global hotkey** ⌃⌥Space by default (configurable). A floating 560px panel shows the token input and a
  workspace picker (defaults by focus hours).
- **Capture with context (macOS):** if the frontmost app is a browser, Slack or an editor, show a
  **"＋ attach current page"** chip with the URL already filled in. One key (⌘L) attaches it. This needs
  Accessibility/Automation permission, requested only the first time the user tries it.
- **Menubar item:** shows the Now item and its timer, the count of items needing a decision, and quick
  add.
- Target: hotkey → item saved in under 2s, with no app switch.

### 7.3 Now & focus timer

Pressing `f` on any item makes it **Now**. An optional mini-window (⌘⇧N) stays on top:
`Deploy v2.3 · 23:10 · ✓ done`. Time tracked here builds the user's estimate accuracy ("you estimate 1h,
you take 1h40m").

---

## 8. Reflection: Weekly Review & Insights

### 8.1 Weekly Review (guided, ~5 minutes)

Prompted on a chosen day (default: Friday afternoon or Monday morning).

1. **Wins:** items completed, grouped by workspace, with the oldest item finally done highlighted
   ("Finally: _Migrate auth to OAuth_, after 23 days").
2. **Someday sweep:** each Someday item: promote, keep or drop. Items untouched for 60+ days are
   pre-selected for dropping.
3. **Stuck patterns:** the reason breakdown (§5) and one concrete suggestion.
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

Insight catalog for v1: size vs completion, weekday overload, rollover trend, stuck-reason mix, estimate
accuracy, oldest open items, Waiting-on duration by person. Each insight is computed locally and needs a
minimum sample size (n ≥ 10) before it appears, so Noto doesn't show noise.

**The empty state has to earn its space.** Before there are 10 samples, the page shows the catalogue
greyed out with a progress-to-unlock meter ("3 / 10 days of history"), never a bare sentence on an empty
page. _(The catalogue and the meter are not built yet — see §20.3. Today the page renders only "oldest
open items" and a large void, which makes it the weakest screen in the app.)_

### 8.3 Time travel

The day strip and `[` / `]` navigate days. Past days are a **read-only log** (what was planned, done,
carried, decided), reconstructed from item state and history. Future days show items planned or deferred
to them (`planned_for`), so planning ahead is possible.

---

## 9. Modes, Presented as Lenses

Modes are three independent controls (**Layout × Order × Pressure**), and the six named modes are presets
over them. The presets, pressure thresholds and switching guarantees are specified in
[03-modes-and-workspaces.md](03-modes-and-workspaces.md); this section covers only how they look.

The mode switcher (⌘⇧M, or click the mode name in the header) shows the preset and a one-line "what
changes" preview, e.g. _"Accountability: sorts by carry, pins your 3 most-carried items, Morning Review
can't be skipped."_ Users can tweak any control; the preset name then shows "Sprint (custom)".

Controls are labelled in words, never enum names: **Priority + carry**, not `PriorityCarry`. Each control
carries a one-sentence tooltip and an ⓘ that opens the Guide at the matching topic (§2.1), because the
three-control model is the least guessable part of the app.

Switching is instant and **never writes to items**. Items that lack the layout's natural field get a
visible lane instead of being hidden ("No date", "Not habits (12)").

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

Someday, Backlog, Today, Waiting and Done are fixed columns mapped to item state. User columns (like
"In progress") sit between Backlog and Today. Only Today accrues carry. The WIP limit shows as `2/3` in
the column header. Over the limit, the header turns warm and a drop into that column asks to swap an item
out.

Column rules, learned from a broken first attempt:

- Columns are a fixed comfortable width with a minimum height, and **the column is the horizontal scroll
  unit** — a column never clips its own contents mid-word.
- Board rows use a **compact trailing rail** (the ask and the carry count only). A board column is too
  narrow for estimates and priorities, and the title must keep its room.
- Adding a column is a ghost **"+ Column"** affordance, not a raw name/WIP form bolted under the board.
- _(Not built: drag-and-drop between columns. Today only keyboard moves work — `⇧←`/`⇧→`.)_

**Timeline (Deadline)**

```
           Today   Thu    Fri  │ Mon    Tue    Wed
 Overdue ▌ Submit report (2d late)
         ├──■ Exam prep ch.4 ──────────┤ due Fri
                  ├── Slides ──┤ due Fri
 No date ▌ 3 items
```

_(Not built: the timeline is currently a grouped list, not bars against a date axis.)_

**Habit grid**

```
                 M T W T F S S   streak
 Morning run     ■ ■ □ ■ ■ · ·   2  (best 11)
 Read 20 pages   ■ ■ ■ ■ ■ · ·   5
 No phone in bed ■ □ ■ □ ■ · ·   1
```

Missed days show as an empty square, not a red X. Habits use **"flex streaks"** (configurable, e.g. "5 of
7 days"), so one miss doesn't erase months of progress. _(Not built: only today's cell is tickable.)_

---

## 10. Item Row & Inspector Specification

### 10.1 Row anatomy (comfortable density, 38px)

```
 ▌ ○  Deploy v2.3 to staging  ⟨GH #511 · merged •⟩   Why?  ↻4   ~1h   !2
 │ │  │                        │                      │     │     │     │
 │ │  title (inline edit: e)   link chip, max 1       │     │     │     └ priority (aligned column)
 │ │  status glyph: ○ planned ◉ now ◌ waiting ✓ done ⊘ dropped
 │ │                          └ "Why?" — a quiet ask, only when stuck
 │ └ the carry glyph + count: the one emphasised trailing value
 └ ONE 3px bar: pressure wins, else the workspace accent (Today-all lens)
```

**One bar, not two.** v2 drew a pressure bar and a workspace bar in adjacent 3px columns, so a row could
show two bars side by side with no way to tell which was which. There is now a single bar with a defined
precedence: **pressure beats workspace**. A workspace tick is only drawn when there is no pressure bar.

**The trailing rail is aligned.** Extra text, the "Why?" pill, carry, estimate and priority sit in
fixed-width columns so they form a readable column down the list. v2 right-aligned them into a single
grey run, which made the one number that matters (carry) impossible to find at a glance. Carry is the
only emphasised value; estimate and priority are `text-3`.

**Pressure presentation** (never colour alone — the glyph and the count carry the meaning too):

| State | Carry glyph | Text token       | Row treatment                      |
| ----- | ----------- | ---------------- | ---------------------------------- |
| Fresh | (none)      | —                | none                               |
| Warm  | `↻2`        | `pressure-warm`  | none                               |
| Hot   | `↻4`        | `pressure-hot`   | 3px left bar                       |
| Stale | `↻7`        | `pressure-stale` | 3px left bar, plus the "Why?" pill |

Pressure is **one ramp** — amber → orange → rust — so escalating reads as _more of the same_, not as
three unrelated alarms. No pulsing, no scaling, no full-row tints, and never red.

| Density     | Row height | Shows                                                            |
| ----------- | ---------- | ---------------------------------------------------------------- |
| Compact     | 28px       | glyph, title, carry. Chips collapse to a provider glyph.         |
| Comfortable | 38px       | everything above, at most 1 chip, tags truncated                 |
| Spacious    | 56px       | plus a second line with notes excerpt or subtasks progress `2/5` |

### 10.2 Inspector

Sections, top to bottom: title and notes (Markdown, inline), **the three numbers** (Age · Carry ·
Defers), stuck prompt (if any), schedule (planned for, due, recurrence), links (expanded cards),
subtasks, **Life of this item** (the `TodoHistory` timeline in plain language: "Carried 3 times",
"Deferred to Mon", "Unblocked via Jira"), then a danger zone (drop, move workspace).

**Target design (not built).** The inspector is currently a form: seven stacked full-width dropdowns
before any content, which reads as settings rather than detail. It should be **read-first** — the values
shown as text, which become a control when clicked — with the controls grouped under _Details_,
_Schedule_, _Links_, _Subtasks_, _Life of this item_ and _Danger zone_, and the "INSPECTOR" header chrome
removed.

---

## 11. Visual System — "Ledger"

### 11.1 Tokens

Both themes are first-class; the app follows the OS by default and can be pinned to Light or Dark
(§12). Every pair below is verified by `ThemeTests` against WCAG 2.1: text tokens reach **≥ 4.5:1** on
`canvas`, `surface`, `sidebar` and `raised`, **and on `hover`**, with one documented exception — dark
`text-3` on `hover` is 3.98:1, so a hovered row renders its metadata in `text-2` instead.

**Dark** — the ramp is monotonic, darkest to lightest:
`sidebar < canvas < surface < raised < border < hover`. Monotonicity matters: if `hover` were darker than
`raised`, a hovered row would read as _pressed_.

| Token            | Dark      | Use                                                        |
| ---------------- | --------- | ---------------------------------------------------------- |
| `canvas`         | `#141210` | Window background                                          |
| `surface`        | `#1B1917` | Lists, panels — "the paper"                                |
| `sidebar`        | `#100F0D` | Recessed chrome                                            |
| `raised`         | `#232120` | Popovers, inspector, chips                                 |
| `hover`          | `#322E2A` | Hover / focused row                                        |
| `border`         | `#2C2825` | Input borders (hairlines are a separate translucent brush) |
| `text-1`         | `#F2EFEA` | Primary text                                               |
| `text-2`         | `#B3ADA4` | Secondary text                                             |
| `text-3`         | `#918B81` | Hints, metadata — the hover exception above                |
| `link`           | `#8FA9E8` | Links, focus ring                                          |
| `done`           | `#8FBE77` | Completion                                                 |
| `pressure-warm`  | `#D9A441` | Carry, warm                                                |
| `pressure-hot`   | `#E0813C` | Carry, hot                                                 |
| `pressure-stale` | `#EA7C46` | Carry, stale                                               |

**Light** — `border < hover < sidebar < raised < canvas < surface`:

| Token            | Light     | Use                         |
| ---------------- | --------- | --------------------------- |
| `canvas`         | `#F6F3EE` | Window background           |
| `surface`        | `#FFFDF9` | Lists, panels — "the paper" |
| `sidebar`        | `#EFEBE3` | Recessed chrome             |
| `raised`         | `#F1EDE6` | Popovers, inspector, chips  |
| `hover`          | `#EBE6DC` | Hover / focused row         |
| `border`         | `#E0DACE` | Input borders               |
| `text-1`         | `#1C1A17` | Primary text                |
| `text-2`         | `#5C564D` | Secondary text              |
| `text-3`         | `#6A645A` | Hints, metadata             |
| `link`           | `#33529E` | Links, focus ring           |
| `done`           | `#4A6B3A` | Completion                  |
| `pressure-warm`  | `#7E5A0C` | Carry, warm                 |
| `pressure-hot`   | `#9E4A15` | Carry, hot                  |
| `pressure-stale` | `#8F3413` | Carry, stale                |

The tightest pairs in the whole system are the light-theme pressure ramp on `hover` (warm 5.03, hot 4.90)
and dark `text-3` on `raised` (4.74) — a `text-3` that is any lighter breaks the first rule, any darker
breaks the hover exception. Treat those as fixed points when touching the palette.

**Workspace accents.** Ten "editorial inks", tuned per theme, all ≥ 3:1 against `canvas` and `surface`
(they are non-text signals: bars, ticks, selection). A workspace stores either one of these names or a
literal `#rrggbb`:

| Name            | Dark      | Light     |     | Name    | Dark      | Light     |
| --------------- | --------- | --------- | --- | ------- | --------- | --------- |
| `ink` (default) | `#7E9BF0` | `#3A5CCC` |     | `teal`  | `#4FC9C0` | `#0F6F6B` |
| `rust`          | `#E08A5A` | `#B4531F` |     | `clay`  | `#D19A82` | `#8A5340` |
| `moss`          | `#8FBE77` | `#4A6B3A` |     | `slate` | `#A9B2C0` | `#4A5568` |
| `ochre`         | `#E0C24F` | `#8A6A00` |     | `wine`  | `#E07A9A` | `#8A2B4A` |
| `plum`          | `#C88BC8` | `#7A3E7A` |     | `olive` | `#B7C56A` | `#5F6B1A` |

The workspace accent **is** the app's accent for that workspace (selection, primary buttons, focus
timer). There is no global brand blue competing with it. Workspace **templates store accent names, not
hex**, so a template's colour adapts to the theme rather than being frozen to one.

**The framework accent is repointed, not left alone.** Avalonia's Fluent theme draws its own controls —
Slider, CheckBox, ToggleSwitch, selection — with a *system* accent that is Windows blue (`#0078D7`).
Setting our own `AccentBrush` does nothing for those; they keep the blue regardless. `ThemeBuilder` also
writes `SystemAccentColor`, its six derived tints and the `SystemControl*AccentBrush` keys, plus the
Slider track/thumb and the CheckBox/ToggleSwitch fills, into both theme dictionaries. Skipping this is
invisible until you look at a Slider, which is how it survived the first pass.

**The lit edge.** In dark, raised surfaces and popovers get a 1px top highlight (white at ~10% alpha).
It is what makes a dark UI read as layered without a shadow, and it is the only decorative effect in the
system.

### 11.2 Type & spacing

- **Typeface:** system UI (SF Pro on macOS, Segoe UI Variable on Windows, Inter as the bundled fallback
  for Linux and WASM) for **all chrome and rows**. One exception: a **display serif** for the page title
  and other places where personality belongs, using a system serif stack (Newsreader, Fraunces, Iowan Old
  Style, Palatino, Georgia) — nothing is bundled, so it degrades honestly. **Tabular numerals** for every
  count, time and carry glyph, so columns don't jitter.
- **Scale:** 11 (micro, uppercase section label) / 12 (meta) / 14 (body **and** row titles, the latter at
  weight 500) / 15 (card headline) / 20 (page title, non-list pages) / 30 (display, list pages). The user
  setting scales the body from 13 to 18 and everything else follows.
- **Hierarchy rule:** content outranks chrome. The display title is the loudest element on a page; row
  titles are 14px medium; metadata is 12px `text-3`. v2 inverted this.
- **Spacing:** 4px base grid. Row padding 8/12. Sections separated by 20px and an uppercase 11px label in
  `text-3`.
- **Radius:** 6px (chips, keyboard hints), 8px (inputs, icon buttons), 12px (panels), 16px (popovers).
- **Elevation:** only true overlays have shadows — popovers, the quick-capture panel, the command palette
  and the toast. Everything else separates by surface value and the lit edge, not by shadow.
- **Keyboard hints are chips.** A shortcut is rendered in a bordered monospace chip, not as bare `⌘K`
  text floating in the layout.

### 11.3 Motion

| Trigger               | Animation                                                                                          | Duration      |
| --------------------- | -------------------------------------------------------------------------------------------------- | ------------- |
| Complete item         | Glyph fills; row holds 600ms (an undo window), then slides into Done                               | 150ms + 200ms |
| Review decision       | Card exits in the direction of the decision (←defer, ↑someday, →today)                             | 180ms         |
| Add item              | Expands from the input                                                                             | 150ms         |
| Section collapse      | Body fades while height changes instantly (no layout animation)                                    | 160ms         |
| Layout switch         | Cross-fade (no morph: cheaper in Avalonia and less disorienting)                                   | 150ms         |
| Day change while open | Banner "It's a new day. 3 items to decide" slides in. The list doesn't reflow underneath the user. | 200ms         |

Easing is exponential (`CubicEaseOut`, `ExponentialEaseOut`) — never bounce or elastic. Only transform,
opacity and colour are animated; **never layout properties**. All motion is gated behind the window's
`motion` class, which is off under the OS reduce-motion setting
(`NSWorkspace.accessibilityDisplayShouldReduceMotion`), with state changes shown instantly.

### 11.4 Contrast budget

`ThemeTests` is the enforcement, not this table. It checks every text token against `canvas`, `surface`,
`sidebar`, `raised` and `hover` in both themes, every accent against `canvas` and `surface`, that both
themes define the same token keys, and that both ramps are monotonic. `ControlStyleTests` separately pins
the _rendered_ dropdown state colours to the tokens, which is why the `ComboBox*` overrides in
`Styles.axaml` must move in lockstep with `ThemeTokens`.

Do not weaken an assertion to make a colour pass. Change the colour.

---

## 12. Keyboard Map

Single-key shortcuts work when the list has focus (Linear/Superhuman style). Modifier shortcuts work
everywhere. This map avoids the OS conflicts in the old spec: ⌘M (minimize), ⌘S (save), ⌥Space (types a
non-breaking space on macOS), Alt+Space (window menu on Windows), and ⌘←/⌘→/⌘⌫ (text-editing keys inside
inputs).

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
| **Switch appearance**    | **⌘⇧L**          | Search                  | /          |
| Backlog view             | g b              | Today view              | g t        |
| Multi-select             | ⇧j / ⇧k, ⌘A      | Undo / Redo             | ⌘Z / ⌘⇧Z   |
| Shortcut help            | ?                |                         |            |

Every decision key works on a **multi-selection**: select 6 items, press `d`, type "mon", and all six are
deferred.

On Windows/Linux, ⌘ maps to Ctrl, and the global capture default becomes Ctrl+Alt+Space.

### 12.1 Appearance

The theme is a per-device setting with three surfaces, because a setting you cannot reach quickly is a
setting nobody uses:

- **Settings → Appearance** — a `System / Light / Dark` segmented control.
- **⌘K** — `Appearance: System|Light|Dark`, plus "Switch appearance".
- **⌘⇧L** — cycles System → Light → Dark → System, confirming with a toast.

`System` means _follow the OS_, not _pick a default_: it maps to `ThemeVariant.Default`, and the window
re-derives accents when the platform variant changes. Switching the theme must repaint **everything**
immediately, including workspace accents — see §15 for how.

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

- **Carry-over review as a card stack.** Swipe → today, ← defer (opens a date sheet), ↑ someday, ↓ drop.
  Buttons are also shown, so gestures aren't the only way to act.
- **Row swipes:** right = complete, left = defer.
- Large central ＋ for capture. Share-sheet target: "Add to Noto" with the shared URL attached.
- Widgets: Now item with timer; "N items need a decision".

### 13.2 Web (WASM)

The same layouts as desktop, responsive at 480 / 768 / 1100px breakpoints. Things the web build can't do,
labelled in the UI rather than hidden: global hotkey (replaced by a bookmarklet/extension later) and
capture with context. Connected-app previews and live links work on web **through the Noto backend's
Connected Apps Gateway**, because browsers can't call provider APIs directly
([10 › Web](10-connected-apps.md#web-connected-apps-gateway)). Without a signed-in backend, web shows
plain links with a "Sign in to Noto Sync for live links" hint.

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

| Concern                           | Approach                                                                                                                                                                                                                                                                                                                                                                                                                          |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Virtualized sectioned list        | `ItemsRepeater` with a grouped source, or `ListBox` with `VirtualizingStackPanel` and section-header items. Keep row templates cheap: chips render from cached view models, never fetch during layout. **Not built — rows are not virtualized yet.**                                                                                                                                                                              |
| Single-key shortcuts              | `KeyBindings` on the list container, active only when focus isn't inside a `TextBox`.                                                                                                                                                                                                                                                                                                                                             |
| Global hotkey and menubar (macOS) | Native interop: `RegisterEventHotKey` (Carbon) for the hotkey, and Avalonia `TrayIcon` + `NativeMenu` for the menubar. Windows: `RegisterHotKey`. Budget this as platform work, not UI work.                                                                                                                                                                                                                                      |
| Capture with context              | macOS Apple Events to browsers / Accessibility API. Requires entitlements and a usage description. Ship it after the core loop works.                                                                                                                                                                                                                                                                                             |
| Floating quick-capture panel      | A borderless `Window` with `Topmost`, shown by the hotkey service, kept warm in memory for the < 2s target.                                                                                                                                                                                                                                                                                                                       |
| Accessibility                     | `AutomationProperties.Name` and `HelpText` on every row ("Deploy v2.3, planned, carried 4 times, estimate 1 hour"). Test with VoiceOver every release. A visible focus ring is required on every focusable control.                                                                                                                                                                                                               |
| Reduce motion                     | Read the OS setting at startup and on change, and gate every `Transitions` block on it.                                                                                                                                                                                                                                                                                                                                           |
| Theming                           | Fluent theme with token overrides in `ThemeDictionaries` (Dark/Light). The workspace accent is swapped at runtime via a dynamic resource.                                                                                                                                                                                                                                                                                         |
| **Repainting accents**            | A `FuncValueConverter` resolves **once** and cannot re-run, so a binding that converted an accent name into a brush kept the old colour after a theme switch. Accents are therefore **shared mutable brushes** — one `SolidColorBrush` per accent key, recoloured in place — and the window re-derives the global accent resources and repaints those brushes on every variant change (including the OS flipping under `System`). |
| **Collapsing a grid rail**        | `IsVisible="False"` on a cell does **not** collapse its grid column, so a fixed-width rail keeps consuming space. To collapse a rail, put it in a separate container and toggle that.                                                                                                                                                                                                                                             |
| **Overlapping bars**              | Use a `Panel` to stack two bars in one grid cell when one must win by precedence (the single 3px row bar).                                                                                                                                                                                                                                                                                                                        |

---

## 16. Onboarding & Empty States

- **No four seeded workspaces.** First launch asks one question: _"What do you want Noto to keep
  honest?"_ (Work · Personal · Both). That creates one or two workspaces with the Sprint/Zen presets.
  More can be added later.
- **Import first:** Todoist, Things, Apple Reminders, TickTick (CSV/JSON) and a Markdown checklist.
  Imported open items start with carry 0. (We don't punish people for history from another app.)
- **Empty Today:** "Nothing planned. Pull from Someday (4) or add something." It shows a calm state,
  never an illustration of a sad mascot.
- **First carry-over** (day 2): a one-time coach mark explains the decision keys.
- **General rule:** an empty state must be _useful_ — say what will appear here, and offer the next
  action. A sentence on an otherwise blank page is a bug, not a design.

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

Two design-level checks the modernization added:

| Check                                                                                | Target |
| ------------------------------------------------------------------------------------ | ------ |
| "Which item needs a decision?" — time to find the carried item on Today              | < 1.5s |
| Every screen constrained to a comfortable measure; no screen mostly empty by default | pass   |

---

## 19. Deliberately Not Doing

- **Shame mechanics** (skulls, "Wall of Shame", pulsing red). They push users to quit, not to finish.
  This extends to colour: nothing about a carried item is red.
- **Imitating Liquid Glass for its own sake.** The macOS Tahoe material's _ideas_ are taken seriously —
  controls are a layer above content, chrome recedes, sidebar recedes, corners align — but Avalonia
  cannot render the real material, and a blur that costs legibility or frame time is worse than a flat,
  honest surface.
- **Dark-first neon, gradients and glassmorphism.** They are the tells of a default theme.
- **AI auto-planning that moves tasks around on its own.** Noto suggests with visible evidence. The user
  decides. (A local "suggest a plan for today" can come later, built on the same evidence.)
- **Inline full preview cards in rows.** They live in the inspector.
- **Streak-loss punishment.** Flex streaks and neutral "missed" states instead.
- **Gamification, mascots and celebration confetti.**
- **Collaboration UI.** Teams is a business-doc aspiration with no model behind it yet. It needs its own
  design pass.
- **Animating layout properties**, or motion that ignores reduce-motion.

---

## 20. Appendix: The Modernization Pass

Recorded here so the reasoning is not lost. The body of this document is the design; this section is the
history of how it got there and what is still owed.

### 20.1 What was wrong

Measured against the 27 headless screenshots (`mise run screenshots`), not against taste.

| #   | Screen(s)                                | Observation                                                                                                                 | Severity |
| --- | ---------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | -------- |
| A   | `today-dark`, `today-light`              | Sidebar / list / inspector were near-identical in value. No depth, no layering.                                             | High     |
| B   | all                                      | One framework blue did six jobs at once (progress, selection, Now, buttons, links), and competed with the workspace accent. | High     |
| C   | `today-*`, `board-dark`                  | Trailing metadata was one grey smear at 12px. Carry — the most important number — did not stand out.                        | High     |
| D   | `today-*`, `board-dark`, `timeline-dark` | The `stuck?` chip was a **red-outlined** pill: red means alarm, contradicting principle 2.                                  | High     |
| E   | `ItemRowView`                            | Three pressure hues plus a separate red for stuck, and two adjacent 3px bars with no way to tell them apart.                | High     |
| F   | `today-*`, `todayall-*`                  | Chrome was louder than content: a 15px centred toolbar title was the largest text while task titles were 13px.              | High     |
| G   | `today-*`, `links-*`                     | The inspector was a form: seven stacked dropdowns before any content.                                                       | High     |
| H   | `board-dark`                             | **Broken.** Fixed columns overflowed and clipped mid-word; the add-column row was a raw form; no drag-and-drop.             | Critical |
| I   | `insights-dark`                          | **Weakest screen.** One list and a large void; the narrative insight cards were not rendered.                               | Critical |
| J   | `settings-*`                             | 574 lines of label + control rows; a `ToggleSwitch` sat below its own label.                                                | Medium   |
| K   | `commandbar-dark`                        | Typing `dep` returned three `Guide: …` rows before the actual item.                                                         | Medium   |
| L   | `weekly-*`, `habits-*`, `insights-*`     | Large dead voids; content was not constrained to a measure.                                                                 | Medium   |
| M   | `today-*`, `review-*`                    | Icon system inconsistent: `PathIcon`s mixed with literal `⌘⇧R` / `Esc` / `↻` text.                                          | Low      |
| N   | `today-dark`                             | Sidebar was inert; seven nav items of equal weight.                                                                         | Medium   |
| O   | `narrow-light`                           | The centred toolbar title duplicated the page header.                                                                       | Medium   |
| P   | `today-*`                                | The 14-day strip was illegible — 14px bars, 2px empty days, 9px labels.                                                     | Medium   |
| Q   | `review-*`                               | The review card was a plain box; the exit was not directional.                                                              | Medium   |
| R   | `habits-dark`                            | Only today's cell was tickable.                                                                                             | Low      |
| S   | `timeline-dark`                          | "Timeline" was a plain grouped list, not a timeline.                                                                        | Medium   |

### 20.2 What shipped

Verified by the full suite: **983 tests, all passing** (`mise run ci`; the `Noto.Platform.Tests.Mac_keychain_*`
test needs real macOS Keychain access, so it only passes outside a sandbox).

- **The visual system (§11).** Warm-neutral ramps, ten editorial accent inks, one pressure ramp, the lit
  edge, warm hairlines, the 6/8/12/16 radius scale and the type scale. The palette was verified
  numerically against the contrast budget **before** it was written, so `ThemeTests` passed on the first
  run. Both ramps are monotonic.
- **Row v2 (§10.1).** One bar with precedence, an aligned trailing rail, the quiet "Why?" pill, and a
  compact rail for board columns.
- **The display header (§3.4).** The title left the toolbar for the content, with day navigation beside
  it.
- **Board (§9.1).** Wider columns, the ghost "+ Column" flyout, the compact rail, and **drag-and-drop
  between columns** — a drop runs the same command as the keyboard move, so the WIP check and the undo
  toast behave identically.
- **Command palette (§7.1).** Block ranking, so guide topics can no longer outrank real items. Pinned by
  a test.
- **Appearance (§12.1).** The segmented control, the ⌘K entries, ⌘⇧L, `System` following the OS live, and
  the accent-repaint fix in §15.
- **Insights (§8.2).** The narrative cards now render, the rollover trend draws as a **sparkline**, and
  the no-history state is a greyed **catalogue with a progress-to-unlock meter** instead of a sentence on
  a blank page. `InsightsEngine` exposes per-insight sample counts for it.
- **Inspector (§10.2).** Rebuilt read-first: no pane header, the title as the heading, and Details /
  Schedule shown as values that only become controls under the pointer or keyboard. The stuck prompt's
  icon moved off red.
- **Settings.** A category rail (Account & sync / Workspace & modes / Capacity & day / Appearance / Data
  & this device) with one focused pane, and the time-zone toggle is a labelled row rather than a switch
  with its caption stacked above it. The Appearance pane is split by scope: **This device** (theme,
  density, text size) and **This workspace** (accent).
- **Accent settings (§11.1).** Ten swatches plus a custom hex field, saved on the workspace rather than
  the device. Selecting one repaints the app immediately, and the Fluent system accent is repointed so
  the Slider, CheckBox and ToggleSwitch follow it instead of staying Windows blue.
- **Day strip (§3.1).** Taller bars on a shared baseline, so an empty day reads as zero on an axis
  instead of a stray dash.
- **Review (§4.1).** A serif greeting, an accent "Today" key, and the **directional exit**: the card
  leaves toward the decision (←defer, ↑someday, ↓drop, →today), so the motion is the confirmation.
- **Shutdown and Weekly Review (§4.3, §8.1).** The same card treatment as Review: a step is a card rather
  than a loose column on a page, the step title is the display serif, and a progress rail shows where you
  are in the three- and five-step flows.
- **Timeline (§9.1).** A real timeline: every row draws a bar from today to its due date on a shared
  14-day axis with weekday labels, and overdue is pinned to the left edge in the warm ramp.
- **Habits (§9.1).** Any scheduled day up to today can be ticked. A missed skip-rule day has no instance,
  so `RecurrenceService.EnsureOccurrenceAsync` materialises it first.
- **Capture (§7.1).** Recognised tokens are coloured inline in the field (a highlighted mirror behind a
  transparent `TextBox`), and pasting a URL into an empty field makes it the task — which the link
  indexer then attaches.
- **List virtualization (§15).** `SectionsList` renders one flattened sequence (header, then rows) through
  a `VirtualizingStackPanel`, so a large backlog only realises what is on screen.
- **Icon and `kbd` consistency (diagnosis row M).** Shortcut hints are chips: the toolbar ⌘K, the Review
  button, the undo toast, the review footer and every row of the shortcuts overlay.
- **Workspace templates** now store accent names rather than hex.

### 20.3 What is still owed

1. **Capture token highlighting in the command bar (§7.1)** — the palette has its own chip row and was left
   alone; only the Today composer highlights inline.
2. **Board titles** still truncate in a narrow column once the ask and carry are drawn.
3. **Progressive toolbar material (§5.4)** — see below.

### 20.4 Deliberately not implemented, and why

These were on the deferred list and are **not** done. Each was a judgement call, not an oversight.

- **The 600ms "row holds, then slides into Done" hold (§11.3).** The undo window is already served by the
  toast, and holding the row means delaying the state change — which would make the command, the undo
  stack and every test that asserts an immediate effect disagree with what is on screen. What shipped
  instead is the part that is free: the glyph **fills** over 600ms rather than snapping.
- **"Add item expands from the input" (§11.3).** The row list is rebuilt on every snapshot refresh, so an
  expand animation on the new row would also fire on every unrelated refresh. Doing it properly means
  diffing rows by id and animating only genuinely new ones — a list-rendering change that deserves its own
  pass rather than a flicker.
- **Progressive toolbar material (§5.4).** The shell's toolbar is **docked**, not an overlay: it occupies
  its own grid row, so page content never passes beneath it and a scroll-driven material would be inert.
  Making it meaningful means converting the shell to an overlay toolbar with the content scrolling under a
  translucent bar — a shell redesign that belongs with WS2, not a style tweak.

### 20.5 Delivery order

Workstreams 1–3 were the spine and had to land first, because everything else consumes the tokens:

| WS  | Workstream                                                                | Status                               |
| --- | ------------------------------------------------------------------------- | ------------------------------------ |
| 1   | Foundations — tokens, type, radii, elevation, icons, `kbd`                | **Done**                             |
| 2   | Shell & IA — header, sidebar, inspector pane, toolbar material, day strip | **Partly done** (header only)        |
| 3   | List & row — row v2, sections, virtualization, focus ring                 | **Partly done** (row v2, focus ring) |
| 4   | Signature screens — Review, Shutdown, Weekly, Insights, Onboarding        | **Mostly done** (Review, Insights)   |
| 5   | Layouts — Board, Timeline, Habits                                         | **Done**                             |
| 6   | Input & overlays — palette, capture                                       | **Done**                             |
| 7   | Settings & account                                                        | **Done** (rail + panes)              |
| 8   | Platform polish & a11y                                                    | Continuous                           |

Guardrails, learned the hard way:

- **Screenshot and token tests pin visuals deliberately.** Update expectations in the _same_ commit as the
  visual change, never in a later "fix tests" commit. Do not weaken an assertion to make a colour pass.
  A weaker assertion hides real bugs: `> 0` coloured runs passed while _no_ colour was being applied.
- **An incremental build can hide a XAML error.** `dotnet build` reported "Build succeeded, 0 Warnings"
  while the Avalonia compiler had failed, and every Avalonia test then failed with "No precompiled XAML
  found for Noto.App.App". If the UI breaks for no visible reason, run
  `dotnet build src/Noto.App --no-incremental` and look for `AVLN` errors.
- **Attached events are not regular ones.** `DragOver`/`Drop` on a `Border` must be written
  `DragDrop.DragOver`/`DragDrop.Drop`, or the XAML compiler rejects the property.
- **`TryGetResource(key, null, …)` does not find a theme-dictionary resource.** Pass
  `Application.Current.ActualThemeVariant` when resolving a brush from code.
- **`TreatWarningsAsErrors` is on and CSharpier owns `.axaml` wrapping** — run `mise run fmt` before
  committing; don't hand-align XAML attributes.
- **A new page needs two registrations** — the `GoAsync` case _and_ the `App.ViewModel` `DataTemplate` —
  or it renders blank.
- **Do not start WS4–7 before the tokens are stable**, or the palette drifts.

### 20.6 File map

| Area                   | Files                                                                                                                              |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Tokens & theme         | `Themes/ThemeTokens.cs`, `Themes/ThemeBuilder.cs`, `Themes/Styles.axaml`                                                           |
| Shell                  | `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `ViewModels/ShellViewModel*.cs`                                             |
| Row                    | `Views/ItemRowView.axaml`, `ViewModels/ItemRowViewModel.cs`, `Views/RowConverters.cs`                                              |
| Today                  | `Views/TodayView.axaml`, `Views/SectionsList.axaml`, `Views/PromptBar.axaml`                                                       |
| Inspector              | `Views/InspectorView.axaml`, `ViewModels/InspectorViewModel.cs`                                                                    |
| Layouts                | `Views/BoardView.axaml`, `Views/HabitGridView.axaml`, `ViewModels/Layouts/*`                                                       |
| Palette                | `ViewModels/CommandBarViewModel.cs`, `Logic/KeyMap.cs`                                                                             |
| Appearance             | `ViewModels/AppearanceViewModel.cs`, `Views/SettingsView.axaml`                                                                    |
| Tests that pin visuals | `tests/Noto.App.Tests/ThemeTests.cs`, `tests/Noto.App.Tests/Ui/ControlStyleTests.cs`, `tests/Noto.App.Tests/Ui/ScreenshotTests.cs` |
