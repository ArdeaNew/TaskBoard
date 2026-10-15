# 架构与本地数据 / Architecture and local data

TaskBoard 是原生 WPF / .NET Framework 4.8 桌面应用。运行版只需程序及相邻配置文件，Windows 10/11 需已具备 Framework 4.8+；无第三方运行库、浏览器内核或 NuGet 包。

TaskBoard is a native WPF / .NET Framework 4.8 desktop app. The runtime needs the executable and its adjacent configuration file on Windows 10/11 with Framework 4.8+. There are no third-party runtime libraries, browser engines, or NuGet packages.

## 模块边界 / Module boundaries

| 位置 / Location | 职责 / Responsibility |
| --- | --- |
| `Program.cs` | STA 入口、数据目录、每个数据目录单实例、应用生命周期。 / STA entry point, data directory, one instance per data directory, and app lifetime. |
| `Core/TaskItem.cs`, `BoardState.cs`, `WeekPlan.cs` | 任务、日常分组、独立完成状态与周归档。 / Tasks, daily groups, independent completion, and weekly archives. |
| `Core/BoardOperations.cs`, `TaskColors.cs` | 每日复制、同步、颜色校验、顺序、清空与快照。 / Daily copies, synchronization, color validation, ordering, clearing, and snapshots. |
| `Core/WeeklyPlanner.cs`, `DateMetrics.cs` | 周导航、迁移、日常任务继承、ISO 周数与剩余百分比。 / Week navigation, migration, daily inheritance, ISO weeks, and remaining percentages. |
| `Persistence/BoardStore.cs`, `CompatibilityData.cs` | JSON 规范化、原子保存、备份恢复及未知旧字段保留。 / JSON normalization, atomic saves, backup recovery, and opaque legacy fields. |
| `UI/MainWindow.cs`, `TaskDragController.cs` | 布局、周导航、编辑/撤销/保存协调；指针、拖拽浮层、自动滚动与动画。 / Layout, week navigation, edit/undo/save coordination; pointer input, drag overlay, scrolling, and animation. |
| `UI/TaskCard.cs`, `ColorPalette.cs` | 编辑、输入法、文字/背景色、编号、完成状态及复用调色板。 / Editing, IME, text/background colors, numbering, completion, and reusable palettes. |
| `UI/DayClearButton.cs`, `ColumnPanel.cs`, `VisualTree.cs` | 底部悬停按钮、布局过渡和共享祖先查询。 / Footer hover actions, layout transitions, and shared ancestor lookup. |
| `UI/MinimalButton.cs`, `BoardDialog.cs` | 共享极简按钮及应用自己的英文消息/确认框。 / Shared minimal buttons and app-owned English messages/confirmations. |
| `Rendering/BoardLabels.cs`, `Typography.cs` | 界面与输出共用的星期/日期指标标签，以及中英文字体映射。 / Shared day/date-metric labels for screen and output, plus Latin/Chinese font mapping. |
| `Rendering/BoardPageRenderer.cs`, `BoardPage.cs` | 无文件访问或对话框的矢量排版、页面尺寸及文字轮廓。 / Vector layout, page dimensions, and text outlines without file access or dialogs. |
| `UI/PrintPreviewWindow.cs`, `PageVisual.cs` | 整页预览、缩放和适合页面，共用矢量绘图。 / Full-page preview, zoom, and fit using the shared vector drawing. |
| `UI/BoardPrinter.cs`, `BoardPrintDocument.cs`, `BoardImageExporter.cs` | 打印机票据、硬边距和固定文档；保存对话框、PNG 编码及原子输出。 / Printer tickets, hard margins, and fixed documents; save dialogs, PNG encoding, and atomic export. |

依赖单向流向模型：`Persistence → Core`、`Rendering → Core`、`UI → Core / Persistence / Rendering`。入口组合存储与窗口；拖拽控制器不依赖主窗口或存储。项目文件统一声明生产引用，核心检查不引用 WPF，绘制检查不引用 UI 或打印组件。

Dependencies point toward the model: `Persistence → Core`, `Rendering → Core`, and `UI → Core / Persistence / Rendering`. The entry point composes storage and UI; the drag controller does not depend on the main window or storage. The project declares production references centrally. Core checks exclude WPF; rendering checks exclude UI and printing components.

## 顺序与日常任务 / Ordering and daily tasks

`Column` 为 1–7，对应周一至周日；旧版 Column 0 迁到周一。完成项位于顶部，其余任务按已保存顺序排列，普通与日常任务没有持续强制分组。点击空白和 Ctrl+N 总是将新任务追加在目标列全部现有任务之后，点击位置不决定插入索引。序号按显示顺序生成，不写入任务文字。

`Column` ranges from 1–7, Monday through Sunday; legacy column 0 migrates to Monday. Completed tasks appear first; pending tasks retain their saved order without continuously separating ordinary and daily tasks. Blank-space clicks and Ctrl+N append after every existing task in the target column; pointer position does not select an insertion index. Numbering follows display order and is not stored in task text.

