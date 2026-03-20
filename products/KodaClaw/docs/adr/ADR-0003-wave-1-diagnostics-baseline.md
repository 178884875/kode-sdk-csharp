# ADR-0003: Wave 1 Diagnostics Baseline

## Status

- `Accepted`

## Date

- 2026-03-18

## Implementation Status

- `KC-0111` `Completed`
- Gateway and Runtime now share a correlation-id contract and a minimal diagnostics query path
- Solution-level test verification and a real failure drill have both passed

## Context

By the end of Wave 1, KodaClaw can initialize a workspace, complete bootstrap, stream chat, and resume the main session. The remaining risk is operability: when chat is misconfigured, bootstrap fails, or main-session resume falls back to a new store, there is no common way to trace what happened across Gateway, Runtime, and ControlPlane.

Wave 1 does not need a full diagnostics UI or durable timeline storage yet, but it does need a stable minimum contract so developers and future product surfaces can answer:

- which request triggered the failure
- which module emitted the record
- whether bootstrap, chat, or main-session resume succeeded or failed

## Decision

We freeze the following Wave 1 diagnostics baseline:

- Gateway generates or forwards `X-KodaClaw-Correlation-Id` for every request and echoes it on the response.
- `DiagnosticEvent` is the minimum structured diagnostics record for Wave 1. It carries `source`, `eventType`, `level`, `message`, `timestamp`, `correlationId`, optional `sessionId`, and optional `attributes`.
- `DiagnosticsQueryResponse` is the minimum query contract returned by the Gateway.
- `IDiagnosticsService` is the shared write/query abstraction; Wave 1 uses an in-memory implementation in `KodaClaw.ControlPlane`.
- `ICorrelationContextAccessor` is the shared ambient context used by Gateway and Runtime to stamp the same correlation id onto downstream lifecycle records.
- Gateway exposes `GET /api/diagnostics/recent` as the minimum diagnostics query entry point, protected by the same bearer token as other product APIs.
- Gateway records authorization failures plus bootstrap/chat lifecycle events.
- Runtime records `main_session.created`, `main_session.resumed`, and `main_session.resume_fallback` so the diagnostics stream covers the Wave 1 resume path.

This decision is intentionally minimal. Wave 1 does not add durable storage, log export bundles, or a diagnostics UI.

## Alternatives Considered

### Option A: rely only on `ILogger`

- Pros: less product code, easy to print to console
- Cons: hard to query in tests and impossible to expose through a stable product API

### Option B: wait for the full timeline/diagnostics system in Iteration 2

- Pros: fewer temporary abstractions
- Cons: leaves Wave 1 failures opaque and makes E2E debugging slower right before dogfood

### Option C: expose raw SDK event-bus envelopes directly

- Pros: closer to the runtime internals
- Cons: leaks low-level details and does not cover bootstrap/auth failures consistently

## Consequences

Positive:

- Gateway, Runtime, and future Web/Desktop consumers now share the same minimum tracing contract.
- Failures can be grouped by correlation id without waiting for the full diagnostics timeline system.
- Resume fallback is no longer silent; the reason and replacement session are queryable.

Costs:

- Wave 1 diagnostics are in-memory only and reset on process restart.
- We maintain a thin diagnostics contract layer that later durable storage must continue to honor.

Risks:

- The event taxonomy is intentionally small and will likely grow in later iterations.
- Without persistence, long-running or post-crash analysis still requires future work.

## Verification

- `L0`: `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `L2`: Gateway integration tests for diagnostics auth, correlation-id propagation, and query responses
- `L2`: Runtime integration tests for `main_session.created`, `main_session.resumed`, and `main_session.resume_fallback`
- `L5`: real Gateway failure drill where an unconfigured chat request emits SSE `error` and `/api/diagnostics/recent` returns the matching record by correlation id

## Related Documents

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_1_SLICES.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/MASTER_TASK_BREAKDOWN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ENGINEERING_PLAYBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0001-wave-1-bootstrap-contracts.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0002-chat-stream-and-bootstrap-contracts.md`

## Related Slice / PR

- Capability Slice: `KC-0111` `Completed`
- Verification: `dotnet test` + diagnostics Gateway integration + Runtime lifecycle diagnostics + real failure drill passed
