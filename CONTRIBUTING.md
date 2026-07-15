# 参与项目

## 提问和反馈

安装、权限、指标解释和诊断结果可以使用 GitHub Issue 中的“使用问题”模板。错误和功能建议请分别使用对应模板。提交公开内容前必须遮盖网络标识、账号、IP、位置和日志中的个人信息。

安全或隐私漏洞必须通过[私密漏洞报告](https://github.com/meyaomiao/wifi-health-console/security/advisories/new)提交。

## 代码变更

公开用户不能直接修改本仓库。代码变更需要通过 Fork 和 Pull Request 提交，并经过仓库所有者审核后才能合并。提交前请：

1. 保持改动范围清晰，不混入无关重构。
2. 为行为变化增加相应测试。
3. macOS 运行 `swift test`。
4. Windows 运行 `dotnet test Windows/WiFiHealthConsole.Windows.slnx --configuration Release`。
5. 不提交密钥、证书、真实网络数据、构建产物或个人路径。

仓库公开可见不等同于授予额外的复制、再分发或商业使用许可；代码使用权以仓库中明确提供的许可证为准。目前仓库未提供开源许可证。
