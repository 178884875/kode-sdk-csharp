# ADR-0002: Wave 1 Chat Stream and Bootstrap Contracts

## Status

- `Accepted`

## Date

- 2026-03-18

## Implementation Status

- `KC-0106` `Completed`
- `KC-0107` `Completed`
- `KC-0108` has validated the frozen SSE contract from the Web shell consumer side
- `KC-0109` has validated bootstrap completion from the Gateway/Web consumer side
- `KC-0110` has validated that the SSE contract stays stable across main-session resume fallback
- `KC-0111` has validated correlation-id propagation and diagnostics records around the frozen SSE/bootstrap flows
- SSE chat path and bootstrap completion path have both passed solution-level validation

## Context

Wave 1 now has a working Workspace initializer, Gateway auth/bootstrap-state endpoint, and main-session orchestration. The next critical path is:

- `KC-0106` chat streaming contract and API
- `KC-0107` bootstrap completion flow

Both tasks cross module boundaries:

- `KodaClaw.Contracts`
- `KodaClaw.Runtime`
- `KodaClaw.Gateway`
- `KodaClaw.Workspace`

If we let each implementation thread invent its own chat payloads or bootstrap write format, integration will get slower and riskier. We need a stable contract before subagents implement the next wave.

## Decision

We freeze the following Wave 1 contracts:

- `ChatStreamRequest` contains a required `message` and an optional `sessionId` for forward compatibility.
- `ChatStreamEvent` is the wire payload for SSE data frames. It supports three event types in Wave 1: `text_chunk`, `done`, and `error`.
- `ChatStreamEvent` always carries `sessionId`; it may also carry `step`, `sequence`, and `timestamp` so Web/Desktop shells can build a minimal replayable timeline.
- Errors reuse the existing `ErrorResponse` contract instead of inventing a second error shape.
- `IChatSessionService` is the Runtime-facing abstraction that Gateway consumes. Gateway should stream SSE from this abstraction instead of directly driving Agent internals.
- Bootstrap completion is modeled as an explicit product action through `IBootstrapService`.
- `BootstrapCompletionRequest` carries already-rendered markdown for `IDENTITY.md` and `USER.md`. Wave 1 does not attempt to standardize a richer structured profile schema yet.
- Gateway exposes bootstrap completion through `POST /api/system/bootstrap-complete`, reusing `BootstrapCompletionRequest` and `BootstrapCompletionResult`.
- `IBootstrapService.CompleteAsync()` is responsible for writing those files, marking bootstrap as completed in workspace app config, and archiving or deleting `BOOTSTRAP.md` when requested.

This contract is intentionally minimal and stable enough for Wave 1.

## Alternatives Considered

### Option A: Let Gateway define its own chat DTOs

- Pros: faster local implementation for one module
- Cons: duplicates Runtime semantics and makes later Web/Desktop reuse brittle

### Option B: Make bootstrap completion write a structured JSON profile now

- Pros: future parsing could be easier
- Cons: adds early schema complexity before we know the final onboarding UX

### Option C: Expose raw Agent event envelopes directly to SSE clients

- Pros: closest to SDK internals
- Cons: leaks low-level runtime details and makes product wire contracts harder to evolve safely

## Consequences

Positive:

- Runtime and Gateway can be implemented in parallel behind a stable chat abstraction.
- Bootstrap completion has a single product entry point instead of ad hoc file writes.
- Web/Desktop clients can rely on a small, replayable event shape.

Costs:

- We maintain a thin product contract layer on top of SDK events.
- Bootstrap completion still uses markdown payloads, so future structured profile work may add another translation layer.

Risks:

- `ChatStreamEvent` may need additional fields later for tool traces or approval events.
- Bootstrap markdown payloads assume upstream summarization/extraction is correct.

## Verification

- `L0`: `dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln`
- `L2`: Runtime integration tests for chat session streaming and bootstrap completion
- `L2`: Gateway integration tests for SSE and auth behavior
- `L3`: contract/replay tests for SSE sequence and bootstrap file outputs

## Related Documents

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ENGINEERING_PLAYBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_1_SLICES.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/WORKSPACE_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0001-wave-1-bootstrap-contracts.md`

## Related Slice / PR

- Capability Slice: `KC-0106`, `KC-0107` `Completed`
- Consumer Validation: `KC-0108` Web shell passed `npm run build` + `npm run test` + `npm run test:e2e` against the SSE adapter
- Consumer Validation: `KC-0109` bootstrap flow passed Gateway integration tests + real bootstrap completion smoke + Playwright onboarding flow
- Verification: `dotnet build` + `dotnet test` + Gateway `/api/chat/stream` smoke + runtime chat resume-fallback smoke + diagnostics failure drill passed
