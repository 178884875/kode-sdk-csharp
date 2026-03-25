---
name: koda-cli
description: KodaClaw management CLI (kc). Use this skill whenever the user asks about automation status, workspace files, inbox approvals, skills, or wants to trigger/list/manage any KodaClaw resource via kc cli commands programmatically.
compatibility: Requires kc CLI installed at ~/.kc-cli/bin/kc. Included with KodaClaw desktop app.
allowed-tools: Bash(kc:*)
metadata:
  version: "1.2"
  kind: builtin-core
---

# koda-cli

`kc` is the KodaClaw management CLI. It communicates with the local Gateway
(`http://127.0.0.1:5076` by default) over HTTP. Config stored in `~/.kc-cli/config.json`.

Always pass `--json` when you need to parse output — text mode is for humans.

## Prerequisites

**1. Verify `kc` is available:**
```bash
kc --version
```
If not found: "`kc` is not in PATH. Install KodaClaw desktop — it puts `kc` in `~/.kc-cli/bin/`."

**2. Check login:**
```bash
kc auth status --json
# → { "authenticated": true, "gatewayUrl": "http://127.0.0.1:5076", "version": "KodaClaw Gateway" }
```
- `authenticated: false` → Gateway not running, tell user to launch KodaClaw
- Error / no token → run step 3

**3. Login (first time only):**
```bash
kc auth login --token <token>
# token is in KodaClaw Settings → Gateway
# saves to ~/.kc-cli/config.json, no flags needed after this
```

## Command reference

### Auth
```bash
kc auth login --token <token> [--url <url>]   # save credentials
kc auth status --json                          # check connectivity
```

### Workspace
```bash
kc workspace status --json
# → { "isIdentitySet": bool, "isSoulSet": bool, "isUserSet": bool, "hasAnyGap": bool }

kc workspace read <target>     # target: identity | soul | user | memory | heartbeat
kc workspace write <target> --content "<text>"
echo "new content" | kc workspace write identity   # stdin also works
```

### Automation
```bash
kc automation list --json
# → [{ "id", "name", "cronExpression", "enabled", "lastRunAt", "nextRunAt", "lastRunStatus" }]

kc automation run <id> --json
# → { "ok": true, "automationId": "<id>", "sessionId": "auto-xxx" }

kc automation enable <id> --json
kc automation disable <id> --json

kc automation runs <id> [--limit 10] --json
# → [{ "runId", "status", "startedAt", "completedAt", "summary", "errorMessage" }]
```

### Inbox
```bash
kc inbox list [--status Open|Acknowledged|Resolved|Archived] --json
# → { "items": [{ "id", "kind", "status", "title", "summary", "requiresAction", "createdAt" }] }

kc inbox approve <id> --json   # marks as Resolved
kc inbox reject <id> --json    # marks as Archived
```

### Skills
```bash
kc skill list --json
# → [{ "name", "kind", "version", "description", "allowedTools", "tags", "compatibility" }]
```

## Error handling

| Exit code | Meaning | What to do |
|-----------|---------|------------|
| 0 | Success | Continue |
| 1 | General error / connection refused | Check Gateway (`kc auth status`) |
| 3 | Not found (404) | Verify ID with list command |

## Agent usage pattern

```
# Step 1: verify kc + auth
kc --version               → not found: stop, inform user
kc auth status --json      → authenticated == false: stop, inform user

# Step 2: operate
kc automation list --json           → find the automation id
kc automation run <id> --json       → confirm ok == true
kc inbox list --json                → find items requiresAction == true
kc inbox approve <id> --json        → confirm approval

# Step 3: report
Summarise from parsed JSON, not raw text.
```

Always use `--json` in agent context — JSON shape is stable, text output may change.
