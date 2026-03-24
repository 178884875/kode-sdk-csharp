# Iteration 44 FREEZE — 多模态 Composer：Model Pill + Vision 图片输入

**冻结日期**：2026-03-23
**范围摘要**：在 Chat Composer 输入组加入两个能力：① 左下角只读 Model Pill，展示当前 session 绑定的模型名称与能力标签；② 当模型具备 Vision 能力时，显示图片附件按钮（支持文件选择与粘贴），用户可在消息中附带图片发送给 Agent。后端需在 Session API 补全模型信息字段，并在消息发送路径上打通 mediaIds → ImageContent 转换。STT / TTS 不在本迭代范围。

---

## Iter 43 审查结论

| 条目 | 状态 | 说明 |
|------|------|------|
| 字段扩展 Contracts（ModelPreset / ModelEndpoint / 两个 Request） | ✅ 完成 | 所有字段齐全，presets.json 已填充 |
| SQLite 迁移（max_output_tokens / is_reasoning） | ✅ 完成 | 两列 idempotent ALTER TABLE，COALESCE 兜底 |
| Gateway 验证 + Handler（POST/PUT/DELETE） | ✅ 完成 | 包含 maxOutputTokens 校验与注入 |
| Runtime isReasoning → 跳过 tool_use | ✅ 完成 | `RegistryAwareModelProvider` L168：`if (isReasoning && Tools is {Count: > 0}) → Tools = null` |
| 前端类型 + 表单字段 | ✅ 完成 | contracts.ts + ModelsSettingsDesk 全部字段对齐 |
| 表单字段顺序 | ⚠️ 微偏差 | `已启用` 勾选框在测试连接按钮之后（位置 9），Freeze 要求在高级选项之后位置 8；不影响功能 |
| 模型卡片展示（isReasoning 徽章 / token 计数） | ✅ 完成 | 推理徽章 + `{ctxK} / {maxOutK}` 均已渲染 |
| KC-BUG-001 SecretRef 前缀修复 | ✅ 完成 | 前端已改 `"keychain:models:"`，后端生成一致 |
| KC-BUG-002 首个模型自动成为默认 | ✅ 完成 | `existingEndpoints.Count == 0 → IsDefault: true` |
| KC-BUG-003 删除默认模型防守 | ✅ 完成 | 无其他启用 endpoint → 409；有则自动切换 |

**结论：Iter 43 实质性完成（95%+）。** 表单顺序微偏差为 UX 小瑕疵，不阻塞验收，可作为 Iter 44 前端 UX 顺手修复。

---

## 新功能范围

### 背景与动机

Chat Composer 目前是纯文本输入组件。用户无法知道 Agent 正在使用哪个模型，也无法向支持 Vision 的模型（Claude 3.5 Sonnet、GPT-4o 等）发送图片。而多模态基础设施（`/api/media/upload`、`IMediaStore`、SDK `ImageContent` 块、`ModelCapabilitySet.Vision` flag）已在 Iter 34 中全部到位，只差最后的输入侧打通。

**用户 Outcome**：用户在 Chat 界面能看到当前用 Koda 在使用什么模型，遇到图文混合问题时可直接附图发送，无需通过工具生成绕路。

---

### 模型过滤规则（全局约束）

> **Chat session 只能使用具备 `TextChat` 能力的模型。** 纯语音模型（STT/TTS only）和纯图像生成模型（ImageGeneration only）不参与 chat session 的默认模型解析，也不应在 Composer 的 Model Pill 中出现。
>
> Vision 附件按钮的显示条件是 **`TextChat & Vision` 同时置位**，而不是仅检查 Vision bit——图像生成模型（`ImageGeneration`）不能作为 Vision 输入模型使用。

这一规则在后端已通过 `RegistryAwareModelProvider.ResolveAsync()` 的 `ResolveDefaultForAsync(TextChat | ToolCalling)` 参数实现，本迭代在前端同步遵守此约束：

| 能力组合 | Chat session 可用 | 显示 Model Pill | 显示 Vision 附件按钮 |
|---------|:-----------------:|:---------------:|:-------------------:|
| TextChat | ✅ | ✅ | ❌ |
| TextChat \| ToolCalling | ✅ | ✅ | ❌ |
| TextChat \| Vision | ✅ | ✅ | ✅ |
| TextChat \| ToolCalling \| Vision | ✅ | ✅ | ✅ |
| ImageGeneration only | ❌ | ❌ | ❌ |
| TTS / STT only | ❌ | ❌ | ❌ |
| Vision only（理论值） | ❌ | ❌ | ❌ |

---

### KC-4401：Session API 补全模型信息字段

**改动层**：`KodaClaw.Contracts` + `KodaClaw.Gateway` + `KodaClaw.Runtime`

**具体内容**：
- `SessionDetail` record 新增三个字段：
  ```csharp
  string? ModelEndpointId       // 当前 session 绑定的 endpoint ID
  string? ModelEndpointName     // 显示用名称，直接返回避免前端二次查询
  int ModelCapabilities         // ModelCapabilitySet bitmask，0 = 未知
  ```
