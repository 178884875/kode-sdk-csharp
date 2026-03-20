# Telegram 渠道接入指南

本文说明如何将 Telegram 机器人接入 KodaClaw，包括需要准备什么凭据、到哪里获取、以及接入后的验证步骤。

---

## 你需要准备什么

| 凭据 | 说明 |
|------|------|
| **Bot Token** | Telegram 机器人的身份令牌，格式：`1234567890:AABBccDDeeFFggHHiiJJkkLLmmNNooPPqqRR` |

仅此一项。不需要自己的服务器、不需要域名、不需要 webhook URL，KodaClaw 使用长轮询（long polling）在本地拉取消息。

---

## 第一步：创建机器人，获取 Bot Token

1. 打开 Telegram，搜索 **@BotFather**（官方机器人管理账号，蓝色认证标志）。

2. 发送命令：
   ```
   /newbot
   ```

3. BotFather 会问两个问题：
   - **机器人的显示名称**（Display Name）：随意，如 `My KodaClaw Assistant`
   - **用户名**（Username）：必须以 `bot` 结尾，全局唯一，如 `my_kodaclaw_bot`

4. 创建成功后，BotFather 会回复一段消息，其中包含：
   ```
   Done! Congratulations on your new bot. You will find it at t.me/my_kodaclaw_bot.
   Use this token to access the HTTP API:

   1234567890:AABBccDDeeFFggHHiiJJkkLLmmNNooPPqqRR

   Keep your token secure and store it safely, it can be used by anyone to control your bot.
   ```

   **请复制这串 Token，这就是你需要提供给 KodaClaw 的全部凭据。**

---

## 第二步：在 KodaClaw 中添加渠道账号

打开 KodaClaw Web UI，进入 **Channels**（渠道）管理页面：

1. 点击 **添加渠道账号**（Add Channel Account）。
2. 选择 **Telegram**。
3. 粘贴 Bot Token。
4. 填写账号标签（Label），如 `Personal Bot`，方便日后识别。
5. 点击保存。

KodaClaw 会在后台启动轮询，连接成功后账号状态变为 **Active**。

---

## 第三步：创建线程绑定（Thread Binding）

线程绑定把一个 Telegram 对话（私聊或群聊）与 KodaClaw 会话关联起来。

**线程绑定会在你第一次发消息时自动创建**，无需手动操作。KodaClaw 收到第一条消息后立即建立绑定并启动 Agent 会话。

### Delivery Mode（发送模式）

Delivery Mode 控制 Agent 生成回复后是否需要人工确认才能发出。

| 模式 | 行为 | 适用场景 |
|------|------|---------|
| `AutoSend` | Agent 直接发送，无需确认 | 日常使用、自动化回复 |
| `DraftApproval` | 发送前需在渠道内回复 `ok` 或在 Web UI 确认 | 高风险操作、首次接入验证期 |
| `RequireApproval` | 同 DraftApproval，但不发送通知预览 | 群组等高可见场景 |

**默认值：**
- 私聊（DirectMessage）→ `AutoSend`
- 群组（Group）→ `DraftApproval`

### 在哪里修改 Delivery Mode

目前有两种方式：

**方式 A：通过 API（推荐）**

```bash
# 查看当前所有 thread bindings
curl http://127.0.0.1:5076/api/channels/threads \
  -H "Authorization: Bearer <your-token>"

# 重新发送一次消息让系统重建 binding（简单粗暴）
# 或直接等 Web UI 的 Channels 设置面板支持后从界面改
```

**方式 B：直接修改 SQLite（开发调试用）**

```bash
sqlite3 ~/.kodaclaw/config/control-plane.db \
  "UPDATE thread_bindings SET delivery_rule_id = 'delivery-default-dm' WHERE connector_kind = 'Telegram';"
```

> **注意**：`delivery_rule_id` 的值决定了运行时使用哪套默认规则。目前支持的内置 ID：
> - `delivery-default-dm` → `AutoSend`（私聊默认）
> - `delivery-default-group` → `DraftApproval`（群组默认）

**方式 C（即将支持）：Web UI Channels 设置面板**

Channels Desk 计划在后续迭代中添加 per-binding 的 Delivery Mode 切换 toggle。

---

## 第四步：测试渠道审批流（可选）

如果你将 Delivery Mode 设为 `DraftApproval`，当 Agent 生成回复草稿时：

1. 你会在 Telegram 收到一条审批通知，格式如下：
   ```
   [草稿 #A3F9C1]
   您好，您的问题已处理完毕，请查收...
   回复 ok A3F9C1 发送 · no A3F9C1 取消
   ```

2. 直接在 Telegram 回复以下任意关键词进行操作：

   | 操作 | 关键词 |
   |------|--------|
   | 批准发送 | `ok`、`yes`、`approve`、`send` |
   | 拒绝草稿 | `no`、`cancel`、`reject` |

3. 可附带 6 位十六进制 Token 精确指定草稿（同时有多个待审批时使用）：
   ```
   ok A3F9C1
   no B2E8D4
   ```

4. 批准后，消息会立刻发出；拒绝后，草稿作废并留下审计记录。

---

## 常见问题

### 机器人收不到消息？

- 确认账号状态为 **Active**（KodaClaw Channels 页面）。
- 确认你已向机器人发过至少一条消息（Telegram 要求用户先主动联系机器人才能接收私聊）。
- 对于群组，需先将机器人添加为群成员。

### Token 泄露了怎么办？

打开 BotFather，发送 `/revoke`，选择对应机器人，立即作废旧 Token 并生成新 Token。然后在 KodaClaw 中更新凭据。

### 可以接入多个 Telegram 机器人吗？

可以，每个机器人对应一个 Channel Account，每个 Account 可以绑定多个线程。

### 群组中机器人默认接收不到所有消息？

Telegram 默认在群中屏蔽机器人间的消息。若需要接收所有消息（非 @mention 消息），需要关闭 Privacy Mode：

1. 打开 BotFather，发送 `/mybots`。
2. 选择机器人 → Bot Settings → Group Privacy → Turn off。

注意：关闭后机器人会读取群内所有消息，请按需设置。

---

## 凭据安全提示

- Bot Token 等同于机器人的密码，**不要**提交到代码仓库、公开 Issue 或截图分享。
- KodaClaw 将 Token 存储在 OS Keychain（macOS 上为 Keychain Access），不写入配置文件。
- 如怀疑 Token 泄露，立即通过 BotFather `/revoke` 吊销。
