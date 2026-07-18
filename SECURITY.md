# 安全策略

## 支持范围

安全修复优先提供给当前最新 Release。旧版本可能不再单独修复，请先确认问题能否在最新版本复现。

## 私密报告漏洞

请通过 GitHub 的[私密漏洞报告](https://github.com/meyaomiao/wifi-health-console/security/advisories/new)提交安全、隐私或发布完整性问题。不要在公开 Issue、讨论、截图或日志中披露可利用细节、凭据、真实网络标识、个人数据或尚未修复的供应链信息。

报告中建议包含：

- 受影响的平台和应用版本
- 最小化且已脱敏的复现步骤
- 实际影响和可能的攻击条件
- 可选的修复建议

## 签名与发布完整性

以下情况也按安全问题私密报告：

- 正式 Release 的 SHA-256 与发布页不一致，或安装包内容与对应源码构建明显不符
- macOS 签名、公证或装订票据异常，或 Windows 安装包显示了与项目政策不一致的签名者
- 发布工作流、`.signpath/` 策略、证书、密钥、审批或 Release artifact 可能被篡改或泄露
- 安装包缺少许可证文件、包含未声明组件，或第三方代码来源和许可无法核实

报告时请提供 Release 链接、文件名、版本、SHA-256、观察到的签名身份和已脱敏的验证输出。不要把可疑安装包、证书私钥或敏感日志上传到公开 Issue。项目当前的签名身份、验证方式和各平台状态见[代码签名政策](CODE_SIGNING_POLICY.md)。

普通安装、使用和指标解释问题请使用公开的[使用问题模板](https://github.com/meyaomiao/wifi-health-console/issues/new?template=question.yml)。
