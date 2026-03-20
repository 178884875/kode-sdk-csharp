# ADR-0001：Wave 1 Workspace / Bootstrap / Gateway 合同冻结

## 状态

- `Accepted`

## 日期

- 2026-03-18

## 实施状态

- `KC-0101` `Completed`
- `KC-0104` `Completed`
- `KC-0105` `Completed`
- `KC-0110` `Completed`
- `KC-0111` 已完成并消费主会话恢复失败原因进入 diagnostics 查询入口
- `KC-0108` 已完成 bootstrap-state Web consumer 验证
- 已通过 solution 级 `build` / `test`、Gateway health/bootstrap-state smoke、create-close-resume fallback 验证与 diagnostics failure drill

## 背景

KodaClaw 已经完成独立 solution、基础 contracts、Gateway 最小宿主与测试骨架，接下来要进入 Wave 1 并行实现：

- `KC-0101` Workspace 初始化器
- `KC-0104` Gateway token 与 bootstrap-state API
- `KC-0105` Main session runtime 组合层

这三个任务分别落在 `Workspace`、`Gateway`、`Runtime` 模块，但它们共享同一组产品合同：Workspace 根目录、bootstrap 状态判定、session 落盘位置，以及 Gateway 在 Wave 1 的认证边界。如果这些合同不先冻结，多个 worker 会在实现中各自猜目录布局、token 来源和 session 元数据，最后集成成本会明显上升。

当前约束：

- KodaClaw 是独立产品，不能把产品逻辑写回 example
- 产品采用 local-first 架构，Workspace 协议必须先稳定
- Wave 1 只做最小可用闭环，不在本轮引入完整 Keychain、SQLite 控制面和前端壳
- 认证需要先有最小实现，但不能把长期安全方案假装已经完成

## 决策

我们决定在 Wave 1 采用以下合同：

- Workspace 的规范根目录固定为用户 home 下的 `~/.kodaclaw`，测试与集成场景允许通过显式 options 覆盖到临时目录。
- Workspace 顶层目录、核心文件名和当前版本号由 `KodaClaw.Contracts/KodaClawWorkspaceLayout.cs` 提供，避免各模块复制字符串常量。
- `config/app.json` 作为 Wave 1 的最小产品配置文件，至少包含 `workspaceVersion`、`bootstrapCompleted` 和 `activeMainSessionId`。
- `identity/device.json` 使用 `DeviceIdentity` 合同；`sessions/<sessionId>/` 作为 runtime 的持久化根，由 `JsonAgentStore` 直接落盘运行态。
- 当 `activeMainSessionId` 指向的主会话 store 无法恢复时，runtime 必须创建新的 main session，回写 `activeMainSessionId`，并把恢复失败原因保存在 runtime handle 中供 diagnostics 使用。
- `GET /api/system/health` 保持匿名可读，用于本机探活与启动探测。
- `GET /api/system/bootstrap-state` 采用 Bearer token 保护；Wave 1 的 token 从配置或环境变量注入，不写入 Workspace，等后续安全迭代再迁移到 Keychain。
- Gateway 对外返回的最小系统合同使用 `SystemHealthResponse` 与 `BootstrapStateResponse`，供 Web 壳、Desktop 壳和集成测试复用。

该方案适用于迭代 1 的 bootstrap、主会话和最小 Gateway 集成。

该方案暂不覆盖：

- OS Keychain 集成
- channel / automation / plugin 的额外 session policy
- 完整控制面配置模型
- 前端持久登录与 token 分发方案

## 备选方案

### 方案 A：把 Gateway token 直接写入 Workspace 文件

- 描述：在 `config/gateway.json` 或同级文件中保存明文 token。
- 优点：实现最简单，Web/CLI 读取方便。
- 缺点：违背 Workspace 不存 secrets 的安全边界，也会让未来 Keychain 迁移更难。

### 方案 B：Wave 1 完全不做 Gateway 认证

- 描述：所有本地 API 都只靠 loopback 访问控制。
- 优点：实现成本最低，最快打通聊天闭环。
- 缺点：与产品目标不一致，后续再补认证时会影响 API、中间件和测试基线。

### 方案 C：本轮直接做完整 Keychain 方案

- 描述：在 Wave 1 就实现系统 Keychain 读写与 token 管理。
- 优点：安全边界最完整。
- 缺点：明显超出当前迭代范围，会拖慢 bootstrap、runtime 和 Web 壳主线。

## 后果

正向影响：

- Workspace、Gateway、Runtime 三个模块可以并行实现，并共享同一份常量和 DTO 合同。
- Wave 1 已经具备最小 auth 边界，不需要以后整体推翻 Gateway 中间件。
- 后续 Desktop/Web 壳接入时，可以直接复用 `bootstrap-state` 合同。

成本：

- 需要维护一份显式 ADR 和共享 contracts，而不是在模块内部各自定义。
- Wave 1 token 方案是过渡态，未来仍需迁移到 Keychain。

风险：

- 如果后续 bootstrap 状态需要更细粒度字段，`BootstrapStateResponse` 可能扩展。
- 如果 runtime 恢复策略变化，`activeMainSessionId` 可能需要升级为更完整的 session 索引。

对后续迭代的限制：

- Keychain 落地时要保持 `bootstrap-state` 外部合同尽量兼容。
- Workspace 版本升级必须通过 `workspaceVersion` 做迁移，而不是隐式猜目录。

## 验证方式

- `L0`：`dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln`
- `L1`：contracts 默认值与 workspace layout 常量单元测试
- `L2`：workspace 临时目录初始化测试、Gateway bootstrap-state 集成测试、runtime + JsonAgentStore 集成测试
- `L3`：bootstrap-state golden response、workspace golden tree
- `L5`：本机启动 Gateway 后用 curl 验证 `health`

## 关联文档

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/WORKSPACE_SPEC.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ENGINEERING_PLAYBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_1_SLICES.md`

## 关联切片 / PR

- Capability Slice: `KC-0101`, `KC-0104`, `KC-0105`, `KC-0110` `Completed`
- Consumer Validation: `KC-0108` Web shell 已消费 `bootstrap-state` 合同并通过 `npm run build` / `npm run test` / `npm run test:e2e`
- Verification: `dotnet build` + `dotnet test` + Gateway health/bootstrap-state smoke + create-close-resume fallback smoke + diagnostics failure drill passed
