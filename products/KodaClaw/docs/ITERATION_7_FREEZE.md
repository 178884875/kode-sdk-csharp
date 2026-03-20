# Iteration 7 Freeze: Hardening v1

Last updated: 2026-03-19
Status: `Frozen` / `Ready`

这份文档用于冻结 Iteration 7 的安全、稳定性、可恢复性与发布前硬化范围，确保 KodaClaw 在已经具备完整产品面的前提下，最后一轮工作不会重新膨胀成“再做一波新功能”。

## 1. 目标

Iteration 7 的目标不是把 KodaClaw 变成一个云端发布平台，也不是一次性做成企业级安全套件，而是把当前已经完成的本地 Gateway、Web 控制台、Desktop Shell、Plugins、Channels、Automations 与 Canvas，补齐长期常驻所需的最后一批硬化能力：

- 把关键 secrets 从环境变量 / 配置过渡到 OS Keychain 或等价安全存储
- 把 device identity、备份恢复、crash repair、诊断导出做成产品级流程，而不只是开发约定
- 把插件信任、sandbox 风险、更新状态做成可见、可解释、可审计的操作面
- 为最终发布前验收建立一份可重复执行的 hardening acceptance baseline

## 2. 冻结后的首期范围

### 2.1 本期必须做实的能力

- `KC-0701` OS Keychain 集成：统一 secret store abstraction，并至少以 macOS Keychain 作为首个强验收实现
- `KC-0702` Secret 注入迁移：模型、渠道、插件、Gateway token 等敏感凭据从 env / dev-config 迁移为受控 secret reference
- `KC-0703` Device identity 完整化：补齐 fingerprint、rotation、metadata 与 workspace/device 关联信息
- `KC-0704` Backup / export / import：支持 workspace、control-plane、session/control metadata 的导出导入
- `KC-0705` Crash recovery / session repair：把异常退出后的恢复路径从“局部防守”提升为产品级 repair 流程
- `KC-0706` Plugin trust model：在现有 `Untrusted` / `Trusted` 基线之上补齐 `Signed` 占位模型与证据面
- `KC-0707` Update mechanism 脚手架：补齐版本检查、更新通道、发布说明与手动升级入口
- `KC-0708` Sandbox policy UI 与风险提示：把 Local / Docker / plugin/channel permission 边界明确展示给用户
- `KC-0709` Diagnostic bundle 导出：一键导出脱敏的日志、timeline、session metadata、repair/update evidence
- `KC-0710` Hardening 最终验收包：把迁移、恢复、导入导出、权限、更新、诊断全部串成最终验收链路

### 2.2 本期明确冻结的实现策略

- Iteration 7 v1 采用 `Keychain-first, local-first, no-cloud-dependency` 策略
- secrets 的产品事实源从“环境变量名 / 明文文件”迁移为“配置中的 secret reference + OS Keychain 中的真实值”
- 导入导出必须默认排除 raw secrets，只保留 secret refs、脱敏 metadata 和 repair checklist
- crash recovery 采用“先检查、后修复、全程留痕”的保守策略，不做不可解释的自动重放或隐式状态重写
- plugin trust 的 `Signed` 只表示“已有可验证证据与摘要”，不是完整公网 PKI、notarization 或自动信任
- update mechanism v1 只冻结“版本检查 + 手动/引导式升级”，不把 silent auto-update、delta patch、签名分发全部塞进本轮
- sandbox 风险提示优先复用现有 Gateway / Control Plane / Desktop / Web 面，不新建一套独立 security console
- 首个强验收平台继续以 macOS 为主，重点覆盖 Keychain 与 Desktop update-check path；代码层面尽量保持跨平台中立

### 2.3 本期明确非目标

- 云端账号同步、远程备份服务、多人协作控制面
- 完整安装器签名、公网分发、silent auto-update 全家桶
- 硬件级 attestation、HSM、复杂反篡改系统
- 自动恢复所有 live session 并保证跨进程无损续跑
- 把 raw secrets 打包进导出文件以换取“更省事”的迁移体验
- 重写桌面壳或新增另一套安全专属前端框架

## 3. 可复用的现有产品 / SDK 基线

当前实现已经提供了若干可以直接复用的硬化基线：

