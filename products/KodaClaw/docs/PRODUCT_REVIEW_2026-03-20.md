# KodaClaw Product Review (2026-03-20)

Last updated: 2026-03-20
Review scope:
- Product target: `docs/PRODUCT.md`
- Live gateway: `http://127.0.0.1:5076`
- Workspace: `/Users/vanzheng/.kodaclaw`
- Auth: Bearer `test-token`

## 1. Executive Verdict

KodaClaw has already crossed the line from "example app" into a usable local agent runtime and control plane:

- bootstrap-state, chat SSE, approvals, inbox, sessions, diagnostics, settings, models, channels, plugins, automations, and canvas all have live API surfaces
- main-session chat and approval resolution both work end to end
- canvas default preview fallback is now reachable
- diagnostics and startup-repair evidence are strong enough to support product hardening work

But against `docs/PRODUCT.md`, KodaClaw is **not yet a complete personal Agent OS**.

The current product shape is still stronger as a **control console** than as a **conversation-centered operating surface with active external execution loops**.

The biggest remaining gap is no longer raw backend surface area. It is the absence of several crucial product loops:

1. inbound channel message -> agent processing -> draft/approval/reply
2. user-defined automation creation -> run -> inbox result
3. bootstrap conversation -> natural-language synthesis -> durable workspace protocol files
4. model hub routing strategy beyond a single default endpoint

## 2. Review Method

This review combines three inputs:

1. PRODUCT intent review from `docs/PRODUCT.md`
2. live gateway verification through `http://127.0.0.1:5076`
3. source-level inspection of gateway/runtime/bootstrap/channel implementation

## 3. What Was Verified Live

### 3.1 Main session and approval loop work

Verified live:

- `GET /api/system/bootstrap-state` returned `Normal`
- `POST /api/chat/stream` returned normal SSE `text_chunk` + `done`
- a forced `bash_run pwd` request created a pending approval and linked inbox item
- `POST /api/approvals/{id}/approve` resolved the approval and resumed the turn
- `/api/sessions/{id}` returned `breakpointState = Ready` after completion
- `/api/diagnostics/recent` and `/api/diagnostics/timeline` contained matching requested/completed/approval evidence

Conclusion:

- the main-thread runtime, approval linkage, inbox linkage, and diagnostics baseline are real and usable

### 3.2 Canvas default fallback works

Verified live:

- `GET /api/canvas/default` returned a preview URL
- requesting that preview URL returned `200 OK`
- fallback HTML rendered a valid empty canvas page

Conclusion:

- the earlier canvas-empty/auth chain issue is no longer the primary blocker

### 3.3 Channel account and thread ingestion work partially

Verified live:

- `POST /api/channels/accounts` successfully created a `GenericWebhook` account
- `POST /api/channels/webhook/{accountId}/events` accepted an inbound webhook event
- the system created a `ThreadBinding`, `channel-group-*` session, default group policy, and audit evidence

Observed thread/session output:

- `deliveryMode = RequireApproval`
- `policy.loadUserProfile = false`
- `policy.loadLongTermMemory = false`
- `sessionKind = ChannelGroup`
- `session.messageCount = 0`
- `pendingApprovalId = null`

Conclusion:

- the system correctly creates isolated channel metadata and safety defaults
- but it does **not** yet turn inbound channel events into an actual agent turn or reply workflow

### 3.4 Automation and plugin surfaces are exposed but under-activated

Verified live:

- `GET /api/automations` returned an empty list
- `POST /api/automations` returned `405 Method Not Allowed`
- `GET /api/plugins` returned an empty list
- `POST /api/plugins/discover` returned an empty list in the current runtime

Conclusion:

- those surfaces exist, but they do not yet support a convincing first-run product workflow in the tested environment

### 3.5 Security and hardening signals are useful but not fully productized

Verified live:

- `GET /api/system/secret-migration-report` showed gateway token and model API key still on `LegacyFallback`
- `GET /api/system/startup-repair-report` returned a valid clean report
- `GET /api/settings/sandbox-risk` returned a detailed risk overview

Conclusion:

- hardening visibility is already good
- long-term secret posture is not yet aligned with the final product ideal

## 4. Product Capability Matrix

| PRODUCT area | PRODUCT intent | Live result | Verdict |
| --- | --- | --- | --- |
| Main conversation | conversation-first, resumable, approval-aware | works end to end | Pass |
| Inbox / Approval | unified human decision center | works end to end | Pass |
| Workspace memory protocol | durable identity / user / memory / heartbeat protocol | partially present, but bootstrap still shallow | Partial |
| Model hub | multi-provider + fallback + task routing | single default endpoint management only | Partial |
| Plugins | install / trust / start / expand product capability | surface exists, first-run value not proven | Partial |
| Channels | external messages become isolated sessions with safe reply flow | thread/session metadata works, agent handling loop missing | Partial / Blocked |
| Automations | scheduled proactive tasks with inbox results | list + patch exist, create/workflow missing | Blocked |
| Canvas | agent-generated local UI surface | empty-state fallback works, richer artifact workflow still thin | Partial |
| Desktop shell | tray/notify/launcher shell | not reviewed in this pass | Not assessed |
| No CLI for primary flows | end users can complete core flows in product UI | still not true for channels/plugins/automations/bootstrap refinement | Fail |

## 5. Highest-Priority Gaps

### P0. Channels are not yet a true external conversation loop

`docs/PRODUCT.md` defines channels as independent session entry points, not just notification sources.

Current state:

- inbound event ingestion works
- binding and session creation work
- policy isolation works
- audit works
- but no agent turn is actually executed from that inbound message

Product impact:

- KodaClaw cannot yet claim to support "Telegram / webhook as real external agent entry"
- the product currently stores channel events better than it responds to them

