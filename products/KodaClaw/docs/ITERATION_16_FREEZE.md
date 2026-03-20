# Iteration 16 Freeze — Channel Session 权限策略 + 渠道侧文字审批

**冻结日期**：2026-03-20
**前置迭代**：Iteration 15（automationsEnabled UI + V2 Chat Stage 收口）
**主题**：ChannelHub 安全收口。固化 Channel Session 的权限边界（工具调用不中断审批），并实现通过渠道文字回复完成草稿审批的基础链路。

---

## 一句话目标

让 Channel Session 的工具调用永远自动通过（不阻塞 Agent turn），并让用户可以直接在 Telegram / 任意渠道里用文字回复批准或拒绝 Agent 的草稿。

---

## In-scope

| KC | 描述 | 规模 |
|----|------|------|
| KC-1601 | Channel Session Permission Policy — 固化 Channel/Automation session 用 `PermissionMode.Auto`，工具调用不产生 mid-turn approval | XS |
| KC-1602 | Channel Text Approval (Phase 1) — 审批通知发回 thread，inbound 文字响应检测，绕过 Agent turn 直接决策 | M |

---

## Out-of-scope（本迭代明确不做）

- Telegram inline keyboard 按钮审批（B 方案）：后续在 Telegram connector 层叠加
- 跨线程审批通知（发到专用审批频道）
- reject 附带原因的文字解析
- 工具级别的 mid-turn approval 在 Channel session 中的任何形式支持
- Main session / Automation session 的 approval 行为改动
- Web UI 渠道审批的任何改动（已有 `/api/approvals/*` 链路不动）

---

## 架构决策

### KC-1601：Channel Session Permission Policy

**问题**：若 Channel session 的工具带有 `RequiresApproval = true`，Agent turn 会挂起等待审批，与渠道异步消息模型根本冲突。

**决策**：Channel session 和 Automation session 的 permission mode 固定为 `PermissionMode.Auto`，不允许配置为 `Approval` 或 `Readonly`。最终安全门只有一道：Channel Delivery Approval（post-turn）。

**实现边界**：
- `ChannelSessionService` 在创建 agent 时强制注入 `PermissionMode.Auto`，忽略外部传入的其他配置
- 写一个单元测试断言该约束，防止未来被误改
- 不修改 Main session 的任何权限配置

### KC-1602：Channel Text Approval

**核心流程**（在现有渠道 turn 管道上叠加）：

```
新消息到达 → ChannelEventIngestionService（不变）
  → [NEW] ChannelTurnOrchestrator 前置检测：文本是否为审批响应？
      匹配 → 找到对应 pending approval → ChannelDeliveryApprovalService.Approve/Reject
                → 发确认通知消息 → 返回 ApprovalDecisionReceived outcome
      不匹配 → 正常 Agent turn（现有逻辑完全不变）

Agent turn 完成，Governance 创建 Approval 后：
  → [NEW] 发审批通知消息到同一 thread（含草稿预览 + 6位token + 操作指引）
```

**Token 设计**：
- Token = `SHA256(draftId)[0..3]` 转大写 hex，共 6 位
- 存在 `PayloadJson` 的 `token` 字段，不修改 `Approval` record 结构
- `ChannelDeliveryEvaluationResult` 新增 `ApprovalToken` 字段，让 Orchestrator 不必再解析 PayloadJson

**审批通知消息格式**：
```
[草稿待审批 · {TOKEN}]
──────────────
{草稿内容预览，最多 96 字符}
──────────────
回复 ok {TOKEN} 发送，no {TOKEN} 取消
```
- 消息通过 `ChannelDeliveryDispatchService.SendNotificationAsync()` 发出
- 不更新 `ThreadBinding.LastOutboundAt`，不记录为 outbound draft
- 不触发再次 inbound 回调（Bot 自身发出的消息不会被 Bot 接收）

**响应检测规则**（`ChannelApprovalResponseParser`，纯静态，无依赖）：
- Approve：`ok` / `yes` / `approve` / `send`（大小写不敏感）
- Reject：`no` / `cancel` / `reject`
- Token：关键词后跟一个空格再跟 6 位 hex（如 `ok A3F9C1`）
- 保守原则：`ok 去发吧` → 不识别（非 6 位 hex token）
- 无 token：仅当该 thread 只有 1 个 pending ChannelDelivery approval 时匹配；若有多个，回复提示带编号

**多个 pending approval 的消歧**：

| 情况 | 处理 |
|------|------|
| 只有 1 个 pending + 无 token | 直接匹配 |
| 只有 1 个 pending + 正确 token | 匹配 |
| 多个 pending + 无 token | 发系统消息："有多个草稿待审批，请带编号（如 ok A3F9C1）" |
| 多个 pending + 正确 token | 匹配对应那一个 |
| approval 已被 Web UI 决定 | `TransitionAsync` 返回 false（NotPending），回复"该草稿已处理" |

**新增 / 修改文件**：

| 文件 | 类型 | 改动 |
|------|------|------|
| `ChannelDeliveryGovernanceService.cs` | 修改 | payloadJson 加 `token` 字段 |
| `ChannelDeliveryEvaluationResult.cs` | 修改 | 加 `ApprovalToken` 字段 |
| `ChannelDeliveryDispatchService.cs` | 修改 | 新增 `SendNotificationAsync(account, binding, text)` |
| `ChannelTurnOrchestrator.cs` | 修改 | 前置审批响应检测 + 批准后发通知 + 注入 2 个新依赖 |
| `ChannelApprovalResponseParser.cs` | 新增 | 纯静态解析器，无 DI |
| `ChannelSessionService.cs`（Runtime） | 修改（KC-1601） | 强制 PermissionMode.Auto |

---

## 验证命令

```bash
# L0
dotnet build KodaClaw.sln

# L1 单元测试
dotnet test tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj \
  --filter "FullyQualifiedName~ChannelApprovalResponseParser|FullyQualifiedName~ChannelDeliveryGovernance|FullyQualifiedName~ChannelTurnOrchestrator|FullyQualifiedName~ChannelSession"

# L2 集成测试（含 approval 链路）
dotnet test tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ChannelTextApproval|FullyQualifiedName~ChannelDeliveryApproval"

# 全量后端
make test-solution
```

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-1601：ChannelSessionService 固化 PermissionMode.Auto + L1 断言测试 |
| Wave 2 | KC-1602 基础层：Token 生成（GovernanceService + EvaluationResult）+ ChannelApprovalResponseParser + L1 单元测试 |
| Wave 3 | KC-1602 集成层：SendNotificationAsync + Orchestrator 前置检测 + HandleApprovalResponseAsync + L2 集成测试 |
| Wave 4 | 全量回归 + BACKLOG 更新 + Dogfood（真实 Telegram DM 场景） |

---

## 验收门槛

1. Channel session agent turn 中调用带 `RequiresApproval = true` 的工具时，不产生 Approval 记录，不暂停 turn
2. Agent 生成草稿后，同一 Telegram thread 收到含 token 的审批通知消息
3. 用户回复 `ok [TOKEN]` → 草稿被发送，Approval status = Approved
4. 用户回复 `no [TOKEN]` → 草稿被取消，Approval status = Rejected，无真实发送
5. 用户说"ok"但无 pending approval → 正常走 Agent turn，Agent 响应
6. 审批已被 Web UI 决定后，渠道端回复收到"该草稿已处理"提示
7. 多个 pending 无 token → 收到带编号提示消息，不误判