- `WorkspaceService` 已经生成最小 `DeviceIdentity`，包含 `DeviceId`、`MachineName`、`Platform`、`CreatedAtUtc`
- `KodaClaw.Gateway` 已经具备 loopback-only + Bearer token 基线，Desktop 已具备运行时 bridge，不需要让 Web 直接读持久化 secrets
- `ModelHub`、`ChannelHub`、`PluginHost`、`ControlPlane` 和 `Desktop Shell` 已经形成稳定 API/产品面，适合做 secret reference 迁移而不是重写业务流程
- `MainSessionService` 与审批控制面已经具备 stale approval 的保守处理路径，可作为 crash repair 的一部分继续扩展
- Desktop 已有 tray / notifications / launch target / startup args / protocol routing，可以承载 update、repair、risk 提示等 operator surface

同时 Iteration 7 必须正视以下缺口：

- 当前模型 API key、Gateway token、channel secret、plugin secret 仍停留在 env/dev-config 时代
- 当前 device identity 还是最小记录，缺少 fingerprint、rotation 与 import/repair 语义
- 当前没有统一的 export/import artifact 格式，也没有 restore preflight
- 当前插件只有 `Untrusted` / `Trusted` trust state，缺少签名/摘要证据面
- 当前没有最终对外可交付的 diagnostic bundle，也没有 update status surface
- 当前 sandbox 风险的真实边界只在文档中说明，还没有稳定 UI 提示链路

## 4. 冻结的 contract 决策

### 4.1 Secret store 与 Keychain contract

Iteration 7 必须冻结一层统一的 product secret abstraction，而不是继续让每个模块各自直接读环境变量。

建议冻结以下 contract：

| 类型 | 建议名称 | 说明 |
| --- | --- | --- |
| Record | `SecretRef` | 持久化在配置中的安全引用，至少包含 `provider` / `scope` / `key` / `displayName` |
| Interface | `ISecretStore` | `GetAsync`、`UpsertAsync`、`DeleteAsync`、`DescribeAsync` |
| Record | `SecretDescriptor` | 不含 raw value，只描述来源、最后更新时间、用途、可见标签 |

行为边界冻结为：

- 真实 secret value 只能从 `ISecretStore` 取出，不写回 workspace / SQLite / plugin folder
- Web renderer 永远不直接拿到 raw secrets
- Desktop main process 和 Gateway 只在运行时按需解析 secret refs
- 环境变量只保留给测试、bootstrap、migration fallback，不再作为长期产品事实源
- macOS Keychain 是首个强验收 provider；测试中允许 in-memory / fake provider，禁止生产路径回退到明文文件

### 4.2 Secret 注入迁移策略

- 迁移必须覆盖至少四类秘密：Gateway token、模型 API keys、渠道凭据、插件 secrets
- 迁移流程固定为：发现 legacy source -> 写入 secret store -> 持久化 `SecretRef` -> 生成 migration report
- 如旧值无法解析或用户未提供值，不允许静默跳过；必须生成 repair item / inbox evidence
- 原有 env/dev-config 不强制立刻删除，但不再作为成功迁移后的主读取路径

### 4.3 Device identity 完整化

当前最小 `DeviceIdentity` 需要扩展为可用于导入导出与修复判断的稳定记录。

建议冻结以下行为：

- `deviceId` 继续作为本地安装实例的稳定主键
- 新增 non-reversible `fingerprintHash`，用于判断“这是不是同一设备环境”，而不是做公网唯一标识
- 补齐 `rotatedAtUtc`、`rotationReason`、`lastSeenAtUtc`、`appVersion`、可选 `workspaceRootHash` 等 metadata
- rotation 必须是显式动作或 repair 流程中的可解释动作，不允许后台悄悄改写 identity
- import / restore 若遇到 device mismatch，必须进入 repair checklist，而不是直接覆盖

### 4.4 Backup / export / import contract

- 首期导出格式冻结为单个归档文件，例如 `kodaclaw-backup-<timestamp>.zip`
- 导出必须包含：workspace 协议文件、`control-plane.db`、必要的 session/control metadata、plugin/channel/model descriptors、repair/update metadata
- 导出必须排除：raw secrets、cache、临时文件、构建产物、无关下载缓存
- 归档内必须存在 manifest（如 `backup-manifest.json`），记录版本、时间戳、校验和、包含项与排除项
- import 必须先做 preflight：版本检查、checksum 检查、目标 workspace 冲突检查、secret/device mismatch 检查
- import 结束后必须产出一份 repair checklist，明确哪些 secret refs 仍待补齐、哪些 device/session 状态需要人工确认

