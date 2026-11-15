# 隐私策略 / Privacy Policy

**应用名称 / App name:** wont sleep
**最后更新 / Last updated:** 2026-11-15

---

## 简体中文

wont sleep（以下简称「本应用」）尊重并保护你的隐私。本策略说明本应用如何处理信息。

### 1. 不收集任何个人信息
本应用**不收集、不存储、不传输、不出售**任何个人信息、设备标识符、位置信息、使用统计或崩溃数据。本应用无需注册账号即可使用。

### 2. 不联网
本应用不包含网络访问逻辑，不会主动连接互联网，也不会与任何第三方服务器通信。

### 3. 仅保存在本机的设置
为了记住你的使用习惯，本应用仅在本机保存以下选项：

- 持续时长
- 阻止休眠的方式
- 是否同时保持屏幕常亮
- 界面外观（跟随系统 / 浅色 / 深色）

存储位置：

- 通过 Microsoft Store 安装（带包标识）时：Windows 应用数据 `LocalSettings`（容器 `wont-sleep`）
- 直接运行可执行文件时：`%LOCALAPPDATA%\wont-sleep\settings.txt`

这些数据**只存在于你的设备上，不会上传到任何地方**。卸载应用或删除上述文件即可清除，无需额外操作。

### 4. 无遥测、无广告、无第三方 SDK
本应用不集成任何分析、崩溃上报、广告、社交或推送类第三方 SDK。

### 5. 系统权限说明
本应用以「完整信任（runFullTrust）」方式运行，这是桌面应用访问 Windows API 的必要方式，用于调用系统电源接口与通知区域图标接口。本应用：

- 不请求管理员权限
- 不修改系统电源计划，退出或停止后立即恢复默认策略
- 不访问通讯录、相机、麦克风、位置、文件内容等任何隐私数据

### 6. 儿童隐私
由于本应用不收集任何用户信息，因此也不涉及儿童个人信息的收集。

### 7. 策略变更
若本策略发生变化，将更新本文件并同步修改上方「最后更新」日期。

### 8. 联系方式
如有任何疑问，请通过 GitHub Issues 联系我们：<https://github.com/sunnysky123/wont-sleep/issues>

---

## English

wont sleep (the "App") respects and protects your privacy. This policy explains how the App handles information.

### 1. No personal data is collected
The App does **not collect, store, transmit or sell** any personal information, device identifiers, location data, usage statistics or crash reports. No account is required.

### 2. No network access
The App contains no networking code. It does not connect to the internet and does not communicate with any third-party server.

### 3. Settings stored on your device only
To remember your preferences, the App stores the following options locally:

- Selected duration
- Sleep-inhibition method
- Whether to keep the display on
- Appearance (follow system / light / dark)

Storage locations:

- Installed from the Microsoft Store (packaged): Windows app data `LocalSettings` (container `wont-sleep`)
- Running the executable directly: `%LOCALAPPDATA%\wont-sleep\settings.txt`

This data **never leaves your device**. Uninstalling the App or deleting the file above removes it.

### 4. No telemetry, ads or third-party SDKs
The App includes no analytics, crash-reporting, advertising, social or push SDKs.

### 5. About the runFullTrust capability
The App runs with full trust, which is the standard deployment model for desktop apps that call Windows APIs (system power APIs and the notification-area icon API). The App:

- never requests administrator privileges
- never modifies your power plan — the default policy is restored as soon as you stop
- never accesses contacts, camera, microphone, location or your files' contents

### 6. Children's privacy
Because the App collects no user information at all, it collects no information from children either.

### 7. Changes to this policy
If this policy changes, this document will be updated along with the "Last updated" date above.

### 8. Contact
Questions? Please open an issue at <https://github.com/sunnysky123/wont-sleep/issues>.
