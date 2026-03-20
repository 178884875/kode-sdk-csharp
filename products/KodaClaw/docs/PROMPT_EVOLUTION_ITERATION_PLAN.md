# KodaClaw Prompt Evolution Iteration Plan

Last updated: 2026-03-20
Execution mode: design -> build -> test -> accept -> align docs
Primary goal: upgrade bootstrap, prompt architecture, and channel execution toward the product shape in `PRODUCT.md`

## 1. Iteration strategy

This work should not be attempted as one large rewrite.

Recommended sequence:

1. fix the bootstrap contract gap that blocks the product promise today
2. establish shared prompt architecture primitives before channel and automation behavior diverge further
3. upgrade bootstrap into a natural-language workflow
4. close the channel inbound execution loop
5. add prompt reports and deeper memory controls after the baseline is stable

## 2. Iteration breakdown

## Iteration 1 - Bootstrap contract correction + prompt profile baseline

### Product outcome

- bootstrap completion durably writes `IDENTITY.md`, `SOUL.md`, and `USER.md`
- web onboarding can capture and submit soul guidance
- automation and channel sessions share a common prompt builder baseline
- prompt profiles become explicit in runtime code

### Scope

Backend:
- extend `BootstrapCompletionRequest` with `SoulMarkdown`
- extend `BootstrapCompletionResult` with `SoulFilePath`
- update `BootstrapService` to write `SOUL.md`
- update Gateway bootstrap validation and response serialization
- introduce prompt baseline primitives such as `PromptProfileId`, `PromptProfile`, `PromptContextDocument`, `PromptBuilder`, `PromptBuildResult`
- migrate automation and channel prompt assembly to the shared builder

Web:
- add soul field to bootstrap state and submit flow
- update contract typings and bootstrap tests

Docs:
- publish prompt architecture v1
- record iteration plan and acceptance evidence

### Acceptance criteria

- `POST /api/system/bootstrap-complete` rejects requests missing any of identity, soul, or user markdown
- successful bootstrap writes all three files into workspace
- archived bootstrap behavior is unchanged except for the new file output
- channel and automation runtime tests still pass with shared prompt builder
- web bootstrap consumer sends `soulMarkdown`

### Test plan

- contract tests for bootstrap file outputs
- gateway integration tests for validation and file writes
- runtime integration tests for automation and channel prompt composition
- web unit/e2e bootstrap flow tests

## Iteration 2 - Natural-language bootstrap draft / confirm flow

### Product outcome

- bootstrap becomes conversation-first instead of textarea-first
- Koda can synthesize draft markdown for `IDENTITY.md`, `SOUL.md`, and `USER.md`
- user can preview, edit, and confirm generated protocol files

### Scope

Backend:
- add `POST /api/system/bootstrap-draft`
- optionally add `POST /api/system/bootstrap-summarize`
- preserve draft metadata or transcript summary for auditability

Web:
- add generate / refresh / confirm UX
- keep manual editing as an override, not the only path

### Acceptance criteria

- a bootstrap chat transcript can generate a three-file draft
- user can confirm a draft without hand-authoring all markdown from scratch
- commit still remains a separate explicit action

## Iteration 3 - Channel inbound turn execution + overlays

### Product outcome

- inbound channel events trigger a real agent turn
- channel turns end in `NoAction`, `DraftCreated`, `ApprovalRequested`, or `Delivered`
- DM vs group prompts use explicit overlays and privacy boundaries

### Scope

Backend:
- add channel turn orchestrator
- append inbound message into session timeline
- execute agent turn and classify structured reply proposal
- route proposal through delivery governance

Control plane:
- show last turn outcome, reply preview, and approval/draft linkage

### Acceptance criteria

- webhook ingestion produces real turn outcomes rather than only session shells
- group threads remain conservative by default
- approval and draft evidence is queryable from Gateway/UI

## Iteration 4 - Prompt reporting + automation refinement

### Product outcome

- prompt composition becomes observable
- automation sessions gain reportable prompt diagnostics and clearer memory boundaries
- model routing work can build on stable prompt metadata

### Scope

- add prompt reports and truncation metadata
- expose prompt report hooks in diagnostics / sessions surfaces
- refine automation prompt overlays and optional memory rules

### Acceptance criteria

- prompt reports can show loaded files and prompt size per run
- automation prompt regressions become diagnosable without manual source inspection

## 3. Delivery rules

Each iteration follows the same checklist:

1. freeze scope
2. implement the minimum useful slice
3. run targeted automated tests
4. perform one product-level acceptance pass
5. update docs before moving on

## 4. Current execution status

