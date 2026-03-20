# ADR-0009: Channels v1 freezes on product-owned connectors, isolated channel sessions, and delivery-rule reuse

## Status

- `Accepted`

## Date

- 2026-03-19

## Implementation Status

- Iteration 5 Wave 0 planning freeze `Completed`
- Iteration 5 Wave 1 foundation `Completed`
- Iteration 5 Wave 2 connectors + diagnostics `Completed`
- Iteration 5 Wave 3 runtime + web `Completed`
- Iteration 5 Wave 4 acceptance `Completed`
- `KC-0501` `Completed`
- `KC-0502` `Completed`
- `KC-0503` `Completed`
- `KC-0504` `Completed`
- `KC-0505` `Completed`
- `KC-0506` `Completed`
- `KC-0507` `Completed`
- `KC-0508` `Completed`
- `KC-0509` `Completed`
- `KC-0510` `Completed`
- `KC-0511` `Completed`

## Background

After Iteration 4 closed, KodaClaw had enough product baseline to start external channels, but not enough clarity to safely split the work without another freeze.

The main thread needed to settle five questions before implementation:

- whether Iteration 5 channels should execute as plugin-hosted `channel` plugins immediately or as product-owned connectors first;
- whether the first real connector should be Telegram webhook mode, Telegram polling mode, or a generic synthetic path only;
- how external threads map to internal sessions without leaking into `main`;
- how DM and group chats load workspace context without polluting privacy boundaries;
- whether outbound delivery should introduce a dedicated channel approval stack or reuse the current approval / inbox surfaces.

Without freezing those boundaries first, different workers would likely invent incompatible connector contracts, session policies, and approval semantics.

## Decision

We decided that:

- Iteration 5 v1 focuses on two connector paths only: a real `telegram` connector and a synthetic `generic-webhook` connector.
- channels stay a long-term plugin-capable domain, but Iteration 5 does not require connectors to run through the PluginHost runtime path. The first accepted connectors are product-owned adapters inside `KodaClaw.ChannelHub`.
- Telegram v1 uses a single bot-token binding model and long polling as the first runnable inbound path. Telegram webhook registration, multi-account routing, and rich media are not part of Iteration 5 acceptance.
- generic webhook exists primarily to normalize events for testing and controlled integrations; it is not frozen as a large-scale webhook gateway.
- external conversations must always map to isolated channel sessions. Direct messages use `SessionKind.ChannelDirectMessage`, group chats use `SessionKind.ChannelGroup`, and neither may bind to `SessionKind.Main`.
- channel memory boundaries are conservative by default. Direct messages may load identity / soul / user context plus thread-local notes, but not `MEMORY.md`; group chats may load only minimal channel policy context and thread-local notes, but not `USER.md` or main memory.
- outbound delivery is governed by a shared `DeliveryRule` with three modes: `AutoSend`, `DraftApproval`, and `RequireApproval`.
- channel delivery approvals reuse the existing Control Plane baseline via `ApprovalKind.ChannelDelivery` and `InboxItemKind.ChannelUpdate`. KodaClaw does not introduce a separate channel approval UI or storage model in this iteration.
- inbound / outbound / approval / failure evidence reuses the existing diagnostics baseline plus a channel audit surface, rather than creating a standalone logging subsystem.
- secrets remain environment-variable or dev-config referenced in Iteration 5; OS Keychain integration is deferred to Iteration 7 hardening.

## Alternatives Considered

### Option A: Execute channels as plugin-hosted connectors immediately

- Description: require Telegram and webhook connectors to run as first-class `channel` plugins through PluginHost before accepting Iteration 5.
- Pros: aligns faster with the long-term plugin direction.
- Cons: couples Iteration 5 to plugin transport/runtime work that was intentionally deferred in Iteration 4, making the first channel slice too large and too risky.

### Option B: Make Telegram webhook mode the first acceptance path

- Description: require webhook registration and externally reachable inbound delivery for the first Telegram slice.
- Pros: closer to a production internet-facing deployment model.
- Cons: adds network/topology complexity, weakens local-first development, and slows down the first controlled acceptance path.

### Option C: Let channel sessions read main memory by default

- Description: treat channel conversations as near-equivalent to the main chat and load `MEMORY.md` plus broader workspace memory into DM and group sessions.
- Pros: more context-rich replies on day one.
- Cons: increases privacy leakage risk, blurs product boundaries, and makes group-chat safety harder to reason about.

### Option D: Build a dedicated channel approval center

- Description: add separate channel approval tables, inbox surfaces, and action routes outside the existing Control Plane.
- Pros: local ownership within the channel subsystem.
- Cons: duplicates existing approval/inbox behavior and fragments the product experience.

## Consequences

- Positive: Iteration 5 becomes small enough to implement safely while still delivering a real external message path.
- Positive: existing approvals, inbox, diagnostics, and session orchestration can be reused instead of duplicated.
- Positive: the privacy boundary between `main`, DM, and group sessions becomes explicit before more connectors are added.
- Positive: generic webhook provides a deterministic connector path for tests, integration fixtures, and acceptance drills.
- Cost: channels are not yet plugin-runtime-managed in this iteration even though the long-term direction remains plugin-friendly.
- Cost: Telegram acceptance is intentionally limited to single-account, text-first behavior.
- Cost: credentials still rely on environment or dev-config references until hardening work lands.
- Constraint on later slices: any future plugin-hosted channel runtime must preserve the normalized event, binding, policy, and delivery semantics frozen here.

## Validation

Planning freeze acceptance in this wave is document-driven:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_FREEZE.md` is the canonical Iteration 5 boundary.
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`, `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`, `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`, and `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md` must stay synchronized with this ADR before implementation starts.
- no Iteration 5 task should move from `Ready` to `Completed` until its slice verification and solution-level regression both pass.

Execution acceptance for this ADR is expected to include:

- channel contract / policy / repository test suites in `KodaClaw.ContractTests` and `KodaClaw.UnitTests`
- connector, delivery, gateway, and runtime integration suites in `KodaClaw.IntegrationTests`
- a dedicated Channels Playwright flow in `kodaclaw-web`
- Iteration 5 DM acceptance and group safety acceptance packs
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e`

## Related Documents

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_FREEZE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/CHANNEL_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0008-plugin-platform-v1.md`

## Related Slices

- `KC-0501`
- `KC-0502`
- `KC-0503`
- `KC-0504`
- `KC-0505`
- `KC-0506`
- `KC-0507`
- `KC-0508`
- `KC-0509`
- `KC-0510`
- `KC-0511`
