# Iteration 5 Acceptance Pack

Last updated: 2026-03-19

## Goal

Freeze the acceptance evidence for Iteration 5 (`KC-0501` ~ `KC-0511`):

- `telegram` and `generic-webhook` connectors can be enumerated and bound from Gateway
- Telegram DM inbound reuses a single thread binding and isolated `ChannelDirectMessage` session across follow-up messages
- DM prompts load `USER.md` plus thread-local summary context, but never `MEMORY.md`
- group inbound always maps to `ChannelGroup`, excludes `USER.md` / `MEMORY.md`, and preserves the safety boundary across follow-up messages
- `DraftApproval` / `RequireApproval` create `ApprovalKind.ChannelDelivery` + `InboxItemKind.ChannelUpdate`, and approval decisions either dispatch or suppress outbound delivery with audit evidence
- the Channels desk remains integrated inside the main web shell and stays green in the full frontend regression

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution restore/build/test baseline | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L1` | Channel contracts | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Channels/ChannelContractsTests.cs` |
| `L2` | Channel repositories, policy, audit storage | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/ChannelHub/SqliteChannelAccountRepositoryTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/ChannelHub/SqliteThreadBindingRepositoryTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/ChannelHub/ChannelPolicyEngineTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/ChannelHub/SqliteChannelAuditRepositoryTests.cs` |
| `L2` | Connector, gateway, runtime, and governance behavior | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/ChannelHub/TelegramConnectorIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/ChannelHub/GenericWebhookConnectorIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/ChannelApiIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/ChannelSessionServiceIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/GatewaySessionsIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/ChannelHub/ChannelDeliveryGovernanceIntegrationTests.cs` |
| `L2` | Approval decision dispatch for channel drafts | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/ChannelHub/ChannelDeliveryApprovalIntegrationTests.cs` |
| `L3` | Integrated DM acceptance: Telegram DM -> binding reuse -> isolated session -> approval approve -> outbound delivery | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration5DirectMessageAcceptanceIntegrationTests.cs` |
| `L3` | Integrated group safety acceptance: webhook group -> isolated session -> approval reject -> no outbound | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration5GroupSafetyAcceptanceIntegrationTests.cs` |
| `L4` | Web Channels desk acceptance | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0508-channels.spec.ts` + full `npm run test:e2e` regression |
| `L5` | Hybrid operator drill for connector surface + Iteration 5 acceptance smokes | commands in this document |

## Standard Verification Commands

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter ChannelContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj --filter "SqliteChannelAccountRepositoryTests|SqliteThreadBindingRepositoryTests|ChannelPolicyEngineTests|SqliteChannelAuditRepositoryTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "TelegramConnectorIntegrationTests|GenericWebhookConnectorIntegrationTests|ChannelApiIntegrationTests|ChannelSessionServiceIntegrationTests|GatewaySessionsIntegrationTests|ChannelDeliveryGovernanceIntegrationTests|ChannelDeliveryApprovalIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "Iteration5DirectMessageAcceptanceIntegrationTests|Iteration5GroupSafetyAcceptanceIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0508-channels.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-dev-i5` or a temporary directory.

### 1. Start Gateway with a disposable workspace

```bash
export KODACLAW_GATEWAY_TOKEN="test-token"
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-dev-i5"
export KODACLAW_DEFAULT_MODEL="gpt-4o-mini"
export OPENAI_API_KEY="stub-key"
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls http://127.0.0.1:5076
```

Expected:

- `GET /api/system/health` returns `healthy`
- `GET /api/system/bootstrap-state` reports a non-bootstrap workspace after initialization

### 2. Inspect connector surface and create the acceptance accounts

```bash
curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/channels/connectors"

curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "id":"telegram-main",
        "connectorKind":"Telegram",
        "displayName":"Telegram Bot",
        "credentialReference":"inline:telegram-token-acceptance"
      }' \
  "http://127.0.0.1:5076/api/channels/accounts"

curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "id":"webhook-group-safety",
        "connectorKind":"GenericWebhook",
        "displayName":"Group Safety Webhook",
        "configurationJson":"{\"sharedSecret\":\"hook-secret\",\"defaultThreadType\":\"Group\"}"
      }' \
  "http://127.0.0.1:5076/api/channels/accounts"
```

Expected:

- connector list exposes both `Telegram` and `GenericWebhook`
- the Telegram account is created as a connected product-owned connector binding
- the webhook account retains the shared secret and defaults new inbound threads to `Group`

### 3. Run the DM acceptance drill

```bash
dotnet test \
  /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "ChannelDeliveryApprovalIntegrationTests|Iteration5DirectMessageAcceptanceIntegrationTests"
```

Expected:

- the same Telegram DM thread reuses one `ThreadBinding` and one `channel-dm-*` session
- the DM prompt contains `workspace/USER.md` plus the thread summary and excludes `workspace/MEMORY.md`
- approval approve sends the outbound Telegram message and appends `approval.approved` + `delivery.sent` audit evidence
- note: Telegram inbound remains fixture-driven in this acceptance drill because public Telegram webhook registration is intentionally out of scope for Iteration 5

### 4. Run the group safety drill

```bash
dotnet test \
  /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter Iteration5GroupSafetyAcceptanceIntegrationTests
```

Expected:

- the same external group thread reuses one `ThreadBinding` and one `channel-group-*` session
- the group prompt excludes both `workspace/USER.md` and `workspace/MEMORY.md`
- rejecting the pending channel approval leaves `LastOutboundAt == null`
- audit includes `message.received` and `approval.rejected`, and does not include `delivery.sent`

### 5. Verify the Channels desk regression

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
VITE_KODACLAW_GATEWAY_URL="http://127.0.0.1:5076" \
VITE_KODACLAW_GATEWAY_TOKEN="$KODACLAW_GATEWAY_TOKEN" \
npm run dev
```

Expected:

- desk switcher exposes `Channels`
- connector and account state render without leaving the main workbench shell
- thread/detail panes remain stable even when a workspace currently has only seeded accounts and no accepted outbound history yet

## Exit Criteria

Iteration 5 is accepted when all of the following are true:

- channel contracts, repositories, policy, connectors, runtime, gateway APIs, governance, and approval-dispatch paths pass targeted automated coverage
- Telegram DM acceptance and group safety acceptance both pass against the real workspace-backed product composition
- approval approve sends only when allowed, approval reject never sends, and both decisions leave durable audit evidence
- the Channels desk passes its targeted Playwright flow and stays green in the full frontend regression suite
- all verification commands in this document pass on the current workspace state
