# 参与开发

TaskBoard 使用原生 WPF、.NET Framework 4.8 和 C# 7.3。请保持界面极简、代码职责清晰和本地运行方式，避免新增不必要的依赖。

在 Windows 上预先安装 .NET SDK 10 和 .NET Framework 4.8 Developer Pack，然后在项目目录执行：

```powershell
./build.ps1 -Test
./package.ps1
```

构建脚本直接使用本机编译器及 Framework 引用程序集，不执行 NuGet 还原。打包要求和输出位置见 [README](README.md)。

- `Core` 只负责模型、排序和日期；`Persistence` 负责本地 JSON；`Rendering` 负责字体和页面绘制；`UI` 负责 WPF 交互及系统打印/保存对话框。业务层不引用界面或文件存储。
- 构建依赖统一放在 `TaskBoard.csproj`。核心检查不引用 WPF，绘制检查不引用界面控件或打印组件；保持这些边界。
- 修改行为时添加有意义的验证；涉及界面时检查缩放、中文输入、拖拽及打印/图片导出。
- 提交变更时说明问题、结果和验证方式。示例与截图使用虚构数据，不提交个人看板、导出文件或构建产物。

本地修改与打包不会自动上传或发布项目。GitHub 工作流仅在仓库托管后由对应事件运行。
