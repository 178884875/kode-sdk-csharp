# KodaClaw Release Summary

Date: 2026-03-19

## 1. Release Scope

本次交付对应 KodaClaw 当前完整产品形态收口：

- Web 主工作台
- Electron 桌面壳
- Workspace / Gateway / Runtime / Control Plane
- Plugins / Channels / Automations / Canvas
- Iteration 7 Hardening v1 全量收口

## 2. 关键结果

- KodaClaw 已具备独立产品形态，不再是 SDK example 的延伸
- `kodaclaw-web` 已统一承载 8 个 desk
- `kodaclaw-desktop` 已交付 attach / managed-child、tray、通知、deep-link、manual update handoff、diagnostic bundle handoff
- Hardening v1 已完成：secret migration、startup repair、backup export/import preflight、manual-first update、sandbox risk、diagnostic bundle

## 3. 本轮额外 review 修复

本轮 review 之后又完成了 4 个正确性修复：

1. diagnostic bundle 新增 JSON / quoted secret redaction
2. secret migration report 改为分页全量扫描 channel / plugin
3. backup import preflight 拒绝大小写碰撞 entry，并稳定解析相对 `archivePath`
4. Gateway 新增当前目录 `.env` / `.env.local` / `appsettings*.json` bootstrap，Runtime Control 设置 default model 后 chat 无需重启即可激活

同时修正了文档漂移：

- `apps/README.md`
- `apps/kodaclaw-web/README.md`
- `docs/ARCHITECTURE.md`
- `docs/DEV_CONFIG.md`
- `docs/USER_MANUAL.md`
- `docs/OPS_RUNBOOK.md`

## 4. 当前验证结果

后端：

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
  - Contract: 64 passed
  - Integration: 154 passed
  - Unit: 73 passed

Web：

- `npm run build`
- `npm run test`
- `npm run test:e2e`

Desktop：

- `npm run test`
- `npm run smoke:wave3`
- `npm run smoke:managed-gateway`
- `npm run package:smoke`

## 5. 交付文档

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_GUIDE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/MAKEFILE_GUIDE.md`

## 6. 已知边界

当前没有阻塞级已知问题，但仍有一些边界属于“后续可继续增强”：

- 真实 Telegram / webhook 线上链路尚未做生产环境 dogfood
- Keychain / 签名分发 / 真实更新分发链路仍以文档和测试为主
- 当前 update 策略仍然是 manual-first，不包含 silent updater

## 7. 建议后续动作

1. 冻结当前版本并准备提交
2. 如果进入下一轮迭代，先补新的 ADR / freeze 文档
3. 把 Makefile 与文档一致性检查纳入后续 release checklist
