# Iteration 19 FREEZE：Skills 系统集成

## 冻结日期
2026-03-21

## 动机

SDK 层 Skills 系统（SkillsManager、SkillsLoader、SkillsInjector、skill_list/skill_activate/skill_resource 工具）已完整实现，但 KodaClaw 层从未接入：`AgentConfig.Skills` 始终为 null，三个 skills 工具虽已在 `DefaultTools` 中注册但实际无法工作。

Skills 是比 Plugin 更轻量的能力扩展路径——通过写 SKILL.md 即可完成"安装"，无需 MCP 进程，对应 PRODUCT.md §5.5"新记忆或知识接入方式"。打通后，Agent 可在对话中自主创建并激活 skill，形成闭合的自增强循环。

## 用户可感知的变化

- 对话中 Agent 可调用 `skill_list` 查看可用 skills，`skill_activate` 激活特定 skill 获得领域知识增强
- 用户或 Agent 将 SKILL.md 写入 `workspace/skills/` 后，下次 session 自动发现，无需重装
- `~/.agents/skills/` 目录中的 skills 可跨产品共享（与其他 Kode Agent 产品复用）
- Web SkillsDesk 可浏览三层 skills 的来源与状态

## 范围

### 三层加载路径

| 层 | 路径 | 优先级 | 说明 |
|----|------|--------|------|
| Layer 1 | `{AppDir}/skills/` | 最低 | 随二进制打包，只读，KodaClaw 特异知识 |
| Layer 3 | `~/.agents/skills/` | 中 | 跨产品共享，用户全局 |
| Layer 2 | `{workspaceRoot}/workspace/skills/` | 最高 | workspace 特定，支持 Agent 自安装 |

同名 skill 高优先级层覆盖低优先级层（Layer2 > Layer3 > Layer1）。

### 在范围内

| KC | 描述 |
|----|------|
| KC-1901 | 三层路径初始化 + 内置 skill（`koda-workspace`）+ AGENTS.md 引导 |
| KC-1902 | `SkillsConfig` 接入三类 session（Main / Channel / Automation） |
| KC-1903 | `GET /api/skills` API + Web SkillsDesk（只读列表，含层级来源标注） |

### 不在范围内

- Skills 版本管理 / 包管理器
- Web UI 内直接激活 skill（激活是 session 内 Agent 行为，不暴露全局 API）
- skill_activate 的用户审批流（Channel/Automation session 的 RequireApprovalTools 已不含 skill_activate，见 KC-1601）
- `~/.agents/skills/` 的创建或初始化（该目录由用户或其他产品管理，KodaClaw 只读取）

## 关键设计决定

| 问题 | 决定 | 理由 |
|------|------|------|
| Layer 1 是否做 | 做，1 个内置 skill（koda-workspace） | 零配置体验 + 给 Agent 自安装提供格式参考 |
| 路径优先级 | Layer2 > Layer3 > Layer1 | workspace 设置覆盖全局，全局覆盖内置 |
| `~/.agents/skills/` 不存在时 | 跳过，不报错 | Layer 3 是可选的，用户可自行创建 |
| session 类型覆盖 | Main + Channel + Automation 全部接入 | 所有 session 都应能获得领域知识增强 |
| SkillsDesk 激活入口 | 不做（只读） | 激活是 session 内行为，通过 Agent 对话激活更自然 |

## 内置 Skill 规格（Layer 1）

**`koda-workspace/SKILL.md`**

内容方向：
- KodaClaw workspace 文件协议总览（IDENTITY/SOUL/USER/MEMORY/HEARTBEAT 用途）
- 各工具的适用场景（workspace_protocol_update vs workspace_memory_append）
- channel/automation/main session 的上下文差异
- 推荐的记忆写入策略和时机

格式：YAML frontmatter（name/description/license）+ Markdown body

## AGENTS.md 新增引导

```
- Use skill_list to discover available skills from all configured paths.
- Use skill_activate to load a skill's full instructions into the current session context.
- Use skill_resource to access a skill's reference documents or asset files.
- To create a new skill, write a SKILL.md file to workspace/skills/<skill-name>/SKILL.md
  using fs_write, then use skill_list to verify discovery.
```

## 验收标准

1. 主会话中 `skill_list` 返回三层路径的 skills，含 `koda-workspace`
2. `skill_activate koda-workspace` 成功注入 workspace 协议知识到 session context
3. Agent 通过 `fs_write` 写 `workspace/skills/test-skill/SKILL.md`，随后 `skill_list` 能发现
4. Channel DM session 和 Automation session 中 skills 同样可用
5. Web SkillsDesk 显示可用 skills 列表，标注层级（built-in / workspace / global）
6. `dotnet test KodaClaw.sln -m:1` 全量通过
