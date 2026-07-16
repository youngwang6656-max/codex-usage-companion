# Codex 页面切换卡顿修复设计

## 问题与证据

Codex 页面切换时仍会显示其原生蓝色加载圈，但伴随组件运行时卡顿更明显。A/B 诊断显示，组件自身没有可测 CPU 峰值；然而页面切换期间，组件专属 `codex app-server` 子进程出现明显 I/O，静止对照阶段为零。

前台 HWND 连续采样证明页面切换期间顶层窗口没有变化，因此窗口跟随和 Z 序不是根因。本机 Codex 官方 app-server schema 说明 `account/rateLimits/updated` 通知已携带可合并的稀疏额度快照；当前实现却丢弃通知参数，并在每条通知后重新调用 `account/read` 与 `account/rateLimits/read`。这些重复 RPC 与 Codex 页面加载并发，放大了卡顿。

## 方案选择

- 方案 A：忽略额度更新通知，仅保留 60 秒轮询。最省资源，但额度变化最多延迟一分钟。
- 方案 B：直接解析并合并通知中的稀疏额度快照，不再为通知发起 RPC。保持实时显示且消除重复读取。采用此方案。
- 方案 C：合并通知后延迟重新读取。可以减少请求次数，但仍会与页面加载竞争资源。

## 行为设计

新增独立、可测试的通知合并路径：

- app-server 协议层转发 `account/rateLimits/updated` 的原始 `params` JSON，而不是无参数事件。
- 用量客户端复用现有协议解析器解析通知快照，并将非空会员、每周额度与重置时间合并到当前快照。
- 通知未提供的会员、重置次数或每周窗口不得清空已有值。
- 通知处理不调用 `account/read` 或 `account/rateLimits/read`。
- 最小化、隐藏后恢复继续立即刷新。
- 60 秒定时刷新和手动刷新保持不变。
- 窗口移动、缩放、全屏、展开/收起和 60 FPS 快速跟随路径不变。

## 结构调整

- `ICodexAppServerSession.RateLimitsUpdated` 携带通知 JSON。
- `CodexAppServerProtocolSession` 克隆并转发通知参数；`StdioCodexAppServerSession` 保持透传。
- `CodexUsageClient` 同步合并通知数据并发布快照，只有显式刷新、恢复和定时器才执行完整读取。
- `CodexWindowTracker`、伴随窗口定位与 Z 序逻辑不修改。

## 测试与验收

- 协议层只转发目标通知，并保留完整参数 JSON。
- 额度通知更新剩余百分比时，账户和额度读取次数保持不变。
- 稀疏通知缺失会员和重置次数时保留旧值。
- 最小化/隐藏恢复、60 秒定时刷新和手动刷新行为不回归。
- 完整 Release 测试、构建、覆盖安装和实机页面切换 A/B 复测通过后更新 GitHub。