- 实现策略：`GET /api/sessions/{id}` handler 在拼装 `SessionDetail` 时，调用 `IModelRegistryRepository.ResolveDefaultForAsync(TextChat | ToolCalling)` 查询当前默认端点，取其 `Id`、`Name`、`Capabilities`（无需持久化 endpoint ID 到 session 表，Session 级别不允许中途切换模型，始终等于当前默认端点）
- 若 registry 无配置端点（或默认端点不含 TextChat），三个字段返回 `null` / `null` / `0`，前端降级隐藏 pill 与附件按钮
- 前端 `contracts.ts`：`SessionDetail` 接口加 `modelEndpointId?: string`、`modelEndpointName?: string`、`modelCapabilities?: number`
- 验证：1 个集成测试——创建 endpoint（capabilities 包含 TextChat | Vision），调 `GET /api/sessions/{id}`，断言三字段非空且 capabilities 同时包含 TextChat 和 Vision bit

**非目标**：不在 session 建立时持久化 modelEndpointId，不支持 per-session 固定模型（下一迭代决策）。

---

### KC-4402：消息发送支持 mediaIds

**改动层**：`KodaClaw.Contracts` + `KodaClaw.Gateway` + `KodaClaw.Runtime`

**具体内容**：

**后端 Contracts**：
```csharp
// 现有 record（KodaClaw.Contracts/ChatStreamRequest.cs），新增可选字段
public sealed record ChatStreamRequest(
    string Message,
    string? SessionId = null,
    IReadOnlyList<string>? MediaIds = null   // 新增
);
```

**Gateway handler**（`GatewayApp.ChatDiagnosticsEndpoints.cs`，`POST /api/chat/stream`）：
- 解析 `MediaIds`，透传给 `IMainSessionService.SendAsync(message, mediaIds)`

**`MainSessionService`**：
- 若 `mediaIds` 非空，调 `IMediaStore.GetAsync(id)` 获取文件流
- 构建 `ImageContent(base64, mimeType)` 列表
- 最终消息内容块：`[ImageContent(img1), ..., TextContent(message)]`
- 单个 mediaId 不存在时：记录诊断日志、跳过该条目（不终止整条消息）
- 空列表 / null：退化为纯文本，行为与现有完全一致

**验证**：
- 1 个 L1 单元测试：`NormalizeUserMessage` 处理有效 / 缺失 / 空 mediaIds 的边界情况
- 1 个 L2 集成测试：POST message 带 mediaIds → SSE 流能正常启动（无异常）

**非目标**：不做图片压缩 / resize（存什么发什么）；不限制单次最大图片数（由客户端 UX 约束）。

---

### KC-4403：ChatComposer Model Pill（只读展示）

**改动层**：`apps/kodaclaw-web`

**新增 props**（`ChatComposer.tsx`）：
```typescript
modelName?: string       // 展示用，如 "claude-3-5-sonnet"
modelCapabilities?: number  // bitmask，用于派生功能按钮可见性
```

**UI**：
- 输入框左下角一个小 pill 按钮（无交互，cursor: default）
- 显示 `modelName`（超长截断 max 18 字符 + ellipsis）
- 若 `modelName` 未提供，pill 不渲染
- 能力徽章（inline，pill 内）：仅在 pill 展开 hover tooltip 里显示，不影响默认宽度

**数据来源**（父组件 `App.tsx`）：
- `App.tsx` 在 `activeSessionId` 变化时调 `GET /api/sessions/{id}`，解析 `modelEndpointName` / `modelCapabilities`（由 KC-4401 直接返回，无需二次查询端点列表）
- 传给 `ChatComposer`

**CSS**：
```css
.composer__model-pill     /* 左下角胶囊，背景 var(--surface-2)，圆角 pill */
.composer__model-pill-name  /* 截断文字 */
```

**验证**：1 个 Vitest 快照测试——给定 `modelName="gpt-4o"` 时，pill 渲染并包含文字。

---

### KC-4404：Vision 图片附件 UI

**改动层**：`apps/kodaclaw-web`，依赖 KC-4401 / KC-4402

**功能描述**：

1. **附件按钮**（Paperclip 图标）：
   - 仅当 `modelCapabilities & TextChat (1<<0)` **且** `modelCapabilities & Vision (1<<2)` 同时置位时渲染（纯 ImageGeneration 模型不满足条件）
   - 点击触发 `<input type="file" accept="image/*" multiple hidden>`
   - 文件选择后立即上传（`POST /api/media/upload`），上传中显示加载态

2. **粘贴支持**：
   - textarea `onPaste` 事件，检测 `clipboardData.files` 中的图片文件
   - 触发同一上传流程

