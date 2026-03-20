# Commit Message Draft

## 推荐标题

```text
feat(kodaclaw): close hardening review gaps and add delivery manuals
```

## 推荐正文

```text
- fix diagnostic bundle redaction for quoted/JSON secret fragments
- paginate secret migration report scanning across channels/plugins
- reject case-colliding backup archive entries and normalize relative import archive paths
- sync app and architecture docs with current web/desktop product shape
- add user, ops, developer, release, and Makefile documentation
- add KodaClaw product Makefile for backend/web/desktop verification flows
```

## 如果你想拆成两次提交

### 提交 1：代码与测试

```text
fix(kodaclaw): harden diagnostics migration and backup import review gaps
```

```text
- redact quoted and JSON-style secret fragments in diagnostic bundles
- paginate secret migration report repository scans
- reject case-colliding backup entries before extraction
- normalize relative backup import archive paths against target workspace
- add integration coverage for all three fixes
```

### 提交 2：文档与交付材料

```text
docs(kodaclaw): add manuals release notes and Makefile guide
```

```text
- sync web desktop and architecture docs with shipped product shape
- add user ops and developer guides
- add release summary and commit draft
- add product-level Makefile and Makefile documentation
```
