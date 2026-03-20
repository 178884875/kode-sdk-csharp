# Iteration 7 Acceptance Pack

Last updated: 2026-03-19

## Goal

Freeze the acceptance evidence for Iteration 7 (`KC-0701` ~ `KC-0710`):

- secrets default to `SecretRef` + secure store / Keychain-style resolution instead of long-term plaintext config
- migration evidence is redacted and inspectable across Gateway token, model endpoints, channel credentials, and plugin runtime secrets
- startup repair is conservative, durable, and operator-visible rather than silently replaying uncertain runtime state
- backup/export/import preserves workspace + control-plane state without leaking raw secrets and always produces a repair checklist
- plugin trust, sandbox blast radius, and channel outbound risk remain visible inside the existing operator surfaces
- update flow stays manual-first with release-channel awareness, fixture-based version checks, and persisted evidence
- diagnostic bundle export is offline-readable, redacted by default, and includes enough repair/update/session context for support
- all backend/frontend/desktop baselines from Iteration 1 ~ Iteration 6 stay green after the hardening slices land

## Automated Acceptance Matrix

| Layer | Coverage | Evidence |
| --- | --- | --- |
| `L0` | Solution-wide hardening regression baseline（串行执行，避免 solution 级集成宿主争用） | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` |
| `L1` | Hardening contracts: secret store, migration report, backup manifest/import, startup repair, update state, sandbox risk, diagnostic bundle | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "SecretStoreContractTests|SecretMigrationReportContractTests|BackupContractsTests|StartupRepairContractsTests|UpdateStateContractsTests|SandboxRiskContractsTests|DiagnosticBundleContractsTests"` |
| `L2` | Hardening APIs/integration: auth, migration evidence, repair report, backup import/export, update check, sandbox risk, diagnostic bundle | `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "GatewayAuthIntegrationTests|SecretMigrationReportIntegrationTests|BackupApiIntegrationTests|StartupRepairApiIntegrationTests|UpdateStateApiIntegrationTests|SandboxRiskApiIntegrationTests|DiagnosticBundleApiIntegrationTests"` |
| `L3` | Iteration 7 integrated smoke across migration + repair + risk + update + bundle + backup restore | `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration7AcceptanceIntegrationTests.cs` |
| `L4` | Web operator surfaces for update watch, sandbox/risk, and diagnostic bundle export | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/models-settings-desk.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx` |
| `L5` | Web E2E coverage for control-plane/operator hardening desks | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0211-sessions-diagnostics.spec.ts tests/kc0212-models-settings.spec.ts tests/kc0213-control-plane-acceptance.spec.ts tests/kc0407-plugins.spec.ts` |
| `L6` | Desktop bridge regression for release-channel/runtime-config hardening path | `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test && npm run smoke:wave3` |
| `L7` | Hybrid operator drill for migration evidence, repair report, update watch, bundle export, and backup/restore | commands in this document |

## Standard Verification Commands

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "SecretStoreContractTests|SecretMigrationReportContractTests|BackupContractsTests|StartupRepairContractsTests|UpdateStateContractsTests|SandboxRiskContractsTests|DiagnosticBundleContractsTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "GatewayAuthIntegrationTests|SecretMigrationReportIntegrationTests|BackupApiIntegrationTests|StartupRepairApiIntegrationTests|UpdateStateApiIntegrationTests|SandboxRiskApiIntegrationTests|DiagnosticBundleApiIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter Iteration7AcceptanceIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3`

## Dogfood Drill

Use a disposable workspace such as `~/.kodaclaw-hardening-i7`.

### 1. Start Gateway with hardening-focused configuration

```bash
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw-hardening-i7"
export KODACLAW_GATEWAY_TOKEN="test-token"
export KODACLAW_UPDATE_RELEASE_CHANNEL="Stable"
export KODACLAW_UPDATE_MANIFEST_PATH="/tmp/kodaclaw-update-manifest.json"

cat > "$KODACLAW_UPDATE_MANIFEST_PATH" <<'JSON'
{
  "generatedAt": "2026-03-19T10:00:00Z",
  "source": "/tmp/kodaclaw-update-manifest.json",
  "channels": [
    {
      "channel": "Stable",
      "latestVersion": "0.1.2",
      "gatewayVersion": "0.1.2",
      "desktopVersion": "0.1.3",
      "downloadUrl": "https://example.com/download",
      "releaseNotesUrl": "https://example.com/release-notes",
      "releaseNotes": [
        "Adds the manual-first update desk."
      ],
      "guidance": "Review the release notes, then complete the guided handoff manually."
    }
  ]
}
JSON

dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls http://127.0.0.1:5076
```

Expected:

- `GET /api/system/health` returns `healthy`
- startup initializes/repairs the workspace and persists `config/startup-repair-report.json`
- `config/update-state.json` is absent until the first manual update check runs
- manifest 中的 `downloadUrl` / `releaseNotesUrl` 必须是绝对 `http/https` URL；若改成 `file:` / `javascript:` 等非法 scheme，Gateway 会清空这些字段并在 `OperatorNotes` 里给出说明

### 2. Generate migration evidence and inspect repair/update/risk surfaces

```bash
curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/system/secret-migration-report"

curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"desktopCurrentVersion":"0.1.0","desktopReleaseChannel":"Stable"}' \
  "http://127.0.0.1:5076/api/system/update-check"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/system/startup-repair-report"

curl -s \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "http://127.0.0.1:5076/api/settings/sandbox-risk"
```

Expected:

- migration report is redacted and persisted to `config/secret-migration-report.json`
- update check persists `config/update-state.json` and returns Gateway/Desktop component snapshots
- invalid manifest links are surfaced through `OperatorNotes` instead of being exposed as clickable release/download targets
- startup repair report points to the latest persisted repair checklist
- sandbox risk output explains Local sandbox best-effort boundaries and returns plugin/channel risk summaries

### 3. Verify the shared web operator surfaces

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
VITE_KODACLAW_GATEWAY_URL="http://127.0.0.1:5076" \
VITE_KODACLAW_GATEWAY_TOKEN="$KODACLAW_GATEWAY_TOKEN" \
npm run dev
```

Expected:

- `Models / Settings` shows `Sandbox & Risk Briefing` plus `Update Watch`
- `Sessions / Diagnostics` can export a diagnostic bundle without leaving the main workbench shell
- `Update Watch` only opens absolute `http/https` release/download links, even if the manifest is malformed
- all operator surfaces continue using the same shared web shell instead of a parallel hardening console

### 4. Export a diagnostic bundle

```bash
curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"main-001","timelineLimit":120}' \
  "http://127.0.0.1:5076/api/diagnostics/bundle-export"
```

Expected:

- response returns a zip path under `cache/diagnostics/`
- the archive root contains `manifest.json` and `redaction-summary.json`
- the bundle contains diagnostics/session/evidence snapshots and excludes raw secrets plus `messages.json` / `tool-calls.json`
- quoted/JSON secret fragments inside diagnostic messages are redacted before the bundle is written
- if a custom `archivePath` is supplied, it must still resolve inside the workspace; workspace-external targets fail with `validation.archive_path_invalid`

### 5. Export and preflight-import a backup artifact

```bash
curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' \
  "http://127.0.0.1:5076/api/system/backup-export"
```

Take the returned `archivePath`, then:

```bash
curl -s \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"archivePath\":\"/absolute/path/from/export.zip\"}" \
  "http://127.0.0.1:5076/api/system/backup-import/preflight"
```

Expected:

- export succeeds without embedding raw secrets
- the returned `archivePath` resolves inside the workspace root; if export is forced to `/tmp/...` or another external path, the API returns `validation.archive_path_invalid`
- preflight returns checksum + version validation and a repair checklist for unresolved secret refs/device state
- malformed zips are rejected during preflight before extraction; missing `backup-manifest.json`、suspicious entry paths, or case-colliding paths are treated as `backup.import.invalid_archive`
- import may proceed only after the checklist is understood; it should not silently mask missing secrets

## Exit Criteria

Iteration 7 is accepted when all of the following are true:

- targeted hardening contracts and integration APIs remain green together with the full solution regression
- there is at least one integrated smoke proving migration evidence, startup repair, sandbox risk, update check, diagnostic bundle export, and backup restore preflight can coexist in the same workspace lifecycle
- web operator surfaces for `Update Watch`, `Sandbox & Risk Briefing`, and `Sessions / Diagnostics` stay green in both component and Playwright coverage
- desktop bridge regression remains green for the manual-first release-channel/runtime-config path
- dogfood drill commands are sufficient for a human operator to reproduce migration evidence, repair evidence, update evidence, diagnostic export, and backup/restore behavior on a disposable workspace
- no Iteration 1 ~ Iteration 6 acceptance baseline is regressed while hardening v1 is closed
