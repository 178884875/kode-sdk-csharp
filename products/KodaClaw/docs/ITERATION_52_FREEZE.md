# Iteration 52 FREEZE — Skills 内容扩充 + Frontmatter 规范

**日期**：2026-03-25
**类型**：新功能
**范围**：KodaClaw.Gateway / KodaClaw.Contracts / kodaclaw-web

---

## 背景与动机

当前 Skills 系统基础设施已完整（三层发现路径、`skill_list`/`skill_activate` 工具、`GET /api/skills` API、SkillsDesk UI），但只有 1 个内置技能 `koda-workspace`，且 `SKILL.md` 是自由格式——没有结构约束，`GET /api/skills` 只能提取 `name` 和 `description`，UI 无法区分"平台核心技能"与"可选场景技能"。

**两个问题需要同时解决：**

1. **内容缺失**：`koda-automation`、`koda-canvas`、`koda-channels`、`koda-memory` 四个覆盖平台核心能力的技能尚未编写，新用户第一次用 `skill_list` 只能发现 1 个技能。
2. **结构缺失**：`SKILL.md` 没有 `kind`（builtin-core/optional）、`tags`、`requires`、`version` 等机器可读字段，SkillsDesk 无法做分组、过滤、工具依赖提示。

**本迭代目标**：补全 4 个核心内置技能文件 + 规范化 frontmatter + 升级 API 和 UI 展示，不引入任何新的运行时机制。

---

## 范围（IN SCOPE）

### 轨道一：SKILL.md Frontmatter 规范（KC-5201）

**新增字段**（均可选，缺失时降级处理）：

```yaml
---
name: koda-automation
description: HEARTBEAT.md 自动化规则编写指南
version: "1.0"
kind: builtin-core        # builtin-core | optional（缺失时视为 optional）
tags: [automation, heartbeat]
requires: []              # 声明依赖工具名，供 UI 提示；缺失时视为空列表
license: built-in
---
```

`kind` 取值语义：
- `builtin-core`：平台核心能力技能，随包发布，始终可见，SkillsDesk 用优先位置展示
- `optional`：场景化技能，也随包发布，但 UI 做视觉区分（缺省值）

#### SkillDescriptor 位置说明

当前 `SkillDescriptor` 是 `GatewayApp.SkillsEndpoints.cs` 内的 `private sealed record`，本迭代**将其提升到 `KodaClaw.Contracts`**（与 `AutomationDefinition`、`WorkspaceGitCommit` 等保持一致），同时扩展新字段：

```csharp
// KodaClaw.Contracts（新建文件 SkillContracts.cs）
public sealed record SkillDescriptor(
    string Name,
    string? Description,
    string Source,          // "built-in" | "global" | "workspace"
    string Path,
    bool HasResources,
    // 新增
    string Kind,            // "builtin-core" | "optional"，缺失时填 "optional"
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Requires,
    string? Version);
```

`GatewayApp.SkillsEndpoints.cs` 内的 `private sealed record SkillDescriptor` 同步删除，改为引用 `KodaClaw.Contracts` 中的类型。

#### Frontmatter 解析实现说明

当前解析器只做简单行匹配（逐行找 `description:`）。新增字段中 `tags` 和 `requires` 为 YAML inline list 格式，**不引入 `YamlDotNet` 等外部库**，在端点内用轻量辅助方法处理：

```csharp
// 解析 "tags: [automation, heartbeat, scheduling]" → ["automation", "heartbeat", "scheduling"]
// 解析 "tags:" 或字段缺失 → []
static IReadOnlyList<string> ParseInlineList(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return [];
    var trimmed = value.Trim();
    if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        return trimmed[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return [trimmed]; // 单值降级
}
```

只支持 inline list（`[a, b, c]`），不支持 YAML block list（`\n- a\n- b`）。所有内置 `SKILL.md` 文件统一使用 inline 格式，这是规范约束。

**`GET /api/skills` 端点升级**（`GatewayApp.SkillsEndpoints.cs`）：
- 逐行扫描 frontmatter 区块（`---` 到 `---`）读取 `kind`、`tags`、`requires`、`version` 字段
- 缺失字段使用安全默认值（`kind="optional"`, `tags=[]`, `requires=[]`, `version=null`）
- 同步更新 `koda-workspace/SKILL.md` frontmatter 补齐新字段

**`contracts.ts` 对应更新**：

