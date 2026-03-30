# Diagnostics 缓存优化方案 v2

## 背景
当前 FileDiagnosticsService 和 InMemoryDiagnosticsService 的缓存策略存在两个结构性缺陷：
1. debug 事件淹没 error/warning（MetricsBridge 高频写入 debug，500 条缓存 80%+ 是 debug）
2. 查询只查内存不查文件（sinceMinutes=1440 实际只查缓存 500 条）

## 改动 A：Record 方法——两级优先缓存淘汰

将单队列 FIFO 改为两级优先淘汰：

- error/critical/warning → 高优先级队列 `_highPriorityCache`，至少保留 200 条
- info/debug → 低优先级队列 `_lowPriorityCache`，优先被淘汰
- 总缓存上限 1000 条

具体改动：
- 新增 `IsHighPriority(string level)` 辅助方法判断 error/critical/warning
- 移除 `_cache` 单一 List，改为 `_highPriorityCache` 和 `_lowPriorityCache`
- Record 按优先级分流到对应队列
- 淘汰：总条数 > 1000 时先淘汰 _lowPriorityCache 最早的；_lowPriorityCache 空了再淘汰 _highPriorityCache
- _highPriorityCache > 200 时也开始淘汰
- Query/GetStats：_highPriorityCache.Concat(_lowPriorityCache)
- LoadFromFile：按 IsHighPriority 分流
- ClearAsync：两个都清理
- Subscribe/Dispose 不变
