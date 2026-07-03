# O-Comtool

![image](http://www.ifreehub.com/image_bed/upload//l_IUV8bODji6Ykb_GV5opSNeGlloQLde8MvxEiBNT0A.png)

O-Comtool是一款简单易用的串口调试助手，本软件提供了丰富的功能，有效提高嵌入式开发调试的效率。

# 特性
* 支持串口参数设置，支持常用波特率选择与波特率自定义
* 支持实时刷新串口，插入新设备即可切换端口，无需重启软件
* 支持ASCII与HEX显示，明码暗码轻松切换
* 支持收发信息时间显示，自动换行，发送回显，轻松显示交互逻辑
* 支持自动计数与重复发送，丢帧与否，一眼便知
* 支持`//` 报文注释，报文含义清晰可见
* 支持报文校验和、CRC快速计算，报文组包快人一步
* 支持加载文件报文，预先导入报文。
* 支持接收区关键字高亮（正则匹配），关键信息一目了然
* 支持实时搜索，可以实时查找关键之，第一时间发现问题
* 支持多达25条报文的快捷发送功能，想发啥就是啥
* 支持软件名称备注，轻松区别多个串口设备
* 支持配置参数导入，导出，到哪都是熟悉的味道
* 更有众多小工具

## V2.2.0 新增特性（相对于 V2.1.0）

**协议调试**
* 新增 Modbus RTU 主机调试工具（工具菜单 → Modbus 主机），支持完整帧构造与自动解析（FC 01/02/03/04/05/06/0F/10）、自动轮询、从机地址扫描（逐地址探测，仅列出在线从机）、原始字节回显（TX/RX hex 带时间戳）
* 新增实时数据曲线图表（工具菜单 → 实时图表），基于 WebView2 内联 ECharts，支持自定义帧解析（偏移/数据类型/字节序/scale）和 Modbus 寄存器值两种曲线来源，双 Y 轴、缩放、环形缓冲、自动平移

**发送增强**
* 新增发送自动追加校验功能：勾选"追加校验"并选择算法（累加和/XOR/LRC/CRC16-Modbus/CRC32/FCS），HEX 模式发送时自动在帧尾追加校验值，CRC16-Modbus 为小端（低字节在前）
* 新增全局 Ctrl+Enter 快捷发送，任意焦点下均触发

**安全加固与稳定性**
* 版本更新检查迁移至 GitHub 自托管（`raw.githubusercontent.com`），关闭 XML 外部实体解析（防 XXE），链接白名单校验（仅允许 http/https 方案进入 Process.Start）
* 串口接收去掉忙等 sleep 循环，消除 ThreadPool 阻塞与 1024 字节截断
* 跨线程 UI 访问统一编组到 UI 线程（移除 `CheckForIllegalCrossThreadCalls = false`）
* 日志写入加锁 + 单生命周期 StreamWriter，消除每帧重复 UTF-8 BOM 与并发写损坏
* 收发比率计算增加除零保护

**可维护性重构**
* 配置导入导出改为表驱动（单一 `BuildIniFields` 定义点），修复导出复制粘贴 bug（SendDisplayEnable/CommentEnable 写成 HightLightEnable 的值）及 chkAutoReply/chkAutoCount/chkRepeatSend 永不持久化的问题
* 抽取公共 helper：`ParseHexBytes`、`UpdateCounters`、`ApplyDisplayPlan`、`CrcUtil`（统一校验算法，含移植自 [BYSerial](https://gitee.com/LvYiWuHen/byserial) 的 LRC/FCS）
* 删除已迁移到 FastColoredTextBox 后遗留的 RichTextBox 高亮死代码（~80 行）
* 修复右键菜单 contextMenuStrip3 错误切换 FCTB 菜单粘贴项的 bug
* 目标框架从 .NET Framework 4.5 升级到 4.8（4.5 已停止支持）

**其他**
* 移除捐赠相关内容（支付宝/微信支付二维码、菜单项）
* 新增 CodeGraph 知识图谱导出（`tools/export_codegraph_html.py` → `codegraph.html`，vis-network 力导向图，自包含离线 HTML）

# 软件截图
![image](http://www.ifreehub.com/image_bed/upload//4UkAGlZ9L40ihPI6BgsKSoIKMIb4O0s8wSRynP0lv8E.png)

![image](http://www.ifreehub.com/image_bed/upload//u41HcEfwQ-x6s6l4OCtS8YiIOgbAXqK3Yii1rvFDPFg.png)

![image](http://www.ifreehub.com/image_bed/upload//MW3EKGsix1Q25JO8JkOEZjwVrHz7LiJpEPTjQGVb81k.png)

# 使用方法
请参考如下链接：

[V1.1.0](http://www.ifreehub.com/archives/3/)

[V2.0.0](http://www.ifreehub.com/archives/13/)

[V2.1.0](http://www.ifreehub.com/archives/24/)

# 如何编译
## 环境

* Visual Studio 2017 及以上（或 MSBuild 4.0+）
* .NET Framework 4.8
* 无需 NuGet 还原：FastColoredTextBox 通过 HintPath + AssemblyResolve 从嵌入资源加载；WebView2 托管 DLL 已随项目放在 `O-ComTool_Pro/Libs/WebView2/`，原生 loader 随构建输出到 bin 目录
## 运行
* 直接打开`O-ComTool_Pro.sln`即可
* 图表功能需要系统安装 [WebView2 Evergreen 运行时](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)（Windows 11 自带，Windows 10 需安装）
* 版本更新检查依赖 `https://raw.githubusercontent.com/cqrg/O-ComTool_pro/main/update/check_version.xml` 的可达性

# 作者有话说
由于作者主要做嵌入式开发，当时开发这个软件也是出于工作须要，现学现卖，**面向搜索引擎编程**，代码质量一般，原本计划基于QT重构该软件，但是如今工作繁忙，无力优化与维护，欢迎各位大佬继续优化或者添加新功能，欢迎重构。

# License
[GPL-3.0](https://github.com/vesamount/O-ComTool/blob/main/LICENSE)

# 感谢
* [FastColoredTextBox](https://github.com/PavelTorgashov/FastColoredTextBox) — 接收区高亮文本框（GPL-3.0）
* [BYSerial](https://gitee.com/LvYiWuHen/byserial) — Modbus RTU 参考实现与 LRC/FCS 算法移植来源（MIT）
* [S_PT_FactoryConfiguration](https://gitee.com/LvYiWuHen/byserial) — WebView2+ECharts 波形图实现参考
* [ECharts](https://github.com/apache/echarts) — 图表渲染引擎（Apache-2.0）
* [vis-network](https://github.com/visjs/vis-network) — CodeGraph 交互式图谱可视化（MIT/Apache-2.0）
* [WebView2](https://aka.ms/webview2) — 嵌入式浏览器控件（MIT）


