# Iteration 56 FREEZE — TTS 语音合成

**日期**：2026-03-25
**类型**：新功能（新增用户可感知能力）
**范围**：`KodaClaw.ModelHub` / `KodaClaw.Runtime` / `KodaClaw.ChannelHub` / `KodaClaw.Gateway/skills`

---

## 背景与动机

KodaClaw 当前只支持文本和图片两种媒体类型。用户通过渠道（Telegram/飞书）接收到的内容全是文字，
在语音场景（睡前摘要、语音提醒、语音报告）下体验缺失。

`ModelCapabilitySet.TextToSpeech` 已在 Contracts 层预留，`ModelsSettingsDesk` 已有 TTS capability 勾选 UI，
但完整实现链路（Service → Tool → Channel 发送）尚未建立。

MiMo TTS API（`platform.xiaomimimo.com`）兼容 OpenAI `/v1/audio/speech` 格式，
因此同一实现类可同时支持 MiMo 和 OpenAI TTS-1，无需分别实现。

---

## 关键设计决策

### 1. 复用 OpenAI-compatible 接口，不专属 MiMo

`OpenAICompatibleTtsService` 对接 `/v1/audio/speech` 端点，与 OpenAI TTS-1 / MiMo TTS 完全一致。
新增两个预置 ModelEndpoint：`mimo-tts-default`（MiMo V2）和 `openai-tts-1`（OpenAI TTS-1）。

### 2. 不创建 Canvas Artifact

`generate_image` 会在 CanvasDesk 留下可视化条目。`generate_speech` **不创建 CanvasArtifact**，
因为音频没有适合"展示"的画布形态——它的主要消费路径是 `channel_send`，而不是本地浏览。

### 3. 音频格式：MP3，Telegram 用 sendAudio

MiMo / OpenAI TTS 默认输出 `audio/mpeg`（mp3）。
Telegram `sendVoice`（圆形气泡）要求 ogg/opus，需格式转换库；
Telegram `sendAudio`（文件条目）直接接受 mp3，无需转换。

**第一期选择**：Connector 按 content-type 路由，`audio/*` → `sendAudio`，
牺牲"语音气泡"效果，换零依赖实现。ogg 转码留 future。

### 4. 飞书支持，微信不支持

飞书 API 支持发送音频文件（`sendAudio`）。微信个人号 API 需要 AMR 格式，无转码库暂不支持，
`sendAudio` 调用在 WeChatConnector 端直接返回 `UnsupportedMediaType` 错误，上层工具
给出友好提示而非静默失败。

### 5. 流式 TTS 不在本期范围

MiMo/OpenAI 支持 chunk 流式音频，适合将来"边合成边播放"的桌面语音交互场景。
本期只做完整 mp3 落盘后返回 mediaId，不做流式。

---

## 范围（IN SCOPE）

### KC-5601：ModelHub — ISpeechService + 实现 + 预置 endpoint

**新增接口**（`KodaClaw.ModelHub/ISpeechService.cs`）：

```csharp
public record GenerateSpeechResult(string MediaId, string ContentType);

public interface ISpeechService
{
    /// <summary>
    /// 将文字合成为音频，存入 MediaStore，返回 mediaId。
    /// text 可包含 MiMo style 标签（如 &lt;style&gt;速度=快,情感=高兴&lt;/style&gt;）。
    /// </summary>
    Task<GenerateSpeechResult> GenerateSpeechAsync(
        string text,
        string? voice = null,
        string? endpointId = null,
        CancellationToken cancellationToken = default);
}
```

**实现类**（`OpenAICompatibleTtsService.cs`）：
- 调用 `/v1/audio/speech`，body: `{ model, input, voice, response_format: "mp3" }`
- `voice` 缺省时使用 endpoint 上配置的默认 voice，再 fallback `mimo_default` / `alloy`
- 调用 `_registry.ResolveDefaultForAsync(ModelCapabilitySet.TextToSpeech)` 解析默认 endpoint
- 响应流写入 `_mediaStore.StoreAsync("speech.mp3", "audio/mpeg", stream)`
- 异常包装为 `InvalidOperationException`，含 HTTP 状态码和原始响应体

**DI 注册**（`ServiceCollectionExtensions.cs`）：
```csharp
services.TryAddSingleton<ISpeechService, OpenAICompatibleTtsService>();
```

