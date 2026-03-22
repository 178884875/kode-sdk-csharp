# Iteration 29 FREEZE：UI/UX 系统性一致性修复 + Workspace 文件完整性

冻结日期：2026-03-21

---

## 背景与动机

KC-W3（Iter 28）完成了 Design Token 系统和 Lucide 图标迁移，但仍存在以下系统性问题：

1. **onboarding.css 完全未使用 CSS 变量**：`#D97706`、`#b45309`、`#f0f7ff` 等硬编码颜色 8+ 处，暗色主题下完全无效
2. **多处内联 CSSProperties 魔法数字**：`ChannelsDesk`、`SkillsDesk`、`CanvasDesk` 中存在 `marginTop: 12`、`gap: 10`、`borderRadius: 18` 等不受 token 系统管控的值
3. **字体大小不规范**：部分组件仍使用 `0.88rem`、`0.78rem` 等裸数值而非 `var(--font-size-sm)` 等 token
4. **WorkspaceIdentityEditor 无加载骨架**：初始挂载时直接显示空 textarea，内容加载前 UX 不完整
5. **PRODUCT.md 中 `MEMORY.md` 和 `HEARTBEAT.md` 无 UI 编辑入口**：后端 `ResolveWorkspaceTarget` 仅支持 identity/soul/user，MEMORY.md（长期记忆）和 HEARTBEAT.md（自动化规则）虽然是 Workspace 协议的核心文件，但 Settings Desk 无对应编辑面板
6. **CLAUDE.md 关键文件描述过时**：仍引用已删除的 `shell-v2/` 和 `V2Shell` 路径

---

## 范围（IN）

### A：CSS 一致性修复（纯前端，零后端）

**A1：onboarding.css 完整迁移到 CSS 变量**
- 替换所有硬编码颜色（`#D97706 → var(--accent)`、`#b45309 → var(--accent-hover)`）
- 替换硬编码背景（`#f5f5f5 → var(--bg-secondary)`）
- 替换语义色（success/error → `var(--success)`/`var(--error)` + soft 变体）
- 结果：onboarding 在暗色模式下正确渲染

**A2：移除内联 CSSProperties 魔法数字**
- `ChannelsDesk.tsx`：将 `toolbarStyle`、`threadButtonStyle` 等内联对象移入 CSS 类（`.channels-toolbar`、`.channel-thread-btn` 等）
- `SkillsDesk.tsx`：内联 `toolbarStyle`、`listStyle` 移入 `.skills-toolbar`、`.skills-list`
- `CanvasDesk.tsx`：`style={{ height: 500 }}` 的 iframe 改为 CSS class `.canvas-preview-frame`

**A3：字体/间距 token 规范化**
- `WorkspaceIdentityEditor`、`ConnectionsSection`、`SystemSection` 中残留的裸 `font-size` 值改用 `var(--font-size-*)`
- `settings-file-textarea` 使用 `var(--font-size-xs)` 而非硬编码 `12px`

### B：WorkspaceIdentityEditor 加载优化（纯前端）

**B1：初始 Skeleton 加载态**
- 每个文件编辑区（IDENTITY.md / SOUL.md / USER.md）挂载时显示 3 行 Skeleton，等待 `GET /api/workspace/file` 返回后替换为 textarea
- 使用现有 `<Skeleton />` 组件

### C：Workspace 文件完整性（前端 + 后端协作）

**C1：后端扩展 `ResolveWorkspaceTarget`**
- 在 `GatewayApp.WorkspaceEndpoints.cs` 中新增：
  - `"memory"` → `KodaClawWorkspaceLayout.MemoryFile`（读写，MEMORY.md）
  - `"heartbeat"` → `KodaClawWorkspaceLayout.HeartbeatFile`（读写，HEARTBEAT.md；写入后 HeartbeatFileWatcherHostedService 自动热更）
- 错误提示更新：`Allowed: identity, soul, user, memory, heartbeat`

