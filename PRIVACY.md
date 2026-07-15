# 隐私说明

最后更新：2026-07-15

Wi-Fi 体检台是本地运行的只读网络诊断工具。项目当前没有自建账号系统、广告、行为分析 SDK 或遥测服务器。

## 本机读取的数据

根据平台能力和系统授权，应用可能读取当前 Wi-Fi 的 SSID、BSSID、频段、信道、频宽、信号强度、附近可见网络、默认网关、网络接口以及 VPN / 代理状态。应用也会生成延迟、抖动、丢包、DNS、HTTPS 和测速结果。

macOS 要求定位权限后才允许应用通过公共 CoreWLAN API 显示部分 Wi-Fi 信息。应用不会读取、保存或上传位置坐标。

## 本地存储

历史采样保存在用户自己的设备上：

- macOS：`~/Library/Application Support/WiFiHealthConsole/history.json`
- Windows：`%LOCALAPPDATA%\WiFiHealthConsole\history.json`

历史记录可能包含当时的 SSID、BSSID 和网络诊断指标，最多保留 2,000 条。卸载应用不会主动上传这些数据；用户可以删除对应目录来清除历史。

## 网络请求

应用不会把诊断结果上传到本项目控制的服务器。执行体检或测速时，为了完成实际网络测量，设备会直接访问 Apple、Microsoft、Cloudflare、`1.1.1.1` 或操作系统选择的测速节点。相关服务提供方会像处理普通网络请求一样看到来源 IP、请求时间和传输量。

打开路由管理页只会让系统浏览器访问自动检测到的本地默认网关，应用不会读取路由器密码，也不会自动修改路由器设置。

## GitHub Issue

本仓库的 Issue、评论和附件是公开内容。提交问题时请遮盖或替换：

- Wi-Fi 密码、账号、令牌和证书
- 完整 SSID、BSSID、公网 IP 和精确位置
- VPN / 代理配置、用户名和个人文件路径
- 未经检查的完整日志或历史记录文件

安全或隐私漏洞请使用 GitHub 的[私密漏洞报告](https://github.com/meyaomiao/wifi-health-console/security/advisories/new)，不要创建公开 Issue。
