# OpenTelemetry 集成实施计划

> **目标**：SDK 层用标准 .NET Diagnostics（ActivitySource + Meter + ILogger）采集信号，
> 开放接口给宿主（KodaClaw）自行接入 OTel exporter，实现 Traces / Metrics / Logs 三件套全覆盖，
> 为后续对接 Jaeger、Grafana、Datadog 等前端观测平台做好准备。

---

## 总体架构

```
┌─────────────────────────────────────────────────────────────────┐
│  Kode.Agent.Sdk（标准 .NET Diagnostics，无 OTel 包依赖）         │
│                                                                  │
│  ActivitySource("Kode.Agent")   ← Traces                        │
│  Meter("Kode.Agent")            ← Metrics                        │
│  ILogger<T>                     ← Logs（结构化）                  │
│                                                                  │
│  子代理（SubAgentRunner）透传 TraceContext → 跨 Agent 链路        │
└───────────────────────────┬─────────────────────────────────────┘
                            │  标准 .NET Diagnostics API
┌───────────────────────────▼─────────────────────────────────────┐
│  KodaClaw.Gateway（OTel 宿主，接 exporter）                       │
│                                                                  │
│  OpenTelemetry.Extensions.Hosting   ← 统一注册                   │
│  Instrumentation.AspNetCore         ← HTTP 请求追踪              │
│  Instrumentation.Http               ← HttpClient 追踪            │
│  Exporter.OpenTelemetryProtocol     ← OTLP（生产）               │
│  Exporter.Console                   ← Console（本地开发）         │
│  Serilog → OTel Logs bridge         ← 日志统一导出               │
└─────────────────────────────────────────────────────────────────┘
```

---

## 现有埋点覆盖矩阵

> 实施前的基线状态，用于对比验收。

| 关键路径 | Activity | Metrics | Logs | 覆盖度 |
|---------|----------|---------|------|--------|
| Run 循环 | ✅ `agent.run` | ✅ | ⚠️ 少 | 80% |
| Step 执行 | ✅ `agent.step` | ✅ | ✅ | 90% |
| 模型请求 | ✅ `agent.model_request` | ✅ | ❌ | 70% |
| **Provider HTTP 层** | ❌ | ❌ | ❌ | **0%** |
| **TTFT（首 token 延迟）** | ❌ | ❌ | ❌ | **0%** |
| 工具执行 | ✅ `agent.tool.execute` | ✅ | ❌ | 75% |
| **工具审批等待** | ❌ | ❌ | ❌ | **0%** |
| Context 压缩 | ❌ | ✅ 仅计数 | ✅ | 50% |
| **子代理 TraceContext** | ❌ | ❌ | ❌ | **0%** |
| Middleware/Hook | ❌ | ❌ | ❌ | 0% |
| Token 成本分摊维度 | ⚠️ 仅 model | ⚠️ 仅 model | — | 30% |

---

## 实施阶段

### Phase 0 — 包基础 `[待做]`

**目标**：在中央包管理中登记 OTel 包，不引入运行时依赖到 SDK。

**变更文件**：`Directory.Packages.props`

新增包版本声明（仅版本声明，不在 SDK csproj 中引用）：

```xml
<!-- OpenTelemetry — 仅宿主使用 -->
<PackageVersion Include="OpenTelemetry"                                    Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting"                 Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore"         Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http"               Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol"     Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Exporter.Console"                   Version="1.12.0" />
<!-- Serilog OTel 桥 -->
<PackageVersion Include="Serilog.Sinks.OpenTelemetry"                      Version="4.0.0" />
```

**KodaClaw.Gateway.csproj** 新增引用：
```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
<PackageReference Include="OpenTelemetry.Exporter.Console" />
<PackageReference Include="Serilog.Sinks.OpenTelemetry" />
```

**验收**：`dotnet build` 0 错误 0 警告。

---

### Phase 1 — Traces 增强 `[待做]`

#### 1-A：Provider 层 HTTP span + TTFT `[P0]`

**目标**：在 AnthropicProvider 和 OpenAIProvider 的 `StreamAsync` 外层各加一个子 span，
记录 HTTP 调用耗时和首 token 延迟。

**变更文件**：
- `src/Kode.Agent.Sdk/Infrastructure/Providers/AnthropicProvider.cs`
- `src/Kode.Agent.Sdk/Infrastructure/Providers/OpenAIProvider.cs`

**新增 span 结构**：

