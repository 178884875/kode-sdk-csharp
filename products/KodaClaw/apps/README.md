# KodaClaw `apps/`

这里存放 KodaClaw 的前端与桌面壳工程。

当前已落地的应用：

- `kodaclaw-web`：共享主控制台，已交付 `Chat Lane`、`Inbox / Approval`、`Sessions / Diagnostics`、`Models / Settings`、`Automations`、`Channels`、`Plugins`、`Canvas` 八个 desk，并已通过 `npm run build`、`npm run test`、`npm run test:e2e`
- `kodaclaw-desktop`：Electron 桌面壳，已交付 Gateway attach/managed-child、tray / menu / shortcut、通知轮询、deep-link / launch target、manual update handoff、diagnostic bundle handoff，并已通过 `npm run test`、`npm run smoke:wave3`、`npm run smoke:managed-gateway`、`npm run package:smoke`
