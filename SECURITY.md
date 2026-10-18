# 安全与隐私 / Security and privacy

TaskBoard 完全在本地运行，无账号、遥测或云同步，不发送网络请求。

TaskBoard runs locally without accounts, telemetry, cloud sync, or network requests.

## 本地数据 / Local data

任务和备份默认保存在 `%LOCALAPPDATA%\TaskBoard`，也可通过 `--data-dir` 指定目录。数据为未加密的 JSON，访问权限取决于 Windows 账户和目录权限。请关闭应用后备份整个数据目录。

Tasks and backups are stored in `%LOCALAPPDATA%\TaskBoard` by default, or a directory selected with `--data-dir`. Data is unencrypted JSON; access depends on Windows account and directory permissions. Close the app before backing up the entire data directory.

提交问题或截图时，请使用虚构任务，不要上传真实任务、数据文件、备份或个人导出图片。

Use fictional tasks in reports and screenshots. Do not upload personal tasks, data files, backups, or exported boards.

## 报告安全问题 / Reporting vulnerabilities

如仓库已启用私密漏洞报告，请通过 **Security → Report a vulnerability** 提交软件版本、Windows 版本及使用虚构数据的复现步骤。请勿在公开 Issue 中披露敏感信息或可利用的漏洞细节。

If private vulnerability reporting is enabled, use **Security → Report a vulnerability** with the app version, Windows version, and reproduction steps using fictional data. Do not disclose sensitive information or exploitable vulnerability details in public issues.