### 4.5 Crash recovery / session repair 策略

- KodaClaw 不承诺跨进程无损续跑 live session；Iteration 7 只承诺“可检查、可修复、可解释”的恢复行为
- 启动 repair 时，系统应检查 stale session、pending approval、running automation、plugin runtime state、channel delivery residue
- repair 结果必须留下可审计记录，优先写入 diagnostics / inbox / repair report，而不是只打日志
- 对于无法安全重放的状态，系统应显式转为 `Interrupted` / `Canceled` / `RepairRequired`，而不是假装恢复成功
- import 后可复用同一套 repair engine，避免导入逻辑和 crash 逻辑各写一套修补器

### 4.6 Plugin trust model

- 当前 `PluginTrustState` 从 `Untrusted` / `Trusted` 扩展为 `Untrusted` / `Trusted` / `Signed` 占位模型
- `Signed` 只表示插件包或 manifest 具备本地可验证的摘要/签名证据，不代表自动启用、自动放权或公网可信根认可
- runtime 工具注入规则继续保持保守：插件至少要满足 `Trusted` 或 `Signed`，并同时 `Enabled`、`Running`、具备 `tool` 能力，才允许进入 fresh session
- UI 必须展示 trust source、digest / signer evidence、权限变化风险与最近验证结果

### 4.7 Update mechanism 脚手架

- Desktop / Gateway 必须能够报告 `currentVersion`、`releaseChannel`、`lastCheckedAt`、`latestKnownVersion`、`updateAvailability`
- 首期只要求“检查 + 展示 + 跳转/引导升级”，不要求 silent update、后台下载、差分补丁、自动回滚
- update check 支持使用本地 fixture manifest 做自动化测试，不依赖公网服务才能完成验收
- version-check 结果应可进入 diagnostics 与 operator surface，而不是只停留在 console log

### 4.8 Sandbox policy UI 与风险提示

- Local sandbox、Docker sandbox、plugin permission、channel outbound 风险必须在 Web / Desktop 中有统一文案边界
- 风险提示要明确说明“best effort” 与真实 blast radius，不能把 Docker 或 Local guardrail 描述成绝对隔离
- 高风险权限变化（如 filesystem wide scope、network outbound、background execution、channel send）必须有持续可见的风险 summary
- 能复用现有 approval / inbox / settings 面时优先复用，不新建独立安全数据库

### 4.9 Diagnostic bundle 导出

- diagnostic bundle 默认必须脱敏
- bundle 至少应包含：gateway / desktop / plugin / channel / automation / diagnostics timeline、repair reports、update check state、settings snapshot、session metadata、日志摘要
- 默认不导出 raw secrets，也不默认导出完整聊天正文；如后续允许更深导出，必须是显式 opt-in
- bundle 结构必须可离线解包阅读，并附带 manifest / checksum / redaction summary

## 5. 模块影响面

| 能力 | 主要模块 | 说明 |
| --- | --- | --- |
| Keychain / secret refs | `KodaClaw.Contracts`、`ModelHub`、`ChannelHub`、`PluginHost`、`Gateway`、`kodaclaw-desktop` | 统一 secret source，清理 env/dev-config 依赖 |
| Device identity / repair | `Workspace`、`Gateway`、`Runtime`、`ControlPlane` | 为 crash/import/export 提供同一套 identity + repair 语义 |
| Backup / import / export | `Workspace`、`Storage`、`ControlPlane`、`Gateway` | 定义 snapshot/export manifest 与 restore preflight |
| Plugin trust hardening | `Contracts`、`PluginHost`、`Gateway`、`kodaclaw-web` | 扩展 trust state、证据字段与 UI 风险面 |
| Update / sandbox / diagnostics | `kodaclaw-desktop`、`kodaclaw-web`、`Gateway`、`ControlPlane` | 把 operator-facing hardening 信息暴露到产品面 |

## 6. 推荐并行波次

### Wave 0：planning freeze

- 主线程：`Completed`
- 交付：
  - `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_FREEZE.md`
  - `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0011-hardening-v1.md`
  - backlog / status / README / parallel-plan 同步

### Wave 1：security foundation

