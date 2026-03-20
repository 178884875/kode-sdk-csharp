# ADR-0008: Plugin Platform v1 freezes on MCP-first tool plugins, stdio transport, and control-plane reuse

## Status

- `Accepted`

## Date

- 2026-03-18

## Implementation Status

- Iteration 4 Wave 0 planning freeze `Completed`
- Iteration 4 Wave 1 foundation `Completed`
- Iteration 4 Wave 2 host runtime `Completed`
- Iteration 4 Wave 3 gateway/runtime/web `Completed`
- Iteration 4 Wave 4 acceptance `Completed`
- `KC-0401` `Completed`
- `KC-0402` `Completed`
- `KC-0403` `Completed`
- `KC-0404` `Completed`
- `KC-0405` `Completed`
- `KC-0406` `Completed`
- `KC-0407` `Completed`
- `KC-0408` `Completed`

## Background

After Iteration 3 closed, KodaClaw had enough product baseline to start the plugin platform, but not enough slack to let multiple workers guess the boundary independently.

The main thread needed to freeze four things before parallel implementation:

- which plugin scope is truly in Iteration 4 and which parts are deferred;
- whether the first runnable path is MCP `stdio`, HTTP variants, or both;
- whether plugin authorization/logging/health should reuse the existing Control Plane baseline or invent new product surfaces;
- how plugin lifecycle state should be modeled so that storage, APIs, Runtime injection, and Web UI do not each reinterpret it differently.

Without freezing these decisions first, subagents would likely diverge on manifest shape, runtime state semantics, and approval behavior.

## Decision

We decided that:

- Iteration 4 v1 fully executes only `tool` plugins end-to-end. `channel`, `memory`, and `ui` plugin types remain valid manifest values but are limited to validation and display in this iteration.
- KodaClaw keeps the `MCP-first` plugin strategy and treats `stdio` as the only required transport for Iteration 4 acceptance. `http`, `streamableHttp`, and `sse` may exist in type definitions but are not part of the first acceptance path.
- plugin lifecycle is represented as two layers instead of one overloaded enum: persisted state (`Installed`, `TrustState`, `Enabled`) and runtime state (`Stopped`, `Starting`, `Running`, `Degraded`) plus operational fields such as `RestartCount` and `LastError`.
- plugin authorization reuses the existing Control Plane baseline. First trust operations flow through `ApprovalKind.PluginAuthorization` and `InboxItemKind.PluginRequest`; KodaClaw does not introduce a separate approval mechanism for plugins.
- Runtime injects plugin tools only when the plugin is manifest-valid, registered, `Trusted`, `Enabled`, and `Running`.
- injected tools must stay namespaced with the SDK MCP convention `mcp__<pluginId>__<toolName>` so that provenance is preserved in diagnostics, tool metadata, and UI.
- plugin discovery/install sources are limited to bundled plugins and explicit local directories. No remote registry, signing, hot upgrade, or rollback belongs to Iteration 4.
- plugin settings remain a placeholder/config summary surface in Iteration 4; arbitrary UI panel execution is postponed.

## Alternatives Considered

### Option A: Execute all plugin types in Iteration 4

- Description: treat `tool`, `channel`, `memory`, and `ui` plugins as equally in-scope for the first plugin platform slice.
- Pros: broader feature headline and fewer explicit deferrals.
- Cons: it would collapse Iteration 4 into parts of Iteration 5 and later UI sandbox work, making the first plugin platform too large to verify safely.

### Option B: Support both `stdio` and HTTP-based MCP transports as first-class acceptance paths

- Description: require the first PluginHost implementation to run subprocess plugins and HTTP/SSE plugins with the same completeness.
- Pros: broader interoperability on day one.
- Cons: more host complexity, more health/reconnect edge cases, and less confidence that the first acceptance path is stable.

### Option C: Model lifecycle as one large status enum

- Description: persist `Discovered -> Installed -> Trusted -> Enabled -> Running -> Degraded -> Stopped -> Disabled` exactly as a single state field.
- Pros: visually mirrors the conceptual state diagram in the spec.
- Cons: creates ambiguity between persisted state and ephemeral runtime state, complicates storage, API updates, and Web rendering.

### Option D: Build a plugin-specific approval/logging system

- Description: add standalone plugin approval tables, plugin inbox, and plugin diagnostics outside the existing Control Plane.
- Pros: local ownership inside the plugin subsystem.
- Cons: duplicates product surfaces that already exist, fragments UX, and increases integration cost.

## Consequences

- Positive: Iteration 4 becomes small enough to implement safely in parallel without watering down the long-term plugin direction.
- Positive: the existing SDK MCP client stack can be reused directly, reducing host implementation risk.
- Positive: the existing Control Plane baseline remains the single user-facing place for authorization and audit surfaces.
- Positive: Runtime injection semantics are explicit, which reduces the risk of leaking stale or degraded plugin tools into new sessions.
- Cost: non-tool plugin categories are intentionally deferred as runnable features even though the manifest accepts them.
- Cost: HTTP-based MCP plugins are not part of the first acceptance pack.
- Cost: the Web settings experience remains intentionally shallow until a future UI/plugin bridge is designed.
- Constraint on later slices: Iteration 5 Channels must build on the plugin contracts frozen here instead of redefining connector/plugin boundaries.

## Validation

Planning freeze acceptance is document-driven in this wave:

- `docs/ITERATION_4_FREEZE.md` is the canonical implementation boundary for Iteration 4.
- `docs/IMPLEMENTATION_BACKLOG.md`, `docs/PARALLEL_EXECUTION_PLAN.md`, and `docs/IMPLEMENTATION_STATUS.md` must be synchronized with this ADR before implementation starts.
- all Iteration 4 implementation tasks remained `Ready` until their slice tests and solution-level regression passed; Iteration 4 is now fully accepted through Wave 4.

Execution acceptance for the iteration is now satisfied by:

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter "PluginManifestContractsTests|PluginPermissionContractsTests"`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter PluginApiContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj --filter PluginRegistryRepositoryTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "PluginLifecycleHostIntegrationTests|PluginHealthIntegrationTests|PluginToolInjectionIntegrationTests|Iteration4AcceptanceIntegrationTests"`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test && npx playwright test tests/kc0407-plugins.spec.ts && npm run test:e2e`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`

## Related Documents

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_4_FREEZE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_4_ACCEPTANCE_PACK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PLUGIN_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PARALLEL_EXECUTION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`

## Related Slices

- `KC-0401`
- `KC-0402`
- `KC-0403`
- `KC-0404`
- `KC-0405`
- `KC-0406`
- `KC-0407`
- `KC-0408`
