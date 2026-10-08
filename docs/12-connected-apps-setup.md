# Connected apps: getting client IDs and secrets

The sync server runs the OAuth code exchange for connected apps, so each provider needs an OAuth app you register yourself. Set the values in `.env` (see [`.env.sample`](../.env.sample)). A provider is enabled when its ID and secret are both set.

**Callback URL:** register this for every provider, with your server's `PUBLIC_URL`:

```
$PUBLIC_URL/v1/gateway/oauth/<provider>/callback
```

For local testing, `PUBLIC_URL=http://localhost:8080` gives `http://localhost:8080/v1/gateway/oauth/github/callback`. Some providers reject plain `http` for non-localhost hosts, so use `https` for a real deployment.

Provider consoles change their layout often. The paths below were current when written; the names of the things you need (client ID, client secret, callback URL, scopes) stay the same.

| Provider  | Env prefix   | Register at                                                 | Scopes Noto requests                                              |
| --------- | ------------ | ----------------------------------------------------------- | ----------------------------------------------------------------- |
| GitHub    | `GITHUB_`    | github.com → Settings → Developer settings → OAuth Apps     | `repo`, `read:user`                                               |
| Slack     | `SLACK_`     | api.slack.com/apps                                          | user scopes: `*:history`, `*:read` for channels, groups, IMs      |
| Atlassian | `ATLASSIAN_` | developer.atlassian.com/console/myapps                      | `read:jira-work`, `read:confluence-content.all`, `offline_access` |
| Linear    | `LINEAR_`    | linear.app → Settings → API → OAuth applications            | `read`                                                            |
| GitLab    | `GITLAB_`    | gitlab.com → User settings → Applications                   | `read_api`                                                        |
| Notion    | `NOTION_`    | notion.so/my-integrations (create a **public** integration) | none (the workspace grants access)                                |
| Figma     | `FIGMA_`     | figma.com/developers/apps                                   | `files:read`                                                      |

## Steps

**GitHub**

1. New OAuth App. Set the homepage to your app URL and the authorization callback URL to the one above.
2. Copy the Client ID. Generate a client secret and copy it.

**Slack**

1. Create New App → From scratch. Slack asks for a **development workspace**: the app is created there. Any workspace you own works.
2. OAuth & Permissions: add the redirect URL above, then add the user scopes from the table.
3. Basic Information → App Credentials: copy the Client ID and Client Secret.
4. To let people in other workspaces connect, turn on **Manage Distribution** → public distribution. Until then, only members of the development workspace can authorize the app.

**Atlassian**

1. Create → OAuth 2.0 integration.
2. Permissions: add the Jira and Confluence API scopes from the table.
3. Authorization: add the callback URL above (OAuth 2.0 (3LO)).
4. Settings: copy the Client ID and generate a Secret.

**Linear**

1. Settings → API → Create new OAuth application. Add the callback URL above.
2. Copy the Client ID and Client Secret.

**GitLab**

1. User settings → Applications → Add new application. Add the callback URL above and the `read_api` scope.
2. Keep it confidential, then copy the Application ID (client ID) and Secret.
3. Self-hosted GitLab: create the application on that instance and use its ID and secret. Only one GitLab pair is configured per server, so each server supports one GitLab instance.

**Notion**

1. My integrations → New integration → **Public** (OAuth needs a public integration).
2. Add the redirect URI above. Under OAuth Domain & URI, copy the client ID and client secret.

**Figma**

1. Developers → Apps → Create new app. Add the callback URL above and the `files:read` scope.
2. Copy the Client ID and Client Secret.

## After you have them

Put them in `.env` and restart the server:

```sh
GITHUB_CLIENT_ID=...
GITHUB_CLIENT_SECRET=...
```

The server uses these values for its own OAuth flow and does not store provider tokens. The native desktop build has its own copy of the secret for providers that need one. Treat that copy as extractable (see [`10-connected-apps.md`](10-connected-apps.md)).
