# 参与开发 / Contributing

TaskBoard 使用原生 WPF、.NET Framework 4.8 和 C# 7.3。请保持极简英文界面、清晰职责、本地运行与轻量依赖，兼容中英文任务内容。

TaskBoard uses native WPF, .NET Framework 4.8, and C# 7.3. Keep the English interface minimal, responsibilities clear, execution local, and dependencies small. Support both Chinese and English task content.

在 Windows 上安装 .NET SDK 10 和 .NET Framework 4.8 Developer Pack，然后执行：

Install .NET SDK 10 and the .NET Framework 4.8 Developer Pack on Windows, then run:

```powershell
.\build.ps1 -Test
.\package.ps1
```

脚本使用本机编译器及 Framework 引用程序集，不执行 NuGet 还原。运行测试版时使用 `--data-dir ".\qa-data"`，避免改动自己的看板。

Scripts use the local compiler and Framework reference assemblies without NuGet restore. Run the test app with `--data-dir ".\qa-data"` to keep your own board untouched.

- `Core` 负责模型、顺序和日期；`Persistence` 负责 JSON；`Rendering` 负责矢量页面；`UI` 负责交互和系统对话框。业务层不引用界面或存储。
  `Core` owns models, ordering, and dates; `Persistence` owns JSON; `Rendering` owns vector pages; `UI` owns interaction and system dialogs. Business logic must not depend on UI or storage.
- 构建依赖统一声明在 `TaskBoard.csproj`；核心检查不引用 WPF，绘制检查不引用 UI 或打印组件。
  Declare build dependencies in `TaskBoard.csproj`. Core checks must not reference WPF; rendering checks must not reference UI or printing components.
- 为行为变更加有意义的验证；涉及界面时检查英文标签、缩放、中文输入法、空白新增、拖拽、打印与导出。
  Add meaningful verification for behavior changes. For UI work, check English labels, scaling, Chinese IME input, blank-space creation, dragging, printing, and export.
- 提交说明问题、结果和验证方式。使用虚构示例，不提交个人数据、截图、导出文件或构建产物；文档保持中英文对照。
  Describe the problem, result, and validation in changes. Use fictional examples; exclude personal data, screenshots, exports, and build output. Keep documentation bilingual.

本地构建、打包和测试不会自动上传或发布。更多边界见[架构说明](docs/ARCHITECTURE.md)和[发布检查](docs/RELEASE_CHECKLIST.md)。

Local builds, packages, and tests do not upload or publish anything. See [architecture](docs/ARCHITECTURE.md) and [release checks](docs/RELEASE_CHECKLIST.md) for details.
