# Iteration 34 FREEZE — 多模态内容基础层（Multi-Modal Foundation）

冻结日期：2026-03-21

---

## 背景与动机

KodaClaw 目前整个内容模型是纯文本的：`ChatStreamRequest.Message` 是 string，`CanvasArtifact` 只存 HTML/Markdown，`ChannelOutboundDraft` 只发文字，`InboxItem` 只有文字摘要。SDK 的 `ContentBlock` 体系虽已有多态架构（Text / ToolUse / ToolResult / Thinking），但没有 `ImageContent`，Provider 层也没有图片映射。

用户后续希望：
1. Agent 能主动生成图片并投递到 Canvas / Channel / Inbox
2. 从 Telegram 收到的图片/语音能被 Agent 理解
3. 用户在 Chat 里能粘贴图片和 Koda 交互

这三条路径有一个共同前提：**整个系统的内容类型系统必须从 string 升级为支持多媒体**。本迭代作为多模态专项的**基础层**，建立后续所有能力的公共设施。

---

## 范围（IN）

本迭代按依赖顺序分为 5 个阶段，每个阶段独立可验证。

### Phase 1（KC-3401~3403）：ModelCapabilitySet 能力标签系统

**目标**：把"这个模型能做什么"从单一 `SupportsToolCalling: bool` 升级为用户可显式配置的能力集合。

核心原则：**`ModelProviderKind` 只决定怎么调用（协议适配），`Capabilities` 才决定能做什么，永远由用户显式配置，不从 provider 推断。**

- 新增 `ModelCapabilitySet` flags 枚举（TextChat / ToolCalling / Vision / ImageGeneration / TextToSpeech / SpeechToText / Embeddings）
- `ModelEndpoint` 以 `Capabilities: ModelCapabilitySet` 替换 `SupportsToolCalling: bool`，`SupportsToolCalling` 保留为向后兼容的计算属性
- `CreateModelEndpointRequest` / `UpdateModelEndpointRequest` 同步替换
- `IModelRegistryRepository` 新增 `ResolveDefaultForAsync(ModelCapabilitySet required)` 查询接口
- SQLite 迁移：`EnsureColumnExistsAsync` 追加 `capabilities INTEGER`（nullable），旧行由 `MapEndpoint` 从 `supports_tool_calling` 自动推导，首次 Update 时落盘
- Frontend Models Desk：`SupportsToolCalling` 单复选框 → 多能力勾选组（7 项）；模型预设 (`/api/models/presets`) 需同步携带推荐的 `capabilities` 默认值

### Phase 2（KC-3404）：SDK 多模态 ContentBlock 扩展

**目标**：让 Kode Agent SDK 能携带图片内容块，这是所有图片路径（Vision 输入、Agent 生成图片发 Channel 等）的公共前置。

- `Kode.Agent.Sdk/Core/Types/Message.cs` 新增 `ImageContent` record（携带 base64 data 或 URL），加入 `[JsonDerivedType(typeof(ImageContent), "image")]`
- `AnthropicProvider`：`ConvertContentBlock` 追加 `ImageContent → ImageBlockParam`（Anthropic SDK 已有此类型）
- `OpenAIProvider`：`ConvertMessage` 对 user message 支持 `ImageContent → ImageContentPart`
- `MessageQueue.Send` 新增重载 `Send(IReadOnlyList<ContentBlock> parts, SendOptions? opts = null)`，不破坏现有 `Send(string text)` 签名
- 不修改 KodaClaw 层任何文件（该 phase 纯 SDK 层）

### Phase 3（KC-3405）：媒体文件存储 + Gateway /api/media 服务

**目标**：生成的图片/音频需要落盘，前端才能展示，Channel 才能发送。

- `WorkspaceService.EnsureInitializedAsync` 新建 `~/.kodaclaw/media/` 目录
- 新增 `IMediaStore` 接口：`SaveAsync(Stream, contentType, fileName?) → mediaId`、`GetStreamAsync(mediaId) → Stream`、`GetMetaAsync(mediaId) → MediaMeta`
- `LocalMediaStore` 实现：文件存 `media/{YYYY-MM}/{id}.{ext}`，元数据存 `media/index.json`（追加写入）
- 新增 `MediaMeta` contract：`{ Id, ContentType, FileName, SizeBytes, CreatedAt }`
- Gateway 新增 `GET /api/media/{id}` 流式文件服务端点（带 Content-Type header）
- **安全约束**：路径限定在 workspace media 目录内，拒绝 path traversal

