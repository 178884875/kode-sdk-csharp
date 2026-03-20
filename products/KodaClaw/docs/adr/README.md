# ADR 目录

这里用于存放 KodaClaw 的架构决策记录（Architecture Decision Records）。

当前 ADR：

- `ADR-0001-wave-1-bootstrap-contracts.md`
- `ADR-0002-chat-stream-and-bootstrap-contracts.md`
- `ADR-0003-wave-1-diagnostics-baseline.md`
- `ADR-0004-control-plane-inbox-baseline.md`
- `ADR-0005-runtime-approval-flow.md`
- `ADR-0006-automation-core-baseline.md`
- `ADR-0007-automation-scheduler-and-canvas-baseline.md`
- `ADR-0008-plugin-platform-v1.md`
- `ADR-0009-channels-v1.md`
- `ADR-0010-desktop-shell-v1.md`
- `ADR-0011-hardening-v1.md`

后续新增 ADR 仍建议采用 `ADR-000x-short-title.md` 这类命名方式。

建议流程：

1. 先复制 `docs/templates/ADR_TEMPLATE.md`
2. 在 capability slice 开始前完成 `Proposed`
3. 方案确认后改成 `Accepted`
4. 被替代后标记 `Superseded`
