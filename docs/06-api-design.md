# Noto — API & Backend Design

## Overview

The backend is **optional for native clients** and exists for:

1. **Cross-device sync** (op-log, see [05 › Sync Architecture](05-architecture.md#sync-architecture-when-enabled))
2. **The web client:** the browser build is a normal local-first sync client. It needs the backend for durable storage (browser storage can be evicted) and for Connected Apps (browsers can't call provider APIs)
3. **The Connected Apps Gateway** for the web client
4. Paid extras on hosted Noto: weekly email digest, backups ([09](09-business-strategy.md))

There is **one write path**: `/sync`. Earlier drafts had parallel CRUD endpoints "for web-only users". They were removed because they bypassed the op-log and its conflict rules. Stats are computed on clients ([04 › Derived Metrics](04-domain-model.md#4-derived-metrics)), so the server has no stats endpoints either. The server can't compute "today" anyway, since that depends on each workspace's time zone and day boundary.

**Base URL:** `https://api.noto.app/v1` (or self-hosted)
**Auth:** `Authorization: Bearer <access JWT>` (15 min), rotating refresh tokens bound to a device
**Content-Type:** `application/json`

---

## Authentication & Devices

| Method | Path             | Body / Notes                                                                                                 |
| ------ | ---------------- | ------------------------------------------------------------------------------------------------------------ |
| POST   | `/auth/register` | `{email, password, device: {id, name, platform}}` → `201 {user_id, access_token, refresh_token, expires_at}` |
| POST   | `/auth/login`    | `{email, password, device}` → `200 {access_token, refresh_token, expires_at}`                                |
| POST   | `/auth/refresh`  | `{refresh_token}` → new pair. The old refresh token is invalidated (reuse detection revokes the device).     |
| POST   | `/auth/logout`   | Revokes this device's refresh token                                                                          |
| GET    | `/devices`       | `[{id, name, platform, last_sync_at, cursor}]`                                                               |
| DELETE | `/devices/:id`   | Revoke a device (lost laptop)                                                                                |

Passwords are hashed with Argon2id. Hosted Noto may add passkeys and OAuth sign-in later; self-hosted keeps email + password.

---

## Sync

### POST `/sync`

One round trip pushes local ops and pulls remote ops after a cursor.

```json
// Request
{
  "device_id": "6f1c…",
  "cursor": 1840,
  "workspaces": ["ws-uuid-1", "ws-uuid-2"],
  "limit": 500,
  "ops": [
    {
      "op_id": "a1…",
      "entity_type": "todo_item",
      "entity_id": "i1…",
      "workspace_id": "ws-uuid-1",
      "kind": "set",
      "field": "status",
      "value": "Done",
      "hlc": "1791375900123:0:6f1c…"
    },
    {
      "op_id": "a2…",
      "entity_type": "item_event",
      "entity_id": "e9…",
      "workspace_id": "ws-uuid-1",
      "kind": "insert",
      "value": { "id": "e9…", "item_id": "i1…", "type": "Completed", "data": { "completed_on": "2026-10-07" },
                 "occurred_at": "2026-10-07T10:05:00Z", "tz": "Asia/Kathmandu", "device_id": "6f1c…", "hlc": "1791375900123:1:6f1c…" },
      "hlc": "1791375900123:1:6f1c…"
    }
  ]
}

// Response 200
{
  "accepted_op_ids": ["a1…", "a2…"],
  "ops": [ /* remote ops with seq > cursor, for the listed workspaces, oldest first */ ],
  "next_cursor": 2340,
  "has_more": true,
  "server_hlc": "1791375900200:0:server"
}
```

Server rules:

- Ops are idempotent by `op_id` (retries are safe).
- Each accepted op gets a monotonically increasing `seq`, and `cursor` refers to `seq`.
- `set` ops apply to the server's current-state row only if `hlc > field_clocks[field]`. **Every** op is still appended to the log, so other devices converge on the same result by applying the same rule.
- Ops for workspaces the device didn't list aren't returned. A workspace with sync disabled on all devices receives no new ops.
- Validation failure on one op (unknown entity type, a workspace the user doesn't own) rejects **that op** with a reason in `rejected: [{op_id, code}]`. The rest still apply.

### GET `/sync/snapshot?workspace_id=…`

Bootstrap for a new device or a re-enabled workspace: `{seq, rows: {workspace, todo_item[], item_event[], …}, field_clocks}`. It's paginated by entity type for large workspaces (`?entity_type=item_event&after=<id>`).

### DELETE `/sync/workspaces/:id`

Removes a workspace's server copy ("Remove from server" when disabling sync). Devices keep their local data.

---

## Account

| Method | Path              | Notes                                                                                      |
| ------ | ----------------- | ------------------------------------------------------------------------------------------ |
| GET    | `/account/export` | Async: `202 {export_id}`, then `GET /account/export/:id` → JSON archive of all synced data |
| DELETE | `/account`        | Deletes all server data and revokes all devices. Local data on devices is untouched.       |
| PUT    | `/account/digest` | Email digest preferences (hosted Noto Sync only)                                           |

---

## Connected Apps Gateway (web client)

Used **only** by the browser build. Native clients talk to providers directly ([10](10-connected-apps.md)). All gateway routes require a signed-in user.

| Method | Path                                | Purpose                                                                                                                                                                            |
| ------ | ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| POST   | `/gateway/oauth/:provider/start`    | `{instance_url?, code_challenge}` → `{authorize_url, state}`. The server holds the provider client secret.                                                                         |
| GET    | `/gateway/oauth/:provider/callback` | Provider redirect target. Exchanges the code (PKCE verifier from the start step), then hands tokens to the opener window via a one-time code. Tokens aren't persisted server-side. |
| POST   | `/gateway/oauth/:provider/redeem`   | `{one_time_code}` → `{access_token, refresh_token?, expires_at, scopes, display_label}` (single use, 60s TTL)                                                                      |
| POST   | `/gateway/oauth/:provider/refresh`  | `{refresh_token}` → new tokens (client secret applied server-side)                                                                                                                 |
| POST   | `/gateway/fetch`                    | `{provider_id, instance_url?, request: {method, path, query?, body?}}` + header `X-Provider-Authorization: Bearer …` → the provider's status, selected headers and body            |
| POST   | `/gateway/opengraph`                | `{url}` → `{title, description, image, site_name}`                                                                                                                                 |

Gateway rules:

- **Allowlist:** `/gateway/fetch` only reaches hosts registered for that provider (e.g. `api.github.com`, `slack.com/api`, `*.atlassian.net`, `api.linear.app`) plus the user's own `instance_url` for self-hosted providers. Custom apps on hosted Noto are limited to public hosts.
- **Methods:** GET only, except GraphQL `POST` for the GitHub/Linear GraphQL endpoints, with read-only query validation. The gateway can't write to third-party systems.
- **SSRF protection** (`/gateway/opengraph`, custom apps): resolve DNS, reject private, loopback and link-local ranges, re-check every redirect, cap at 5 redirects, 1 MB and 5s.
- **No storage:** `X-Provider-Authorization` and response bodies are never logged or stored. Metrics record only `provider_id`, status code and latency.
- **Self-hosted** gateways can reach intranet hosts (e.g. on-prem Jira) by setting `GATEWAY_ALLOW_PRIVATE_HOSTS=jira.acme.internal,…`.

---

## Server Architecture

```
Noto.Server/
├── Program.cs
├── Endpoints/        Auth, Devices, Sync, Account, Gateway
├── Sync/             OpIngest, LwwApplier, SnapshotService, TombstoneGc
├── Gateway/          OAuthBroker, ProviderAllowlist, FetchProxy, OpenGraphFetcher, SsrfGuard
├── Digest/           WeeklyDigestJob (hosted only)
├── Data/             ServerDbContext (EF Core 10), Migrations
├── Auth/             JwtService, RefreshTokenStore, Argon2PasswordHasher
├── Middleware/       RateLimiting (built-in ASP.NET Core limiter), ProblemDetails
└── Dockerfile
```

**Tables:** `users`, `devices`, `refresh_tokens`, `ops (seq bigserial PK, op_id unique, user_id, workspace_id, entity_type, entity_id, kind, field, value jsonb, hlc, device_id, received_at)`, `current_rows (entity_type, entity_id, user_id, workspace_id, row jsonb, field_clocks jsonb)`, `workspace_sync (user_id, workspace_id, enabled)`.

---

## Self-Hosting

```yaml
# docker-compose.yml
services:
  noto-api:
    image: noto/server:latest
    ports:
      - "8080:8080"
    environment:
      DATABASE_URL: postgresql://noto:${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}@db:5432/noto
      JWT_SIGNING_KEY: ${JWT_SIGNING_KEY:?set JWT_SIGNING_KEY}
      PUBLIC_URL: ${PUBLIC_URL:?set PUBLIC_URL} # used for OAuth callback URLs
      # Optional, enables the gateway for each provider:
      # GITHUB_CLIENT_ID / GITHUB_CLIENT_SECRET, SLACK_CLIENT_ID / ..., ATLASSIAN_CLIENT_ID / ...
    depends_on:
      - db

  db:
    image: postgres:17-alpine
    volumes:
      - noto-data:/var/lib/postgresql/data
    environment:
      POSTGRES_DB: noto
      POSTGRES_USER: noto
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}

volumes:
  noto-data:
```

**Minimal self-host (SQLite, no Docker):**

```bash
JWT_SIGNING_KEY=… PUBLIC_URL=https://noto.example.com \
  ./noto-server --db sqlite --data-dir ./noto-data --port 8080
```

The server refuses to start without the required secrets. There are no default passwords.

---

## Rate Limits

| Endpoint             | Limit                                              |
| -------------------- | -------------------------------------------------- |
| `/auth/*`            | 10 req/min per IP                                  |
| `/sync`              | 60 req/min per device                              |
| `/sync/snapshot`     | 10 req/min per device                              |
| `/gateway/fetch`     | 300 req/min per user, 60/min per provider per user |
| `/gateway/opengraph` | 60 req/min per user                                |
| `/account/*`         | 5 req/min per user                                 |

---

## Error Responses

[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) Problem Details:

```json
{
  "type": "https://noto.app/errors/workspace-not-owned",
  "title": "Workspace not owned by user",
  "status": 403,
  "code": "WORKSPACE_NOT_OWNED",
  "detail": "Workspace 'abc-123' belongs to another account"
}
```

| HTTP | Meaning                                                                                                               |
| ---- | --------------------------------------------------------------------------------------------------------------------- |
| 400  | Validation error                                                                                                      |
| 401  | Not authenticated / access token expired                                                                              |
| 403  | Not authorized (wrong user, revoked device, gateway host not allowlisted)                                             |
| 404  | Not found                                                                                                             |
| 409  | `cursor` ahead of the server (e.g. a server restored from backup). The client must re-bootstrap via `/sync/snapshot`. |
| 413  | Payload too large (> 2,000 ops per push)                                                                              |
| 429  | Rate limited (`Retry-After` header)                                                                                   |
| 502  | Gateway: provider unreachable or returned invalid data                                                                |
| 500  | Server error                                                                                                          |

Sync never returns a "conflict" error. Concurrent edits are resolved by per-field LWW, and losing text values are kept on clients ([05](05-architecture.md#model)).