```
agent.model_request
  └── provider.stream  ← 新增（包含整个 HTTP 流）
        tag: provider = "anthropic" | "openai"
        tag: model
        tag: http.ttft_ms    ← 首 token 延迟（ms）
        tag: http.stream_ms  ← 流完成总耗时（ms）
        tag: http.retry_count
```

**TTFT 采集逻辑**（伪代码）：

```csharp
var ttftStopwatch = Stopwatch.StartNew();
bool firstTokenSeen = false;

await foreach (var chunk in _client.Messages.CreateStreaming(...))
{
    if (!firstTokenSeen && chunk is TextDelta)
    {
        ttftStopwatch.Stop();
        providerActivity?.SetTag("http.ttft_ms", ttftStopwatch.ElapsedMilliseconds);
        KodeAgentMetrics.ModelTtft.Record(ttftStopwatch.ElapsedMilliseconds, modelTag);
        firstTokenSeen = true;
    }
    yield return MapChunk(chunk);
}
```

**重试计数**：在现有重试循环（Agent.cs L1646-1772）增加计数 tag：
```
agent.model_request tag: retry_count = N
```

#### 1-B：子代理 TraceContext 传播 `[P1]`

**目标**：orchestration 工具 spawn 子 Agent 时，子 Agent 的 `agent.run` span 作为父 span 的子 span，
在 Jaeger 中形成完整调用树。

**变更文件**：
- `src/Kode.Agent.Tools.Orchestration/Internal/SubAgentRunner.cs`
- `src/Kode.Agent.Sdk/Core/Agent/Agent.cs`（RunAsync 入口接受 parent context）

**方案**：

```csharp
// SubAgentRequest 新增字段
public ActivityContext? ParentActivityContext { get; init; }

// SubAgentRunner.RunAsync 调用处
var request = new SubAgentRequest
{
    // ...
    ParentActivityContext = Activity.Current?.Context,  // 捕获调用时的 context
};

// Agent 内部：创建 agent.run span 时链接父 context
using var runActivity = KodeAgentActivitySource.Source.StartActivity(
    "agent.run",
    ActivityKind.Internal,
    parentContext: request.ParentActivityContext ?? default);
```

**span 结构效果**：
```
isolate_task (tool span)
  └── agent.run (子 Agent)
        └── agent.step
              ├── agent.model_request
              └── agent.tool.execute
```

#### 1-C：工具审批等待独立 span `[P2]`

**目标**：将"等待人工审批"的时间从 `agent.tool.execute` span 中独立出来。

**变更文件**：`src/Kode.Agent.Sdk/Core/Agent/Agent.cs`（ExecuteToolAsync）

**新增 span 结构**：

```
agent.tool.execute
  ├── agent.tool.approval_wait  ← 新增（仅当需要审批时）
  │     tag: tool.name
  │     tag: approval.result = "approved" | "denied"
  └── （工具实际执行，在原 span 内）
```

#### 1-D：Context 压缩 span `[P2]`

**目标**：压缩操作本身是一次 LLM 调用，应有独立 span。

**变更文件**：`src/Kode.Agent.Sdk/Core/Context/ContextManager.cs`

```
agent.step
  └── agent.context_compress  ← 新增
        tag: messages.removed
        tag: messages.retained
        tag: compression_ratio
        tag: tokens.compression_input
        tag: tokens.compression_output
```

---

### Phase 2 — Metrics 增强 `[待做]`

#### 2-A：Token 成本维度扩展 `[P0]`

**目标**：Token 指标增加 `session_type` 和 `agent_role` 维度，使成本可按业务分摊。

**变更文件**：
- `src/Kode.Agent.Sdk/Diagnostics/KodeAgentMetrics.cs`（指标定义保持不变，调用处扩展 tag）
- `src/Kode.Agent.Sdk/Core/Agent/Agent.cs`（埋点调用处）
- `src/Kode.Agent.Sdk/Core/Types/AgentConfig.cs` 或类似（新增 `SessionType`、`AgentRole` 字段）

**新 Tag 方案**：

```csharp
// AgentConfig 新增（由宿主注入）
public string SessionType { get; init; } = "main";   // main | channel | automation
public string AgentRole { get; init; } = "primary";  // primary | sub-agent

// 埋点调用处
var tags = new TagList
{
    { "model", request.Model },
    { "session_type", _config.SessionType },
    { "agent_role", _config.AgentRole },
};
KodeAgentMetrics.TokensInput.Add(usage.InputTokens, tags);
KodeAgentMetrics.TokensOutput.Add(usage.OutputTokens, tags);
```

