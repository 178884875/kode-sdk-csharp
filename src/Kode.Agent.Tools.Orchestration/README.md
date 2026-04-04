# Kode.Agent.Tools.Orchestration

[中文文档](./README-zh.md) | English

Orchestration tools for the Kode Agent SDK. This package provides 10 multi-agent coordination patterns — running isolated sub-agents, sequential pipelines, parallel research, self-correcting retries, specialist delegation, context distillation, validate-and-fix loops, fan-out/fan-in, map-reduce, and adversarial debate — without any dependency on application-specific infrastructure.

## Installation

```bash
dotnet add package Kode.Agent.Tools.Orchestration
```

## Directory Structure

```
src/Kode.Agent.Tools.Orchestration/
├── IsolateTask/          # isolate_task — single isolated sub-agent
├── Pipeline/             # pipeline — sequential multi-stage execution
├── ParallelResearch/     # parallel_research — concurrent sub-agents
├── RetryWithReflection/  # retry_with_reflection — failure-aware retry
├── AskSpecialist/        # ask_specialist — specialist persona sub-agent
├── ContextDistill/       # context_distill — large-content summarisation
├── ValidateAndFix/       # validate_and_fix — execute-validate-fix loop
├── FanOutFanIn/          # fan_out_fan_in — parallel + synthesis
├── MapReduce/            # map_reduce — chunk-based parallel + reduce
├── Debate/               # debate — adversarial proponent/opponent/judge
├── Internal/             # SubAgentRunner, InMemoryAgentStore (shared internals)
└── ServiceCollectionExtensions.cs
```

## Tools

### isolate_task

Runs a task in an isolated sub-agent. The sub-agent's entire message history is discarded afterwards — only its final summary is returned. Protects the parent context window from large intermediate results.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Side effects | None |
| Default tools | `fs_read`, `fs_glob`, `fs_grep`, `fs_list`, `bash_run`, `bash_logs` |

```json
{
  "tool": "isolate_task",
  "arguments": {
    "task": "List all TODO comments in the src/ directory and summarise by category.",
    "workDir": "/path/to/project",
    "tools": ["fs_grep", "fs_list"],
    "maxIterations": 15
  }
}
```

---

### pipeline

Executes a sequence of stages where each stage runs in an isolated sub-agent. The summary from each stage is automatically passed as context to the next. Stages share no context window — only the summary flows forward.

| Property | Value |
|----------|-------|
| Read-only | No |
| Side effects | Depends on stage tools |
| Context handoff | Previous stage summary prepended to next stage task |

```json
{
  "tool": "pipeline",
  "arguments": {
    "stages": [
      { "name": "Gather",      "task": "Scan workspace/memory/ and list all memory files.", "tools": ["fs_list", "fs_read"] },
      { "name": "Consolidate", "task": "Based on the gathered file list, identify duplicates.", "tools": ["fs_read"] }
    ],
    "stopOnFailure": true
  }
}
```

**Return value:**
```json
{
  "stages": [
    { "name": "Gather",      "success": true, "summary": "..." },
    { "name": "Consolidate", "success": true, "summary": "..." }
  ],
  "completedStages": 2,
  "totalStages": 2,
  "succeeded": true
}
```

---

### parallel_research

Runs multiple independent research tasks simultaneously, each in an isolated sub-agent. Total time is roughly that of the slowest single task.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Side effects | None |
| Concurrency | Unlimited by default; set `maxConcurrency` to cap |

```json
{
  "tool": "parallel_research",
  "arguments": {
    "tasks": [
      { "name": "Auth module",    "task": "Summarise the authentication flow in src/auth/." },
      { "name": "Storage module", "task": "Summarise the storage layer in src/storage/." }
    ],
    "maxConcurrency": 3,
    "failFast": false
  }
}
```

---

### retry_with_reflection

Runs a task and, on failure, retries with the error and a reflection prompt prepended. The sub-agent is asked to diagnose what went wrong and approach the problem differently.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Default retries | 2 (3 total attempts) |
| Max retries | 5 |