`DailyGroupId` 关联所选周内的日常任务文字与文字色。**Apply to every day** 补齐缺少的副本，并在操作时把未完成组成员放到每天末尾；已完成成员保持顺序，重复操作不重复生成。之后保留用户排序。各天完成状态和背景独立，日常任务不能跨天拖动。新副本继承源背景；新周优先继承对应星期的背景。

`DailyGroupId` links daily text and text color within the selected week. **Apply to every day** creates missing copies and moves pending group members to each day's end at that moment; completed members keep their order and repeated use creates no duplicates. Later user ordering is preserved. Completion and background remain independent, and daily tasks cannot move between days. New copies inherit the source fill; a new week prefers the corresponding weekday's fill.

可选 `BackgroundColor` 为规范化的 `#RRGGBB`；缺失、null 或无效值表示不填充。快照包含背景，撤销、归档、拖拽、预览及导出使用同一任务状态。

Optional `BackgroundColor` is normalized `#RRGGBB`; missing, null, or invalid values mean no fill. Snapshots include the background so undo, archives, dragging, preview, and export use the same task state.

## 周与日期指标 / Weeks and date metrics

`ActiveWeekStart` 为所选周的周一，格式 `yyyy-MM-dd`；`Weeks` 中各 `WeekPlan` 保存独立的 `WeekStart` 与 `Tasks`。根 `Tasks` 对应显示周，保存时同步其归档。启动打开系统本周；切周前提交编辑并保存，失败时不切换，成功后重置撤销和滚动位置。

`ActiveWeekStart` is the selected Monday in `yyyy-MM-dd` form; each `WeekPlan` in `Weeks` stores its own `WeekStart` and `Tasks`. Root `Tasks` represents the displayed week and synchronizes its archive on save. Startup opens the system's current week. Switching commits and saves first; a failed save prevents navigation, while success resets undo and scroll positions.

已有周恢复原状态。首次打开本周或未来周，从最近较早的已知周复制日常任务并重置完成状态；无归档的过去周为空。查看本周时跨周自动进入新周，查看其他周时保留选择。日常同步不影响其他周。无归档的旧任务迁入升级时的本周。

Existing weeks restore their state. A new current or future week copies daily tasks from the nearest earlier known week and resets completion; unarchived past weeks are empty. The current-week view follows system week changes; other selected weeks stay selected. Daily synchronization does not cross weeks. Legacy tasks without an archive migrate into the week current at upgrade.

`DateMetrics` 使用系统本地日期：ISO 周按周四所属年计算；月余为 `(月总天数 - 当日 + 1) / 月总天数`，年余同理，包含今天并处理闰年。显示周数取所选周，剩余百分比始终取系统当天；界面、打印与 PNG 通过 `BoardLabels` 使用一致标签。

`DateMetrics` uses local system dates: ISO weeks belong to the year containing Thursday; month remaining is `(days in month - day + 1) / days in month`, with the equivalent year calculation including today and handling leap years. The displayed week follows selection; remaining percentages always use today. `BoardLabels` keeps screen, print, and PNG labels consistent.

## 保存与恢复 / Saving and recovery

默认目录 `%LOCALAPPDATA%\TaskBoard` 可由 `--data-dir` 替换。`board.json` 保存所有周；`board.backup.json` 为前次成功数据；损坏原件改名为 `board.corrupt-*.json` 或 `board.backup.corrupt-*.json` 保留。同目录临时文件在正常写入后清理。

`--data-dir` can replace the default `%LOCALAPPDATA%\TaskBoard` directory. `board.json` stores all weeks; `board.backup.json` stores the preceding successful state. Damaged originals are retained as `board.corrupt-*.json` or `board.backup.corrupt-*.json`. Temporary files in the same directory are cleaned after successful writes.

保存合并为 300 ms 延迟写入；未提交编辑用编辑前快照，避免空白草稿写盘。关闭、切周、打印和导出前提交编辑。先写完并刷新临时文件，再同卷原子替换；无法安全保留损坏文件时停止保存。备份或迁移前关闭应用并复制整个数据目录。

Save requests coalesce over 300 ms. Uncommitted edits use their pre-edit snapshot so empty drafts do not reach disk. Closing, navigation, printing, and export commit edits first. Writes finish and flush a temporary file before same-volume atomic replacement; saving stops if damaged files cannot be safely preserved. Close the app and copy the entire data directory for backup or migration.

未知旧字段只作为不透明兼容数据保留，不参与界面或业务。`IExtensibleDataObject` 传递扩展数据，持久化层另存未知 JSON 成员树，规避 Framework 序列化器对空字符串及特殊键名的处理问题；格式和成员顺序可能变化。

