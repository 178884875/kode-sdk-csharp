# kodaclaw-web

KodaClaw 的共享 Web 控制台。当前已经不只是 Iteration 1 的 bootstrap/chat 原型，而是产品级主工作台：

- `Chat Lane`：bootstrap / main chat、SSE 流式消息、会话恢复
- `Inbox / Approval`：inbox 列表、审批决策、状态反馈闭环
- `Sessions / Diagnostics`：session 浏览、timeline、diagnostic bundle export
- `Models / Settings`：模型端点、默认模型、风险提示、`Update Watch`
- `Automations`：自动化定义列表、启停、运行历史入口
- `Channels`：账号、线程、connector 与 delivery/workflow 视图
- `Plugins`：插件列表、详情、trust evidence、运行状态与日志
- `Canvas`：artifact 列表、预览与默认回退视图

当前 UI 保持同一套共享 shell，不再区分“首期独立控制台”和“后期 hardening/desktop 专用界面”。

## 本地运行

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
cp .env.example .env.local
npm install
npm run dev
```

## 环境变量

- `VITE_KODACLAW_GATEWAY_URL`：Gateway 绝对地址；当前控制台默认直接请求该地址，因此浏览器会走真实跨域，Gateway 需保持默认 loopback CORS 或显式放行你的额外 origin
- `VITE_KODACLAW_GATEWAY_TOKEN`：Gateway Bearer token
- Vite dev server 的 `/api` proxy 仍是兜底能力，但只在使用相对路径时生效；当前默认开发方式依赖 Gateway 自身的 CORS 白名单。

## 验证

```bash
npm run build
npm run test
npm run test:e2e
```

当前自动化覆盖包含：

- Playwright：bootstrap、chat resume、Inbox / Approval、Sessions / Diagnostics、Models / Settings、Automations、Canvas、Plugins、Channels、control-plane acceptance
- Vitest：App shell、desk 组件、desktop runtime config adapter、chat stream adapter

## 相关文档

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`