### P0. Automation is not yet product-creatable

`docs/PRODUCT.md` requires controlled proactivity.

Current state:

- automation list/detail/runs/patch exist
- but create is missing from the live API surface
- the tested environment contained no automation objects users could operate

Product impact:

- proactive behavior still depends too much on fixture/setup/manual preparation
- this keeps the product short of the promised "active agent" identity

### P1. Bootstrap is still manual markdown editing, not natural-language onboarding

PRODUCT says bootstrap should establish identity, user profile, and soul through a first conversation.

Current state:

- users still manually edit markdown textareas for `IDENTITY.md` and `USER.md`
- `SOUL.md` is not written by bootstrap completion at all
- the completion contract only accepts `IdentityMarkdown` and `UserMarkdown`

Product impact:

- bootstrap feels like a configuration form, not an onboarding conversation
- the most important product-layer protocol is still edited manually instead of derived from natural language
- the current flow underuses the runtime and makes onboarding feel less agentic than the rest of the product vision

### P1. Model hub remains a registry, not a routing control plane

Current state:

- endpoint CRUD works
- default endpoint switching works
- dynamic runtime activation works
- but there is no visible primary/fallback chain or task-specific routing policy

Product impact:

- this falls short of PRODUCT's model-control ambition
- the system cannot yet express "different tasks prefer different models with fallback"

### P2. Plugin first-run value is still too weak

Current state:

- plugin APIs and desks exist
- current runtime showed no discoverable/installed plugins in practice

Product impact:

- plugins are architecturally important, but product onboarding does not make them feel central yet

## 6. Source-Level Review Notes

### 6.1 Bootstrap contract is too narrow for the stated product

Current contract and implementation only commit `IDENTITY.md` and `USER.md`:

- `src/KodaClaw.Contracts/BootstrapCompletionRequest.cs`
- `src/KodaClaw.Workspace/BootstrapService.cs`
- `src/KodaClaw.Gateway/Endpoints/GatewayApp.SystemEndpoints.cs`

But `docs/PRODUCT.md` explicitly expects bootstrap to establish:

- `IDENTITY.md`
- `USER.md`
- `SOUL.md`

This mismatch should be treated as product debt, not just implementation detail.

### 6.2 Channel runtime currently ensures session existence, but not channel-turn execution

The webhook endpoint calls `ChannelEventIngestionService.IngestAsync(...)` and then `EnsureChannelSessionAsync(...)`.

That is enough to:

- create or restore a session shell
- compose channel-safe context
- preserve policy boundaries

But it is not enough to:

- append the inbound message into the agent timeline
- trigger a model turn
- produce a structured output draft
- route that output into delivery governance

This is the core missing loop.

### 6.3 Automation API is operator-oriented, not user-journey-oriented

The current automation endpoint group supports:

- list
- detail
- runs list
- patch enabled state

That is a good operator surface, but not a complete product surface.

## 7. Recommendations

### 7.1 P0: Close the channel conversation loop

Add a channel turn orchestrator that performs:

1. inbound event normalization
2. binding/session recovery
3. append user-visible inbound message to channel session store
4. execute one agent turn with channel policy context
5. classify output into one of:
   - no reply
   - draft reply
   - approval-required reply
   - direct send
6. reuse `ChannelDeliveryGovernanceService` and `ChannelDeliveryApprovalService`
7. persist audit/inbox/approval evidence

Expected outcome:

- channels become a real agent entry point instead of a passive metadata surface

### 7.2 P0: Add automation creation and dry-run

Minimum product-worthy automation flow:

- `POST /api/automations`
- create from NL prompt + schedule
- manual dry-run
- last result -> inbox + canvas preview when relevant

Expected outcome:

- users can experience product-level proactivity without CLI or fixture setup

### 7.3 P1: Rebuild bootstrap around natural language

Bootstrap should become:

1. conversational intake
2. structured extraction
3. markdown proposal
4. human confirm/edit
5. commit to workspace protocol files

At minimum, bootstrap should start generating:

- `IDENTITY.md`
- `USER.md`
- `SOUL.md`

Expected outcome:

- first run feels like "meeting your agent" instead of filling a config form

### 7.4 P1: Upgrade model hub into routing policy

Add product-level routing policy objects or settings for:

- main chat default
- channel default
- automation default
- fallback chain
- tool-capable vs non-tool-capable preference

### 7.5 P1: Improve first-run product activation

After bootstrap, the product should guide users through three real activation steps:

1. connect one channel
2. create one automation
3. enable one plugin or sample extension

### 7.6 P2: Move secrets to the final posture by default

Use bootstrap/setup wizard time to migrate:

- gateway token
- default model key
- connector credentials
- plugin runtime secrets

into OS secret storage references by default.

## 8. Suggested Next Milestones

### Milestone A: Agent OS core loop

- channel inbound -> response/draft/approval works
- automation create + run works
- bootstrap NL synthesis works

### Milestone B: Product shell maturity

- conversation-first shell is default
- plugin onboarding is alive
- canvas becomes a common result surface, not only an empty fallback

### Milestone C: Long-term trust posture

- secret refs become default
- update flow and backup flow are integrated into first-run/maintenance UX

## 9. Final Assessment

If judged as a local agent runtime and operator control plane, KodaClaw is already strong.

If judged against `docs/PRODUCT.md` as a **personal Agent operating system**, the product is still in a transition phase:

- **main thread** is credible
- **control plane** is credible
- **channel loop** is not complete yet
- **automation loop** is not complete yet
- **bootstrap** is not yet worthy of the product's own vision

The next highest-value work is therefore not another desk. It is completing the missing user loops that make the existing desks meaningful.
