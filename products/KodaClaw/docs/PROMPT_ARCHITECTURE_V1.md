# KodaClaw Prompt Architecture V1

Last updated: 2026-03-20
Status: v1 baseline drafted; Iteration 1 implemented for bootstrap contract and shared automation/channel prompt composition; Iteration 2 implemented for natural-language bootstrap draft generation; Iteration 3 webhook + Telegram polling inbound turn slices implemented for structured channel outcomes; Iteration 4 implemented for prompt reporting, automation diagnostics, and explicit memory-boundary evidence
Source inputs:
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT_REVIEW_2026-03-20.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/CHANNELS_BOOTSTRAP_EXECUTION_PLAN_2026-03-20.md`
- OpenClaw prompt/bootstrap/channel patterns reviewed on 2026-03-20

## 1. Why KodaClaw needs a prompt architecture

KodaClaw is no longer missing raw runtime primitives. It is missing a stable product-layer method for deciding:

- which system prompt shape applies to each session kind
- which workspace files are allowed into that session
- when user profile and memory are recalled versus intentionally omitted
- how bootstrap turns natural language into durable protocol files
- how prompt size and truncation are observed before quality silently regresses

The current implementation still relies on ad hoc prompt strings:

- main session uses a single static `SystemPrompt`
- automation and channel sessions each build their own local prompt text
- bootstrap completion writes only `IDENTITY.md` and `USER.md`
- there is no prompt report, no prompt budget, and no prompt profile abstraction

This is workable for a prototype, but not for a personal Agent OS that needs durable behavioral boundaries.

## 2. Design goals

Prompt architecture v1 should satisfy the following:

1. prompt behavior is compositional, not hidden in one giant string
2. session kinds are first-class and produce different overlays
3. workspace file loading is policy-driven and inspectable
4. memory recall is conservative by default, especially in channels
5. bootstrap is natural-language first, markdown commit second
6. future changes can add prompt reports without rewriting every runtime surface

## 3. Lessons taken from OpenClaw

The most useful OpenClaw patterns are not specific wording. They are product structures:

### 3.1 Section-based prompt construction

OpenClaw builds prompts from explicit sections instead of scattering raw string concatenation. KodaClaw should do the same so that each section has a clear owner.

### 3.2 Prompt modes per session kind

OpenClaw does not treat onboarding, main work, subagents, and channel-like surfaces as the same mode. KodaClaw should formalize different prompt profiles for:

- Bootstrap
- Main
- Automation
- ChannelDirectMessage
- ChannelGroup
- future: Subagent

### 3.3 Workspace file injection as a contract

OpenClaw treats workspace files as an explicit prompt source, filtered by session kind. KodaClaw already does a conservative version of this in channels and automation. V1 should make that a shared rule instead of duplicated local logic.

### 3.4 Conservative memory recall

OpenClaw is careful about when memory is recalled. KodaClaw should follow the same principle:

- main sessions may load more personal context
- automation sessions may load task-relevant durable context
- group channels should default to no user profile and no long-term memory

### 3.5 Budget visibility

OpenClaw treats prompt budget and truncation as first-class. KodaClaw does not need full token accounting in Iteration 1, but the architecture must leave room for prompt reports and truncation evidence.

### 3.6 Onboarding as a dedicated lifecycle

OpenClaw treats onboarding as a distinct session shape. KodaClaw should stop thinking of bootstrap as a markdown form and instead treat it as a draft -> confirm -> commit lifecycle.

## 4. Proposed architecture

## 4.1 Core objects

Introduce the following runtime prompt concepts:

- `PromptProfileId`
  - stable identity for a prompt mode
- `PromptProfile`
  - metadata and base instructions for one mode
- `PromptContextDocument`
  - one injected workspace file with display path and content
- `PromptBuilder`
  - section-based builder for final system prompt assembly
- `PromptBuildResult`
  - final prompt text plus debug metadata for future diagnostics
- future: `PromptReport`
  - prompt budget, truncation, loaded files, and omitted files

## 4.2 Prompt profile matrix

| Profile | Purpose | Key behavior |
| --- | --- | --- |
| `Bootstrap` | onboarding and protocol synthesis | asks clarifying questions, extracts durable identity/user/soul intent, avoids premature tool-heavy behavior |
| `Main` | primary user work thread | full workspace collaboration, approval-aware action policy, strongest continuity |
| `Automation` | scheduled or proactive execution | constrained to automation definition + selected files, concise results, approval-safe |
| `ChannelDirectMessage` | private external thread | can load user profile when policy allows, replies as bounded extension of Koda |
| `ChannelGroup` | shared external thread | conservative memory policy, explicit mention gating, lowest privacy spill risk |
| `Subagent` | future delegated work unit | narrow task scope, reduced memory load, result-oriented output |

## 4.3 Prompt composition model

Every prompt should be assembled in this order:

1. base profile instruction
2. product safety / approval rules
3. session metadata
4. mode-specific overlay
5. loaded context file list and file contents
6. future: budget / truncation notes

That gives KodaClaw a stable skeleton even when specific wording changes.

## 4.4 Workspace file injection policy

### Baseline file classes

- global operating files
  - `AGENTS.md`
  - `IDENTITY.md`
  - `SOUL.md`
- user preference files
  - `USER.md`
- continuity files
  - `MEMORY.md`
  - `memory/YYYY-MM-DD.md`
- proactive files
  - `HEARTBEAT.md`
- thread-local files
  - `workspace/channels/<bindingId>/SUMMARY.md`
- task-local explicit inputs
  - automation `InputPaths`

### V1 loading matrix

| File / Context | Bootstrap | Main | Automation | Channel DM | Channel Group |
| --- | --- | --- | --- | --- | --- |
| `AGENTS.md` | optional | yes | yes | policy | policy |
| `IDENTITY.md` | draft/seed | yes | yes | policy | policy |
| `SOUL.md` | draft/seed | yes | yes | policy | policy |
| `USER.md` | draft/seed | yes | yes | policy and DM only | no |
| `MEMORY.md` | no | yes | later policy | no in v1 | no |
| `HEARTBEAT.md` | no | optional | yes | no | no |
| channel thread summary | no | no | no | policy | policy |
| explicit task inputs | no | task-driven later | yes | no | no |

Notes:

- Channel group sessions stay conservative even if stored policy becomes overly broad.
- Bootstrap should eventually synthesize `IDENTITY.md`, `SOUL.md`, and `USER.md` drafts before those files become stable prompt inputs.
- Main session prompt loading can move to the shared resolver after Iteration 1 baseline is proven on automation and channels.

## 4.5 Memory recall policy

Memory must be explicit and mode-aware.

### Main

- may load durable identity, soul, user profile, and long-term memory
- should later support lightweight recency summaries instead of dumping entire histories

### Automation

- loads durable operating files by default
- loads explicit task inputs
- should not automatically load broad personal memory unless the automation type calls for it

### Channel direct message

- may load `USER.md` if policy allows
- must not load long-term memory by default in v1
- should prefer recent thread summary over broad memory recall

### Channel group

- must not load `USER.md` or long-term memory in v1
- should only operate on channel-safe identity/soul plus optional thread summary

## 4.6 Natural-language bootstrap lifecycle

Bootstrap should evolve into three steps:

1. `draft`
   - Koda reviews bootstrap conversation history and produces candidate markdown for `IDENTITY.md`, `SOUL.md`, and `USER.md`
2. `confirm`
   - user edits or approves the draft in the UI
3. `commit`
   - Gateway writes the files, archives bootstrap material, and switches workspace into normal mode

Iteration 1 only corrects the commit contract so `SOUL.md` is part of the durable output.
Iteration 2 adds `bootstrap-draft` and a conversation-first UI flow.
Current implementation now supports transcript -> draft generation, editable preview fields, summary feedback, and explicit commit as a separate user action.

## 4.7 Prompt budget and reporting plan

Prompt report support is not required to ship Iteration 1, but the architecture must reserve space for it.

Future `PromptReport` fields should include:

- `profileId`
- `characterCount`
- `estimatedTokenCount`
- `loadedFiles`
- `omittedFiles`
- `truncationApplied`
- `truncationReason`
- `generatedAt`

Initial use cases:

- session diagnostics desk
- automation run evidence
- channel safety debugging
- model routing and regression review

## 4.8 Channel-specific overlays

Channels need overlay instructions on top of the shared base profile.

### Direct message overlay

- reply as a bounded extension of Koda
- use user profile only if policy allows it
- keep replies concise and approval-safe

### Group overlay

- assume partial context and public visibility
- avoid personal memory recall
- prefer `no_reply` unless the thread clearly addresses Koda or policy allows replying
- future: mention gating and command gating should feed this profile

## 4.9 Automation overlay

Automation needs its own overlay:

- treat the automation definition as the primary objective
- prefer concise structured outcomes
- do not perform risky external actions without approval path
- use only listed input files plus the baseline operating files

## 5. Recommended implementation sequence

### Iteration 1

- fix bootstrap completion contract to write `SOUL.md`
- introduce prompt profile baseline objects and shared prompt builder
- migrate automation and channel prompt assembly onto the shared builder

### Iteration 2

- add bootstrap draft/summarize API
- upgrade bootstrap UI to generate, preview, edit, and confirm drafts
- preserve transcript/archive traceability

### Iteration 3

- add channel inbound turn execution and reply proposal envelope
- add profile overlays for `ChannelDirectMessage` and `ChannelGroup`
- expose outcome evidence in Gateway and control plane

Current slice status:

- webhook-triggered inbound turns now run through a dedicated channel turn orchestrator
- runtime channel sessions now emit a structured reply envelope before delivery governance
- Gateway/UI can inspect the latest `ChannelTurnOutcome` for each thread
- Telegram polling inbound now reuses the same orchestration path, including startup restore for connected accounts
- channel prompt overlays now more clearly distinguish private DM delegation from public group observation/mention gating
- channel outcomes now carry lightweight decision diagnostics such as `reasonCode` and `hasExplicitMention` for explainable no-action / rejection states
- Gateway thread detail now also exposes contract-backed `policyEvidence` tokens so operator evidence is stable across UI surfaces
- remaining work is to deepen prompt reporting/observability on top of those overlay rules

### Iteration 4

- session runtimes now persist a first-pass prompt report with profile id, generated timestamp, prompt size/budget, loaded context files, truncation evidence, recent history, and full system prompt
- Gateway session detail and the Sessions / Diagnostics desk now expose that prompt report plus a previous-build diff for main, automation, and channel sessions
- automation prompt overlays now explicitly state that `workspace/MEMORY.md` stays out unless it is intentionally loaded as an automation input
- Automations desk now surfaces latest-run prompt diagnostics directly on the automation detail panel, including loaded files, budget/truncation evidence, and memory-boundary messaging
- prepare task-based model routing hooks

## 6. Acceptance bar for V1 architecture

Prompt Architecture V1 is considered established when:

- bootstrap completion durably writes `IDENTITY.md`, `SOUL.md`, and `USER.md`
- automation and channel prompts are built through a shared builder rather than duplicated string patterns
- prompt profiles are explicit in code and testable
- docs clearly define file loading and memory boundaries by session kind
- later iterations can add prompt reports and NL bootstrap without breaking the contract