Unknown legacy fields remain opaque compatibility data, unused by UI or business logic. `IExtensibleDataObject` carries extensions, while persistence retains unknown JSON member trees to avoid Framework serializer issues with empty strings and special keys. Formatting and member order may change.

## 交互与性能 / Interaction and performance

无持续绘制循环：日期每 30 秒检查；仅拖拽时启用滚动计时器。浮层复用 `TaskCard`，按目标列宽重新测量，列位置采用 FLIP 过渡；落位、取消或切周时清理动画与浮层，鼠标移动时不写盘。

There is no continuous rendering loop: dates are checked every 30 seconds, and scrolling timers run only during dragging. The overlay reuses `TaskCard`, remeasures at the target column width, and uses FLIP layout transitions. Placement, cancellation, and navigation clean up animations and overlays; pointer movement does not write to disk.

每天底部 40 DIP 区域停留 220 ms 后显示 **Clear day**，移开后淡出且保留区域高度。清空前提交编辑、取消拖拽，并显示具体日期与任务数，默认 **Cancel**；确认后仅删除该列并保留撤销快照。应用自己的对话框为英文，系统打印/保存对话框跟随 Windows 语言。

Each 40 DIP footer reveals **Clear day** after a 220 ms hover and fades out on exit without changing layout. Clearing commits edits, cancels dragging, and confirms the exact date/count with **Cancel** as default. Confirmation deletes only that column and retains an undo snapshot. App-owned dialogs are English; native print/save dialogs follow Windows language.

## 打印与导出 / Printing and export

输出使用所选周的保存快照，包含全部七天及滚动范围外任务，不包含其他周或旧记录区。默认 A4 横向、约 8 mm 页边距；打印票据按设备能力校验并尊重所选纸张与硬边距，必要时缩小排版以保持完整一页。

Output uses a saved snapshot of the selected week, including all seven days and off-screen tasks, excluding other weeks and the removed records area. Defaults are A4 landscape with about 8 mm margins. Printer tickets are validated against device capabilities, respecting paper and hard margins; layout shrinks when needed to fit one complete page.

`BoardPage` 保存纸张尺寸及冻结文字轮廓；`FormattedText.BuildGeometry` 避免打印机端中文字体缺失或替换。预览的 `PageVisual` 与 `BoardPrintDocument` 使用同一矢量绘图，后者放入单页 `FixedDocument` 后由 `PrintDocument` 提交。设置改变后同时更新预览。流水线检查写入并重新读取 XPS，验证星期标题、任务轮廓与背景。

`BoardPage` stores page dimensions and frozen text outlines. `FormattedText.BuildGeometry` avoids missing or substituted CJK fonts at the printer. Preview's `PageVisual` and `BoardPrintDocument` share the vector drawing; the latter places it in a single-page `FixedDocument` submitted by `PrintDocument`. Changed settings update the preview too. Pipeline checks write and reopen XPS to verify day headers, task outlines, and fills.

PNG 使用同一页面，以 192 DPI 编码，先写同目录临时文件再替换。页眉及默认文件名使用所选周，剩余百分比仍取系统当天。

PNG encodes the same page at 192 DPI through a same-directory temporary file and replacement. Header and default filename use the selected week; remaining percentages still use today.

## 离线与发布 / Offline operation and releases

应用不含 HTTP、套接字、遥测、自动更新或嵌入网页；构建/打包脚本不下载依赖，XML 命名空间 URL 仅为标识符。打印队列及用户选择的文件路径是否联网由 Windows 和用户配置决定，应用自身不上传。

The app contains no HTTP, sockets, telemetry, automatic updater, or embedded web content. Build/package scripts download no dependencies; XML namespace URLs are identifiers. Windows and user configuration determine whether selected print queues or file paths use a network. The app itself uploads nothing.

预装 .NET SDK 10 与 Framework 4.8 Developer Pack 后，`build.ps1 -Test` 运行四组检查。`package.ps1` 生成 `TaskBoard-1.4.1-portable.zip`、`TaskBoard-1.4.1-source.zip` 和 `SHA256SUMS.txt`；便携包只含程序、配置、说明和许可证，源码包按白名单排除数据及构建产物。

With .NET SDK 10 and Framework 4.8 Developer Pack installed, `build.ps1 -Test` runs four suites. `package.ps1` produces `TaskBoard-1.4.1-portable.zip`, `TaskBoard-1.4.1-source.zip`, and `SHA256SUMS.txt`. The portable package holds only the app, configuration, guide, and license; a source allowlist excludes data and build output.

GitHub Actions 在推送、拉取请求或手动触发时构建、检查并保存附件，无 Release 发布步骤。本地构建和打包不会上传；实际检查结果见 [VALIDATION.md](../VALIDATION.md)。

GitHub Actions builds, tests, and saves artifacts on pushes, pull requests, or manual runs; it has no Release publishing step. Local builds and packages do not upload anything. See [VALIDATION.md](../VALIDATION.md) for actual results.