3. **附件预览区**（textarea 上方，仅在有附件时渲染）：
   - 缩略图列表（max-width 80px，object-fit cover，圆角）
   - 每张图右上角 ✕ 删除按钮（移除预览 + mediaId）
   - 上传中占位符（spinner + 进度感知）

4. **提交时**：父组件把 `pendingMediaIds[]` 附带到 `sendMessage()` 调用中（`useChatConsole.sendMessage` 需同步新增 `mediaIds?` 参数，透传至 `streamChatEvents({ Message, SessionId, MediaIds })`）

**新增 props**（`ChatComposer.tsx`）：
```typescript
attachedMedia?: AttachedMedia[]          // { mediaId, previewUrl, contentType }[]
onAttachMedia?: (files: File[]) => void  // 触发上传（父管理）
onRemoveMedia?: (mediaId: string) => void
```

**上传状态**（父组件 `App.tsx` 或 `useChatConsole` hook 管理）：
```typescript
type AttachedMedia = {
  mediaId: string
  previewUrl: string     // 本地 URL.createObjectURL 或 /api/media/{id}
  contentType: string
  uploading?: boolean    // 上传中占位
}
```

**提交后**：发送成功后清空 `attachedMedia`

**新增 `api.ts` 函数**：
```typescript
// 后端已有 POST /api/media/upload，前端缺封装
async function uploadMedia(file: File): Promise<MediaMeta>
// 返回 { id, fileName, contentType, sizeBytes, storedAt }
// 用于 KC-4404 上传流程
```

**`useChatConsole` hook 改动**：`sendMessage(text: string, mediaIds?: string[])` 新增可选参数，透传至 `streamChatEvents`。

**CSS**：
```css
.composer__attachment-bar    /* flex row，overflow-x auto，max-height 100px */
.composer__attachment-thumb  /* 单张缩略图容器 */
.composer__attachment-remove /* ✕ 绝对定位右上角 */
.composer__attach-btn        /* Paperclip 按钮，与 submit 对称样式 */
```

**验证**：
- 1 个 Vitest 测试：`supportsVision=false` → 附件按钮不渲染
- 1 个 Vitest 测试：`attachedMedia=[...]` → 预览区渲染对应数量缩略图
- L5 Dogfood：使用 Claude 3.5 Sonnet，拖入一张截图 → Agent 能正确描述图片内容

---

## 非目标（本迭代不做）

- **STT（语音→文字输入）**：需要 `MediaRecorder` API + 转录端点 + Whisper provider 路由，另立 Iter 45
- **TTS（文字→语音输出）**：需要 TTS provider 路由 + 前端音频播放器，另立专项
- **Session 创建时选择模型**：允许用户在新建 session 时指定非默认模型，另立迭代
- **模型中途切换**：本迭代 model pill 只读，不支持 dropdown 切换
- **PDF / 文档上传**：仅支持 image/* 类型，文档解析另立专项
- **图片压缩 / resize**：直接 upload 原图，压缩逻辑留给后续
- **多模态 Channel 输入**：用户通过 Telegram 等渠道发来的图片转发给 Agent，另立专项

---

## 依赖关系

```
KC-4401（Session 暴露 modelCapabilities）
    └─ KC-4403（Model Pill，需要 modelCapabilities 数据源）
    └─ KC-4404（附件按钮可见性，需要 Vision capability flag）

KC-4402（SendMessageRequest 支持 mediaIds）
    └─ KC-4404（提交时附带 mediaIds）
```

实施顺序：KC-4401 + KC-4402 并行 → KC-4403 + KC-4404 并行

---

## 验收标准

1. **Model Pill 展示**：Chat 界面左下角显示当前默认模型名称（如 "claude-3-5-sonnet"），无 Vision 模型时不显示附件按钮
2. **Vision 附件按钮**：切换到 Vision 能力模型后，Paperclip 图标出现；切换到不支持 Vision 的模型后消失（通过修改默认 endpoint 验证）
3. **图片上传**：选择/粘贴图片后在 composer 内显示缩略图；✕ 按钮可移除
4. **图文发送**：附图发送后，Claude 3.5 Sonnet / GPT-4o 能正确理解并描述图片内容（L5 Dogfood）
5. **纯文本降级**：无附件时发送行为与 Iter 43 前完全一致，不引入任何回归
6. **L0**：`dotnet build` 0 错 0 警告；`npm run typecheck` 通过
7. **L2**：Session model info 集成测试 + mediaIds 消息路径集成测试全绿
8. **L3**：`SessionDetail` 契约测试覆盖新字段存在性

---

## KC 条目登记（待写入 BACKLOG）

| 条目 | 模块 | 层级 |
|------|------|------|
| KC-4401 | Contracts + Gateway + Runtime | L2 集成测试 |
| KC-4402 | Contracts + Gateway + Runtime | L1 单元 + L2 集成 |
| KC-4403 | kodaclaw-web | L1 Vitest |
| KC-4404 | kodaclaw-web | L1 Vitest + L5 Dogfood |
