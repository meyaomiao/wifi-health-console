# Code signing policy

本页说明 Wi-Fi 体检台的代码审核、构建与签名责任。项目源码采用 [MIT License](LICENSE)，第三方组件及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 当前签名状态

- macOS 正式安装包由维护者使用 Apple `Developer ID Application` 证书签名，提交 Apple 公证并装订票据。签名身份和公证凭据只保存在维护者受保护的本机钥匙串中，不进入仓库或 CI 日志。
- Windows v0.4.1 安装包尚未获得 Authenticode 签名，发布页明确标记为“未知发布者”风险。用户应只从本仓库的 GitHub Release 下载，并核对 `SHA256SUMS.txt`。
- 项目正在准备申请 SignPath Foundation 的免费开源代码签名，但尚未获批，也没有任何发行版使用 SignPath Foundation 证书。获批并完成集成前，不会宣称 SignPath 为本项目提供签名。

## 团队角色

| 角色 | 成员 | 职责 |
| --- | --- | --- |
| Authors | [@meyaomiao](https://github.com/meyaomiao) | 维护源码、工作流和发布配置；处理经过审核的外部贡献 |
| Reviewers | [@meyaomiao](https://github.com/meyaomiao) | 审核外部 Pull Request、依赖许可、测试结果和发布变更 |
| Approvers | [@meyaomiao](https://github.com/meyaomiao) | 人工批准正式签名请求并核对来源提交、产物与版本 |

外部贡献者只能通过 Fork 和 Pull Request 提交变更，不能直接推送或合并到受保护的 `main`。团队成员必须为 GitHub 和未来的 SignPath 账户启用多因素认证。

## Windows 签名约束

若 SignPath Foundation 批准本项目，Windows 签名流程将遵守以下约束：

1. 只有 GitHub 托管的 runner 根据本公开仓库的受保护源码构建二进制产物。
2. 签名工作流、SignPath 策略、安装器与本政策由 `.github/CODEOWNERS` 保护。
3. 每次签名请求都必须由 Approver 人工核对并批准；不允许静默自动签名。
4. 只签署本项目从源码构建的应用启动器和安装包。第三方上游二进制只作为已声明依赖随包分发，不会单独冒充本项目产物签名。
5. SignPath 的 organization、project 和 policy 标识只在批准后配置；在此之前不添加无法工作的占位签名步骤。

获批并成功接入后，本页与发布页将加入 SignPath 要求的正式署名，并明确哪些版本和文件实际获得了签名。

## 审核与发布流程

1. 所有外部变更通过 Pull Request 审核；关键发布、许可证与工作流文件需要 CODEOWNERS 审核。
2. macOS 运行 Swift 测试、通用架构检查、签名验证、公证、票据验证和 Gatekeeper 检查。
3. Windows 在 GitHub 托管的 Windows runner 上运行测试、格式检查、x64 / ARM64 自包含构建、安装器构建和 x64 安装 / 启动 / 升级 / 卸载冒烟测试。
4. 发布文件从同一个公开提交构建，生成 SHA-256 校验表后统一上传至一个跨平台 GitHub Release。
5. 发布说明必须准确写明每个平台的签名状态和未覆盖的验证范围。

## 隐私与安全

应用不包含广告、遥测或用户行为分析。只有在用户主动运行诊断或测速时，应用才会访问 [PRIVACY.md](PRIVACY.md) 中列出的网络测试端点；SSID、BSSID、历史记录和诊断结果不上传到项目维护者的服务器。

证书、私钥、API 令牌和公证凭据不得提交到仓库或附加到 Issue。签名异常、校验和不匹配、Release 资产被替换或构建供应链问题应按 [SECURITY.md](SECURITY.md) 通过 GitHub 私密漏洞报告提交。