```json
{
  "tool": "retry_with_reflection",
  "arguments": {
    "task": "Find the logging config file and report its current log level.",
    "maxRetries": 2,
    "tools": ["fs_glob", "fs_read", "fs_grep"]
  }
}
```

**Return value (success on second attempt):**
```json
{
  "success": true,
  "summary": "Log level is Warning in appsettings.json.",
  "totalAttempts": 2,
  "attempts": [
    { "attempt": 1, "success": false, "error": "File not found: logging.json" },
    { "attempt": 2, "success": true,  "summary": "Log level is Warning in appsettings.json." }
  ]
}
```

> Even on final failure the tool returns `success: false` with the full attempt log rather than throwing.

---

### ask_specialist

Routes a task to a sub-agent with a custom system prompt describing a specialist persona. The specialist's entire context is discarded after completion — only the answer is returned.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Side effects | None |
| Persona | Injected via `SystemPromptOverride` |

```json
{
  "tool": "ask_specialist",
  "arguments": {
    "task": "Review the authentication module for security vulnerabilities.",
    "specialistRole": "You are a senior application security engineer specialised in OWASP Top 10.",
    "tools": ["fs_read", "fs_grep"]
  }
}
```

**Return value:**
```json
{
  "summary": "Found 2 potential issues: missing rate limiting on /api/login and JWT stored in localStorage.",
  "specialistRole": "You are a senior application security engineer...",
  "stopReason": "end_turn"
}
```

---

### context_distill

Passes large content to a reasoning-only sub-agent (no tools) that compresses it into a focused summary. Useful when tool output or accumulated context would overflow the parent's window.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Sub-agent tools | None (reasoning only) |
| Max output | Configurable via `maxOutputWords` (default 300) |

```json
{
  "tool": "context_distill",
  "arguments": {
    "content": "...10 000 lines of build logs...",
    "focusQuestion": "What are the root causes of the test failures?",
    "maxOutputWords": 200
  }
}
```

**Return value:**
```json
{
  "distillation": "Three tests fail due to missing environment variable DATABASE_URL ...",
  "focusQuestion": "What are the root causes of the test failures?"
}
```

---

### validate_and_fix

Runs a task, then uses a separate validator sub-agent to evaluate the output against structured criteria. If validation fails the executor is asked to fix and the loop repeats up to `maxFixRounds` times.

| Property | Value |
|----------|-------|
| Read-only | No |
| Validator | Reasoning-only sub-agent, no tools |
| Always returns | `ToolResult.Ok` (never `Fail`); check `passed` field |

```json
{
  "tool": "validate_and_fix",
  "arguments": {
    "task": "Generate a JSON config file for the logging system.",
    "validationCriteria": "Must include 'level', 'sinks', and 'format' keys. Level must be one of: debug/info/warn/error.",
    "maxFixRounds": 2,
    "tools": ["fs_write", "fs_read"]
  }
}
```

**Return value:**
```json
{
  "passed": true,
  "output": "Created logging.json with level=info, sinks=[console], format=json.",
  "rounds": 1
}
```

---

### fan_out_fan_in

Runs multiple research tasks in parallel (fan-out), then synthesises all results with a dedicated sub-agent (fan-in). The synthesis agent receives all task summaries as context.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Side effects | None |
| Synthesis | Separate sub-agent with its own tool list |

```json
{
  "tool": "fan_out_fan_in",
  "arguments": {
    "tasks": [
      { "name": "Frontend", "task": "Summarise the React component architecture." },
      { "name": "Backend",  "task": "Summarise the API layer and middleware." }
    ],
    "synthesisTask": "Based on the frontend and backend summaries, identify integration pain points.",
    "maxConcurrency": 2
  }
}
```

**Return value:**
```json
{
  "synthesis": "The main integration pain points are ...",
  "fanOut": [
    { "name": "Frontend", "success": true,  "summary": "..." },
    { "name": "Backend",  "success": true,  "summary": "..." }
  ],
  "succeeded": 2
}
```

---

### map_reduce

