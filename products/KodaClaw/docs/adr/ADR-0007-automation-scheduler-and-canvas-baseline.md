# ADR-0007: Automation scheduler, Inbox delivery, and Canvas metadata reuse the shared control-plane baseline

## Status

- `Accepted`

## Date

- 2026-03-18

## Implementation Status

- `KC-0302` `Completed`
- `KC-0305` `Completed`
- `KC-0306` `Completed`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "HeartbeatAutomationCompilerContractTests|AutomationContractsTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj --filter "FullyQualifiedName~KodaClaw.UnitTests.Automation|FullyQualifiedName~SqliteCanvasArtifactRepositoryTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter FullyQualifiedName~AutomationSchedulerIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`

## Background

Iteration 3 second backend wave needed three decisions frozen together before implementation:

- durable scheduling had to be testable and default-safe inside the product host;
- automation results had to surface through the existing Inbox domain instead of inventing a parallel notification path;
- Canvas artifacts needed a metadata index now, but the actual Canvas rendering/API flow would come later.

The main thread also froze two safety constraints:

- KodaClaw must not pretend it can resume stale automation runs after process loss;
- Canvas metadata must stay workspace-relative and must not allow absolute paths or path traversal.

## Decision

We decided that:

- `KodaClaw.Automation` owns the minimal durable scheduler baseline through `IAutomationScheduler`, `AutomationSchedulerOptions`, `IAutomationClock`, `SystemAutomationClock`, and `FakeAutomationClock`.
- the background hosted scheduler is registered by default but starts as a no-op because `AutomationSchedulerOptions.Enabled` defaults to `false`; manual verification uses `RunOnceAsync()` / `TickAsync()`.
- stale `Queued` / `Running` history records are marked `Failed` on the next scheduler cycle, and the owning definition is updated with failure state plus `NextRunAt = now + FailureRetryDelay`; KodaClaw does not fake an in-process resume.
- every completed automation run upserts an `InboxItemKind.AutomationResult` record with stable id `automation-result-<runId>`, source `automation.scheduler`, route `/automations/<automationId>`, and payload JSON containing `automationId`, `runId`, `status`, `summary`, and `errorMessage`.
- `CanvasArtifactKind`, `CanvasArtifact`, `CanvasArtifactQuery`, and `ICanvasArtifactRepository` live in `KodaClaw.Contracts`, while `SqliteCanvasArtifactRepository` lives in `KodaClaw.Storage`.
- Canvas metadata reuses the same workspace-local `config/control-plane.db` file and stores artifact rows in a dedicated `canvas_artifacts` table.
- `EntryPath` and `AssetDirectory` must both be workspace-relative and remain under `workspace/canvas`; absolute paths and traversal segments are rejected during repository validation.

## Alternatives Considered

### Option A: Let the SDK scheduler own persistence and recovery

- Description: reuse the SDK scheduling primitives directly and try to restore product runs from SDK internals.
- Pros: less product code in the short term.
- Cons: the SDK does not expose a true persisted resume contract for KodaClaw automation runs, so the product would over-promise crash recovery behavior and make tests brittle.

### Option B: Store automation notifications and Canvas metadata in separate product databases

- Description: open one extra SQLite file for automation notifications and another for Canvas metadata.
- Pros: clearer local ownership per domain.
- Cons: more workspace state fragmentation, worse backup/debug ergonomics, and more cross-domain wiring when the Gateway and Web layers need combined views later.

### Option C: Allow absolute Canvas paths and normalize them lazily

- Description: persist whatever file path callers provide and only sanitize during later API/render time.
- Pros: fastest initial implementation.
- Cons: unsafe path semantics, higher traversal risk, and inconsistent behavior between repositories, Gateway APIs, and future Canvas rendering.

## Consequences

- Positive: scheduler behavior is deterministic in tests because time comes from `IAutomationClock`, not wall-clock calls spread across the implementation.
- Positive: Gateway composition stays safe by default because the hosted scheduler is present but disabled unless explicitly enabled.
- Positive: automation results now flow into the same Inbox surface as approvals and future alerts, which keeps control-plane UX coherent.
- Positive: Canvas gets a stable metadata contract and index early without blocking on the later rendering/API slices.
- Cost: stale automation runs are always failed and retried later; there is no attempt to continue partially executed work after process loss.
- Cost: Canvas callers must adopt workspace-relative paths immediately; existing absolute-path shortcuts are intentionally rejected.
- Constraint on later slices: `KC-0307` must build APIs on top of `ICanvasArtifactRepository`, and `KC-0308` / `KC-0309` should reuse the existing Inbox/automation route conventions rather than redefine them.

## Validation

- L2: `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "HeartbeatAutomationCompilerContractTests|AutomationContractsTests"`
- L2: `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj --filter "FullyQualifiedName~KodaClaw.UnitTests.Automation|FullyQualifiedName~SqliteCanvasArtifactRepositoryTests"`
- L2: `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter FullyQualifiedName~AutomationSchedulerIntegrationTests`
- L3: `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- L4: inspect a real workspace `config/control-plane.db` and confirm `automation_definitions`, `automation_runs`, and `canvas_artifacts` coexist while automation runs also materialize Inbox results.

## Related Documents

- `docs/ARCHITECTURE.md`
- `docs/WORKSPACE_SPEC.md`
- `docs/IMPLEMENTATION_STATUS.md`
- `docs/PARALLEL_EXECUTION_PLAN.md`
- `docs/adr/ADR-0006-automation-core-baseline.md`

## Related Slices

- `KC-0302`
- `KC-0305`
- `KC-0306`