当前 `SkillDescriptor` 是 `SkillsDesk.tsx` 内的**本地 type**（第 11-17 行），`fetchSkills` 也是组件内部私有函数——与其他所有 Desk（automations/models/plugins/mcp）统一走 `contracts.ts` + `api.ts` 的惯例不一致。KC-5201 负责补齐：

- `contracts.ts` 新增 `SkillDescriptor` 接口（当前文件中不存在）
- `api.ts` 新增 `fetchSkills(signal?)` 函数（从 SkillsDesk 本地迁移）

```typescript
// contracts.ts（新增，当前不存在）
export interface SkillDescriptor {
  name: string;
  description?: string;
  source: string;
  path: string;
  hasResources: boolean;
  kind: string;             // "builtin-core" | "optional"
  tags: string[];
  requires: string[];
  version?: string;
}

// api.ts（从 SkillsDesk.tsx 迁移）
export async function fetchSkills(signal?: AbortSignal): Promise<SkillDescriptor[]>
```

### 轨道二：4 个核心内置技能文件（KC-5202）

新增以下文件（纯 Markdown，无代码改动，`.csproj` 的 `skills/**` glob 自动打包）：

#### `koda-automation/SKILL.md`
覆盖：HEARTBEAT.md YAML 语法、cron 表达式格式、`channels:` 推送配置、`delivery-mode: none/auto/approval` 三种模式、常用模板（每日晨报、每周总结、定时提醒）。

#### `koda-canvas/SKILL.md`
覆盖：Canvas artifact 类型（Markdown/HTML/Image）、`canvas_write` 工具参数、多 section 文档结构、图片生成调用方式（`generate_image` + `CanvasArtifactKind.Image`）、artifact 命名规范。

#### `koda-channels/SKILL.md`
覆盖：`channel_send` 工具参数（`bindingId`/`content`/`mediaId?`）、Telegram vs 飞书 vs 微信的格式差异（Markdown 支持程度、消息长度限制）、媒体附件发送、`delivery-mode` 对 channel 推送的影响、BindingId 从哪里获取。

#### `koda-memory/SKILL.md`
覆盖：`MEMORY.md` 索引格式（指针而非内容）、`workspace_memory_append` 何时写（稳定事实 vs 短暂对话）、`workspace_protocol_update target=memory` 结构化写入、旧记忆如何归档、与 USER.md 的区别（画像 vs 事件记录）。

**所有新技能 frontmatter 统一使用 inline list 格式**（与解析器约束一致）：

```yaml
---
name: koda-automation
description: ...
version: "1.0"
kind: builtin-core
tags: [automation, heartbeat, scheduling]
requires: [workspace_protocol_update, workspace_read]
license: built-in
---
```

### 轨道三：SkillsDesk UI 升级（KC-5203）

当前 SkillsDesk 展示：name、description、source badge、path、hasResources badge。

#### 本地类型和函数迁移

KC-5203 负责完成 KC-5201 引入的 contracts.ts/api.ts 迁移对接：
- 删除 `SkillsDesk.tsx` 内的本地 `type SkillDescriptor`（第 11-17 行），改为从 `contracts.ts` 导入
- 删除 `SkillsDesk.tsx` 内的本地 `fetchSkills` 函数（第 26-35 行），改为从 `api.ts` 导入

#### 分组策略

**保留按 source 的三组结构**（built-in / global / workspace），source 语义对用户有意义。kind 作为卡片内的视觉标记，**仅在 built-in 组内按 kind 排序**：builtin-core 优先，optional 在后，同 kind 内按字母序。global / workspace 组维持字母序。

```
built-in 组
  ├── [核心] koda-automation   ← builtin-core 优先
  ├── [核心] koda-canvas
  ├── [核心] koda-channels
  ├── [核心] koda-memory
  ├── [核心] koda-workspace
  └── [可选] ...（未来可选技能）
workspace 组
  └── 字母序，无 kind badge
```

#### 新增展示元素

- **Kind badge**：`builtin-core` 用 amber 品牌色 badge 显示"核心"，`optional` 用中性色显示"可选"
- **Tags**：技能卡片底部展示 tag chips（上限 3 个，超出省略）
- **Requires**：若 `requires` 非空，展示"依赖工具"提示行

#### i18n

`SkillsDesk.tsx` 使用 `useLocaleText()` 内联对象（**不**使用 `app-strings.ts`），新增字段在**组件内 `useLocaleText()` 调用处**的 zh/en 对象中扩展：

