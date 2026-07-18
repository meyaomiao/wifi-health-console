# Wi-Fi 体检台 Windows Preview

> 当前统一产品版本：v0.4.1 · Windows 10 / 11 64 位 · x64 / ARM64 · 图形化 EXE 安装器

Windows 版使用 .NET 10 与 Avalonia 构建桌面界面，用 Windows Native WLAN API 采集真实 Wi-Fi 信息。它不是命令行工具，也不是把网页套进窗口；普通用户双击 EXE 安装后，直接在桌面应用里点击使用。

Avalonia 组件按 macOS 版的页面结构实现：深色侧边栏、指标卡、统一对齐和间距、一致的健康状态色、下载 / 上传独立曲线，以及能总览附近网络的完整信道雷达。两个平台共享“**结论 → 证据 → 动作**”的信息结构，但分别调用各自系统能力采集数据。

## 下载与安装

| 设备 | 安装器 |
| --- | --- |
| 普通 Intel / AMD Windows 电脑 | [WiFi-Health-Console-Setup-x64.exe](https://github.com/meyaomiao/wifi-health-console/releases/download/v0.4.1/WiFi-Health-Console-Setup-x64.exe) |
| Windows on ARM 设备 | [WiFi-Health-Console-Setup-arm64.exe](https://github.com/meyaomiao/wifi-health-console/releases/download/v0.4.1/WiFi-Health-Console-Setup-arm64.exe) |

大多数 Windows 电脑选 `x64`。只有明确运行 Windows on ARM 的设备才选 `ARM64`。

1. 下载与电脑架构对应的 EXE。
2. 双击安装器，按向导完成安装。
3. 从开始菜单或可选的桌面快捷方式打开“Wi-Fi 体检台”。

**安装和使用都不需要 PowerShell、命令提示符或其他命令行操作。**

### SmartScreen 提示

Windows Preview 如果尚未配置 Windows Authenticode 代码签名证书，SmartScreen 可能提示“Windows 已保护你的电脑”或“未知发布者”。请先确认文件来自本项目的官方 [v0.4.1 统一 Release](https://github.com/meyaomiao/wifi-health-console/releases/tag/v0.4.1)，并对照发布页中的 `SHA256SUMS.txt`。

macOS 安装包使用的 **Apple Developer ID Application** 证书只能签名 Apple 平台应用，不能签名 Windows `.exe`，也不能转换成 Windows 代码签名证书。

## 已实现功能

| 页面 | Windows Preview v0.4.1 的实际行为 |
| --- | --- |
| 概览 | 显示 SSID、频段、信道、频宽、真实 RSSI、收发协商速率和统一健康结论；SNR / CCA 缺失时明确标为“部分完成”，每个指标都说明影响、标准与本次结果 |
| 60 秒体检 | 分开检查无线空口、局域网、宽带出口和 VPN / 代理；测量网关延迟 / 抖动 / 丢包、DNS、HTTPS 与公网延迟 |
| 网速测速 | 通过 Cloudflare 先下载、后上传，每 500 ms 绘制两张独立平滑曲线；直接给出 Mbps、MB/s、文件耗时、空闲延迟和负载响应 RPM |
| 信道雷达 | 扫描 2.4 / 5 / 6 GHz 附近网络，用同一页三段式频谱总览展示全部频段，也可切换单频段详情，并给出可解释的频段、频宽和信道建议 |
| 历史趋势 | 保存本机 JSON 历史，支持“变更前 / 变更后”标记和对比；对比前会校验时间顺序与 SSID，最多保留 2,000 条 |
| 路由管理 | 自动检测当前 Wi-Fi 的默认网关并打开候选管理页，不固定为 `192.168.31.1`，不会自动修改路由器设置 |

## 真实数据与系统边界

- SSID、BSSID、RSSI、信道和收发协商速率来自 Windows Native WLAN API，不解析受系统语言影响的 `netsh` 文本。
- 附近网络来自 `WlanScan` 和 `WlanGetNetworkBssList`；20 / 40 / 80 / 160 MHz 频宽尽可能从 HT / VHT / HE 信息元素解析。
- Windows 公开 WLAN API 不提供可靠的噪声、SNR 和路由器侧 CCA。无法取得时页面会明确显示“未检测”，不会伪造数值。
- 应用只能读取当前 Windows 电脑的 Wi-Fi 链路，不能读取 Android 手机、iPhone、其他电脑或路由器客户端列表中的真实 RSSI。
- 应用是只读诊断工具：不会登录路由器，不会修改信道、频宽、DNS 或其他设置。

## 第一次使用

1. 先让 Windows 电脑连接到要诊断的 Wi-Fi。
2. 打开“概览”并刷新，先看结论，再看异常卡片的证据和动作。
3. 运行完整“60 秒体检”，再根据需要运行“网速测速”和“信道雷达”。

Windows 的 Wi-Fi 扫描可能受系统定位权限控制。如果概览或信道雷达显示权限不足，点击应用里的“打开设置”，然后在 Windows “隐私和安全性 → 位置”中允许定位服务和桌面应用访问位置。应用不读取或保存位置坐标；该权限只是 Windows 对 Wi-Fi 识别与扫描的系统要求。

## 安装器行为

- 提供 `WiFi-Health-Console-Setup-x64.exe` 与 `WiFi-Health-Console-Setup-arm64.exe`。
- 安装到当前用户的 `%LOCALAPPDATA%\Programs\WiFiHealthConsole`，不请求管理员权限。
- 自动创建开始菜单快捷方式；安装界面可选是否创建桌面快捷方式。
- 再次运行相同或更高版本的安装器会覆盖升级现有程序。
- 卸载会删除程序、卸载信息和快捷方式，但默认保留 `%LOCALAPPDATA%\WiFiHealthConsole` 中的历史记录与设置，方便重装后继续使用。

## 开发者：GitHub Actions 构建

工作流文件为 `.github/workflows/windows-installers.yml`，支持手动运行，也会在 Windows 相关代码变更时执行。流程会：

1. 使用 .NET 10 恢复依赖并运行测试。
2. 发布 `win-x64` 和 `win-arm64` 自包含、多文件、未裁剪的应用。
3. 安装 NSIS 并生成两个 EXE 安装器。
4. 对 x64 安装器执行静默安装、启动、卸载和历史保留冒烟测试。
5. 校验 ARM64 产物、生成仅含 Windows 安装器的 `SHA256SUMS-windows.txt`，并上传 Actions artifact；正式跨平台 Release 会另行生成统一的 `SHA256SUMS.txt`。

在 GitHub 仓库的 **Actions → Windows EXE installers → Run workflow** 可以手动触发。完成后从该次运行底部的 **Artifacts** 下载。

## 开发者：Windows 代码签名

未配置签名时，CI 仍会生成可测试的未签名安装器。要生成 Authenticode 签名版本，需要在 GitHub 仓库的 Actions secrets 中配置：

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`：Windows 代码签名 PFX 文件的 Base64 内容。
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`：该 PFX 的密码。

必须使用支持 Windows Authenticode Code Signing 的证书。macOS 使用的 **Apple Developer ID Application** 证书不能签名 Windows `.exe`，也不能转换成 Windows 代码签名证书。正式公开分发前，应从受信任的代码签名 CA 获取 Windows 证书；若暂时没有证书，Windows SmartScreen 可能在首次下载时显示“未知发布者”。

PowerShell 生成 PFX Base64 的示例：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("windows-code-signing.pfx")) | Set-Clipboard
```

## 开发者：本地构建安装器

以下命令只面向从源码构建的开发者；普通用户不需要执行。需要 Windows、.NET 10 SDK 和 NSIS。先发布两个架构：

```powershell
dotnet restore .\Windows\WiFiHealthConsole.Windows.slnx
dotnet test .\Windows\tests\WiFiHealthConsole.Core.Tests\WiFiHealthConsole.Core.Tests.csproj -c Release --no-restore
dotnet publish .\Windows\src\WiFiHealthConsole.App\WiFiHealthConsole.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o .\Windows\artifacts\publish\win-x64 -p:PublishSingleFile=false -p:PublishTrimmed=false
dotnet publish .\Windows\src\WiFiHealthConsole.App\WiFiHealthConsole.App.csproj -c Release -r win-arm64 --self-contained true --no-restore -o .\Windows\artifacts\publish\win-arm64 -p:PublishSingleFile=false -p:PublishTrimmed=false
```

然后生成安装器：

```powershell
.\Windows\scripts\Build-WindowsInstallers.ps1
```

输出位于 `Windows\artifacts\installers`。
