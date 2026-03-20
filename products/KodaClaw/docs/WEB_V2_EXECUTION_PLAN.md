# Web V2 Execution Plan

Last updated: 2026-03-20
Status: Phase 1 completed, Phase 2 baseline completed, Phase 3 in progress, Phase 4 in progress

## 1. Scope

Execution update (2026-03-20):

- Phase 1 P0 remediation is completed in code and targeted validation.
- Canvas preview now uses Gateway-signed preview URLs that support iframe navigation plus relative asset loading.
- Session completion now persists the final `Ready` breakpoint on the SDK `Send()` path.
- Phase 2 shell scaffolding is now landed in the main app without creating a second web app, and `shell-v2` is now the default path.
- Phase 3 has started: the V2 context rail now keeps the active main session plus recent traces visible on the chat path.
- The Phase 3 main-stage chat densification and session/thread affordance pass is still active.
- A first visual calibration pass is now also landed inside Phase 3: chat-mode context copy is lighter, shell density is tighter, and narrow-screen ordering now keeps the main stage ahead of the context support rail.
- Phase 4 is now active across the first three pane migrations: `Sessions / Diagnostics`, `Inbox / Approval`, and the first `Models / Settings` slice.

This document freezes the execution order for the next KodaClaw web work:

1. Fix the two P0 product bugs found in live dogfood
2. Introduce a new conversation-first shell inside the existing `kodaclaw-web`
3. Keep one web app, one build target, and one desktop/web integration surface

This plan intentionally does **not** create a second app such as `kodaclaw-webv2`.

## 2. Decisions

### 2.1 One web app only

Keep the only product web app at:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web`

Do not create a parallel app package. Instead, introduce the V2 shell inside the existing app with isolated internal structure.

### 2.2 V2 shell lives inside the current app

The new shell will be introduced under a dedicated internal slice, expected shape:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/shell-v2/`

The rest of the app keeps its existing API client, i18n, desktop runtime bridge, and test harness.

### 2.3 P0 fixes land before shell rewrite

The following bugs must be fixed before the V2 shell is considered valid:

1. Canvas default preview authentication / fallback rendering
2. Session `breakpointState` cleanup after chat completion

### 2.4 Canvas fix should be product-grade

Do not solve Canvas preview with a frontend-only blob workaround unless blocked.

Preferred direction:

- Gateway supplies a preview-safe URL/token mechanism for iframe loading
- Web consumes that mechanism without bypassing existing auth guarantees

If code inspection shows an equally safe and simpler same-origin fix, it may be used, but the final solution must support iframe navigation and asset loading cleanly.

## 3. Delivery order

### Phase 0 - Freeze implementation contract

Deliverables:

