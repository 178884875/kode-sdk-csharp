# ADR 模板

> 文件建议命名：`ADR-0001-short-title.md`

# ADR-XXXX：标题

## 状态

- `Proposed`
- `Accepted`
- `Superseded`
- `Rejected`

## 日期

- YYYY-MM-DD

## 背景

说明当前遇到的问题、约束、上下文边界，以及为什么需要做这个决策。

建议回答：

- 这个决策是为了解决什么问题
- 为什么现在必须做
- 涉及哪些模块或产品能力
- 有哪些非功能性约束（安全、恢复、兼容、性能、发布）

## 决策

明确写出本次选择的方案。

建议格式：

- 我们决定……
- 该方案适用于……
- 该方案暂不覆盖……

## 备选方案

至少列出 2 个备选方案，并说明为什么不选：

### 方案 A

- 描述
- 优点
- 缺点

### 方案 B

- 描述
- 优点
- 缺点

## 后果

说明这个决策带来的影响：

- 正向影响
- 成本
- 风险
- 对后续迭代的限制

## 验证方式

说明怎么证明这个决策是可行的：

- L0/L1/L2/L3/L4/L5 哪几层验证
- 需要哪些 fixture、golden files、dogfood 环境
- 什么结果算通过

## 关联文档

- `PRODUCT.md`
- `ARCHITECTURE.md`
- `WORKSPACE_SPEC.md`
- `PLUGIN_SPEC.md`
- `CHANNEL_SPEC.md`
- `ENGINEERING_PLAYBOOK.md`

## 关联切片 / PR

- Capability Slice:
- PR:
