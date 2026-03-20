# ADR-0010: Desktop Shell v1 freezes on Electron, runtime config bridge, and Gateway attach-or-launch

## Status

- `Accepted`

## Date

- 2026-03-19

## Implementation Status

- Iteration 6 Wave 0 planning freeze `Completed`
- Iteration 6 Wave 1 shell foundation `Completed`
- Iteration 6 Wave 2 gateway bridge + shell lifecycle `Completed`
- Iteration 6 Wave 3 desktop operator surfaces `Completed`
- Iteration 6 Wave 4 acceptance `Completed`
- `KC-0601` `Completed`
- `KC-0602` `Completed`
- `KC-0603` `Completed`
- `KC-0604` `Completed`
- `KC-0605` `Completed`
- `KC-0606` `Completed`
- `KC-0607` `Completed`
- `KC-0608` `Completed`

## Background

After Iteration 5 closed, KodaClaw already had a mature local Gateway, a broad web console, and validated product surfaces for approvals, channels, plugins, automations, canvas, and diagnostics.

What it still lacked was a real desktop shell boundary.

Before implementation, the main thread needed to freeze five questions:

- whether the first desktop shell should use Electron, Tauri, or another host;
- whether desktop should embed product logic directly or stay a shell around the existing Gateway + web app;
- how the renderer obtains `gatewayUrl` and `gatewayToken` at runtime instead of through build-time Vite env only;
- whether desktop notifications should invent a new subsystem or reuse the current Control Plane surfaces;
- how launch targets and deep-link behavior should work without forcing a full router rewrite first.

Without freezing those decisions first, implementation would likely diverge across window management, auth handoff, and renderer integration.

## Decision

We decided that:

- Iteration 6 v1 uses `Electron + TypeScript` as the first desktop shell.
- the desktop application remains a shell. It does not embed SDK runtime logic, approval logic, or product state ownership; those continue to live in `KodaClaw.Gateway` and the existing product modules.
- the renderer reuses the current `kodaclaw-web` application instead of introducing a separate desktop-only UI stack.
- desktop supports two Gateway lifecycle modes: `AttachOnly` and `ManagedChild`. It may attach to a local Gateway or launch and supervise one, but it never reimplements Gateway behavior.
- desktop auth reuses the current Bearer token model. No separate desktop authentication protocol is introduced in this iteration.
- desktop-to-renderer integration is frozen around a preload-driven runtime config bridge that provides `gatewayUrl`, `gatewayToken`, platform metadata, and the initial launch target.
- notifications reuse the existing Control Plane surfaces (`Inbox`, `Approvals`, `Settings`, quiet hours) rather than creating a desktop-only notification backend.
- the first accepted deep-link model is desktop-internal launch targeting to desks such as `chat`, `inbox`, `canvas`, and `channels`; custom OS protocol registration may exist, but it is not the only acceptable path.
- the first strongly validated packaging path is macOS. Cross-platform friendliness is encouraged, but macOS remains the acceptance anchor for v1.

## Alternatives Considered

### Option A: Build the first shell with Tauri

- Description: use Tauri instead of Electron for the first desktop iteration.
- Pros: smaller runtime footprint and stronger Rust-hosted security defaults.
- Cons: higher integration uncertainty with the existing web and Node-centric tooling, slower first delivery, and more friction for gateway process orchestration in the current codebase context.

### Option B: Build a separate desktop-only frontend

- Description: create a second UI stack optimized specifically for desktop instead of reusing `kodaclaw-web`.
- Pros: complete freedom for native-feeling interaction design.
- Cons: duplicates product UI logic, doubles maintenance cost, and delays the first runnable desktop shell.

### Option C: Let desktop embed product logic directly

- Description: move more business logic into the Electron main process and reduce Gateway to a thinner transport.
- Pros: potentially fewer HTTP round-trips inside the local machine.
- Cons: breaks the architecture principle that Gateway remains the product brain, increases coupling, and weakens inspectability and test reuse.

### Option D: Create a dedicated desktop notification system

- Description: add a new notification store and dedicated desktop event model instead of reusing Inbox / Approvals / Settings.
- Pros: local ownership inside the desktop shell.
- Cons: duplicates user-facing state, fragments auditability, and creates inconsistency with browser-driven product behavior.

## Consequences

- Positive: the first desktop shell is small enough to deliver without disturbing the existing product architecture.
- Positive: the validated web desks can move into a packaged desktop experience quickly.
- Positive: the existing Gateway, Control Plane, and settings model stay authoritative.
- Positive: runtime config injection solves the current mismatch between packaged desktop deployment and Vite build-time configuration.
- Cost: Electron is accepted as the first host even though future shells could use other technologies.
- Cost: system protocol registration, auto-update, Keychain, and richer native integration are intentionally not the first acceptance focus.
- Cost: Gateway packaging remains lighter-weight in the first iteration than a fully self-contained desktop distribution.
- Constraint on later slices: future native features must preserve the shell/brain separation frozen here.

## Validation

Planning freeze acceptance in this wave is document-driven:

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_6_FREEZE.md` is the canonical Iteration 6 boundary.
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`, `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`, `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`, and `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md` must stay synchronized with this ADR before implementation starts.
- no Iteration 6 task should move from `Ready` to `Completed` until its slice verification and solution-level regression both pass.

Execution acceptance for this ADR is expected to include:

- desktop packaging smoke for at least one platform
- shell bridge contract coverage for runtime config and launch targets
- Gateway attach/start smoke
- manual tray / notification / deep-link dogfood drill
- full product regression after shell integration

Current execution evidence already in hand:

- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:managed-gateway`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run typecheck && npm run build && npm run package:smoke`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/desktop-config.spec.ts src/__tests__/app-shell.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`

## Related Documents

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_6_FREEZE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop/README.md`

## Related Slices

- `KC-0601`
- `KC-0602`
- `KC-0603`
- `KC-0604`
- `KC-0605`
- `KC-0606`
- `KC-0607`
- `KC-0608`
