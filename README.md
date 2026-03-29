# 剪贴板历史（ClipboardHistory）

基于需求文档 `剪贴板历史管理器-需求文档.md` 实现的 Windows 桌面应用：**C#、.NET 8、WPF**，本地 **SQLite** 持久化。

## 环境

- Windows 10 / 11（x64）
- [.NET SDK](https://dotnet.microsoft.com/download)（当前工程目标框架为 **net10.0-windows**，与已安装的 .NET 10 运行时一致；若需改为 LTS，可将 `ClipboardHistory.csproj` 中的 `TargetFramework` 改为 `net8.0-windows` 并安装 [.NET 8 运行时](https://dotnet.microsoft.com/download/dotnet/8.0)）

## 生成与运行

```bash
dotnet restore ClipboardHistory.sln
dotnet build ClipboardHistory.sln -c Release
dotnet run --project src/ClipboardHistory/ClipboardHistory.csproj -c Release
```

或在 Visual Studio 中打开 `ClipboardHistory.sln`，将 `ClipboardHistory` 设为启动项目后 F5。

## 功能概览

| 能力 | 说明 |
|------|------|
| 监听剪贴板 | 使用 `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`，仅捕获 **Unicode 文本** |
| 防自触发 | 通过 `ClipboardSuppression` 在应用内 `Clipboard.SetText` 前后压制一次监听闭环 |
| 连续去重 | 可在设置中关闭；默认跳过与上一条完全相同的连续复制 |
| 列表 / 卡片 | 工具栏切换；影响预览文本换行与最大高度（虚拟化列表保持流畅） |
| 搜索 | 输入防抖后在线程池执行 SQL `LIKE`，再在 UI 线程刷新集合 |
| 删除 / 编辑 | 支持多选删除；编辑使用对话框 |
| 复制 / 追加合并 | 复制为「选中序列中第一条」写回剪贴板；**追加合并**为方案 A：多条选中按 `AppendSeparator` 拼成一段文本再 `SetText` |
| 托盘 | 关闭窗口时（若启用「最小化到托盘」）隐藏到托盘；托盘可退出 |
| 全局热键 | 默认 **Ctrl+Shift+V** 显示/隐藏主窗口，可在设置 JSON 中修改 |

## 设置文件

路径：`%LocalAppData%\ClipboardHistory\settings.json`

常用字段：

- `MaxHistoryEntries`：最多保留条数（默认 500）
- `MaxEntryLength`：单条最大字符数（超出截断）
- `DedupeConsecutive`：是否跳过连续重复
- `AppendSeparator`：追加合并分隔符（默认系统换行）
- `CloseOnCopy`：复制/合并写回剪贴板后是否隐藏窗口
- `MinimizeToTray`：点关闭是否缩小到托盘（默认 true）
- `ToggleHotkeyModifiers` / `ToggleHotkeyVk`：全局热键（与 Win32 `RegisterHotKey` 一致）

数据库：`%LocalAppData%\ClipboardHistory\history.db`（单连接 + `SemaphoreSlim` 串行访问）。

## 架构说明

- **MVVM**：`MainViewModel` + `CommunityToolkit.Mvvm`
- **服务**：`HistoryRepository`、`SettingsService`、`ClipboardMonitorService`、`HotkeyService`
- **虚拟化**：`ListBox` + `VirtualizingStackPanel` + `VirtualizationMode=Recycling`
- **Hwnd**：`MainWindow` 上挂接 `HwndSource` 处理剪贴板与热键消息

## 许可证

按你的仓库策略自行补充（当前未附 LICENSE）。
