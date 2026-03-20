# Iteration 1 Acceptance Pack

Last updated: 2026-03-18

## Goal

Freeze the acceptance evidence for Iteration 1 (`KC-0112`):

- bootstrap completes from the product shell
- main chat streams successfully
- closing and reopening the product resumes the same main session
- failures leave visible diagnostics evidence

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution builds and test projects restore | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L2` | Real workspace gateway flow: bootstrap -> chat -> shutdown -> restart -> resume | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration1AcceptanceIntegrationTests.cs` |
| `L2` | Runtime session create/resume/fallback | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/MainSessionServiceIntegrationTests.cs` |
| `L2` | Gateway diagnostics auth/correlation/query | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/GatewayDiagnosticsIntegrationTests.cs` |
| `L4` | Web bootstrap -> chat -> reopen -> resume UI acceptance | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0112-bootstrap-chat-resume.spec.ts` |
| `L5` | Real failure drill: unconfigured chat emits SSE `error` and diagnostics can be queried by correlation id | manual commands in this document |

## Standard Verification Commands

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-dev` or a temporary directory.

### 1. Start Gateway

```bash
export KODACLAW_GATEWAY_TOKEN="test-token"
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-dev"
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls http://127.0.0.1:5076
```

Expected:

- `GET /api/system/health` returns `healthy`
- `GET /api/system/bootstrap-state` reports `Bootstrap` on first run

### 2. Start Web Shell

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
VITE_KODACLAW_GATEWAY_URL="http://127.0.0.1:5076" \
VITE_KODACLAW_GATEWAY_TOKEN="test-token" \
npm run dev
```

Expected:

- browser opens in bootstrap mode
- bootstrap form can be completed and the shell switches to main mode

### 3. Send First Main-Chat Turn

Expected:

- chat timeline shows streamed assistant output
- workspace snapshot moves to `Normal`
- the Gateway writes or updates `activeMainSessionId`

### 4. Restart and Resume

Expected:

- after stopping and restarting Gateway, the browser reopens directly in main mode
- the same `activeMainSessionId` is reported by bootstrap-state
- a new chat turn succeeds without re-running bootstrap

### 5. Failure Drill

Start Gateway without `KODACLAW_DEFAULT_MODEL` and provider credentials, then run:

```bash
curl -sN \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "X-KodaClaw-Correlation-Id: smoke-chat-fail" \
  -H "Content-Type: application/json" \
  -d '{"message":"hello"}' \
  http://127.0.0.1:5076/api/chat/stream

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/diagnostics/recent?correlationId=smoke-chat-fail"
```

Expected:

- chat stream returns `event: error`
- diagnostics query returns at least `gateway.chat.requested` and `gateway.chat.failed` with the same correlation id

## Exit Criteria

Iteration 1 is accepted when all of the following are true:

- bootstrap, chat, and session resume all pass automated coverage
- the web shell passes bootstrap/chat/reopen UI acceptance
- diagnostics can explain at least one real failure path
- all verification commands in this document pass on the current workspace state
