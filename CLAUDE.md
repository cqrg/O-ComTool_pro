# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

O-ComTool 是一款 Windows 桌面串口调试助手，使用 **C# / Windows Forms / .NET Framework 4.5** 编写。目标是辅助嵌入式开发调试：串口收发、ASCII/HEX 显示、报文校验和与 CRC、关键字正则高亮、快捷发送、配置导入导出等。

代码由作者自述为"面向搜索引擎编程"，质量一般，`MainForm.cs` 是近 90KB 的单体核心，绝大多数业务逻辑集中于此。所有 UI 文本与注释均为简体中文。

## 构建与运行

- **工具链**：Visual Studio（README 标注 2017，`.sln` 兼容 2013+）/ MSBuild。
- **目标框架**：.NET Framework 4.5，`OutputType = WinExe`，`AnyCPU`。
- **解决方案入口**：`O-ComTool_Pro.sln` → 项目 `O-ComTool_Pro/O-ComTool_Pro.csproj`。
- **命令行构建**（在仓库根目录）：
  - Debug：`msbuild O-ComTool_Pro.sln /p:Configuration=Debug`
  - Release：`msbuild O-ComTool_Pro.sln /p:Configuration=Release`
  - 产物路径：`O-ComTool_Pro/bin/Debug/` 或 `O-ComTool_Pro/bin/Release/`。
- **无单元测试 / lint / CI**：仓库不包含测试工程、分析器或 CI 配置；不要假设存在这些命令。验证修改靠手动运行程序。

### FastColoredTextBox (FCTB) 依赖

唯一的外部依赖是 [FastColoredTextBox](https://github.com/PavelTorgashov/FastColoredTextBox)（用于接收区高亮文本框 `fctbReceive`）。注意：

- `.csproj` 里的 `<HintPath>` 指向仓库外的 `..\..\FastColoredTextBox\...\FastColoredTextBox.dll`，**该路径在干净环境中通常不存在**。
- 实际运行时通过 `Program.cs` 的 `CurrentDomain_AssemblyResolve` 钩子，从嵌入资源加载。所以即使 `HintPath` 失效，只要资源存在即可运行。**修改 FCTB 相关代码时不要破坏这个回退加载机制。**

## 架构要点

### 入口与主窗体

- `Program.cs` → `Main()` 设置 `AssemblyResolve` 钩子（见上）后启动 `MainForm`。
- `MainForm.cs` / `MainForm.Designer.cs` / `MainForm.resx` 是程序主体：串口参数设置、打开/关闭、收发、计数、日志、快捷发送列表管理、配置导入导出、高亮编译。**几乎所有新功能都会落在这里**——这是项目最大的技术债，也是当前现实。

### 串口与线程模型（关键）

- 串口对象是 WinForms 拖入的 `serialPort1` 组件；`btnOpenCom_Click` 配置 `PortName/BaudRate/DataBits/Parity/StopBits/Handshake` 并 `Open()`。
- 接收回调 `serialPort1_DataReceived` 在**后台线程**触发。代码在构造函数里显式 `Control.CheckForIllegalCrossThreadCalls = false;` 并通过 `BeginInvoke` 将 UI 更新切回主线程。
  - **改写接收路径时务必保持 `BeginInvoke`/`Invoke` 模式**，不要直接在 DataReceived 里操作控件；也不要重新启用 `CheckForIllegalCrossThreadCalls`。
- 串口列表**不做后台监听**，而是在 `cmbCom_DropDown`（下拉时）调用 `SerialPort.GetPortNames()` 即时刷新——这就是"插入新设备即可切换，无需重启"的实现方式。

### 定时器驱动

多个 `System.Windows.Forms.Timer` 驱动周期行为：`timerAutoReply`（自动回复，仅通用发送）、`timerRepeat`（重复发送）、`timerReceiveLed`（接收指示灯闪烁）、`timerProcessBar`（进度条）。Interval 来自 `nudReplyDelay` / `nudRepeatInterval` 等 NumericUpDown 控件。

### 配置持久化（双轨）

1. **应用设置**：`app.settings` / `Properties/app.Designer.cs`（强类型 `app.Default.*`），保存串口参数默认值、两套显示方案的颜色与字体、快捷发送标题/数据（XML 序列化的 `ArrayOfString`）、高亮正则、更新检查时间等。代码中大量直接读写 `app.Default.*`。
2. **INI 导入导出**：`tsmExportConfig_Click` / `tsmImportConfig_Click` 通过 `kernel32.dll` 的 `WritePrivateProfileString` / `GetPrivateProfileString`（P/Invoke，定义在 `MainForm.cs` 底部）读写 `.ini` 文件，分节 `[SerialPort]/[Receive]/[Send]/[GeneralSend]/[QuickSend]/[Option]`。
   - INI 与 `app.Default` 两套字段并不完全一一对应；新增持久化字段时**同时考虑两处**，避免导入导出丢失。

### 在线更新检查

`UpdateHelper.check_update(url)` 从 `http://www.ifreehub.com/octservice/check_version.xml` 拉取版本信息，`StartCheckVersion` 在启动时按 `app.Default.LastCheckTime` 节流（每天一次），新版本弹 `CheckUpdate` 对话框。`HttpWebRequestHelper` 提供同步/异步 GET。注意服务器与协议为 HTTP + XML，属于历史遗留。

### 自定义控件与对话框窗体

- `QuickSend : UserControl`：快捷发送条目控件，运行时双击标题进入编辑态（用 `timer1` 检测鼠标离开来提交）。由 `MainForm` 维护 `List<QuickSend> quicksend_list`。
- `O_ScrollBar : Component`、`HightLightRichTextBox : Component`：自绘滚动条与高亮富文本框。
- 工具/对话框窗体：`Check`（校验和/CRC 计算）、`Format`（格式化）、`ASCII`（ASCII 表）、`Note`、`About`、`Donate`、`Option`（设置）、`Update`/`CheckUpdate`（更新）。每个窗体都是标准的 `*.cs + *.Designer.cs + *.resx` 三件套。

## 代码风格与约定

- 命名空间统一 `O_ComTool_Pro`；控件采用 WinForms 设计器默认的匈牙利前缀（`cmb*`/`btn*`/`txb*`/`chk*`/`nud*`/`tsm*`/`pic*`/`rad*`）。
- 字段多为实例级 `public`/私有字段直接挂在 `MainForm` 上，无依赖注入、无分层。
- 中文注释与用户可见字符串保持中文。
- 修改 Designer 文件（`*.Designer.cs`）应通过 Visual Studio 窗体设计器；手改易破坏 `.resx` 同步。

## 修改建议

- 新增收发/显示逻辑时优先复用 `MainForm` 现有定时器与 `BeginInvoke` 模式。
- 新增可配置项：同时更新 `app.settings` 默认值、`App.config` 的 `<userSettings>`、以及 INI 导入导出代码块。
- 涉及 FCTB 的改动须确认 `Program.CurrentDomain_AssemblyResolve` 仍能从资源加载 DLL。
