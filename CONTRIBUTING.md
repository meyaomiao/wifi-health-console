# 参与项目

Wi-Fi 体检台以 [MIT License](LICENSE) 开源。提交 Issue 或 Pull Request 前，请同时阅读[第三方许可声明](THIRD-PARTY-NOTICES.md)、[代码签名政策](CODE_SIGNING_POLICY.md)和[安全策略](SECURITY.md)。

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

## 贡献许可

提交代码、文档、测试、图片或其他内容，即表示你确认自己有权提交这些内容，并同意该贡献按本仓库的 [MIT License](LICENSE) 分发。不要提交来自专有项目、许可证不兼容项目，或来源和授权不明确的代码与资源。

保留上游版权、作者归属和许可证声明。MIT 许可证只覆盖本项目原创内容，不会覆盖或替代第三方材料自己的许可证。

## 依赖与第三方材料

新增或升级直接依赖、随应用分发的间接依赖、字体、图片、原生库、运行时或安装工具时，Pull Request 必须：

1. 说明名称、版本、来源和用途。
2. 确认采用 OSI 认可的开源许可证，且不包含专有组件或限制再分发的未授权内容。
3. 更新 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 和 `licenses/` 中对应的完整许可证或第三方声明。
4. 确认发布产物仍会携带适用的许可证文件，并运行相应打包验证。

仅有“源码可见”、缺少明确许可证、许可证元数据不完整或无法确认再分发权利的依赖，不能直接引入。存在疑问时，请先开 Issue 讨论。

## 签名与发布链路

签名政策、GitHub Actions 工作流、`.signpath/`、许可证文件、打包脚本和安装器脚本属于受保护的发布链路。相关变更必须在 Pull Request 中说明影响，并由 CODEOWNERS 审核。不得绕过人工审批、降低校验强度，或在日志和产物中暴露证书、私钥、密码及服务凭据。

项目正在准备申请 SignPath Foundation 免费开源代码签名服务，但尚未获批。除非受保护流程已实际完成签名并通过验证，不得在文档、安装器或 Release 中宣称 Windows 产物已由 SignPath 签名。