**预置 ModelEndpoint**（在 `DefaultModelEndpoints` 或等价位置新增）：
```json
{
  "id": "mimo-tts-default",
  "name": "MiMo V2 TTS",
  "provider": "MiMo",
  "modelId": "mimo-v2-tts",
  "baseUrl": "https://api.xiaomimimo.com",
  "capabilities": 16,   // TextToSpeech = 1 << 4
  "defaultVoice": "mimo_default",
  "apiKeyEnvironmentVariable": "MIMO_API_KEY"
}
```
```json
{
  "id": "openai-tts-1",
  "name": "OpenAI TTS-1",
  "provider": "OpenAI",
  "modelId": "tts-1",
  "baseUrl": "https://api.openai.com",
  "capabilities": 16,
  "defaultVoice": "alloy",
  "apiKeyEnvironmentVariable": "OPENAI_API_KEY"
}
```

---

### KC-5602：Runtime — GenerateSpeechTool + MediaStore 音频支持

**`GenerateSpeechTool`**：

```
名称: generate_speech
参数:
  - text (required): 要合成的文字，支持 MiMo style 标签
      示例: "今日天气晴好" 或 "<style>速度=快,情感=高兴</style>提醒：会议5分钟后开始"
  - voice (optional): 音色 preset
      可选值: mimo_default | male | female | Nova_default | alloy | echo | fable
      缺省: 使用 endpoint 配置的默认 voice
  - endpoint_id (optional): 指定 TTS endpoint ID，缺省使用 TextToSpeech 默认 endpoint

返回:
  { ok: true, mediaId: "...", mediaUrl: "/api/media/...", contentType: "audio/mpeg", voice: "..." }

错误:
  未配置 TextToSpeech endpoint → 明确错误提示（引导用户在 ModelsSettingsDesk 添加 TTS endpoint）
  API 调用失败 → 透传错误信息
```

诊断事件（Emit）：
```csharp
Emit(context, "speech_generated", new {
    mediaId,
    contentType = "audio/mpeg",
    voice,
    textLength = text.Length,
    endpointId
});
```

**MediaStore 音频支持**：
- `LocalMediaStore.StoreAsync` 目前按 content-type 决定文件扩展名，需补 `audio/mpeg` → `mp3`、`audio/ogg` → `ogg`、`audio/wav` → `wav` 的映射
- `GET /api/media/{id}` 已有的端点无需改动，只需确认响应 `Content-Type` 头正确透传

---

### KC-5603：ChannelHub — Telegram + 飞书 音频发送

**TelegramConnector**：

在 `SendMessageAsync` 中，现有分支已按 content-type 路由（image → `sendPhoto`），新增：
```csharp
case string ct when ct.StartsWith("audio/", StringComparison.OrdinalIgnoreCase):
    await SendAudioAsync(bindingId, content, mediaBytes, contentType, cancellationToken);
    break;
```

`SendAudioAsync` 调用 Telegram Bot API `sendAudio`（multipart/form-data），字段：
- `chat_id`、`audio`（mp3 bytes）、`caption`（消息文字，可选）

**FeishuConnector**：

飞书 `sendAudio` API 路径：先 `uploadFile`（type=audio）获取 file_key，再发 audio message。
新增 `SendAudioAsync` 实现，错误时 fallback 发纯文字提示。

**WeChatConnector**：

音频发送直接返回 `UnsupportedMediaTypeException`（不静默失败）。
上层 `ChannelOutboundGatewayService` 捕获此异常，`channel_send` 工具返回：
```json
{ "ok": false, "error": "微信个人号不支持发送音频文件" }
```

---

### KC-5604：Skills 文档更新

**`koda-channels/SKILL.md`**：

1. `allowed-tools` 行追加 `generate_speech`
2. 媒体附件发送小节增加"语音消息"段落，教 Agent 组合用法：
   ```
   # 先合成语音
   generate_speech(text="今日简报：xxx", voice="female")
   # → mediaId: "media-abc123"

   # 再发送到渠道
   channel_send(bindingId="telegram-personal", content="今日语音简报", mediaId="media-abc123")
   ```
3. 各平台差异表新增"音频"行（Telegram ✓ sendAudio / 飞书 ✓ sendAudio / 微信 ✗ 不支持）
4. MiMo style 标签使用示例（情感/速度控制）

---

### KC-5605：测试

