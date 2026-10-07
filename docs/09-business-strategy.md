# Noto — Business & Monetization Strategy

## Market Position

**Category:** Productivity / task management
**Niche:** Daily planning with accountability. Noto turns rollover into decisions and learns from them. It's local-first and affordable.

### Competitive Landscape

| Competitor     | Strength                                                                                 | Gap Noto targets                                                                                                                                              |
| -------------- | ---------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sunsama        | Daily planning + shutdown rituals, rollover, integrations (Jira, GitHub, Linear, Slack…) | Closest competitor. Premium-priced subscription, cloud-only. Rollover is tracked, but there are no per-item decisions with reasons and no learning from them. |
| Akiflow        | Fast capture, inbox consolidation, time blocking                                         | Premium-priced, cloud-only, calendar-centric rather than accountability-centric                                                                               |
| Amazing Marvin | Highly configurable "strategies", including procrastination tracking                     | Steep configuration curve. Noto's equivalents are opinionated presets.                                                                                        |
| Todoist        | Polish, ubiquity, integrations                                                           | Overdue items pile up silently. No carry, decisions or capacity. SaaS-dependent.                                                                              |
| TickTick       | Habits, calendar, Pomodoro                                                               | No accountability lens, no workspace-level context separation in time                                                                                         |
| Things 3       | Design, Apple-native, Today/Logbook                                                      | Apple-only, no web, no stats, no integrations                                                                                                                 |
| Obsidian Tasks | Local-first, extensible                                                                  | Requires Obsidian. Tasks are a plugin, not a product.                                                                                                         |
| Notion         | Flexible, collaborative                                                                  | Overkill for daily todos, slow, SaaS-dependent                                                                                                                |
| Habitica       | Gamification                                                                             | Too game-like for professional contexts                                                                                                                       |

### Noto's Differentiation

Item age isn't unique on its own (Sunsama and Marvin both track rollover or procrastination). The defensible combination is:

1. **Decisions + reasons + insights.** Every carried item gets a one-keystroke decision. Stuck items are asked _why_, and the answers become personal insights.
2. **Honest planning.** Capacity and a personal pace forecast prevent rollover before it happens.
3. **Live links.** Linked PRs and tickets drive todo state (unblock, suggest completion), beyond static previews.
4. **Local-first, self-hostable, and an order of magnitude cheaper** than Sunsama or Akiflow.
5. **Tunable pressure** (gentle → relentless) per workspace, plus focus hours.

---

## Business Model

### Option A: Open Core (Recommended)

| Tier          | Price                | Features                                                                                                                                  |
| ------------- | -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| **Noto Free** | $0                   | Full native app: all workspaces, presets, insights, connected apps (direct, native), import/export. Self-host the sync server yourself.   |
| **Noto Sync** | $4/month or $36/year | Hosted sync, web app, **hosted Connected Apps Gateway** (connected apps on web), weekly email digest, encrypted backups, priority support |

Why people pay when self-hosting is free (the Bitwarden model): convenience, a managed gateway with registered OAuth apps for every provider, backups, the digest and the web app without running a server.

**Future (not in projections):** Noto Teams (shared workspaces, delegated Waiting-on, team insights). It needs its own design: sharing, permissions and a multi-user conflict model. Per-field LWW is built for one person's devices, not for collaboration.

### Option B: One-Time Purchase + Sync Add-on

| Product       | Price          | Features                                   |
| ------------- | -------------- | ------------------------------------------ |
| **Noto**      | $29 (one-time) | Native apps, perpetual license             |
| **Noto Sync** | $3/month       | Hosted sync, web, gateway, digest, backups |

### Option C: Fully Open Source + Hosting

All code AGPL; revenue only from hosted Sync ($5/month).

---

## Revenue Projections (Option A)

Assumptions are deliberately moderate. Freemium productivity apps typically convert in the low single-digit percent range.

| Assumption                            | Year 1   | Year 3   |
| ------------------------------------- | -------- | -------- |
| Active free users                     | 10,000   | 100,000  |
| Free → Sync conversion                | 3%       | 4%       |
| Paying users                          | 300      | 4,000    |
| Blended price (mix of monthly/annual) | $3.50/mo | $3.50/mo |

| Year | MRR     | ARR      |
| ---- | ------- | -------- |
| 1    | $1,050  | $12,600  |
| 3    | $14,000 | $168,000 |

**Upside levers:** higher annual-plan share, the Teams tier once designed, a one-time "Supporter" purchase. **Costs to cover:** hosting (sync + gateway egress), email, OAuth app reviews, Apple Developer Program, code signing.

---

## Go-To-Market Strategy

### Phase 1: Developer Community (Months 1–3 after v1.0)

- Show HN, Product Hunt, r/productivity, r/selfhosted, r/dotnet, r/macapps
- Technical posts: "rollover as a query", HLC per-field sync, Avalonia on macOS
- Open-source the core and server

### Phase 2: Beyond Developers (Months 3–6)

- Short videos of the Morning Review loop (the signature moment)
- "Sunsama alternative" comparison page (honest, with a feature table)
- Productivity creators, students (Deadline/Habit presets)

### Phase 3: Expand (Months 6–12)

- More providers (Slack, GitLab, Notion, Asana)
- Windows, Linux, mobile
- Teams design discovery with existing users

---

## Key Metrics (KPIs)

| Metric                                           | Target           | Why                                             |
| ------------------------------------------------ | ---------------- | ----------------------------------------------- |
| Weekly active users                              | 2,000 by month 6 | Core engagement                                 |
| DAU/MAU                                          | > 35%            | Daily-use product; strong for productivity apps |
| Day-30 retention                                 | > 25%            | The loop has to stick                           |
| Share of carried items with an explicit decision | > 60%            | Proves the decision loop works                  |
| Median carry at completion                       | < 1.5            | Proves accountability works                     |
| Free → Sync conversion                           | ≥ 3%             | Validates paid value                            |
| Monthly paid churn                               | < 4%             |                                                 |
| NPS                                              | > 40             |                                                 |

All product metrics come from **opt-in** telemetry only.

---

## Legal & Compliance

| Area                    | Approach                                                                                                                |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| **Privacy**             | Local-first. No telemetry without opt-in. Credentials never leave native devices. The web gateway doesn't store tokens. |
| **GDPR**                | Account export and deletion endpoints. Local JSON export.                                                               |
| **Data residency**      | Self-host option                                                                                                        |
| **Third-party APIs**    | Comply with each provider's API terms (Slack and Atlassian app review, GitHub OAuth app policies)                       |
| **Open source license** | MIT for client core and self-hostable server. Hosted-only components (digest, billing) proprietary.                     |
| **Terms of Service**    | Required for hosted Sync users                                                                                          |