### Phase 4（KC-3406~3407）：CanvasArtifact Image kind + generate_image 工具

**目标**：Agent 能主动生成图片，结果可见于 Canvas。这是"Agent 主动给用户发图片"的最短路径。

- `CanvasArtifactKind` 新增 `Image`（Contracts 层）
- 新增 `IGenerationService` 接口（KodaClaw.ModelHub）：`GenerateImageAsync(prompt, endpointId?) → GenerateImageResult { MediaId, Url }`
- `OpenAIImageGenerationService`：调用 DALL-E 3，下载图片 → 存入 `IMediaStore` → 返回 mediaId
- 跨模型路由：工具内部调用 `ResolveDefaultForAsync(ImageGeneration)`，**与 session 当前使用的 chat endpoint 完全隔离**
- 新增内置工具 `generate_image`（KodaClaw.Runtime）：参数 `prompt(required)`, `style?(optional)`；调用 `IGenerationService`；写入 `CanvasArtifact(kind=Image, entryPath=media/{id})`；回复 Agent "已生成图片，Canvas artifact id: {id}"
- 工具注册到 `MainSessionOptions.DefaultTools` 和 `AutomationSessionOptions.Tools`
- Frontend CanvasDesk：对 `kind === Image` 的 artifact 展示 `<img src="/api/media/{id}" />`，不走 iframe

### Phase 5（KC-3408）：ChannelOutboundDraft 媒体投递 + Telegram sendPhoto

**目标**：Agent 通过 Automation/Channel session 生成图片后，能通过 `channel_send` 发到 Telegram，完成"Agent 主动给用户发图片"完整链路。

- `ChannelOutboundDraft` 新增 `MediaAttachments: IReadOnlyList<MediaReference>?`（`MediaReference = { MediaId, ContentType }`）
- `channel_send` 工具新增可选参数 `mediaId?`：若传入，将 mediaId 附到 `ChannelOutboundDraft.MediaAttachments`
- `TelegramConnector.SendAsync`：检测 `MediaAttachments`，若有图片则调用 Telegram `sendPhoto` API，`caption` 使用 `text` 字段
- Approval 流中 `InboxApprovalDesk` 对含 `MediaAttachments` 的 `ChannelDelivery` 待审批条目展示缩略图预览（`/api/media/{id}` img 标签）

---

## 非目标（OUT）

- **Chat Vision 输入**（用户粘贴图片到聊天框）：留待下一迭代（Iter 35）
- **InboxItem 富内容**（Inbox 条目携带图片）：留待 Iter 35
- **Channel 入站媒体**（理解从 Telegram 收到的图片/语音）：留待 Iter 36+
- **TTS / STT**：留待 Iter 36+
- **Stable Diffusion / 非 OpenAI ImageGen 提供商**：本期只做 OpenAI DALL-E 3
- **图片生成参数 UI**（尺寸/风格选择器）：当前工具参数已含 `style?`，UI 配置留待后续
- **媒体存储配额/清理策略**：本期不限制磁盘用量

---

## 关键契约变更

| 类型 | 变更 |
|------|------|
| 新增枚举 | `ModelCapabilitySet`（flags，7 个值） |
| 修改 record | `ModelEndpoint`：`SupportsToolCalling` → `Capabilities`，保留计算属性 |
| 修改 record | `CreateModelEndpointRequest` / `UpdateModelEndpointRequest`：同上 |
| 新增接口方法 | `IModelRegistryRepository.ResolveDefaultForAsync(ModelCapabilitySet)` |
| 新增枚举值 | `CanvasArtifactKind.Image` |
| 修改 record | `ChannelOutboundDraft`：新增 `MediaAttachments?` |
| 新增接口 | `IMediaStore`（KodaClaw.Workspace） |
| 新增 contract | `MediaMeta`、`MediaReference`（KodaClaw.Contracts） |
| 新增接口 | `IGenerationService`（KodaClaw.ModelHub） |
| 新增 SDK 类型 | `ImageContent : ContentBlock`（Kode.Agent.Sdk） |
| 新增 Gateway 端点 | `GET /api/media/{id}` |
| 数据库迁移 | `model_endpoints` 表新增 `capabilities` 列（nullable INTEGER） |

