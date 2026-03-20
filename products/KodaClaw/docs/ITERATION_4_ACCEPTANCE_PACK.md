# Iteration 4 Acceptance Pack

Last updated: 2026-03-19

## Goal

Freeze the acceptance evidence for Iteration 4 (`KC-0401` ~ `KC-0408`):

- a bundled `tool` plugin can be discovered from product-managed roots
- Gateway exposes the full plugin control-plane API for list/detail/discover/trust/enable/start/stop/logs
- Web can inspect plugin manifest, permissions, runtime posture, and logs through `PluginsDesk`
- fresh main sessions inject only `Trusted + Enabled + Running` namespaced plugin tools
- stopped or degraded plugins do not leak tools into new sessions
- degraded plugin faults leave observable log and diagnostics evidence without taking Gateway down

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution restore/build/test baseline | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L1` | Plugin manifest / permission / API contracts | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Plugins/PluginManifestContractsTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Plugins/PluginPermissionContractsTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Plugins/PluginApiContractsTests.cs` |
| `L2` | Plugin lifecycle, health, degraded isolation | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/PluginHost/PluginLifecycleHostIntegrationTests.cs` + `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/PluginHost/PluginHealthIntegrationTests.cs` |
| `L2` | Gateway plugin API behavior | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/PluginApiIntegrationTests.cs` |
| `L2` | Runtime plugin tool injection rules | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/PluginToolInjectionIntegrationTests.cs` |
| `L3` | Integrated Iteration 4 smoke: bundled discover -> trust/enable/start -> fresh-session injection -> stop/degraded isolation | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration4AcceptanceIntegrationTests.cs` |
| `L4` | Web plugin desk acceptance | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0407-plugins.spec.ts` |
| `L5` | Manual dogfood drill for bundled plugin operations | commands in this document |

## Standard Verification Commands

- `dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "PluginManifestContractsTests|PluginPermissionContractsTests|PluginApiContractsTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "PluginLifecycleHostIntegrationTests|PluginHealthIntegrationTests|PluginApiIntegrationTests|PluginToolInjectionIntegrationTests|Iteration4AcceptanceIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run typecheck`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0407-plugins.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-dev-i4` or a temporary directory.

### 1. Build the fixture server once

```bash
dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj
```

Expected:

- `KodaClaw.PluginFixtureServer.dll` exists under `tests/Fixtures/KodaClaw.PluginFixtureServer/bin/Debug/net10.0`

### 2. Start Gateway with bundled plugin fixtures enabled

```bash
export KODACLAW_GATEWAY_TOKEN="test-token"
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-dev-i4"
export KODACLAW_BUNDLED_PLUGINS_ROOT="/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/Plugins"
export KODACLAW_DEFAULT_MODEL="gpt-4o-mini"
export OPENAI_API_KEY="stub-key"
dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls http://127.0.0.1:5076
```

Expected:

- `GET /api/system/health` returns `healthy`
- bundled plugin fixtures become discoverable through `/api/plugins/discover`

### 3. Discover bundled plugin fixtures

```bash
curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/discover"
```

Expected:

- response contains `plugin.bundled.fixture`
- response contains `plugin.bundled.degraded`
- both are marked with `installSource = Bundled`

### 4. Trust, enable, and start the healthy bundled fixture

```bash
curl -s -X POST -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.fixture/trust"

curl -s -X POST -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.fixture/enable"

curl -s -X POST -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.fixture/start"

curl -s -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.fixture"
```

Expected:

- plugin transitions to `Trusted`, `Enabled`, `Running`
- detail payload shows `mcp__plugin.bundled.fixture__echo`
- `healthSummary.status` is `Healthy`

### 5. Inspect logs and degraded evidence

```bash
curl -s -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.fixture/logs?limit=20"

curl -s -X POST -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.degraded/trust"

curl -s -X POST -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.degraded/enable"

curl -s -X POST -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/plugins/plugin.bundled.degraded/start"
```

Then run the automated degraded-path acceptance:

```bash
dotnet test \
  /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "Iteration4AcceptanceIntegrationTests.Iteration_4_acceptance_should_keep_gateway_healthy_when_bundled_plugin_degrades"
```

Expected:

- healthy plugin logs contain startup evidence
- degraded plugin does not crash Gateway
- diagnostics expose `plugin.degraded`

### 6. Verify the Web plugin desk

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
VITE_KODACLAW_GATEWAY_URL="http://127.0.0.1:5076" \
VITE_KODACLAW_GATEWAY_TOKEN="$KODACLAW_GATEWAY_TOKEN" \
npm run dev
```

Expected:

- desk switcher exposes `Plugins`
- plugin list/detail/actions/log evidence render correctly
- operator can see trust/enable/start/stop posture without leaving the shell

## Exit Criteria

Iteration 4 is accepted when all of the following are true:

- plugin contracts, lifecycle, health, gateway APIs, and runtime injection pass targeted automated coverage
- bundled fixture plugins can be discovered and exercised through the integrated acceptance smoke
- degraded plugin evidence is visible through logs/diagnostics while Gateway remains healthy
- `PluginsDesk` passes targeted Playwright acceptance and stays green in the full frontend regression suite
- all verification commands in this document pass on current workspace state