```typescript
// 在 useLocaleText({ zh: {...}, en: {...} }) 内扩展
// zh
skillKindBuiltinCore: '核心',
skillKindOptional: '可选',
skillRequiresLabel: '依赖工具',
// en
skillKindBuiltinCore: 'Core',
skillKindOptional: 'Optional',
skillRequiresLabel: 'Requires',
```

#### CSS

新增类全部使用 CSS token 变量，不允许硬编码颜色/间距：

```css
.skill-card__kind-badge           /* kind 标签基础样式 */
.skill-card__kind-badge--core     /* builtin-core: var(--brand-amber) */
.skill-card__kind-badge--optional /* optional: var(--text-tertiary) */
.skill-card__tags                 /* tag 容器 */
.skill-card__tag                  /* 单个 tag chip */
.skill-card__requires             /* 依赖工具行 */
```

---

## 非目标（OUT OF SCOPE）

- 不实现"技能安装"按钮（optional 技能目前也都在内置层，不需要安装动作）
- 不实现 Onboarding 技能推荐步骤（Persona → Skills 映射推荐留后续迭代）
- 不实现"技能市场"或网络下载机制
- 不改变三层发现路径或 `skill_list`/`skill_activate` 工具的行为
- 不增加 `requires` 的运行时校验（仅用于 UI 提示，不阻塞激活）
- 不修改 `KodaClaw.Workspace/WorkspaceService.cs` 的 `GetSkillsPaths()` 逻辑
- 不支持 YAML block list 格式（`\n- item`），内置技能统一使用 inline list

---

## 关键契约

### SkillDescriptor（C# + TypeScript）

```csharp
// KodaClaw.Contracts/SkillContracts.cs
public sealed record SkillDescriptor(
    string Name,
    string? Description,
    string Source,
    string Path,
    bool HasResources,
    string Kind,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Requires,
    string? Version);
```

```typescript
// contracts.ts
export interface SkillDescriptor {
  name: string;
  description?: string;
  source: string;
  path: string;
  hasResources: boolean;
  kind: string;
  tags: string[];
  requires: string[];
  version?: string;
}
```

### SKILL.md Frontmatter 完整示例

```yaml
---
name: koda-automation
description: HEARTBEAT.md 自动化规则编写指南——语法、调度、渠道推送
version: "1.0"
kind: builtin-core
tags: [automation, heartbeat, scheduling]
requires: [workspace_protocol_update, workspace_read]
license: built-in
---
```

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `KodaClaw.Contracts` | 新建 `SkillContracts.cs`，将 `SkillDescriptor` 从 Gateway 移入并扩展 4 个字段 |
| `KodaClaw.Gateway` | `GatewayApp.SkillsEndpoints.cs` 删除内部 record，引用 Contracts 类型，扩展 frontmatter 解析；`skills/koda-workspace/SKILL.md` 补齐 frontmatter |
| `KodaClaw.Gateway/skills/` | 新增 4 个技能目录及 `SKILL.md` 文件 |
| `apps/kodaclaw-web` | `contracts.ts` 新增 `SkillDescriptor` 接口（当前不存在）；`api.ts` 新增 `fetchSkills`（从 SkillsDesk 本地迁移）；`SkillsDesk.tsx` 删除本地类型和 fetch 函数、展示升级、`useLocaleText` 扩展 3 个 i18n 键；CSS 新增 6 个 token 类 |

---

## 验证矩阵

| 层级 | 验证内容 | 测试项目 | 工具 |
|------|---------|---------|------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | — | dotnet / tsc |
| L1 | `SkillFrontmatterParserTests`：完整 frontmatter 全字段解析；缺失 kind → "optional"；缺失 tags/requires → `[]`；缺失 version → null；inline list `[a, b, c]` 正确拆分 | `KodaClaw.UnitTests` | xUnit |
| L2 | `SkillsEndpointIntegrationTests`：`GET /api/skills` 返回 5 个技能；koda-automation `kind="builtin-core"`；tags/requires 非空且为数组 | `KodaClaw.IntegrationTests` | WebApplicationFactory |
| L3 | `SkillDescriptorContractTests`：`SkillDescriptor` JSON round-trip 含全部新字段；新字段缺失时反序列化不抛异常（安全降级） | `KodaClaw.ContractTests` | FluentAssertions |
| L5 | Dogfood：主会话 `skill_list` 发现 5 个技能；`skill_activate koda-automation` 后 Agent 能正确回答 HEARTBEAT.md 语法；SkillsDesk 展示 amber "核心" badge 和 tags | — | 真实 Gateway + Web |
