# KodaClaw `tests/`

这里用于承载 KodaClaw 的产品级测试分层，独立于现有 SDK 测试。

建议结构：

```text
tests/
  KodaClaw.UnitTests/
  KodaClaw.IntegrationTests/
  KodaClaw.ContractTests/
  KodaClaw.E2E/
  Fixtures/
```

建议约定：

- 后端测试沿用现有仓库的 `xUnit + FluentAssertions + Moq`
- `UnitTests`：纯逻辑、policy、决策、manifest、路径与 schema
- `IntegrationTests`：Gateway、Runtime、Storage、PluginHost、Automation 等模块协同
- `ContractTests`：API、SSE、workspace 初始化、payload 正规化、golden files
- `E2E`：主流程验证，例如 bootstrap、审批、自动化、渠道接入
- `Fixtures`：workspace 样本、plugin 样本、channel payload 样本、golden outputs

当前迭代 1 的验收包暂时落在两处：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration1AcceptanceIntegrationTests.cs`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0112-bootstrap-chat-resume.spec.ts`

当前迭代 2 的验收包位置：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_2_ACCEPTANCE_PACK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration2AcceptanceIntegrationTests.cs`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0213-control-plane-acceptance.spec.ts`

当前迭代 3 第一波 automation 基线位置：

- Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Automation/AutomationContractsTests.cs`
- Parser Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Workspace/HeartbeatAutomationCompilerContractTests.cs`
- Unit：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/Automation/SqliteAutomationDefinitionRepositoryTests.cs`
- Unit：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/Automation/SqliteAutomationRunRepositoryTests.cs`
- Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/AutomationSessionServiceIntegrationTests.cs`

当前迭代 3 第二波 automation + canvas 基线位置：

- Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Automation/AutomationSchedulerIntegrationTests.cs`
- Unit：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/Storage/SqliteCanvasArtifactRepositoryTests.cs`
- Runtime stability：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/ChatSessionServiceIntegrationTests.cs` 现已补充临时目录清理重试，确保 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 回归稳定。

当前迭代 3 第三波 API / Web / Acceptance 位置：

- Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Automation/AutomationApiContractsTests.cs`
- Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Canvas/CanvasContractsTests.cs`
- Gateway Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/AutomationApiIntegrationTests.cs`
- Gateway Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/CanvasApiIntegrationTests.cs`
- Smoke Acceptance：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration3AcceptanceIntegrationTests.cs`
- Web Component：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/__tests__/automations-desk.spec.tsx`
- Web Component：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/__tests__/canvas-desk.spec.tsx`
- Web E2E：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0308-automations.spec.ts`
- Web E2E：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0309-canvas.spec.ts`
- Acceptance Pack：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_3_ACCEPTANCE_PACK.md`

当前迭代 4 plugin baseline 位置：

- Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Plugins/PluginManifestContractsTests.cs`
- Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Plugins/PluginPermissionContractsTests.cs`
- Contract：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Plugins/PluginApiContractsTests.cs`
- Unit：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/PluginHost/SqlitePluginRegistryRepositoryTests.cs`
- Unit：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/PluginHost/SqlitePluginLogRepositoryTests.cs`
- Gateway Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/PluginApiIntegrationTests.cs`
- Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/PluginHost/PluginLifecycleHostIntegrationTests.cs`
- Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/PluginHost/PluginHealthIntegrationTests.cs`
- Runtime Integration：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Runtime/PluginToolInjectionIntegrationTests.cs`
- Smoke Acceptance：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration4AcceptanceIntegrationTests.cs`
- Web Component：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/__tests__/plugins-desk.spec.tsx`
- Web E2E：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0407-plugins.spec.ts`
- Bundled Fixtures：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/Plugins/README.md`
- Fixture：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/Program.cs`
- Acceptance Pack：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_4_ACCEPTANCE_PACK.md`

更完整的范式说明见 `docs/ENGINEERING_PLAYBOOK.md`。