Splits a large item list into chunks, processes each chunk in parallel with a map sub-agent, then passes all results to a reduce sub-agent for final synthesis.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Placeholder | Use `{item}` in `mapTask` — replaced with each chunk |
| Concurrency | Unlimited by default |

```json
{
  "tool": "map_reduce",
  "arguments": {
    "items": ["file1.md", "file2.md", "file3.md", "file4.md", "file5.md"],
    "mapTask": "Read {item} and extract its key topics.",
    "reduceTask": "Merge all extracted topics into a single de-duplicated topic list.",
    "chunkSize": 2,
    "tools": ["fs_read"]
  }
}
```

**Return value:**
```json
{
  "reduction": "Unified topic list: auth, storage, MCP, channels, automation.",
  "totalItems": 5,
  "totalChunks": 3,
  "mapResults": [
    { "chunk": 1, "success": true, "summary": "Topics: auth, storage." },
    { "chunk": 2, "success": true, "summary": "Topics: MCP, channels." },
    { "chunk": 3, "success": true, "summary": "Topics: automation." }
  ],
  "succeeded": 3
}
```

---

### debate

Runs two adversarial debater sub-agents (proponent and opponent) for one or more rounds, then asks a judge sub-agent to evaluate the arguments and deliver a verdict.

| Property | Value |
|----------|-------|
| Read-only | Yes |
| Sub-agent tools | None (reasoning only) |
| Default rounds | 1 |
| Max rounds | 3 |

```json
{
  "tool": "debate",
  "arguments": {
    "topic": "Should we migrate from REST to GraphQL for the public API?",
    "contextInfo": "Current API has ~200 endpoints, mobile clients are the primary consumers.",
    "rounds": 2
  }
}
```

**Return value:**
```json
{
  "topic": "Should we migrate from REST to GraphQL for the public API?",
  "verdict": "Migration is premature. REST with OpenAPI spec and a BFF pattern addresses client needs at lower risk ...",
  "rounds": [
    {
      "round": 1,
      "proponent": "GraphQL reduces over-fetching, critical for mobile bandwidth ...",
      "opponent": "Migration cost is high, REST caching is mature ..."
    }
  ],
  "succeeded": true
}
```

---

## Usage

### Register All Orchestration Tools

```csharp
using Kode.Agent.Tools.Orchestration;

toolRegistry.RegisterOrchestrationTools(
    modelProvider,
    modelId,
    sandboxFactory,
    loggerFactory); // optional
```

### With Dependency Injection (e.g. KodaClaw)

```csharp
services.AddKodaClawRuntime(options =>
{
    options.DefaultModel = "gpt-4o-mini";
    // Orchestration tools are registered automatically when ISandboxFactory is available
});
```

### Tool Whitelist

All orchestration tools spawn sub-agents that are restricted to a hard whitelist of safe tools, regardless of what the caller requests:

```
fs_read  fs_glob  fs_grep  fs_list
bash_run  bash_logs  bash_kill
todo_read
```

Write tools (`fs_write`, `fs_edit`, `fs_rm`) and channel tools (`channel_send`) are **never** granted to sub-agents.

> Exception: `validate_and_fix` and `pipeline` may grant write tools if you explicitly configure them and your use case requires it — but they are still excluded from validator/judge sub-agents.

## Design Patterns

Each tool maps to a pattern from [Anthropic's "Building effective agents"](https://www.anthropic.com/research/building-effective-agents):

| Tool | Pattern |
|------|---------|
| `isolate_task` | Orchestrator-Worker |
| `pipeline` | Pipeline |
| `parallel_research` | Parallelization |
| `retry_with_reflection` | Reflection |
| `ask_specialist` | Specialist Delegation |
| `context_distill` | Context Distillation |
| `validate_and_fix` | Evaluate-and-Fix Loop |
| `fan_out_fan_in` | Fan-Out / Fan-In |
| `map_reduce` | Map-Reduce |
| `debate` | Adversarial Debate |

## License

Part of the Kode Agent SDK project. MIT License.