- 主线程：先落 shared `SecretRef` / `ISecretStore` / repair-report contract
- Worker A：`KC-0701`
  - 写集：`src/KodaClaw.Contracts`、`src/KodaClaw.Gateway`、`src/KodaClaw.ModelHub`、测试 secret-store fixtures
  - 交付：Keychain abstraction、macOS-first provider、test doubles、基础验证
- Worker B：`KC-0703`
  - 写集：`src/KodaClaw.Contracts`、`src/KodaClaw.Workspace`、`tests/KodaClaw.ContractTests/Workspace`
  - 交付：扩展后的 `DeviceIdentity`、rotation / fingerprint / metadata 基线
- 主线程：收口契约、避免 `KC-0701` 与 `KC-0703` 在 shared contracts 上冲突

### Wave 2：secret migration + portability

- Worker A：`KC-0702`
  - 写集：`src/KodaClaw.ModelHub`、`src/KodaClaw.ChannelHub`、`src/KodaClaw.PluginHost`、`src/KodaClaw.Gateway`
  - 交付：模型 / 渠道 / 插件 / Gateway token 的 secret-ref 迁移、migration report、回归测试
- Worker B：`KC-0704`
  - 写集：`src/KodaClaw.Workspace`、`src/KodaClaw.Storage`、`src/KodaClaw.ControlPlane`、`src/KodaClaw.Gateway`
  - 交付：backup manifest、export/import preflight、restore 流程
- Worker C：`KC-0705`
  - 写集：`src/KodaClaw.Runtime`、`src/KodaClaw.Gateway`、`src/KodaClaw.ControlPlane`
  - 交付：repair engine、startup inspection、异常退出修复证据
- 主线程：整合 migration / import / repair 的共享 evidence 与 UI surface

### Wave 3：operator hardening surfaces

- Worker A：`KC-0706`
  - 写集：`src/KodaClaw.Contracts`、`src/KodaClaw.PluginHost`、`src/KodaClaw.Gateway`、`apps/kodaclaw-web`
  - 交付：plugin `Signed` placeholder、trust evidence、risk UI
- Worker B：`KC-0707`
  - 写集：`apps/kodaclaw-desktop`、必要时 `src/KodaClaw.Gateway`
  - 交付：version-check scaffold、release channel、desktop update surface、fixture-based smoke
- Worker C：`KC-0708`
  - 写集：`apps/kodaclaw-web`、必要时 `apps/kodaclaw-desktop`
  - 交付：sandbox / permission / risk 提示 UI、文案与可见状态
- Worker D：`KC-0709`
  - 写集：`src/KodaClaw.ControlPlane`、`src/KodaClaw.Gateway`、`apps/kodaclaw-web`
  - 交付：diagnostic bundle export、redaction summary、导出入口与验证
- 主线程：收口 operator-facing 信息模型，确保 update / risk / diagnostics 不各自发明 schema

### Wave 4：acceptance

- Worker A：`KC-0710`
  - 写集：`docs/ITERATION_7_ACCEPTANCE_PACK.md`、`tests`、必要的 smoke harness
  - 交付：迁移、导出导入、crash repair、risk prompt、update check、diagnostic bundle 综合验收
- 主线程：执行全量 solution/web/desktop 回归、文档同步、关闭 worker

## 7. 退出标准

Iteration 7 退出标准冻结为：

1. KodaClaw 的核心 secrets 已经默认走 Keychain / secure store，而不是长期依赖 env 或明文配置
2. 模型、渠道、插件、Gateway token 的 secret migration 路径可执行且有 repair evidence
3. `DeviceIdentity` 具备 fingerprint / rotation / metadata，并能被 import / repair 流程正确消费
4. export / import 可以恢复 workspace 与 control-plane 状态，同时不泄露 raw secrets
5. crash recovery 不夸大“自动续跑”能力，但能稳定给出可解释 repair 结果
6. plugin trust model 能表达 `Untrusted` / `Trusted` / `Signed`，并把证据与风险展示到产品面
7. Desktop / Web 能明确展示 update status、sandbox 边界与高风险权限说明
8. diagnostic bundle 可一键导出、默认脱敏、足以支持 support/debug
9. Iteration 1 到 Iteration 6 已冻结的功能回归不被破坏
10. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md` 最终能够冻结本轮综合验证矩阵