**C2：Settings Desk 新增 Memory 编辑区**
- 新建 `src/components/settings/MemorySection.tsx`
  - 编辑 `MEMORY.md`（长期记忆索引）
  - 使用现有 `useWorkspaceFile("memory")` 模式（复用 WorkspaceIdentityEditor 的 hook 逻辑）
  - 显示提示：MEMORY.md 是 Koda 跨会话记忆的索引，可手动整理
- 新建 `src/components/settings/HeartbeatSection.tsx`（**只读预览**）
  - 展示 `HEARTBEAT.md` 当前内容（只读 textarea）
  - 说明文字：HEARTBEAT.md 由 Koda 在对话中自动更新，也可在此查看

**C3：Settings Desk 布局更新**
- 在 Settings Desk 顶部重组为分区：
  - 身份文件（IDENTITY.md / SOUL.md / USER.md）—— 已有
  - 记忆（MEMORY.md）—— 新增
  - 自动化规则预览（HEARTBEAT.md，只读）—— 新增
  - 连接 —— 已有
  - 偏好 —— 已有
  - 系统 —— 已有

### D：CLAUDE.md 文档修正

- 修正关键文件表格：删除 `shell-v2/` 和 `V2Shell` 的引用，更新为当前实际文件路径（`AppShell.tsx`、`Sidebar.tsx`、`MainContent.tsx`）

---

## 非目标（OUT）

- **Tool call 可视化**：需要 SSE 协议扩展（`tool_start`/`tool_end` 事件），留下一个迭代
- **ChannelsDesk 拆分重构**：1700+ 行组件状态机重构，单独专项
- **i18n 文本文件抽取**：将各组件内嵌的 zh/en 对象抽取到 locales 文件，单独专项
- **`useFetch` 通用 hook**：各组件 loading/error/refresh 逻辑统一，单独专项

---

## 关键契约变更

| 变更 | 详情 |
|------|------|
| `GET /api/workspace/file?target=memory` | 新增，返回 `WorkspaceFileResponse` |
| `PUT /api/workspace/file?target=memory` | 新增，接受 `WorkspaceFileUpdateRequest` |
| `GET /api/workspace/file?target=heartbeat` | 新增（只读语义，但实现上允许写入） |
| `ResolveWorkspaceTarget` 错误消息 | 更新为 `Allowed: identity, soul, user, memory, heartbeat` |

---

## 验证命令

```bash
# L0：编译 + 类型检查
cd apps/kodaclaw-web && npm run typecheck && npm run build
dotnet build products/KodaClaw/KodaClaw.sln

# L1：前端单元测试
npm run test

# L2：后端集成测试（workspace endpoints）
dotnet test products/KodaClaw/KodaClaw.sln --filter "Workspace" -m:1

# L5 手动验收
make run-gateway
npm run dev
# 验收点：
# 1. Onboarding 在系统暗色模式下样式正常（amber/green/error 颜色响应主题）
# 2. Settings Desk → MEMORY.md 可编辑保存
# 3. Settings Desk → HEARTBEAT.md 显示当前内容（只读）
# 4. WorkspaceIdentityEditor 初始显示 Skeleton，加载完成后显示内容
```

---

## 受影响文件

| 文件 | 类型 |
|------|------|
| `src/onboarding/onboarding.css` | 修改（全量 CSS 变量化） |
| `src/components/ChannelsDesk.tsx` | 修改（移除内联样式） |
| `src/components/SkillsDesk.tsx` | 修改（移除内联样式） |
| `src/components/CanvasDesk.tsx` | 修改（移除内联样式） |
| `src/components/settings/WorkspaceIdentityEditor.tsx` | 修改（加 Skeleton） |
| `src/components/settings/MemorySection.tsx` | 新增 |
| `src/components/settings/HeartbeatSection.tsx` | 新增 |
| `src/components/SettingsDesk.tsx` | 修改（新增两个 section） |
| `src/shell/app-shell.css` | 修改（新增内联样式对应的 CSS 类） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.WorkspaceEndpoints.cs` | 修改（扩展 ResolveWorkspaceTarget） |
| `CLAUDE.md` | 修改（关键文件表格更新） |