- this document
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT_LIVE_CAPABILITY_AUDIT_2026-03-20.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/FRONTEND_UIUX_REFACTOR_PLAN_V2.md`

Exit criteria:

- bug list, architecture direction, and rollout order are frozen before coding

### Phase 1 - P0 bug fixes

#### KC-W2-001 Canvas preview auth + fallback

Status: Completed

Goal:

- make the Canvas default preview render reliably in live product usage
- eliminate the observed unauthorized access path for default preview loading

Likely write set:

- `src/KodaClaw.Gateway/Endpoints/GatewayApp.CanvasEndpoints.cs`
- `src/KodaClaw.Gateway/Infrastructure/GatewayApp.CanvasFiles.cs`
- `apps/kodaclaw-web/src/components/CanvasDesk.tsx`
- `apps/kodaclaw-web/src/lib/api.ts`
- related contract/integration/component/e2e tests

Validation:

- targeted Gateway tests for canvas preview access
- `npm run test -- --run src/__tests__/canvas-desk.spec.tsx`
- `npx playwright test tests/kc0309-canvas.spec.ts`

#### KC-W2-002 Session breakpoint finalize cleanup

Status: Completed

Goal:

- ensure chat completion returns session status to a stable post-run state
- remove stale `StreamingModel` from control-plane surfaces after completed runs

Likely write set:

- `src/KodaClaw.Runtime/*`
- `src/KodaClaw.Gateway/Infrastructure/GatewayApp.SessionInfrastructure.cs`
- related runtime/gateway integration tests

Validation:

- targeted runtime/session integration tests
- targeted gateway session integration tests

Exit criteria for Phase 1:

- `Completed`: both P0 bugs are fixed in code and covered by targeted backend/frontend validation.
- `Pending live rerun after shell refresh`: the original dogfood repro should be rechecked against the rebuilt live app before Phase 2 is declared production-ready.
- `Completed`: docs are updated with the final Phase 1 status.

### Phase 2 - Shell V2 skeleton

#### KC-W2-003 V2 shell scaffolding in current app

Status: Completed

Goal:

- add `shell-v2` structure without breaking the existing app entrypoint
- introduce a swappable shell seam while keeping a single Vite app

Planned work:

- extract shell layout primitives
- define rail / context / stage composition
- introduce a temporary V2 switch seam for safe rollout

Delivered:

- added `src/shell-shared/` for shared shell typing and variant selection
- added `src/shell-v1/LegacyShell.tsx` as the compatibility fallback shell
- added `src/shell-v2/GlobalRail.tsx`, `ContextRail.tsx`, `MainStage.tsx`, `V2Shell.tsx`, and `shell-v2.css`
- rewired `src/App.tsx` to compose shared `workbench` + `contextPanel` once, then render `LegacyShell` or `V2Shell`
- preserved existing `desk-tab-*` test ids so existing desk automation remains stable
- froze rollout seam to default `shell-v2`, with `?shell=v1` and `localStorage["kodaclaw.shellVariant"]` still available as explicit overrides

Likely write set:

- `apps/kodaclaw-web/src/App.tsx`
- `apps/kodaclaw-web/src/index.css`
- `apps/kodaclaw-web/src/shell-v2/*`
- supporting tests

Validation:

- `npm run typecheck`
- `npm run build`
- `npm run test -- src/__tests__/app-shell.spec.tsx`

### Phase 3 - Chat-first primary stage

#### KC-W2-004 Conversation-first default workbench

Status: In progress

Goal:

- make chat the default visible working surface
- keep timeline and composer in the main stage without scrolling past a landing-style hero

Planned work:

- move global status into compact chrome
- reduce summary-card dominance
- promote chat timeline + composer to the main stage
- introduce context rail with current/related session entries

Current delivered slice:

- `ContextRail` now switches to a chat-specific `Session pulse` panel when `mainDesk === "chat"`
- the panel loads `/api/sessions`, highlights the active main session, and keeps recent traces visible without leaving the V2 shell
- a compact bridge back to the full Sessions / Diagnostics desk is now present from the chat context rail
- `MainStage` now also has a chat-specific compact header: active main session, gateway, health, and workspace are rendered as dense stage chips instead of a large overview hero
- the V2 chat surface now stretches the timeline/composer into a taller operator canvas with a sticky composer treatment
- recent session cards in the V2 chat context rail can now open `Sessions / Diagnostics` directly and pre-focus the requested session detail/timeline
- chat-mode context chrome no longer repeats the large desk title; it now uses a lighter helper heading/copy so the main conversation stage remains the dominant visual surface
- shell spacing, card density, radii, and shadow strength have been tightened to feel more like a workstation and less like a landing-style dashboard
- at narrow widths, layout order is now `GlobalRail -> MainStage -> ContextRail`, preventing the chat stage from being pushed below the first viewport

Validation:

- `npm run test -- src/__tests__/chat-context-rail.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/app-shell.spec.tsx`
- `npm run build`
- `npm run test:e2e -- tests/kc0213-control-plane-acceptance.spec.ts`
- desktop/mobile `agent-browser` layout smoke
- smoke Playwright flow

### Phase 4 - Desk migration to list-detail panes

#### KC-W2-005 Inbox / Sessions list-detail conversion

Status: In progress

Current delivered slice:

- `SessionsDiagnosticsDesk` has been reorganized into a clearer pane flow: session rail on the left, focused detail stage on the right
- the selected session now surfaces kind, created/last-event timestamps, message breakdown, pending approval call count, and last SFP index in the detail stage
- diagnostics timeline and diagnostic bundle export now sit as sibling operator panels instead of reading like one long stacked page
- `InboxApprovalDesk` now mirrors the same operator grammar: stacked inbox / approval queues on the left, persistent inbox focus + approval focus panels on the right
- inbox and approval selection are now linked when a relation exists, so switching either side keeps the paired object detail visible without losing the inline approve / reject / status controls
- session detail hydration now waits for the fresh selection payload before swapping the stage, avoiding stale summary/timeline flashes during rapid session switching

Validation:

- `npm run test -- src/__tests__/inbox-approval-desk.spec.tsx src/__tests__/chat-context-rail.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/app-shell.spec.tsx`
- `npm run build`
- `npm run test:e2e -- tests/kc0210-inbox-approval.spec.ts tests/kc0213-control-plane-acceptance.spec.ts`

#### KC-W2-006 Models / Settings list-detail conversion

Status: In progress

Current delivered slice:

- `ModelsSettingsDesk` now uses the same pane shell grammar as the other migrated desks: model registry on the left, focused model detail/composer on the right
- the default or first model endpoint is now auto-focused so the right-stage editor reads like a true detail surface instead of an unrelated long form
- a light stage index now points to `Runtime preferences`, `Update watch`, and `Risk briefing`, while all three supporting panels remain mounted so the current acceptance flow and stable selectors stay intact

Validation:

- `npm run test -- src/__tests__/models-settings-desk.spec.tsx src/__tests__/inbox-approval-desk.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/chat-context-rail.spec.tsx src/__tests__/app-shell.spec.tsx`
- `npm run build`
- `npm run test:e2e -- tests/kc0212-models-settings.spec.ts tests/kc0213-control-plane-acceptance.spec.ts`

#### KC-W2-007 Channels / Plugins / Automations / Canvas list-detail conversion

Goal:

- convert current desk pages into pane-based operator flows

Rules:

- preserve current API contracts
- preserve current domain behavior
- improve hierarchy and first-run action affordance

Validation:

- desk component tests
- existing Playwright suites per desk

### Phase 5 - Empty-state CTA system + final polish

#### KC-W2-008 First-run CTA completion

Goal:

- every empty state must tell the user the next available action

Examples:

- Channels: connect Telegram / create webhook account
- Plugins: install local plugin
- Automations: create or import automation
- Canvas: open default workspace canvas / inspect published artifact

Validation:

- component tests for empty-state rendering
- end-to-end sanity checks on empty workspaces

## 4. Proposed internal structure

Target structure for the V2 shell:

- `src/shell-v2/layout/`
- `src/shell-v2/navigation/`
- `src/shell-v2/panes/`
- `src/shell-v2/chrome/`
- `src/shell-v2/state/`

Suggested responsibilities:

- `layout/`: global rail, context rail, main stage
- `navigation/`: desk metadata, selection models, badges
- `panes/`: desk-specific list/detail composition wrappers
- `chrome/`: top toolbar, health, locale, update, workspace chips
- `state/`: shell mode selection and view-model helpers

## 5. Rollout rules

1. Do not remove existing tested behavior until the replacement path is covered
2. Prefer additive refactors first, then replace old layout paths
3. Keep routing, API clients, i18n, and desktop bridge stable
4. Preserve test ids unless there is a strong reason to move them
5. Every completed slice must update docs before moving on

## 6. Testing protocol

Minimum required validation per completed slice:

- targeted unit/component tests
- targeted integration/e2e tests for changed surfaces
- `npm run build`
- `npm run test`

Required before claiming a milestone complete:

- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

For Gateway-affecting slices:

- targeted `dotnet test` for affected projects
- expand to solution-level validation before closure if the write set touches shared runtime/gateway contracts

## 7. Documentation sync list

After each accepted slice, sync at least:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- this execution plan

When user-facing behavior changes materially, also sync:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_GUIDE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`

## 8. Subagent split

Subagents are allowed only with disjoint write scopes.

Recommended split:

- `gateway-worker`
  - ownership: canvas preview auth and session finalize fixes
  - write set: `src/KodaClaw.Gateway`, `src/KodaClaw.Runtime`, related tests

- `web-shell-worker`
  - ownership: shell-v2 layout and navigation
  - write set: `apps/kodaclaw-web/src/shell-v2`, `App.tsx`, `index.css`

- `web-tests-worker`
  - ownership: component/e2e adaptation for changed UI
  - write set: `apps/kodaclaw-web/src/__tests__`, `apps/kodaclaw-web/tests`

The main thread remains responsible for:

- final design decisions
- cross-slice integration
- regression execution
- documentation closure

## 9. Immediate next action

Start with Phase 1:

1. inspect and fix Canvas preview auth
2. inspect and fix session breakpoint finalize cleanup
3. prove both fixes with automated tests
4. update the live audit and status docs
