# Iteration 3 Acceptance Pack

Last updated: 2026-03-18

## Goal

Freeze the acceptance evidence for Iteration 3 (`KC-0301` ~ `KC-0310`):

- `HEARTBEAT.md` can compile into durable automation definitions
- the scheduler can execute a due automation and persist run history
- successful automation runs publish an inbox result that is queryable from Gateway and Web surfaces
- canvas artifacts can be published, queried, resolved as the default entry, and served from `workspace/canvas/**`
- the Automations / Canvas desks stay integrated inside the main KodaClaw shell and pass full regression

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution restore/build/test baseline | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L1` | Automation contract wrappers + canvas contract wrappers | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Automation/AutomationApiContractsTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Canvas/CanvasContractsTests.cs` |
| `L2` | Scheduler / heartbeat / inbox delivery baseline | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Automation/AutomationSchedulerIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/AutomationSessionServiceIntegrationTests.cs` |
| `L2` | Gateway automation API behavior (list/detail/runs/patch) | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/AutomationApiIntegrationTests.cs` |
| `L2` | Gateway canvas API behavior (list/default/detail/publish/fs) | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/CanvasApiIntegrationTests.cs` |
| `L3` | Iteration 3 integrated smoke: heartbeat -> run -> inbox -> canvas publish/query/fs | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration3AcceptanceIntegrationTests.cs` |
| `L4` | Web desk acceptance: automations + canvas | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0308-automations.spec.ts` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0309-canvas.spec.ts` |
| `L5` | Manual dogfood drill for automation/canvas control-plane flows | commands in this document |

## Standard Verification Commands

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "AutomationApiContractsTests|CanvasContractsTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "AutomationApiIntegrationTests|CanvasApiIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter Iteration3AcceptanceIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run typecheck`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0308-automations.spec.ts tests/kc0309-canvas.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-dev-i3` or a temporary directory.

### 1. Start Gateway

```bash
export KODACLAW_GATEWAY_TOKEN="test-token"
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-dev-i3"
export KODACLAW_DEFAULT_MODEL="gpt-4o-mini"
export OPENAI_API_KEY="stub-key"
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls http://127.0.0.1:5076
```

Expected:

- `GET /api/system/health` returns `healthy`
- `GET /api/system/bootstrap-state` returns workspace metadata in `Normal` mode after bootstrap is complete

### 2. Run the Integrated Iteration 3 Smoke

```bash
dotnet test \
  /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter Iteration3AcceptanceIntegrationTests
```

Expected:

- one due heartbeat automation run is executed successfully
- `/api/inbox?kind=AutomationResult` returns the generated automation result
- `/api/canvas/default` resolves to the published artifact entry
- `/api/canvas/fs/workspace/canvas/...` serves the generated HTML entry

### 3. Inspect Automation APIs

```bash
curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/automations?enabled=true&limit=20"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/automations/<automation-id>/runs?limit=20"
```

Expected:

- automation definitions include `lastRunStatus`, `lastRunAt`, and `nextRunAt`
- run history shows the latest `Succeeded` or `Failed` result with session correlation

### 4. Inspect Inbox + Canvas APIs

```bash
curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/inbox?kind=AutomationResult&limit=20"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/canvas?limit=20"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/canvas/default"
```

Expected:

- inbox automation-result items route to `/automations/<automationId>`
- canvas list exposes artifact metadata and `defaultEntryPath`
- default canvas entry exposes a stable `entryUrl`

### 5. Verify the Web Desks

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
VITE_KODACLAW_GATEWAY_URL="http://127.0.0.1:5076" \
VITE_KODACLAW_GATEWAY_TOKEN="$KODACLAW_GATEWAY_TOKEN" \
npm run dev
```

Expected:

- desk switcher exposes `Automations` and `Canvas`
- the Automations desk shows list/filter/detail/runs/toggle controls
- the Canvas desk shows artifact list, metadata, and iframe preview/fallback behavior

## Exit Criteria

Iteration 3 is accepted when all of the following are true:

- automation scheduler / runtime / inbox / canvas backends pass targeted integration coverage
- the integrated Iteration 3 smoke passes against the real workspace-backed Gateway
- Automations + Canvas desks pass targeted Playwright acceptance and the full frontend regression suite
- `dotnet test KodaClaw.sln -m:1`, `npm run test`, and `npm run test:e2e` all pass on current workspace state
