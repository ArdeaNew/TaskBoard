# 安全与本地数据 / Security and local data

当前版本为 1.4.1，运行于 Windows 和 .NET Framework 4.8。应用不发送网络请求，无账号、遥测或云同步；打印由 Windows 打印系统处理。

The current version is 1.4.1 for Windows and .NET Framework 4.8. The app makes no network requests and has no accounts, telemetry, or cloud sync. Windows handles printing.

任务及备份默认保存在 `%LOCALAPPDATA%\TaskBoard`，可通过 `--data-dir` 指定目录。数据为未加密的 JSON，访问权限取决于 Windows 账户及目录。旧字段仅作不透明兼容数据保留。导出文件保存在用户选择的位置。

Tasks and backups default to `%LOCALAPPDATA%\TaskBoard`, with `--data-dir` available for another location. Data is unencrypted JSON; access depends on the Windows account and directory permissions. Legacy fields are retained only as opaque compatibility data. Exports go to the location you choose.

请勿向公开仓库或 Issue 提交真实任务、健康/消费记录、备份或导出图片。

Do not submit real tasks, health/spending records, backups, or exported boards to the public repository or issues.

安全问题请通过仓库的 **Security → Report a vulnerability** 报告（如已启用），附软件版本、Windows 版本和虚构数据的最小复现。请勿在公开 Issue 发布敏感信息或可利用细节。

Report security issues through **Security → Report a vulnerability**, if enabled, with the app version, Windows version, and a minimal reproduction using fictional data. Keep sensitive information and exploit details out of public issues.