---

## 验证命令

```bash
# Phase 1 - 契约与 ModelHub
dotnet test tests/KodaClaw.ContractTests --filter "ModelCapability"
dotnet test tests/KodaClaw.IntegrationTests --filter "ModelEndpoint"
npm run test -- src/__tests__/models-settings-desk.spec.tsx
npm run typecheck && npm run build

# Phase 2 - SDK
dotnet test tests/Kode.Agent.Tests --filter "ImageContent"
dotnet build

# Phase 3 - 媒体存储
dotnet test tests/KodaClaw.IntegrationTests --filter "MediaStore"

# Phase 4 - CanvasArtifact + 工具
dotnet test tests/KodaClaw.IntegrationTests --filter "GenerateImage"
npm run test -- src/__tests__/canvas-desk.spec.tsx

# Phase 5 - Channel 投递
dotnet test tests/KodaClaw.IntegrationTests --filter "ChannelMedia"

# L0 全量回归
dotnet test KodaClaw.sln -m:1
npm run typecheck && npm run build && npm run test
```

---

## 受影响文件

| 文件 | 类型 | Phase |
|------|------|-------|
| `src/KodaClaw.Contracts/ModelCapabilitySet.cs` | 新增 | 1 |
| `src/KodaClaw.Contracts/ModelEndpoint.cs` | 修改 | 1 |
| `src/KodaClaw.Contracts/CreateModelEndpointRequest.cs` | 修改 | 1 |
| `src/KodaClaw.Contracts/UpdateModelEndpointRequest.cs` | 修改 | 1 |
| `src/KodaClaw.Contracts/IModelRegistryRepository.cs` | 修改 | 1 |
| `src/KodaClaw.Contracts/MediaMeta.cs` | 新增 | 3 |
| `src/KodaClaw.Contracts/MediaReference.cs` | 新增 | 3 |
| `src/KodaClaw.Contracts/CanvasArtifactKind.cs` | 修改 | 4 |
| `src/KodaClaw.Contracts/ChannelOutboundDraft.cs` | 修改 | 5 |
| `src/KodaClaw.ModelHub/SqliteModelRegistryRepository.cs` | 修改 | 1 |
| `src/KodaClaw.ModelHub/IGenerationService.cs` | 新增 | 4 |
| `src/KodaClaw.ModelHub/OpenAIImageGenerationService.cs` | 新增 | 4 |
| `src/KodaClaw.Workspace/IMediaStore.cs` | 新增 | 3 |
| `src/KodaClaw.Workspace/LocalMediaStore.cs` | 新增 | 3 |
| `src/KodaClaw.Workspace/WorkspaceService.cs` | 修改 | 3 |
| `src/KodaClaw.Runtime/GenerateImageTool.cs` | 新增 | 4 |
| `src/KodaClaw.Runtime/ChannelSendTool.cs` | 修改（加 mediaId 参数） | 5 |
| `src/KodaClaw.ChannelHub/TelegramConnector.cs` | 修改 | 5 |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.MediaEndpoints.cs` | 新增 | 3 |
| `src/Kode.Agent.Sdk/Core/Types/Message.cs` | 修改 | 2 |
| `src/Kode.Agent.Sdk/Core/Agent/MessageQueue.cs` | 修改 | 2 |
| `src/Kode.Agent.Sdk/Infrastructure/Providers/AnthropicProvider.cs` | 修改 | 2 |
| `src/Kode.Agent.Sdk/Infrastructure/Providers/OpenAIProvider.cs` | 修改 | 2 |
| `apps/kodaclaw-web/src/components/ModelsSettingsDesk.tsx` | 修改 | 1 |
| `apps/kodaclaw-web/src/components/CanvasDesk.tsx` | 修改 | 4 |
| `apps/kodaclaw-web/src/components/InboxApprovalDesk.tsx` | 修改 | 5 |
