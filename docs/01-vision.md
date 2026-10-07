# Noto — Vision & Product Overview

## Elevator Pitch

**Noto** is a cross-platform, accountability-driven todo system where the day is the unit of work. Unfinished items don't vanish or pile up silently: they roll over, and **every carried item asks for a decision** (keep, defer, break down, wait, drop). Noto learns from those decisions to help you plan days you can actually finish. Workspaces separate life contexts in data and in time, and **modes** change how each workspace looks and how hard it pushes.

---

## Core Philosophy

### 1. Nothing Rots Silently

Every todo has a birthday and a history. Noto tracks how often an item slipped past the day you planned it (**carry**), and makes that visible. This isn't punishment. It's **accountability through visibility**, paired with a one-keystroke way out.

### 2. Rollover Means a Decision

Each new day, items you didn't finish are carried forward, and each one gets a decision in the Morning Review (or the evening Shutdown). Rollover is never just "the list got longer". Completed items stay in their day forever, so any past day can be revisited.

### 3. Plan Days You Can Finish

Today shows your commitments against your capacity, and your own history forecasts how much will actually get done. Overcommitting is visible before the day starts, not as rollover the next morning.

### 4. Workspaces Separate Contexts, in Data and in Time

Your work deadlines shouldn't mix with your grocery list. **Workspaces** are isolated containers with their own settings, and focus hours keep Work quiet in the evening. An opt-in **Today (all)** lens shows the single day you actually live.

### 5. Modes Change the Lens, Never the Data

A mode is a preset of three controls: **layout** (list, board, timeline, habit grid), **order**, and **pressure** (gentle, honest, relentless). Switching never changes your items.

### 6. Local-First, Cloud-Optional

Native apps work fully offline with local SQLite. A self-hostable backend adds sync and is required only for the web app, which needs it for durable storage and connected apps.

---

## Target Platforms

macOS comes first because it's the primary daily driver. Web is next because it covers other desktops cheaply. Other native platforms come later; the model and UI are designed for them now.

| Platform | Technology       | Priority | Notes                                                                    |
| -------- | ---------------- | -------- | ------------------------------------------------------------------------ |
| macOS    | Avalonia Desktop | P0       | MVP. Includes native menubar, global capture, Keychain                   |
| Web      | Avalonia WASM    | P1       | Sync client; needs the backend for storage durability and connected apps |
| Windows  | Avalonia Desktop | P2       |                                                                          |
| Linux    | Avalonia Desktop | P2       | Global hotkey limited on Wayland                                         |
| iOS      | Avalonia Mobile  | P2       | Widgets and share extension are native work                              |
| Android  | Avalonia Mobile  | P2       |                                                                          |

Avalonia WASM on **mobile browsers** isn't considered a substitute for mobile apps (text input and performance are weak spots there).

---

## Key Differentiators

1. **Decisions, not reminders.** Morning Review and Shutdown turn every carried item into a one-keystroke decision.
2. **Stuck reasons → insights.** Noto asks _why_ an item is stuck (too big, blocked, unclear…), offers the matching fix, and reports patterns back ("items over 2h carry 3.8× on average").
3. **Honest planning.** A capacity bar and a personal pace forecast, built from your own history.
4. **Live links.** GitHub, Jira, Linear and Slack links show live status and can unblock or suggest completing the todo.
5. **Modes as lenses.** Layout, order and pressure per workspace. Pressure is tunable, from gentle to relentless.
6. **Local-first.** No account needed for native apps. Self-hostable sync.
7. **Cross-platform** from one codebase (AvaloniaUI).

The design behind these is in [07-ui-ux-design.md](07-ui-ux-design.md).
