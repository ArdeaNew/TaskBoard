# 架构 / Architecture

TaskBoard 是原生 WPF / .NET Framework 4.8 应用，没有第三方运行依赖。任务数据完全保存在本地，应用不发送网络请求。

TaskBoard is a native WPF / .NET Framework 4.8 app with no third-party runtime dependencies. Task data stays local, and the app makes no network requests.

## 模块 / Modules

| 目录 / Location | 职责 / Responsibility |
| --- | --- |
| `Program.cs` | 应用入口、数据目录与单实例管理。 / App entry point, data directory, and single-instance management. |
| `Core` | 任务与收集箱模型、顺序、日常任务、周计划和日期指标。 / Task and inbox models, ordering, daily tasks, weekly plans, and date metrics. |
| `Persistence` | JSON 读写、数据兼容、原子保存与备份恢复。 / JSON storage, compatibility, atomic saves, and backup recovery. |
| `Rendering` | 中英文字体、日期标签，以及预览、打印和图片共用的矢量页面。 / Chinese/English typography, date labels, and vector pages shared by preview, print, and export. |
| `UI` | 窗口、任务编辑、拖拽、菜单、撤销，以及 Windows 打印和保存对话框。 / Windows, task editing, dragging, menus, undo, and Windows print/save dialogs. |
| `tests` | 核心、存储、绘制与交互测试。 / Core, storage, rendering, and interaction tests. |
| `tools` | 构建支持和虚构演示图片生成。 / Build support and fictional demo image generation. |

依赖方向为 `Persistence → Core`、`Rendering → Core`、`UI → Core / Persistence / Rendering`。核心层不引用界面或存储。

Dependencies flow as `Persistence → Core`, `Rendering → Core`, and `UI → Core / Persistence / Rendering`. Core does not depend on UI or storage.

## 模型与交互 / Model and interaction

`BoardState` 包含周计划和跨周独立的收集箱。`WeekPlan` 按周一日期保存任务；`TaskItem` 保存文字、颜色、所在列和完成状态。日常任务通过 `DailyGroupId` 关联，同组各天保持独立完成状态与背景色。

`BoardState` contains weekly plans and a week-independent inbox. `WeekPlan` stores tasks by the week's Monday; `TaskItem` holds text, colors, column, and completion. `DailyGroupId` links daily occurrences while each day retains its own completion and fill.

`WeeklyPlanner` 负责周导航、日常任务继承和未完成任务延续；`InboxOperations` 负责分组及收集箱与看板之间的移动。界面调用这些规则，不另行实现同一业务逻辑。

`WeeklyPlanner` owns navigation, daily-task inheritance, and unfinished-task rollover. `InboxOperations` owns grouping and transfers between the inbox and board. UI calls these rules rather than duplicating business logic.

主窗口协调保存与撤销；收集箱窗口通过回调共享这些操作，不持有存储实例。任务卡片、调色板和字体由两个窗口复用。拖拽控制器独立管理指针与动画，输出使用提交后的任务状态。

The main window coordinates saving and undo. The inbox shares those operations through callbacks and owns no storage instance. Both windows reuse task cards, palettes, and typography. A separate drag controller handles pointer input and animation; output uses committed task state.

## 本地保存 / Local storage

默认数据目录为 `%LOCALAPPDATA%\TaskBoard`，可通过 `--data-dir` 指定其他目录。`board.json` 保存所有周与收集箱；`board.backup.json` 保存前次成功写入的数据。

The default data directory is `%LOCALAPPDATA%\TaskBoard`, overridable with `--data-dir`. `board.json` holds all weeks and the inbox; `board.backup.json` retains the previous successful save.

保存先写同目录临时文件，再原子替换。读取失败时尝试备份，并保留损坏文件。未知旧版根字段作为兼容数据保留。更新程序不要求迁移数据。

Saving writes a temporary file in the same directory before atomic replacement. Loading can recover from the backup while retaining damaged files. Unknown legacy root fields are preserved as compatibility data. Updating the app requires no data migration.

## 日期与输出 / Dates and output

ISO 周数跟随所选周；月余和年余百分比依据系统当天计算，剩余天数包含今天。界面、打印预览与 PNG 使用相同日期标签。

The ISO week number follows the selected week. Month/year remaining use the current system date and include today. Screen, print preview, and PNG share the same date labels.

预览与打印共用矢量页面及文字轮廓，默认 A4 横向，并适配打印机边距。PNG 使用相同布局；周输出不包含收集箱。

Preview and print share vector pages and text outlines, defaulting to A4 landscape while respecting printer margins. PNG uses the same layout; weekly output excludes the inbox.
