# TaskBoard

极简的 Windows 周任务看板。七天并列，黑白线条，英文字体为等宽 Consolas，中文为宋体。原生 WPF / .NET Framework 4.8，零第三方依赖，完全本地运行，无需账号。

[下载最新版](https://github.com/ArdeaNew/TaskBoard/releases/latest) · [使用说明](#快速开始) · [开发文档](docs/ARCHITECTURE.md)

## 快速开始

1. 使用 Windows 10/11，系统需具备 .NET Framework 4.8 或更高版本。
2. 在仓库的 Releases 页面下载 `TaskBoard-<版本>-portable.zip`，解压到任意目录。
3. 双击 `TaskBoard.exe`，保留同目录的 `TaskBoard.exe.config`。无需安装，也无需开发工具。

程序默认打开本周，窗口居中，占可用屏幕宽、高的 70%。任务会自动保存到本机，不联网、不上传数据。

## 任务操作

- **新增与编辑**：单击某一天的空白处新增；单击任务编辑。Enter 保存，Shift+Enter 换行，Esc 取消。中文输入法选词时不会提前保存。
- **颜色**：点击任务上的圆点设置文字颜色；右击任务，选择“背景色”可填充整个任务区块。两者均支持预设色和 `#RRGGBB` 自定义色值，背景可清除。
- **排序与移动**：拖动任务调整顺序；普通任务可拖到另一天，日常任务只能在当天排序。编辑时可拖动左侧的小拖动柄；拖至列的上下边缘会自动滚动。
- **完成**：点击任务末尾的小方块切换完成状态。已完成任务显示蓝色标记并排在最上方；未完成的普通任务随后，未完成的日常任务始终排在最后。每天的序号自动更新。
- **日常任务**：右击任务，选择“应用到每天”。所选周的七天各有一份，重复操作不会重复添加；同组任务的文字和文字颜色同步，各天的背景色、完成状态独立。新副本继承原任务背景，之后可单独调整。
- **删除与清空**：右击任务可删除。鼠标停留在某一天底部时显示清空按钮，确认后只清空所选周当天的任务；可用 Ctrl+Z 撤销。
- **打印与导出**：顶部打印图标打开整页预览，可缩放或选择“适合页面”，再点击“打印”选择打印机。默认 A4 横向、约 8 mm 页边距，一页容纳全部任务；文字与线条以矢量输出。PNG 图片同样包含完整七天、滚动区域以外的任务、序号、文字色、背景色和完成标记。

## 按周计划

顶部左右箭头切换周，点击周数选择日期，房屋图标返回本周。每周从周一开始，周数遵循 ISO 8601。

各周的任务、顺序和完成状态独立保存。首次打开本周或未来周时，会从最近较早的已知周带入日常任务并重置完成状态；普通任务不带入。已经安排过的周保持原样，尚未安排的过去周为空白。

查看本周时，系统进入新周会自动切换；查看历史或未来周时保持所选周。顶部周数对应所选周，月余和年余百分比始终按系统当天计算，剩余天数包含今天。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| Ctrl+N | 在所选周中，今天对应的星期列新增任务 |
| F2 / Enter | 编辑当前选中的任务 |
| Enter | 保存编辑 |
| Shift+Enter | 编辑时换行 |
| Esc | 取消编辑或拖动 |
| Delete | 删除当前选中的任务 |
| Ctrl+Z | 撤销最近的任务操作；切换到其他周后重新开始记录 |
| Ctrl+P | 打开所选周打印预览；预览中再次按下则打印 |
| Ctrl+Shift+S | 将所选周导出为 PNG |

## 数据与备份

默认数据目录为 `%LOCALAPPDATA%\TaskBoard`：

- `board.json`：当前数据，包含各周计划。
- `board.backup.json`：上一次保存的数据备份。

备份或迁移时，先关闭程序，再复制整个数据目录。数据文件损坏时，程序会保留损坏文件并尝试从备份恢复。

需要独立的数据目录时，可这样启动：

```powershell
.\TaskBoard.exe --data-dir "D:\TaskBoardData"
```

## 从源码构建

开发需要预先安装 .NET SDK 和 .NET Framework 4.8 Developer Pack。项目没有第三方包，构建无需联网恢复依赖。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

程序输出到 `artifacts\app`，测试与预览输出到 `artifacts\tests`。可使用独立目录试运行，避免影响自己的任务：

```powershell
.\artifacts\app\TaskBoard.exe --data-dir ".\qa-data"
```

生成便携包、源码包和 SHA-256 校验文件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

输出位于 `dist`。更多开发说明见 [架构说明](docs/ARCHITECTURE.md)、[贡献指南](CONTRIBUTING.md)、[发布检查](docs/RELEASE_CHECKLIST.md) 和 [验证记录](VALIDATION.md)。

## 许可证

本项目采用 [MIT 许可证](LICENSE)。
