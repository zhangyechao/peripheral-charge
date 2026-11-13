# 余电 · Peripheral Charge

把 2.4 GHz 无线键鼠的电量放到 Windows 系统托盘里。鼠标悬停可看各设备的百分比，单击可看设备列表和上次刷新时间。C# 单文件程序，通过 Windows 自带的 HID 接口在本地运行。

## 下载与使用

1. 从 [GitHub Releases](https://github.com/zhangyechao/peripheral-charge/releases/latest) 下载 `PeripheralCharge.exe`。适用于 Windows 10 / 11 和 .NET Framework 4.7.2 或更高版本。
2. 插入接收器，双击程序，在任务栏右下角或「显示隐藏的图标」里找到电池图标。
3. 图标显示当前有效读数中的最低值。单击图标或再次双击 EXE 可以查看每台设备，右键可以立即刷新或退出。关闭详情窗口后仍会在托盘更新。

每 60 秒读取一次。读数降到 15% 或更低时，程序会在当前会话提醒一次。电量回升至 20% 或设备报告充电后，下次进入低电量区间可再次提醒。通知的实际显示受 Windows 通知设置影响。

## 设备与验证情况

| 设备 | 接收器 VID:PID | 项目验证情况 |
| --- | --- | --- |
| 高斯 GS3104T PRO，2.4G | `05AC:024F` | 已在 Windows 实机读到百分比，重复查询一致，响应校验和正确。仅匹配指定产品名称及 32 字节厂商接口。 |
| 迈从 A7 鼠标 | `5253:1021` | 已实现经典协议，并通过报文回放。该版本尚待 A7 真机复核。 |
| 迈从 G87 键盘 | `41E4:2001` | 已通过公开的 52% 实测报文回放。该版本尚待 G87 真机复核。 |
| Compx 接收器，使用 AULA F75 同族协议 | `3554:FA09` | 已实现带校验和的状态查询。实机能识别接收器，此次未取得有效电量。此编号由多款键盘共用。 |

每轮刷新都会重新查找已连接的接收器。超时显示「无应答」，访问被拒显示「无法访问」，不合格式的电量包显示「读数无效」。这几种状态不触发低电量提醒，也不沿用上一轮的百分比。无线设备休眠时可按键或移动鼠标，再用「立即刷新」复查。电量精度取决于接收器固件，当前实机验证覆盖报文读取，电池实际容量和整段充放电曲线尚未校准。

## 从源码构建

在 Windows 的 PowerShell 中，从本仓库根目录运行以下命令。构建使用系统自带的 .NET Framework 编译器，无需下载 NuGet 包。

```powershell
.\build.ps1 -Test
.\dist\PeripheralCharge.exe
```

运行 `PeripheralCharge.exe --details` 可直接打开详情窗口。命令行也可以单次查询。以下写法会在 PowerShell 等待 GUI 子系统程序结束后打印 UTF-8 输出。

```powershell
.\dist\PeripheralCharge.exe --status | Out-Host
.\dist\PeripheralCharge.exe --list   | Out-Host
```

`--status` 的退出码为 0（至少一个有效读数）、2（本轮无有效读数）或 1（扫描失败）。`--list` 仅列出 HID 产品名、VID/PID、使用页以及报告长度，可用于判断新型号的接口。两种模式均不写日志。日常托盘模式只向匹配的厂商接口请求设备状态。

## 协议来源与适配

- 高斯在一只实机接收器上使用无编号的 32 字节输出报文。请求头为 `20 01`，响应头为 `20 01 00`，第 4 个载荷字节给出百分比，末字节为前 31 字节之和的低 8 位。全零电量载荷可能是请求回显，本版将其视为无有效电量。不同批次的 GS3104T PRO 可能采用不同协议。[同族报文说明](https://github.com/tarikmanoar/aula-f75-max/blob/main/docs/packet-catalog.md) · [高斯官方型号和批次说明](https://www.ganss.cn/forum/post/334635/)
- A7 使用 65 字节 Feature Report。状态回包回显查询命令，电量位于完整 Windows 帧偏移 11，需逐字节取反。旧示例的排版及偏移存在冲突，此处按设备协议字段顺序读取。[经典协议字段表](https://github.com/alexfrih/mchose-linux/blob/main/PROTOCOL.md) · [Windows 帧偏移对照](https://github.com/HifumiDaisuki01/MchoseBatteryTray/blob/main/PROTOCOL.md)
- G87 使用 `FF02` 页的 `13 4A` 状态查询，电量在响应偏移 5。旧版 `FF04/06` 返回的容量字段会持续报满电。Compx 的请求在第 20 个字节带加和校验。[原帖作者的 G87 修订](https://github.com/marsuyone/mchose-battery-tray/tree/7206b4607eee98fb58951a317a00da83f774e1ba) · [Compx 传输格式](https://github.com/TrueIG/AULA-F75-configurator/blob/5573c9cd573173736ae91f84d8b2b38337c98383/src/transport.js)

项目源于小黑盒作者 Hosei 展示的托盘用法。本仓库独立实现 Windows 程序和图标，以 MIT 许可发布。欢迎携完整型号、连接方式和 `--list` 中对应设备的几行提交兼容性反馈。[灵感原帖](https://www.xiaoheihe.cn/app/bbs/link/569472ac415a) · [反馈问题](https://github.com/zhangyechao/peripheral-charge/issues) · [MIT 许可证](LICENSE)