| 层级 | 内容 | 测试文件 |
|------|------|---------|
| L1 | `OpenAICompatibleTtsService`：endpoint 解析、API key 获取、body 构造、mediaStore 写入、错误处理 | `OpenAICompatibleTtsServiceTests` (~8 个) |
| L1 | `GenerateSpeechTool`：参数验证、voice 缺省、无 TTS endpoint 时错误提示、诊断事件 | `GenerateSpeechToolTests` (~6 个) |
| L2 | `TelegramConnector`：audio content-type 路由 → sendAudio；image 路由不受影响回归 | `TelegramConnectorAudioTests` (~4 个) |
| L2 | Gateway 集成：`POST /api/tools/generate_speech`（若有端点）或 tool-via-session 路径 | `GenerateSpeechIntegrationTests` (~3 个) |
| L3 | `GenerateSpeechResult` / `ISpeechService` contract；MiMo + OpenAI preset endpoint JSON schema | `SpeechContractTests` (~4 个) |
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | — |

---

## 非目标（OUT OF SCOPE）

- **Telegram sendVoice（语音气泡）**：需 ogg/opus 转码，留 future
- **微信语音消息**：需 AMR 格式转码，留 future
- **流式 TTS / 实时播放**：留给桌面语音交互场景
- **Web 端音频播放器**：Canvas 不展示音频，无 inline player
- **STT 语音理解**：需 SDK 层 `AudioContent : ContentBlock` 支持，独立规划
- **Canvas 音频 Artifact 类型**：audio 无可视化展示需求
- **InboxApprovalDesk 音频预览**：音频附件在 inbox 中仅显示文件类型图标，不做播放器
- **多音色同时比较**：tool 一次只合成一个 voice

---

## 关键契约

### `generate_speech` 工具调用

```jsonc
// 请求
{
  "text": "今日天气晴好，适合出行。",
  "voice": "female",        // 可选
  "endpoint_id": null       // 可选
}

// 成功响应
{
  "ok": true,
  "mediaId": "media-abc123",
  "mediaUrl": "/api/media/media-abc123",
  "contentType": "audio/mpeg",
  "voice": "female"
}

// 失败响应（无 TTS endpoint）
{
  "ok": false,
  "error": "No enabled TextToSpeech endpoint found. Please configure a TTS model in ModelsSettingsDesk."
}
```

### OpenAI-compatible TTS API body

```json
{
  "model": "mimo-v2-tts",
  "input": "今日天气晴好",
  "voice": "female",
  "response_format": "mp3"
}
```

### 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `KodaClaw.ModelHub` | 新增 `ISpeechService` / `OpenAICompatibleTtsService` / DI 注册 / 2 个预置 endpoint |
| `KodaClaw.Runtime` | 新增 `GenerateSpeechTool`；`MediaStore` 补 audio ext 映射 |
| `KodaClaw.ChannelHub` | `TelegramConnector` + `FeishuConnector` 新增 `sendAudio` 路径；`WeChatConnector` 明确拒绝音频 |
| `KodaClaw.Gateway/skills` | `koda-channels/SKILL.md`：`allowed-tools` 追加 + 音频发送文档 |
| `KodaClaw.Contracts` | `ModelCapabilitySet.TextToSpeech` 已有，无需改动 |

**零改动模块**：`KodaClaw.Workspace`、`KodaClaw.Automation`、`KodaClaw.ControlPlane`、`KodaClaw.Storage`、`CanvasDesk`、`AutomationsDesk`、SDK

---

## 验证矩阵

| 层级 | 验证内容 |
|------|---------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 |
| L1 | TtsService unit（endpoint 解析、body 构造、mediaStore 写入）|
| L1 | GenerateSpeechTool unit（参数校验、诊断事件、错误提示）|
| L2 | Telegram audio content-type 路由 → sendAudio |
| L2 | Gateway 集成：generate_speech 调用链打通 |
| L3 | GenerateSpeechResult contract JSON round-trip |
| L5 | Dogfood：在主会话让 Koda 合成一段语音，发到 Telegram，收到音频文件 |

---

## 依赖与风险

**新增 NuGet 依赖**：无（直接使用 `HttpClient`，同 `OpenAIImageGenerationService`）

**MiMo API Key**：需在 `.env` 设 `MIMO_API_KEY` 或通过 ModelHub Keychain 配置，无 key 时报清晰错误。

**飞书 uploadFile 权限**：飞书 App 需要 `im:message:send_as_bot` + 文件上传权限，
若权限不足 `sendAudio` 降级为文本提示（不中断工具调用）。