**KodaClaw 配置处**（MainSessionService / ChannelSessionService / AutomationSessionService）：
```csharp
new AgentConfig
{
    SessionType = "automation",
    AgentRole = "primary",
    // ...
}
```

#### 2-B：TTFT 指标 `[P0]`

**目标**：新增 TTFT（Time to First Token）Histogram，单位 ms。

**变更文件**：`src/Kode.Agent.Sdk/Diagnostics/KodeAgentMetrics.cs`

```csharp
public static readonly Histogram<double> ModelTtft =
    Meter.CreateHistogram<double>(
        "kode.agent.model.ttft",
        unit: "ms",
        description: "Time to first token from model request start");
```

Tag：`model`、`provider`（anthropic | openai）

#### 2-C：模型错误分类 `[P1]`

**目标**：`ModelErrors` 增加 `error_type` 维度，用于区分处置策略。

**变更文件**：`src/Kode.Agent.Sdk/Core/Agent/Agent.cs`（重试/错误处理路径）

```csharp
var errorType = ex switch
{
    HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "rate_limit",
    HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }    => "auth",
    HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable } => "server_unavailable",
    OperationCanceledException                                          => "timeout",
    _                                                                   => "unknown",
};
KodeAgentMetrics.ModelErrors.Add(1,
    new TagList { { "model", model }, { "error_type", errorType } });
```

#### 2-D：压缩 Token 成本独立统计 `[P1]`

**目标**：Context 压缩自身消耗的 Token 单独计入，不混入业务 Token。

**变更文件**：`src/Kode.Agent.Sdk/Diagnostics/KodeAgentMetrics.cs`

```csharp
public static readonly Counter<long> CompressionTokensInput =
    Meter.CreateCounter<long>("kode.agent.context.compression.tokens.input");
public static readonly Counter<long> CompressionTokensOutput =
    Meter.CreateCounter<long>("kode.agent.context.compression.tokens.output");
```

#### 2-E：工具分类维度 `[P2]`

**目标**：`ToolDuration` 增加 `tool_category` 维度，识别慢类别。

分类规则：
```
fs_*           → "filesystem"
bash_*         → "shell"
isolate_task / pipeline / parallel_research / ... → "orchestration"
mcp__*         → "mcp"
workspace_*    → "workspace"
其他           → "builtin"
```

---

### Phase 3 — Logs 增强 `[待做]`

#### 3-A：Provider 层结构化日志 `[P1]`

**目标**：AnthropicProvider / OpenAIProvider 在关键路径加结构化日志。

**变更文件**：
- `src/Kode.Agent.Sdk/Infrastructure/Providers/AnthropicProvider.cs`
- `src/Kode.Agent.Sdk/Infrastructure/Providers/OpenAIProvider.cs`

两个 Provider 的构造函数已接受 `ILoggerFactory?`，补充调用即可。

需要记录的事件：

| 事件 | 级别 | 结构化字段 |
|------|------|-----------|
| Stream 请求开始 | Debug | model, has_tools, message_count |
| 首 token 到达 | Debug | model, ttft_ms |
| Stream 完成 | Debug | model, input_tokens, output_tokens, stop_reason |
| 重试触发 | Warning | model, attempt, error_type, delay_ms |
| Stream 异常 | Error | model, error_type, exception |

#### 3-B：工具执行结构化日志 `[P2]`

**目标**：Agent.ExecuteToolAsync 记录工具执行摘要（不记录参数内容，防隐私泄露）。

```csharp
_logger?.LogDebug("Tool executed: {ToolName} in {DurationMs}ms, success={Success}",
    toolUse.Name, sw.ElapsedMilliseconds, toolResult.Success);
```

#### 3-C：Serilog → OTel Logs 桥接 `[P2]`

**目标**：KodaClaw.Gateway 的 Serilog 日志通过 OTel 协议导出，日志自动携带 TraceId/SpanId 实现关联。

**变更文件**：`products/KodaClaw/src/KodaClaw.Gateway/Composition/GatewayApp.Composition.cs`

```csharp
// 在 UseSerilog 配置中追加 OTel sink
.WriteTo.OpenTelemetry(options =>
{
    options.Endpoint = otlpEndpoint;  // 与 Traces/Metrics 同一个 collector
    options.Protocol = OtlpProtocol.Grpc;
    options.ResourceAttributes = new Dictionary<string, object>
    {
        ["service.name"] = "kodaclaw-gateway",
        ["service.version"] = version,
    };
})
```

