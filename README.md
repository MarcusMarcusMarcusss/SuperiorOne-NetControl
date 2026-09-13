# SuperiorOne Net

中文 Windows 网络流量监控，面向 Windows 10/11 x64，使用系统自带 .NET Framework 4.x，无需安装 Python、Node 或抓包驱动。

## 使用

双击 `dist/SuperiorOneNet.exe`，允许管理员权限。界面每秒更新各软件的上传与下载速度、所选日期的上传/下载累计量。支持今天、本月、全部记录、指定日期、搜索、仅显示当前有流量的软件以及 CSV 导出。点击表头按数值排序；悬停软件名称查看路径。同一路径的多个进程合并统计。

最小化窗口后在系统托盘继续监控，双击托盘恢复。关闭窗口或点击托盘的退出菜单会停止采集并保存。不自动设置开机启动。

数据每 10 秒及正常退出时保存到 `%LOCALAPPDATA%\SuperiorOneNet\usage.json`，保留上一版 `.bak`。意外退出可能损失最近 10 秒左右的记录。数据仅保存在本地，不上传，不读取网络内容。

## 统计口径

- 使用 Windows ETW 内核 TCP/IP 和 UDP 发送/接收事件，支持 IPv4 / IPv6，按事件中的 PID 和字节数统计。
- 包含局域网和回环通信，不是运营商账单用量。VPN、虚拟网卡、协议开销等可能造成差异；不另外累计 TCP 重传事件。
- 只统计本程序运行期间，不能恢复安装前或关闭时的流量。速度始终是实时速度，日期筛选只作用于累计用量。
- 权限受限或已退出的短命进程可能仅显示进程名或 PID。进程信息最多缓存 2 秒，极短时间的 PID 重用可能影响归属。
- ETW 会批量交付事件，速度可能有短暂波动。高负载下事件丢失会在界面提示。
- 这是监控工具，不提供限速、断网或防火墙功能。

## 构建与验证

运行 `powershell -ExecutionPolicy Bypass -File .\build.ps1`，使用 Windows 自带的 64 位 .NET Framework C# 编译器生成 exe。

`dist\SuperiorOneNet.exe --self-test <绝对路径报告文件>` 可进行记账和持久化检查，无需管理员权限。

`dist\SuperiorOneNet.exe --capture-test <绝对路径报告文件>` 需要管理员权限，用真实本机 TCP、UDP IPv4/IPv6 及向 1.1.1.1 查询 example.com 的 DNS 请求检查 ETW 的进程归属和上传下载计数。测试使用程序同名互斥锁，请先退出界面。ETW 交付延迟可能超过固定测试等待时间，需结合报告判断。

原生 ETW ABI 固定为 x64，请勿编译为 x86。参考：[Windows 系统追踪会话](https://learn.microsoft.com/en-us/windows/win32/etw/configuring-and-starting-a-systemtraceprovider-session)、[TCP 发送事件](https://learn.microsoft.com/en-us/windows/win32/etw/tcpip-sendipv4)、[ETW 消费接口](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_logfilew)。
