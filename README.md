# Codex 用量伴随组件

Windows 11 x64 原生 WPF 组件，在 Codex Desktop 窗口下方显示每周剩余用量、重置时间、可用重置次数和当前会员类型。

## 行为

- Windows 登录后由当前用户自启动，无任务栏或托盘图标。
- Codex 打开时显示；移动、缩放、最小化、恢复和关闭时同步跟随。
- 普通窗口底栏与 Codex 重叠 2 个物理像素，不根据任务栏或屏幕边缘回退。
- 跟随由 WinEvent 事件驱动：拖动期间最高 60 FPS，按 CPU 与丢帧率在 60/30/20/15 FPS 间自适应。
- 右侧刷新按钮重建 `codex app-server` 会话；电源按钮只控制伴随底栏展开或收起，不停止后台用量同步。
- 收起后电源按钮位于模型选择器左侧；根据 Codex 窗口逻辑宽度连续计算横向偏移，纵向保持输入工具栏固定中心，不执行截图识别、UI Automation 或周期重定位。
- 展开动画约 180 ms，收起动画约 160 ms；中途反向点击从当前进度继续。
- 收起状态进入全屏时 Codex 填满工作区；全屏内请求展开时不改动 Codex 边界，按钮无提示收起，返回窗口化后自动展开。
- 全屏窗口的正常还原点在动画前冻结，二次点击最大化后仍恢复全屏前的位置与尺寸。
- 可见性偏好与全屏展开策略保存在 `%LOCALAPPDATA%\CodexUsageCompanion\settings.json`；不保存截图、定位缓存、账户或额度数据。
- 文字使用 Codex Windows 字体栈：`Segoe UI, Microsoft YaHei UI`，统一 14 px。
- 仅显示接近每周周期的额度窗口，不显示旧的 5 小时限制。
- 不读取 `~/.codex/auth.json`，不保存令牌、邮箱或 app-server 响应。

## 首次认证

组件使用 Codex CLI 的官方认证。首次使用时在终端执行：

```powershell
codex login
```

完成后点击伴随条右侧的顺时针刷新按钮。

## 构建与安装

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Install.ps1
```

安装位置：`%LOCALAPPDATA%\Programs\CodexUsageCompanion`。

卸载：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Uninstall.ps1
```

## 开发验证

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-cli-home"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\CodexUsageCompanion.sln --no-restore
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\CodexUsageCompanion.sln --no-restore
```
