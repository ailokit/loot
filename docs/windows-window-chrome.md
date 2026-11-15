# Windows 自定义窗口装饰

Windows 使用 Avalonia 12 的 `WindowDecorations.Full`、扩展客户区及
`WindowDecorationsTheme`，通过 `WindowsWindowDecorations.axaml` 绘制标题栏。
`LootWindowChrome.axaml` 继续负责其他平台，macOS 的原生红绿灯配置保持原样。

保留 `Full` 很关键：Avalonia 的 Win32 后端由此维护 `WS_CAPTION`、
`WS_SYSMENU`、`WS_THICKFRAME` 和与窗口能力相符的最小化/最大化样式。
旧实现使用 `None` 并在 `OnOpened` 中调用 `SetWindowLongPtr`，但是 Win32
后端在切换窗口状态时会重新计算样式，导致补丁被覆盖，无法持续保留 DWM 动画。
现在边框、阴影、缩放和状态变化均交给框架，不再直接修改 HWND 样式或 DWM 边距。
圆角也使用公开的 `Win32Properties.SetWindowCornerPreference`。

标题栏按钮使用框架约定的 `PART_MinimizeButton`、`PART_MaximizeButton`、
`PART_CloseButton`，由 `WindowDrawnDecorations` 注册点击及能力变化处理。
`WindowDecorationProperties.ElementRole="MaximizeButton"` 在 Win32 下映射为
`HTMAXBUTTON`，使系统识别最大化/还原区域并提供 Windows 11 Snap Layouts。
最大化按钮不设置 Avalonia ToolTip，以免与系统悬停弹框重叠。
应用的关闭即隐藏策略仍由 `LootWindow.OnClosing` 统一处理。

装饰层的 DataContext 通过一个普通上下文对象引用所属窗口；自定义标题栏控件恢复使用窗口的 ViewModel。
正文通过 `WindowDecorationMargin` 获取框架计算的边距，避免重复标题栏、遮挡正文，
也能在全屏及 DPI 变化时更新布局。

## 自动检查

`WindowsWindowChromeTests` 在 Windows 的真实 Win32 后端创建 HWND，检查：

- 最大化、最小化、关闭、拖动区域和自定义控件的原生命中测试。
- 最大化、还原、最小化、隐藏后重新显示以及改变窗口能力后原生样式仍正确。
- 标准按钮部件的点击处理、正文边距、自定义控件的 ViewModel 和所属窗口。

此检查在其他系统上跳过。命令：

```sh
dotnet test src/Loot.Tests/Loot.Tests.csproj --filter FullyQualifiedName~WindowsWindowChromeTests
```

## Windows 11 人工验收

系统启用「贴靠窗口」及「悬停在最大化按钮上时显示贴靠布局」、窗口动画后，验证：

1. 普通状态和最大化状态分别悬停最大化/还原按钮，出现系统分屏弹框；选择区域能够贴靠。
2. 点击标题栏按钮及任务栏，反复最小化、最大化和还原，观察系统动画。
3. 双击标题栏、拖动最大化窗口还原、拖到屏幕边缘贴靠及右击标题栏打开系统菜单。
4. 在 100%、150%、200% DPI 和跨显示器移动后重复前述操作，确认命中区域和正文边距正确。
5. 聊天窗口的置顶、设置按钮可用，关闭后可从托盘重新显示；确认、更新等非缩放窗口无最大化入口。
6. 浅色、深色、全屏和退出全屏后，标题栏无重复、正文无遮挡。
7. macOS 原生红绿灯、标题和自定义按钮，以及 Linux 原有标题栏和缩放行为保持一致。

自动检查验证系统动画和 Snap 所需的框架路径与原生条件；弹框显示及视觉动画仍需 Windows 11 实机验收。

## 依据

- [Avalonia Windows 自定义标题栏文档](https://docs.avaloniaui.net/docs/platform-specific-guides/windows/)
- [Avalonia 12 窗口装饰迁移说明](https://v11.docs.avaloniaui.net/docs/avalonia12-breaking-changes/)
- [12.1.5 Win32 窗口样式和状态切换](https://github.com/AvaloniaUI/Avalonia/blob/12.1.5/src/Windows/Avalonia.Win32/WindowImpl.cs)
- [12.1.5 原生按钮命中测试和输入转发](https://github.com/AvaloniaUI/Avalonia/blob/12.1.5/src/Windows/Avalonia.Win32/WindowImpl.CustomCaptionProc.cs)
- [12.1.5 装饰模板的按钮部件和状态管理](https://github.com/AvaloniaUI/Avalonia/blob/12.1.5/src/Avalonia.Controls/Chrome/WindowDrawnDecorations.cs)
