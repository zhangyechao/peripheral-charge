# 验证记录

验证环境：Windows；便携版 CPython 3.13.11；原 APK 核心 1.3.4。iOS 运行库固定为 Python-Apple-support 3.13-b14，已下载并核对官方 SHA-256、查看归档结构；未在 Windows 解包运行 Apple 二进制。

`tools/test_port.py`：12 项通过，Windows 和云端 macOS 均已执行。

- 原网页成功返回，包含 iOS 日历导出和输入框适配。
- 示例课表、成绩接口可读取；iCalendar 含实际事件。
- 登录状态接口可读取；不代表真实学校认证已通过。
- 偏好设置写入后可读取。
- 无令牌请求返回 401；错误 Host 返回 403；服务器只绑定 `127.0.0.1`。
- 原加密文件接口在桌面桥接替身下完成 AES-GCM 加密/解密，并能检测密文篡改。
- 模拟钥匙串失败后没有写出明文或加密文件。
- Pillow 13.0.0 能运行原验证码算法和 JPEG 解码；这是合成图兼容性检查，不是学校真实验证码准确率测试。
- WebVPN URL 加解密兼容，以及 CFB 分块和不完整块的适配接口。

上述桌面测试使用临时目录和示例数据，未登录学校，未读取用户校园账号；加密操作由桌面 cryptography 库提供，用于验证 Python 接口契约。实际 iOS 桥接另由下述模拟器检查验证。

2026-11-15：云端 macOS 15 / Xcode 16.4 已完成 iPhone ARM64 Release 编译、模拟器 ARM64 Release 编译和 IPA 打包；检查确认主程序是 iPhoneOS 的 Mach-O 可执行文件，并包含 Python / Pillow 运行库。

`tools/smoke_simulator.py` 已通过：iPhone 16 模拟器，iOS 18.5，Release 配置。检查实际运行的内嵌 Python 和原生桥接：

- iOS Keychain 创建和读取密钥。
- CryptoKit AES-GCM 加密、解密及拒绝篡改密文。
- CommonCrypto AES-CFB 通过 NIST 已知向量；校园 WebVPN 地址编码往返一致。
- 原加密文件接口保存、读取和删除测试记录。
- iOS Pillow JPEG 编码和解码。
- 带令牌的本机 HTTP 页面响应；WKWebView 完成页面加载。

已人工查看 `simulator.png`：课表首页正常显示，处于未登录状态，没有自动填充示例课表。`simulator-report.json` 保存检查结果。模拟器专用身份仅嵌入模拟器程序，不进入 iPhone IPA；真机安装仍需自己的签名。

[成功构建记录](https://github.com/zhangyechao/peripheral-charge/actions/runs/43673159767)，代码提交 `a94575c5e2e81d3a13d01b2336bab4bf126f70e7`。

交付 IPA：`SuperJLU-1.3.4-iOS-unsigned.ipa`，25,143,286 字节。下载后已核对 SHA-256：`6fcdd86f4c4478b38520a18ff48fbf841435fae11a7e3e812d60fee5316d89e7`，与云端构建报告一致。压缩归档也已与 GitHub 的摘要核对。

用户设备：iPhone 15，用户提供的系统版本为 iOS 26.6.1；尚未在此设备安装。

尚未完成：用户 iPhone 的签名安装、钥匙串真机验证、真实账号同步、VPN 和单点登录、分享和下载真机验收。没有 App Store / TestFlight 发布。

桌面复现：使用 Python 3.13，把 cryptography 和 Pillow 13.0.0 安装到 `.tools/testdeps`，然后运行 `python tools/test_port.py`。此测试目录不进入应用或交付 ZIP。
