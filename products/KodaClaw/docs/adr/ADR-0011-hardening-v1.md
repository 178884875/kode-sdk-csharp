# ADR-0011: Hardening v1 freezes on keychain-first secrets, conservative repair, and manual-first updates

## Status

- `Accepted`

## Date

- 2026-03-19

## Implementation Status

- Iteration 7 Wave 0 planning freeze `Completed`
- Iteration 7 Wave 1 security foundation `Completed`
- Iteration 7 Wave 2 secret migration + portability `Completed`
- Iteration 7 Wave 3 operator hardening surfaces `Completed`
- Iteration 7 Wave 4 acceptance `Completed`
- `KC-0701` `Completed`
- `KC-0702` `Completed`
- `KC-0703` `Completed`
- `KC-0704` `Completed`
- `KC-0705` `Completed`
- `KC-0706` `Completed`
- `KC-0707` `Completed`
- `KC-0708` `Completed`
- `KC-0709` `Completed`
- `KC-0710` `Completed`
- Iteration 7 release-closure hardening patch `Completed`

## Background

After Iteration 6 closed, KodaClaw already had a real local product shape: Gateway, Web console, Desktop shell, Plugins, Channels, Automations, Canvas, and the supporting Control Plane.

What it still lacked was a hardened operator boundary.

Before implementation, the main thread needed to freeze six questions:

- whether secrets should remain environment-variable driven for longer or move behind a product-level OS Keychain abstraction now;
- whether export/import should include raw secrets for convenience or preserve security boundaries and require repair on the target machine;
- whether crash recovery should attempt aggressive automatic replay or stay conservative and inspectable;
- how to extend plugin trust beyond `Untrusted` / `Trusted` without pretending KodaClaw already has a full internet PKI pipeline;
- whether the first update mechanism should be a full silent auto-updater or a smaller manifest-check + guided-upgrade path;
- how sandbox risk, update status, and diagnostics evidence should surface in the product without inventing a second security subsystem.

Without freezing those decisions first, different workers would likely invent incompatible secret formats, restore behavior, trust semantics, and operator surfaces.

## Decision

We decided that:

- Iteration 7 v1 is `Keychain-first` and `local-first`. Product secrets move behind a shared secret-store abstraction and the first strongly validated provider is macOS Keychain.
- persisted product config stores only `SecretRef`-style references and redacted metadata. Raw secret values are not written into workspace files, SQLite state, plugin folders, or frontend bundles.
- environment variables remain acceptable only as bootstrap, migration, and test fallbacks; they are no longer the intended long-term product source of truth.
- migration evidence is redacted and inspectable: Gateway exposes a secret-migration report and persists a workspace artifact, but neither surface returns raw secret values.
- export/import preserves workspace, control-plane, and session/control metadata, but excludes raw secrets by default. Restore must produce a repair checklist for missing secrets or device mismatches instead of silently downgrading the model.
- crash recovery is conservative. KodaClaw does not over-promise cross-process live-session replay; it prefers explicit inspection, repair evidence, and safe state transitions such as `Interrupted`, `Canceled`, or `RepairRequired`.
- plugin trust extends to a placeholder `Signed` level in addition to the existing `Untrusted` and `Trusted` states. `Signed` means there is local verification evidence such as digest or signer metadata; it does not auto-enable the plugin or bypass user trust decisions. In the first implementation pass, KodaClaw records digest evidence on every discover/install/detail path and optionally verifies a local `plugin.signature.json` sidecar to promote trust from digest-only to `Signed`.
- the first update mechanism is manual-first: version manifest check, release channel awareness, release notes, and guided handoff to update. Silent background installation is intentionally out of scope for this hardening pass.
- sandbox risk prompts, update status, migration evidence, and diagnostic bundles reuse the current Gateway / Control Plane / Desktop / Web product surfaces rather than forming a separate security backend.
- macOS remains the first hardening acceptance anchor for Keychain and desktop update-check paths, while code should stay as cross-platform-friendly as practical.

## Alternatives Considered

### Option A: Keep secrets environment-variable and file based for another iteration

- Description: defer Keychain integration again and continue relying on env/dev-config references.
- Pros: faster short-term implementation and fewer cross-module changes.
- Cons: leaves the most sensitive product boundary in a development-era state and blocks a credible local-first release posture.

### Option B: Include raw secrets in export/import bundles for convenience

- Description: package model keys, bot tokens, and other secrets into the backup artifact to simplify migration.
- Pros: easier one-step restore experience.
- Cons: weakens the main security boundary, makes the backup artifact far more sensitive, and conflicts with the documented Keychain-first direction.

### Option C: Attempt aggressive automatic crash replay

- Description: try to resume running sessions, approvals, and automations across crashes as if nothing happened.
- Pros: more seamless appearance when it works.
- Cons: overstates what the current runtime can guarantee, increases silent corruption risk, and makes repair evidence harder to trust.

