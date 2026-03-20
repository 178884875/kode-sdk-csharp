# Iteration 2 Acceptance Pack

Last updated: 2026-03-18

## Goal

Freeze the acceptance evidence for Iteration 2 (`KC-0213`):

- approval pauses a risky action and can be decided through control-plane surfaces
- inbox reflects approval lifecycle and resolution state
- sessions and diagnostics provide auditable runtime evidence
- models and settings can be managed from the product control-plane desks

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution build + restore baseline | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L2` | Runtime approval lifecycle (pending -> approved, stale pending -> canceled) | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/MainSessionServiceIntegrationTests.cs` |
| `L2` | Gateway approval API behavior (list/detail/approve/reject) | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/ApprovalApiIntegrationTests.cs` |
| `L2` | Gateway inbox API behavior (query/detail/status transition) | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/InboxApiIntegrationTests.cs` |
| `L2` | Gateway sessions summary/detail for pending approvals | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/GatewaySessionsIntegrationTests.cs` |
| `L2` | Gateway diagnostics timeline/recent query | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/GatewayDiagnosticsIntegrationTests.cs` |
| `L2` | Gateway model endpoint management API | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/ModelApiIntegrationTests.cs` |
| `L2` | Gateway settings API persistence/validation | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/SettingsApiIntegrationTests.cs` |
| `L4` | Web desk acceptance: inbox/approval, sessions/diagnostics, models/settings | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0210-inbox-approval.spec.ts` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0211-sessions-diagnostics.spec.ts` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0212-models-settings.spec.ts` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0213-control-plane-acceptance.spec.ts` |
| `L5` | Manual dogfood drill for approval/inbox/sessions/diagnostics/models/settings | manual commands in this document |

## Standard Verification Commands

- `dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter MainSessionServiceIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter ApprovalApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter InboxApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewaySessionsIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewayDiagnosticsIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter ModelApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter SettingsApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run typecheck`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0210-inbox-approval.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0211-sessions-diagnostics.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0212-models-settings.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0213-control-plane-acceptance.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-dev-i2` or a temporary directory.

### 1. Start Gateway

```bash
export KODACLAW_GATEWAY_TOKEN="test-token"
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-dev-i2"
export KODACLAW_DEFAULT_MODEL="approval-model"
export OPENAI_API_KEY="stub-key"
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls http://127.0.0.1:5076
```

Expected:

- `GET /api/system/health` returns `healthy`
- `GET /api/system/bootstrap-state` is reachable and includes mode/session metadata

### 2. Bootstrap and Create Baseline Settings/Model Snapshot

```bash
curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"identityMarkdown":"# Koda Identity","userMarkdown":"# User Profile","archiveBootstrapFile":true}' \
  "http://127.0.0.1:5076/api/system/bootstrap-complete"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/models"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/settings"
```

Expected:

- bootstrap completion succeeds
- model list and settings snapshot are readable

### 3. Trigger Approval Lifecycle (Automated Drill)

Run the existing integration flow that creates a real pending approval and decides it through Gateway:

```bash
dotnet test \
  /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "ApprovalApiIntegrationTests.Approval_approve_with_live_session_transitions_status"
```

Expected:

- test passes with `Pending -> Approved` transition
- linked inbox item is resolved after approval

### 4. Inspect Control-Plane APIs

```bash
curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/approvals?status=Pending&limit=20"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/inbox?status=Open&limit=20"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/sessions?limit=20"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/diagnostics/timeline?limit=50"
```

Expected:

- approvals and inbox reflect the latest lifecycle state
- sessions summary is queryable for audit context
- diagnostics timeline includes approval/runtime events

### 5. Verify Settings Persistence

```bash
curl -s \
  -X PUT \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"defaultLandingRoute":"/inbox","theme":"Dark","requireApprovalForExternalActions":true,"notificationsEnabled":true,"quietHoursEnabled":true,"quietHoursStartLocalTime":"22:00","quietHoursEndLocalTime":"08:00","updatedAt":"1970-01-01T00:00:00Z"}' \
  "http://127.0.0.1:5076/api/settings"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/settings"
```

Expected:

- `PUT /api/settings` returns `200`
- follow-up `GET /api/settings` reflects updated values and a refreshed `updatedAt`

### 6. Verify Web Control-Plane Desks

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
VITE_KODACLAW_GATEWAY_URL="http://127.0.0.1:5076" \
VITE_KODACLAW_GATEWAY_TOKEN="$KODACLAW_GATEWAY_TOKEN" \
npm run dev
```

Expected:

- desk switcher can navigate `Inbox / Approval`, `Sessions / Diagnostics`, and `Models / Settings`
- controls are usable and data snapshots are visible in each desk

## Exit Criteria

Iteration 2 is accepted when all of the following are true:

- approval/inbox/session/diagnostics/model/settings backends pass automated integration coverage
- web control-plane desks pass targeted Playwright acceptance and full e2e suite
- manual drill demonstrates that control-plane data is inspectable end to end
- all verification commands in this document pass on current workspace state
