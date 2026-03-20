# KodaClaw Makefile 指南

Last updated: 2026-03-19

## 1. 文件位置

- Makefile：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/Makefile`

## 2. 设计目的

这个 Makefile 不是替代文档，而是把当前最常用的后端 / Web / Desktop 验证与启动命令统一成稳定入口，方便：

- 本地开发
- 交付前回归
- 验收执行
- 新成员上手

## 3. 查看可用目标

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw help
```

## 4. 常用目标

### 后端

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw test-solution
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw test-contract
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw test-integration
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw test-unit
```

### Web

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw web-build
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw web-test
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw web-e2e
```

### Desktop

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw desktop-test
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw desktop-wave3-smoke
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw desktop-managed-smoke
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw desktop-package-smoke
```

### 一键验证

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-backend
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-web
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-desktop
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-all
```

## 5. 启动辅助目标

### 启动 Gateway

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw run-gateway
```

可覆盖变量：

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw run-gateway \
  GATEWAY_URL=http://127.0.0.1:5077 \
  WORKSPACE_ROOT=$HOME/.kodaclaw-dev
```

### 启动 Web

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw dev-web
```

### 启动 Desktop

附着模式：

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw dev-desktop-attach
```

ManagedChild 模式：

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw dev-desktop-managed
```

## 6. 推荐用法

### 日常开发

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw test-solution
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw web-test
```

### 交付前回归

```bash
make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-all
```

## 7. 说明

- `test-solution` 强制使用串行基线 `-m:1`
- Makefile 只是命令入口，不会替代 `DEV_CONFIG.md`、`ITERATION_7_ACCEPTANCE_PACK.md`、`USER_MANUAL.md`
- 如果某些目标依赖环境变量或本地依赖，请先参考：
  - `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
  - `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`
