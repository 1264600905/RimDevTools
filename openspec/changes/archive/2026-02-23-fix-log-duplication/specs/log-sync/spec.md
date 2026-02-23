## ADDED Requirements

### Requirement: 日志同步必须包含去重机制
系统在将日志条目写入文件之前，SHALL 检查该条目是否在近期（由最多 500 条记录定义的窗口）内已被记录。

#### Scenario: 记录唯一的日志
- **WHEN** 一条新的、在近期记录中不存在的日志被 `ProcessLog` 方法捕获
- **THEN** 该日志条目 SHALL 被成功写入到 `.log` 文件中
- **AND** 该日志条目 SHALL 被添加到一个内存中的哈希集合以便后续检查

#### Scenario: 过滤重复的日志
- **WHEN** 一条与近期记录中已存在的条目完全相同的日志被 `ProcessLog` 方法捕获
- **THEN** 该日志条目 SHALL **NOT** 被再次写入到 `.log` 文件中
- **AND** 日志文件的大小 SHALL 保持不变

### Requirement: 去重缓存必须有界
用于去重的内存缓存（哈希集合）的大小 SHALL 与日志记录队列（`recentLogs`）的大小保持同步，防止内存无限制增长。

#### Scenario: 缓存大小与队列同步增长
- **WHEN** 一条新日志被添加到 `recentLogs` 队列
- **THEN** 同一条日志记录也 SHALL 被添加到去重哈希集合中

#### Scenario: 缓存大小与队列同步缩减
- **WHEN** 一条旧日志因为超出 500 条限制而从 `recentLogs` 队列中被移除 (Dequeue)
- **THEN** 对应的日志记录也 SHALL 从去重哈希集合中被移除