---

### Phase 4 — KodaClaw Gateway OTel 宿主配置 `[待做]`

**目标**：Gateway 注册 OTel，接入 ActivitySource("Kode.Agent") 和 Meter("Kode.Agent")，
统一导出到 OTLP（生产）/ Console（开发）。

**变更文件**：`products/KodaClaw/src/KodaClaw.Gateway/Composition/GatewayApp.Composition.cs`

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("kodaclaw-gateway", serviceVersion: version))
        .AddSource(KodeAgentActivitySource.SourceName)   // SDK traces
        .AddAspNetCoreInstrumentation()                   // HTTP 请求
        .AddHttpClientInstrumentation()                   // HttpClient（Provider HTTP 层）
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
        .AddConsoleExporter())                            // 本地开发用
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(...)
        .AddMeter(KodeAgentMetrics.MeterName)            // SDK metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()                      // GC/Thread/Memory
        .AddOtlpExporter(...)
        .AddConsoleExporter())
    .WithLogging(logging => logging                       // OTel Logs（补充 Serilog 桥）
        .SetResourceBuilder(...)
        .AddOtlpExporter(...));
```

**环境变量配置**（`.env`）：
```
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_SERVICE_NAME=kodaclaw-gateway
OTEL_SDK_DISABLED=false
```

---

### Phase 5 — 验收测试 `[待做]`

#### 单元测试（L1）

| 测试文件 | 验证内容 |
|---------|---------|
| `KodeAgentMetricsTests.cs` | Token 指标维度完整性、TTFT histogram 存在 |
| `ProviderActivityTests.cs` | AnthropicProvider/OpenAI 的 span 能被 TestActivityListener 捕获 |
| `SubAgentTraceContextTests.cs` | SubAgentRunner 传播 parent TraceContext，子 span 的 ParentId 正确 |

#### 集成测试（L2）

| 测试文件 | 验证内容 |
|---------|---------|
| `OtelGatewaySetupTests.cs` | Gateway 启动后 ActivitySource 和 Meter 已注册，SDK version 正确 |
| `TokenDimensionIntegrationTests.cs` | 不同 session_type 的 token 计数落到正确维度 |

#### 手动验收（L5）

- 启动本地 Jaeger（`docker run -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one`）
- 触发一次 Chat → 工具调用 → 子代理流程
- Jaeger UI 中验证：
  - [ ] `agent.run` span 包含完整子 span 树
  - [ ] Provider span 显示 TTFT
  - [ ] 子代理 span 链接到父 Agent 的 span
  - [ ] 工具执行 span 有 `tool.name` tag
- Prometheus/Grafana 验收：
  - [ ] `kode.agent.tokens.input` 按 session_type 聚合有值
  - [ ] `kode.agent.model.ttft` histogram 分布正常
  - [ ] `kode.agent.model.errors` 按 error_type 有分类

---

## 追踪表

| ID | 阶段 | 描述 | 优先级 | 状态 | 影响范围 |
|----|------|------|--------|------|---------|
| OT-0 | Phase 0 | NuGet 包版本声明 | P0 | ✅ 完成 | Directory.Packages.props, KodaClaw.Gateway.csproj |
| OT-1A | Phase 1 | Provider 层 HTTP span + TTFT | P0 | ✅ 完成 | AnthropicProvider, OpenAIProvider |
| OT-1B | Phase 1 | 子代理 TraceContext 传播 | P1 | ✅ 完成 | SubAgentRunner, Agent.cs |
| OT-1C | Phase 1 | 工具审批等待独立 span | P2 | ✅ 完成 | Agent.cs ProcessToolCallsAsync |
| OT-1D | Phase 1 | Context 压缩独立 span | P2 | ✅ 完成 | Agent.cs StepAsync |
| OT-2A | Phase 2 | Token 指标 session_type / agent_role 维度 | P0 | ✅ 完成 | KodeAgentMetrics, AgentConfig, 三类 Session |
| OT-2B | Phase 2 | TTFT Histogram 新指标 | P0 | ✅ 完成 | KodeAgentMetrics, AnthropicProvider, OpenAIProvider |
| OT-2C | Phase 2 | 模型错误 error_type 维度 | P1 | ✅ 完成 | Agent.cs 重试路径 |
| OT-2D | Phase 2 | 压缩 Token 独立 Counter | P1 | ✅ 完成 | KodeAgentMetrics（指标已定义，由 ContextManager 调用方填充）|
| OT-2E | Phase 2 | 工具分类维度 tool_category | P2 | ✅ 完成 | Agent.cs ProcessToolCallsAsync |
| OT-3A | Phase 3 | Provider 层结构化日志 | P1 | ✅ 完成 | AnthropicProvider, OpenAIProvider |
| OT-3B | Phase 3 | 工具执行结构化日志（脱敏） | P2 | ✅ 完成 | Agent.cs ProcessToolCallsAsync |
| OT-3C | Phase 3 | Serilog → OTel Logs 桥接 | P2 | ✅ 完成 | GatewayApp.Composition.cs |
| OT-4 | Phase 4 | Gateway OTel 宿主配置 | P1 | ✅ 完成 | GatewayApp.Composition.cs |
| OT-5A | Phase 5 | 单元测试：Metrics / Activity / TraceContext | P1 | ✅ 完成 | KodeAgentMetricsTests, AgentOtelClassifiersTests, OtelSessionTypeTaggingTests |
| OT-5B | Phase 5 | 集成测试：Gateway OTel 注册 + 维度验证 | P2 | ⬜ 跳过（L5 Dogfood 补充）| KodaClaw.IntegrationTests |
| OT-5C | Phase 5 | 手动验收：Jaeger + Prometheus | P2 | ⬜ 待 L5 | 本地环境 |

状态说明：⬜ 待做 / 🔵 进行中 / ✅ 完成 / ❌ 阻塞

---

## 设计约束

### 1. SDK 保持零 OTel 依赖

`Kode.Agent.Sdk.csproj` **不引用** `OpenTelemetry` 包。
所有埋点通过标准 .NET Diagnostics API（`System.Diagnostics.ActivitySource`、
`System.Diagnostics.Metrics.Meter`）完成，OTel 只在宿主侧注册 listener。

### 2. 敏感数据默认不进遥测

以下内容**默认不记录**到任何 span tag 或 log field：
- 工具调用参数（`toolUse.Input`）
- 模型输入/输出文本
- 用户消息内容

如需调试，可在 `AgentConfig.DiagnosticsOptions.IncludeSensitiveData = true` 时额外记录，
且只写入 `Debug` 级别日志，不写入 Span 属性。

### 3. Tag 命名遵循 OTel 语义约定

参照 OTel Semantic Conventions（GenAI 工作组草案）：

| 当前名称 | 规范化名称 |
|---------|-----------|
| `agent.model` | `gen_ai.request.model` |
| `tokens.input` | `gen_ai.usage.input_tokens` |
| `tokens.output` | `gen_ai.usage.output_tokens` |
| `tool.name` | `gen_ai.tool.name` |
| `http.ttft_ms` | `gen_ai.client.time_per_output_token`（近似） |

> **注意**：命名规范化作为 Phase 1 的一部分同步完成，避免后期迁移成本。

### 4. 子代理 Tag 继承

子代理的所有 span 自动继承 `session_type` 和 `session_id` 作为 Baggage，
以便在 Jaeger 中按 session 过滤完整调用链。

---

## 关键文件速查

| 文件 | 角色 |
|------|------|
| `src/Kode.Agent.Sdk/Diagnostics/KodeAgentActivitySource.cs` | Activity source 定义 |
| `src/Kode.Agent.Sdk/Diagnostics/KodeAgentMetrics.cs` | Meter + 所有指标定义 |
| `src/Kode.Agent.Sdk/Core/Agent/Agent.cs` | 主要埋点调用处 |
| `src/Kode.Agent.Sdk/Infrastructure/Providers/AnthropicProvider.cs` | Provider 层（OT-1A）|
| `src/Kode.Agent.Sdk/Infrastructure/Providers/OpenAIProvider.cs` | Provider 层（OT-1A）|
| `src/Kode.Agent.Sdk/Core/Context/ContextManager.cs` | 压缩 span（OT-1D）|
| `src/Kode.Agent.Tools.Orchestration/Internal/SubAgentRunner.cs` | 子代理 context（OT-1B）|
| `Directory.Packages.props` | 包版本（OT-0）|
| `products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj` | OTel 包引用（OT-0）|
| `products/KodaClaw/src/KodaClaw.Gateway/Composition/GatewayApp.Composition.cs` | 宿主配置（OT-3C, OT-4）|
