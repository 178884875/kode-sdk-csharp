# Iteration 65 FREEZE — 微信媒体消息：图片 + 文件入站/出站（Phase 1）

> 冻结日期：2026-04-05
> 状态：FROZEN

---

## 目标

让微信（iLink）连接器支持图片和文件类型消息的收发，覆盖最高频的非文本场景。

**用户路径（入站）**：用户在微信发图片/文件 → `WeChatConnector` 解析 item_list type=2/4 → 从 iLink CDN 下载并 AES 解密 → 写入 `IMediaStore` → 附 `MediaReference` 进入 ChannelTurnOrchestrator → Agent 可见媒体内容。

**用户路径（出站）**：Agent 调用 `channel_send(mediaId=...)` → `WeChatConnector.SendAsync` 读取 `MediaAttachments` → 读 `IMediaStore` → AES 加密 → `getuploadurl` 获取 CDN 地址 → 上传 → `sendmessage` 带 `media` 结构发送。

---

## 范围（IN SCOPE）

- **媒体类型**：图片（item_list type=2）和文件（item_list type=4）的入站与出站
- **`IWeChatCdnClient` / `HttpWeChatCdnClient`**：AES-ECB 加解密 + CDN HTTP 上传/下载
- **`IWeChatApiClient.GetUploadUrlAsync`** 扩展（`POST /ilink/bot/getuploadurl`）
- **`WeChatApiContracts.cs`**：补全 `ILinkImageItem`、`ILinkFileItem`、`ILinkMedia`、`ILinkGetUploadUrlRequest/Response`、`ILinkUploadParam`
- **`WeChatConnector` 入站扩展**：`PollLoopAsync` 解析 type=2/4，下载解密写 MediaStore，附 MediaReference
- **`WeChatConnector` 出站扩展**：`SendAsync` 处理 `draft.MediaAttachments`，加密上传并发消息
- **DI 注册**：`IWeChatCdnClient` 在 `ServiceCollectionExtensions.AddKodaClawChannelHub` 中注册
- **L1 测试**：AES 加解密往返；key 两种编码格式（图片 vs 文件）
- **L2 测试**：WeChatConnector 入站/出站媒体集成测试

---

## 非目标（OUT OF SCOPE）

- 语音（type=3）：入站取 `text` 转文字字段，不处理音频附件；出站不支持
- 视频（type=5）：Phase 2 处理
- AMR 格式转码：不引入 FFmpeg 或外部转码服务
- CDN 文件大小限制校验：由 iLink 服务端拒绝，不在客户端做
- 出站语音：Phase 2 或按需决定

---

## 关键设计决策

### 1. AES 模式：ECB + PKCS7（来源：wechat_cdn.py 确认）

iLink 使用 AES-128-**ECB**，不是 CBC，无 IV：
```csharp
using var aes = Aes.Create();
aes.Mode = CipherMode.ECB;
aes.Padding = PaddingMode.PKCS7;
aes.Key = keyBytes;
```

### 2. AES Key 编码有两种格式（隐藏坑）

- **图片（type=2）**：`aes_key = base64(raw 16 bytes)` → `Convert.FromBase64String(aesKey)`
- **文件/语音/视频（type=3/4/5）**：`aes_key = base64(hex string of 16 bytes)` → 先 base64 decode 得到 hex string，再 hex decode

```csharp
private static byte[] ParseAesKey(string aesKeyBase64, bool isImage)
{
    var decoded = Convert.FromBase64String(aesKeyBase64);
    if (isImage) return decoded;                               // raw bytes
    var hex = Encoding.UTF8.GetString(decoded);
    return Convert.FromHexString(hex);                        // hex → bytes
}
```

### 3. CDN 上传结果从响应 Header 取（不在 body）

上传成功后，`encrypt_query_param` 在 HTTP 响应 Header `x-encrypted-param` 中返回，不在 JSON body。

```csharp
var uploadResp = await _httpClient.PostAsync(uploadParam.Url, content, ct);
var encryptQueryParam = uploadResp.Headers.GetValues("x-encrypted-param").First();
```

### 4. CDN 下载 URL 格式

```
GET {_cdnBaseUrl}/{encrypt_query_param}
```

`_cdnBaseUrl` 使用与 API 相同的 `BaseUrl`（`https://ilinkai.weixin.qq.com`），无需独立配置。

### 5. 媒体消息独立发送

每个 `MediaAttachment` 作为独立的 `sendmessage` 请求发送，文字消息紧随其后，与微信原生行为一致。

### 6. getuploadurl 调用时 filesize 是加密后的大小

```
rawsize  = 原始字节数
rawmd5   = 原始字节的 MD5（hex，小写）
filesize = AES-ECB 加密后字节数（因 PKCS7 padding，通常 rawsize 向上取整到 16 的倍数）
aeskey   = 上面生成的 base64 key（格式取决于 media_type）
```

---

## 变更文件清单

| 文件 | 变更 |
|------|------|
| `WeChatApiContracts.cs` | 新增 image/file item + media + getuploadurl DTO |
| `IWeChatApiClient.cs` | 新增 `GetUploadUrlAsync` |
| `HttpWeChatApiClient.cs` | 实现 `GetUploadUrlAsync` |
| `IWeChatCdnClient.cs` | **新建** |
| `HttpWeChatCdnClient.cs` | **新建**（AES-ECB + CDN HTTP） |
| `WeChatConnector.cs` | 扩展 `SendAsync` + 入站 item 解析 |
| `ServiceCollectionExtensions.cs` | 注册 `IWeChatCdnClient` |

---

## 验证命令

```bash
dotnet build KodaClaw.sln                                      # L0: 0 错 0 警告
dotnet test --filter "WeChatCdn"                               # L1: AES 加解密往返
dotnet test --filter "WeChatMedia"                             # L1/L2: 媒体入站/出站
dotnet test KodaClaw.sln -m:1                                  # 全量回归
```

L5：真机发送图片到微信 → 确认 Agent 收到；Agent 发图片 → 确认微信端显示。