| Iteration | Status | Notes |
| --- | --- | --- |
| Iteration 1 | Completed on 2026-03-20 | bootstrap now writes `SOUL.md`; automation/channel prompts use shared builder baseline |
| Iteration 2 | Completed on 2026-03-20 | `bootstrap-draft` landed; web onboarding now supports generate -> preview/edit -> confirm |
| Iteration 3 | Completed on 2026-03-20 | webhook and Telegram polling inbound now share the same channel turn orchestrator, with structured outcomes and contract-backed policy evidence surfaced in Gateway/UI |
| Iteration 4 | Completed on 2026-03-20 | sessions detail now exposes prompt reports with profile, prompt size/budget, truncation evidence, recent history, and prompt diff summaries; Automations desk now surfaces latest-run prompt diagnostics and explicit memory-boundary evidence |

## 5. Exit criteria for this phase

This phase is successful when KodaClaw has:

- a documented prompt architecture
- a fixed bootstrap persistence contract aligned with `PRODUCT.md`
- an iteration-ready runtime prompt baseline shared across at least automation and channels
- a clear next step for NL bootstrap and channel turn execution

## 6. Iteration 1 acceptance snapshot

Implemented:

- bootstrap completion contract now requires `identityMarkdown`, `soulMarkdown`, and `userMarkdown`
- `BootstrapService` now writes `IDENTITY.md`, `SOUL.md`, and `USER.md`
- web onboarding now exposes a soul editor and submits `soulMarkdown`
- automation and channel sessions now build prompts through shared prompt profile / prompt builder primitives

Verified on 2026-03-20:

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter BootstrapServiceContractTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~BootstrapFlowIntegrationTests|FullyQualifiedName~AutomationSessionServiceIntegrationTests|FullyQualifiedName~ChannelSessionServiceIntegrationTests|FullyQualifiedName~Iteration1AcceptanceIntegrationTests"`
- `npx vitest run src/__tests__/app-shell.spec.tsx`
- `npx playwright test tests/kc0109-bootstrap.spec.ts tests/kc0112-bootstrap-chat-resume.spec.ts`
- `npm run build`

Result:

- Iteration 1 acceptance passed
- next execution target is Iteration 2: natural-language bootstrap draft / confirm flow

## 7. Iteration 2 acceptance snapshot

Implemented:

- added `POST /api/system/bootstrap-draft` for transcript-driven draft synthesis of `IDENTITY.md`, `SOUL.md`, and `USER.md`
- bootstrap draft generation now uses the shared bootstrap prompt profile and current seed markdown when present
- web onboarding now supports generate draft from chat, editable preview fields, draft summary display, and explicit final commit
- bootstrap commit remains a separate `bootstrap-complete` action so generated drafts do not auto-persist

Verified on 2026-03-20:

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter BootstrapServiceContractTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~BootstrapDraftApiIntegrationTests|FullyQualifiedName~BootstrapDraftServiceIntegrationTests|FullyQualifiedName~BootstrapFlowIntegrationTests|FullyQualifiedName~AutomationSessionServiceIntegrationTests|FullyQualifiedName~ChannelSessionServiceIntegrationTests|FullyQualifiedName~Iteration1AcceptanceIntegrationTests"`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx vitest run src/__tests__/app-shell.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0109-bootstrap.spec.ts tests/kc0112-bootstrap-chat-resume.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`

Result:

- Iteration 2 acceptance passed
- next execution target is Iteration 3: channel inbound turn execution + overlays

## 8. Iteration 3 slice A snapshot

Implemented:

- added a reusable `ChannelTurnOrchestrator` that turns inbound webhook messages into real runtime turns
- channel runtime now supports `RunInboundTurnAsync(...)` with a strict JSON reply contract for `no_reply` vs `propose_reply`
- turn results now surface as structured `ChannelTurnOutcome` records (`NoAction`, `DraftCreated`, `ApprovalRequested`, `Delivered`, `Failed`)
- Gateway thread detail and thread list now expose the latest turn outcome for control-plane inspection
- Channels desk now renders the latest turn outcome summary and reply preview when present

Verified on 2026-03-20:

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "FullyQualifiedName~ChannelContractsTests|FullyQualifiedName~BootstrapServiceContractTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~ChannelApiIntegrationTests|FullyQualifiedName~ChannelDeliveryApprovalIntegrationTests|FullyQualifiedName~ChannelDeliveryGovernanceIntegrationTests|FullyQualifiedName~ChannelSessionServiceIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~Iteration5DirectMessageAcceptanceIntegrationTests|FullyQualifiedName~Iteration5GroupSafetyAcceptanceIntegrationTests"`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx vitest run src/__tests__/channels-desk.spec.tsx src/__tests__/app-shell.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`

