# 参与开发 / Contributing

TaskBoard 使用 WPF、.NET Framework 4.8 和 C# 7.3。请保持界面简洁、完全本地运行，以及无第三方运行依赖。

TaskBoard uses WPF, .NET Framework 4.8, and C# 7.3. Keep the interface minimal, operation fully local, and runtime free of third-party dependencies.

## 构建与测试 / Build and test

在 Windows 上安装 .NET SDK 10 和 .NET Framework 4.8 Developer Pack，然后执行：

Install .NET SDK 10 and the .NET Framework 4.8 Developer Pack on Windows, then run:

```powershell
.\build.ps1 -Test
.\package.ps1
```

程序位于 `artifacts\app`，发布包位于 `dist`。使用独立数据目录运行开发版本：

The app is built into `artifacts\app`, with packages in `dist`. Run development builds with a separate data directory:

```powershell
.\artifacts\app\TaskBoard.exe --data-dir ".\qa-data"
```

## 提交更改 / Submit changes

- 保持[模块职责](docs/ARCHITECTURE.md)清晰，为行为变化提供相应测试。
  Follow the [module boundaries](docs/ARCHITECTURE.md) and cover behavior changes with relevant tests.
- 界面保持英文，支持中英文任务输入；文档保持中英文对照。
  Keep UI labels in English, support Chinese and English task input, and maintain bilingual documentation.
- 说明解决的问题、用户可见的变化和验证方式。
  Describe the problem, user-visible result, and how the change was verified.
- 示例与截图仅使用虚构内容，不提交个人数据、导出文件、配置密钥或构建产物。
  Use fictional examples and screenshots. Exclude personal data, exports, secrets, and build output.