### Option D: Ship a full production auto-updater immediately

- Description: combine update checks with background download, install, signing, rollback, and distribution in the same iteration.
- Pros: closer to a polished release pipeline.
- Cons: makes Iteration 7 too large, couples platform packaging with release infrastructure, and distracts from the more urgent local hardening gaps.

### Option E: Require a full PKI-backed plugin signing system before extending trust

- Description: block plugin trust evolution until KodaClaw has a complete signing, notarization, and publisher-trust chain.
- Pros: stronger long-term security story.
- Cons: too large for the current phase and would delay valuable risk visibility and digest-based evidence that can be delivered now.

## Consequences

- Positive: secrets gain a real product-owned boundary instead of remaining scattered across env conventions.
- Positive: export/import and crash recovery become inspectable workflows rather than undocumented operator rituals.
- Positive: plugin trust, sandbox risk, update state, and diagnostics become visible product concepts instead of hidden implementation details.
- Positive: the Desktop shell can now evolve toward a releasable operator experience without breaking the `desktop is shell, gateway is brain` principle.
- Cost: restore flows may require users to rebind secrets on a new machine instead of providing a one-click full clone.
- Cost: the first updater is intentionally smaller than a production auto-update service.
- Cost: `Signed` is accepted as a placeholder evidence level, not a full ecosystem trust story.
- Constraint on later slices: future release/distribution work must preserve the secret-ref, repair-ledger, and redacted bundle boundaries frozen here.

## Validation

Planning freeze acceptance in this wave is document-driven:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_FREEZE.md` is the canonical Iteration 7 boundary.
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`, `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`, `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`, and `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md` must stay synchronized with this ADR before implementation starts.
- no Iteration 7 task should move from `Ready` to `Completed` until its slice verification and solution-level regression both pass.

Execution acceptance for this ADR is expected to include:

- secret-store contract / unit / integration coverage for Keychain-backed secret resolution and migration
- import/export + repair integration tests and at least one disaster-recovery drill
- plugin trust, sandbox risk, and diagnostic bundle UI / API coverage
- `KC-0707` validation now includes `UpdateStateContractsTests`, `UpdateStateApiIntegrationTests`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/models-settings-desk.spec.tsx`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0212-models-settings.spec.ts`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3`, and `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `KC-0708` validation now includes `SandboxRiskContractsTests`, `SandboxRiskApiIntegrationTests`, `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`, and `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e`
- `KC-0709` validation now includes `DiagnosticBundleContractsTests`, `DiagnosticBundleApiIntegrationTests`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/sessions-diagnostics-desk.spec.tsx`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0211-sessions-diagnostics.spec.ts`, `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`, and `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test && npm run test:e2e`
- `KC-0710` validation now includes `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`, `Iteration7AcceptanceIntegrationTests`, the targeted hardening contract/integration suite, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test && npm run smoke:wave3`, and serial full regression `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- post-closure hardening validation now also includes `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~UpdateStateApiIntegrationTests|FullyQualifiedName~BackupApiIntegrationTests|FullyQualifiedName~DiagnosticBundleApiIntegrationTests|FullyQualifiedName~SecretMigrationReportIntegrationTests"`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/models-settings-desk.spec.tsx`, `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`, and serial full regression `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- full regression across `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`, `kodaclaw-web`, and `kodaclaw-desktop`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`

Current evidence already in hand includes planning freeze, the completed Wave 1 foundation, the completed `KC-0702` migration slice with redacted report evidence, the completed `KC-0704` backup/export/import v1 slice, the completed `KC-0705` startup repair slice, the completed Wave 3 `KC-0706` / `KC-0707` / `KC-0708` / `KC-0709` operator hardening surfaces, the completed Wave 4 `KC-0710` acceptance closure, and the completed post-closure hardening patch (workspace-scoped archive paths, pre-extraction archive inspection, http/https-only update URLs, hermetic secret migration regression):

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_FREEZE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter SecretStoreContractTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter WorkspaceServiceContractTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter SecretMigrationReportContractTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewayAuthIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter SecretMigrationReportIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter BackupContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter StartupRepairContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter UpdateStateContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter DiagnosticBundleContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter BackupApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter StartupRepairApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter UpdateStateApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter DiagnosticBundleApiIntegrationTests`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/models-settings-desk.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/sessions-diagnostics-desk.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0211-sessions-diagnostics.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0212-models-settings.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter Iteration7AcceptanceIntegrationTests`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`

## Related Documents

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_FREEZE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/WORKSPACE_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/CHANNEL_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PLUGIN_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop/README.md`

## Related Slices

- `KC-0701`
- `KC-0702`
- `KC-0703`
- `KC-0704`
- `KC-0705`
- `KC-0706`
- `KC-0707`
- `KC-0708`
- `KC-0709`
- `KC-0710`