Current status:

- Iteration 3 is now in progress with the webhook/gateway path closed
- next slice should wire the same orchestrator into connector-managed inbound paths such as Telegram polling, then deepen DM/group overlays and approval evidence

## 9. Iteration 3 slice B snapshot

Implemented:

- Telegram account upserts now reconcile live polling state through `ChannelInboundGatewayService`
- Telegram polling inbound now routes through the same `ChannelTurnOrchestrator` path used by webhook ingress
- Gateway startup now restores persisted connected Telegram accounts with inbound polling enabled
- Gateway runtime reconciliation now marks Telegram accounts `Connected`, `Disconnected`, or `Degraded` based on connector startup outcome
- direct-message Telegram polling turns now create the same draft approval evidence and `ChannelTurnOutcome` metadata as webhook-triggered turns
- DM vs group channel prompt overlays now carry clearer private-delegate vs public-observer guidance, with explicit no-reply bias for unmentioned group turns
- `ChannelTurnOutcome` now carries lightweight diagnostics (`reasonCode`, `hasExplicitMention`) so group no-action and approval rejection paths are explainable from Gateway responses
- Channels desk now renders those diagnostics so operators can see why a turn was blocked, drafted, or rejected without opening raw audit JSON
- Inbox / Approval desk now renders channel delivery payload context so operators can inspect account, thread, delivery mode, draft id, and reply preview before deciding
- Gateway `ChannelThreadDetail` now exposes contract-backed `policyEvidence` tokens for group mention gating, pending approvals, and operator rejection/no-action evidence
- Channels desk now prefers that Gateway-backed policy evidence and only keeps legacy UI composition as a compatibility fallback
- Inbox / Approval desk now exposes direct thread-detail and thread-audit API drill-down paths for channel delivery approvals

Verified on 2026-03-20:

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~ChannelApiIntegrationTests|FullyQualifiedName~Iteration5DirectMessageAcceptanceIntegrationTests|FullyQualifiedName~Iteration5GroupSafetyAcceptanceIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~ChannelDeliveryApprovalIntegrationTests|FullyQualifiedName~ChannelDeliveryGovernanceIntegrationTests|FullyQualifiedName~ChannelSessionServiceIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "FullyQualifiedName~ChannelContractsTests|FullyQualifiedName~BootstrapServiceContractTests"`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx vitest run src/__tests__/channels-desk.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx vitest run src/__tests__/inbox-approval-desk.spec.tsx src/__tests__/channels-desk.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~ChannelApiIntegrationTests|FullyQualifiedName~ChannelDeliveryApprovalIntegrationTests|FullyQualifiedName~Iteration5GroupSafetyAcceptanceIntegrationTests|FullyQualifiedName~Iteration5DirectMessageAcceptanceIntegrationTests"`

Current status:

- Iteration 3 now has both webhook and Telegram polling inbound loops closed through the same orchestration path
- stable explainability evidence now lives in Gateway contracts; next slice can move to Iteration 4 prompt reporting / deeper runtime observability

## 10. Iteration 4 acceptance snapshot

Implemented:

- session-scoped `PromptReport` contracts now persist prompt profile id, prompt size, loaded context files, generated timestamp, and full system prompt
- main, automation, and channel session runtimes now write `prompt-report.json` alongside session state whenever they build or refresh a system prompt
- Gateway session detail now hydrates the stored prompt report into `/api/sessions/{id}` responses
- Sessions / Diagnostics desk now renders prompt profile, prompt size, loaded context files, and prompt preview for operator inspection
- prompt reports now also carry character budget, remaining budget, truncated context files, and truncation notes when oversized context is clipped
- prompt report storage now keeps a recent history window and Gateway exposes a diff against the previous prompt build (character delta + added/removed context files + truncation change)
- automation prompt assembly now spells out a hard memory boundary: `workspace/MEMORY.md` stays out unless it is explicitly loaded as an automation input
- Automations desk now pulls the latest automation run session detail so operators can inspect prompt profile, budget, truncation state, loaded files, and memory-boundary evidence without leaving the automation surface

Verified on 2026-03-20:

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "FullyQualifiedName~GatewaySessionsIntegrationTests|FullyQualifiedName~AutomationSessionServiceIntegrationTests|FullyQualifiedName~ChannelSessionServiceIntegrationTests|FullyQualifiedName~ChatSessionServiceIntegrationTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx vitest run src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/automations-desk.spec.tsx src/__tests__/app-shell.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0308-automations.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`

Result:

- Iteration 4 acceptance passed
- prompt observability now spans runtime -> Gateway -> Sessions UI -> Automations UI, with automation memory boundaries explicit in both prompt text and operator surfaces
