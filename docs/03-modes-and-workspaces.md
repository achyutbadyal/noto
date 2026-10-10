# Noto — Modes & Workspaces Design

## Workspace System

A **Workspace** is the top-level organizational container. It isolates:

- Todo items, their history, recurrence rules and tags
- Layout, order and pressure settings (its **mode**)
- Theme color
- Stats and insights
- Sync on/off
- Time zone, day boundary, focus hours and daily capacity

### First Launch

Noto doesn't seed four workspaces. It asks one question, _"What do you want Noto to keep honest?"_, and creates:

| Answer   | Workspaces created                                 |
| -------- | -------------------------------------------------- |
| Work     | Work (Sprint preset, focus hours Mon–Fri 09–18)    |
| Personal | Personal (Zen preset)                              |
| Both     | Both of the above                                  |

Health (Habit preset) and Side Projects (Kanban preset) are offered as one-click templates in "New workspace". Users can create, rename, reorder, archive and delete workspaces freely. Deletion is soft, with a 30-day recovery period (UC-02).

### Workspace Properties

The full field list is in [04 › Workspace](04-domain-model.md#23-workspace). The behavior-relevant fields:

```
Workspace {
    time_zone, tz_follows_device, day_boundary     // logical day (04 › Time Semantics)
    focus_hours?                                   // e.g. Mon–Fri 09:00–18:00
    capacity_unit, daily_capacity                  // minutes (default 360) | items
    preset                                         // sprint | zen | deadline | habit | kanban | accountability | custom
    layout, sort_order_mode, pressure              // the three mode controls (below)
    pressure_overrides?, layout_settings
    sync_enabled
}
```

### Focus Hours

Focus hours separate contexts in **time** as well as in data:

- Outside its focus hours, a workspace's items drop out of the cross-workspace **Today (all)** lens, and its badge counts go quiet.
- Quick capture defaults to the workspace whose focus hours are active (falling back to the configured default).
- The morning nudge and Shutdown prompt are tied to the start and end of focus hours.
- Data stays fully accessible. Opening the workspace directly always shows everything.

---

## Mode System

A **mode** changes how a workspace _looks and pushes_, never what's stored. Earlier drafts defined six monolithic modes, each bundling layout, sorting and pressure. That caused contradictions: Deadline made `due_date` required, Kanban disabled rollover, and Habit made everything recurring. A mode is now **three independent controls**, and the six named modes are **presets** over them.

| Control      | Options                                                                 |
| ------------ | ----------------------------------------------------------------------- |
| **Layout**   | `list` (sectioned) · `board` · `timeline` · `habit_grid`                |
| **Order**    | `manual` · `priority_carry` · `due_date` · `carry_desc` · `time_of_day` |
| **Pressure** | `gentle` · `honest` · `relentless`                                      |

### Presets

| Preset            | Layout          | Order               | Pressure                      | Best for                               |
| ----------------- | --------------- | ------------------- | ----------------------------- | -------------------------------------- |
| Sprint            | list            | priority_carry      | honest                        | Work, daily throughput                 |
| Zen               | list (Spacious) | manual              | gentle                        | Personal, low pressure                 |
| Deadline          | timeline        | due_date            | honest                        | Exams, launches, time-boxed projects   |
| Habit             | habit_grid      | time_of_day         | gentle                        | Health, routines                       |
| Kanban            | board           | manual (per column) | gentle (stuck-in-column only) | Side projects, multi-phase work        |
| Accountability    | list            | carry_desc          | relentless                    | Any workspace where you want the truth |

Changing any control turns the preset into `"<Preset> (custom)"`. Each layout keeps its own settings in `layout_settings`, so switching away and back restores board columns, timeline range and so on.

### Pressure

This table is the **single source** of escalation thresholds. It replaces the per-mode scales in earlier drafts. Thresholds apply to **carry** (the number of planned days that passed with the item undone; see [04 › Derived Metrics](04-domain-model.md#41-age-carry-defers)), not to raw age.

| Level      | Fresh     | Warm | Hot   | Stale | Stuck chip at            | Extra behavior                                                                                                                    |
| ---------- | --------- | ---- | ----- | ----- | ------------------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| gentle     | carry 0–4 | 5–9  | 10–13 | 14+   | carry ≥ 14 or defers ≥ 5 | No left bar. Morning Review is optional and off by default.                                                                       |
| honest     | 0         | 1–2  | 3–5   | 6+    | carry ≥ 3 or defers ≥ 3  | Morning Review on.                                                                                                                |
| relentless | 0         | 1    | 2–3   | 4+    | carry ≥ 2 or defers ≥ 2  | The 3 highest-carry items are pinned on top. Morning Review can't be skipped as a whole (individual items can still be deferred). |

`pressure_overrides` can replace any number. How each state looks is defined in [07 › Row anatomy](07-ui-ux-design.md#101-row-anatomy-comfortable-density-38px).

In the `board` layout under `gentle`, the only signal is **stuck-in-column**: an unscheduled item has had no `ColumnChanged`/`Planned` event for N days (default 7, set in `layout_settings`).

### What switching guarantees

Switching layout, order or pressure **never writes to items**. Specifically:

| Layout       | How items without the layout's "natural" field are shown                                                                                                                                                                                                                                                                                                                                               |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `list`       | Sections Now / Planned / Waiting on / Done today, plus a Backlog view for Unscheduled and Someday items.                                                                                                                                                                                                                                                                                               |
| `board`      | Fixed columns **Someday · Backlog · Today · Waiting · Done (7 days)**. User columns can be inserted between Backlog and Today; they set `board_column` on Open, unscheduled items. Dropping into Today sets `PlannedFor = today`. Dropping into Done completes the item, and dropping into Waiting asks "on whom?". Status remains the single source of truth: no separate "Done column" state exists. |
| `timeline`   | Items with `due_date` or a future `PlannedFor` are placed on the timeline. Overdue items are pinned on top. All others sit in a **No date** lane. Nothing is required.                                                                                                                                                                                                                                 |
| `habit_grid` | Shows recurrence rules with `missed_behavior = skip`. Other items are listed under "Not habits (n)", one click away.                                                                                                                                                                                                                                                                                   |

Rollover is identical in every mode, because rollover is a query ([04 › Day Change](04-domain-model.md#5-day-change-replaces-the-rollover-algorithm)). Board users simply keep most items Unscheduled, so they don't accrue carry. Habit-like rules don't carry because their missed occurrences are never materialized.

### Order strategies

| Order            | Sort key                                                                                       |
| ---------------- | ---------------------------------------------------------------------------------------------- |
| `manual`         | `manual_rank` (fractional index; concurrent reorders on two devices merge without renumbering) |
| `priority_carry` | priority desc → carry desc → `manual_rank`                                                     |
| `due_date`       | overdue first → due date asc → priority desc; no due date last                                 |
| `carry_desc`     | carry desc → age desc                                                                          |
| `time_of_day`    | Morning → Midday → Afternoon → Evening → unset, then `manual_rank`                             |

Sections (Now, Planned, Waiting, Done) are always applied first. Order sorts within a section.

### Preset-specific features (now available everywhere)

| Feature (old mode)                   | Now                                                                                                                                     |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| Focus Item (Sprint)                  | **Now** item, in every workspace (`now_item_id`)                                                                                        |
| Someday bucket (Zen)                 | `is_someday`, in every workspace                                                                                                        |
| Overdue section (Deadline)           | Overdue pinned on top under the `due_date` order and in the timeline                                                                    |
| Streaks + heatmap (Habit)            | Flex streaks, derived ([04 › Habit stats](04-domain-model.md#45-habit-stats-rules-with-missed_behavior--skip))                          |
| WIP limits, columns, labels (Kanban) | `layout_settings.board`; labels are **tags**                                                                                            |
| Wall of Shame (Accountability)       | Replaced by "3 highest-carry items pinned" under `relentless` pressure. No shame language.                                              |
| Weekly email digest (Accountability) | Available to any workspace **with Noto Sync** (it needs a server; see [09](09-business-strategy.md)). The in-app Weekly Review is free. |

### Stats shown per preset

All stats are defined in [04 › Workspace stats](04-domain-model.md#44-workspace-stats--insights). Presets choose only which widgets appear by default:

| Preset         | Default widgets                                                                        |
| -------------- | -------------------------------------------------------------------------------------- |
| Sprint         | Capacity bar, done today, rollover rate (7d), completion streak                        |
| Zen            | Open count, recently completed                                                         |
| Deadline       | Due today / this week / overdue                                                        |
| Habit          | Per-habit flex streak, heatmap                                                         |
| Kanban         | Items per column, WIP, stuck-in-column count                                           |
| Accountability | Highest-carry items, median carry at completion, rollover rate trend, stuck reason mix |

---

## Extensibility

Layouts are **code** (each is a view + view model). Orders and pressure levels are **data**. A preset is pure data:

```
Preset {
    id: string
    name: string
    icon: emoji
    description: string
    layout: "list" | "board" | "timeline" | "habit_grid"
    order: OrderStrategyId
    pressure: "gentle" | "honest" | "relentless"
    pressure_overrides?: { warm, hot, stale, stuck_carry, stuck_defers }
    widgets: WidgetId[]
    density?: "compact" | "comfortable" | "spacious"
}
```

Users can save their own presets. Adding a new **layout** requires a release.
