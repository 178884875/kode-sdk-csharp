# KC-W4 FREEZE — 前端服务端状态管理现代化（TanStack Query）

> 冻结日期：2026-04-06
> 状态：FROZEN
> 类型：优化/重构（大改，跨多个迭代）
> 对应迭代：Iter 67（KC-6701/6702）→ 68（KC-6801/6802）→ 69（KC-6901~6903）→ 70（KC-7001~7003）

---

## 背景与动机

当前前端的所有 API 数据一律用 `useState + useEffect + 手动 ref flag` 管理。这一模式在单组件场景下工作，但无法解决"同一份服务端数据被多个组件独立持有"的问题——任意一方发生写操作，其他持有方感知不到，只能靠刷新页面。

典型症状（用户反映）：
- 在 Models Desk 添加新模型后，返回会话页，ChatComposer 模型选择器里看不到新模型
- 在 Settings → Connections 添加渠道账号后，Channels Desk 仍显示旧列表
- 修改 automation，返回后需要手动刷新才能看到最新状态
- 5+ 个 Settings 子 Section 各自独立 `fetchSettings()`，没有共享缓存

根本原因：这类数据不是 UI 状态，而是**服务端缓存**（server state）。服务端缓存需要失效（invalidation）和跨组件共享，React `useState` 做不到。

---

## 解决方案

引入 **TanStack Query v5**（@tanstack/react-query）管理所有来自 Gateway API 的服务端状态。

核心模式：
```
组件 A 调用 useQuery(['models'], fetchModels)   ← 读
组件 B 调用 useMutation(createModelEndpoint, {
  onSuccess: () => queryClient.invalidateQueries({ queryKey: ['models'] })
})                                               ← 写后失效

→ 组件 A 自动重新 fetch，无需任何手动协调
```

---

## 范围（IN SCOPE）

- 所有从 Gateway API 读取的 `useState` 数据迁移到 `useQuery`
- 所有 API 写操作迁移到 `useMutation`，配套 `invalidateQueries`
- 轮询 hooks（`useInboxUnreadCount`、`useDiagnosticsHealth`）改用 `refetchInterval`
- 建立统一的 `queryKeys.ts` 常量文件

## 范围（OUT OF SCOPE）

- **不引入 Zustand**：App.tsx 中的纯客户端状态（`mainDesk`、`pendingModelChange`、`draft`）prop drilling 层级不超过 3 层，暂无必要，后续视复杂度决定
- **不改动 `useChatConsole`**：SSE 流式消息是真正的客户端状态，与服务端缓存模式不同
- **不改动后端**：纯前端重构，API contract 不变
- **不改动 `useGatewaySnapshot`**：它有自己的刷新语义（`refresh()`），暂时保留，后续可迁移

---

## 分阶段计划

| 迭代 | 条目 | 内容 | 解决的问题 |
|-----|------|------|-----------|
| Iter 67 | KC-6701/6702 | 基础设施 + models 迁移 | 模型新增后 chat 不感知（最高优先级） |
| Iter 68 | KC-6801/6802 | settings + channels 迁移 | settings 多处重复 fetch；渠道数据孤岛 |
| Iter 69 | KC-6901~6903 | inbox/approvals + automations + plugins | approve/trigger 后列表不刷新 |
| Iter 70 | KC-7001~7003 | canvas + sessions + workspaceFiles + 收尾 + E2E | 剩余组件 + 全量回归 |

---

## 验收标准

1. `npm run typecheck` 每阶段通过
2. `npm run build` 每阶段通过
3. `npm run test` 单元测试通过（覆盖受影响组件）
4. KC-W4-007 完成后：`npm run test:e2e` 全量 Playwright 通过
5. L5 Dogfood：添加新模型 → 回到 chat → 选择器立即可见，无需刷新
6. L5 Dogfood：Channels Desk 删除账号 → 打开 Settings → Connections，数据同步

---

## 技术约束

- TanStack Query v5 API（`useQuery`/`useMutation` signature 与 v4 不同，注意 `queryKey` 是数组）
- `queryClient.invalidateQueries({ queryKey: ['models'] })` 使用对象参数（v5 风格）
- `QueryClientProvider` 包裹在 `I18nProvider` 内层（`main.tsx`）
- 不使用 `suspense` 模式（现有组件有自己的 loading 处理，避免改动太多）
- 不使用 `optimisticUpdate`（保持简单，mutation 完成后 invalidate 即可）
