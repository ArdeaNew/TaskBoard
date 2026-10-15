# 发布检查 / Release checklist

目标为已具备 .NET Framework 4.8+ 的 Windows 10/11；运行用户无需开发工具。仅使用虚构数据与隔离目录，保留用户真实看板。

Target Windows 10/11 with .NET Framework 4.8+ already installed; end users need no development tools. Use fictional data in isolated directories and preserve the user's real board.

## 构建与验证 / Build and verify

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

- 核心：ISO 周/日期边界与闰年、包含今天的月余/年余、迁移、周归档、日常继承、顺序、原子保存及恢复。
  Core: ISO/date boundaries and leap years, month/year remaining including today, migration, weekly archives, daily inheritance, ordering, atomic saves, and recovery.
- 顺序：空白点击与 Ctrl+N 总在当天末尾新增；普通与日常待办保持用户顺序；完成置顶；取消空白草稿不落盘，切周撤销隔离。
  Ordering: blank clicks and Ctrl+N always append; pending ordinary/daily tasks keep user order; completion moves to the top; canceled drafts stay off disk; undo is isolated across navigation.
- 交互：编辑、中文输入法、跨天拖拽、取消与自动滚动；颜色预设/自定义/清除、每日日常背景独立、撤销及保存后恢复。
  Interaction: editing, Chinese IME, cross-day dragging, cancellation, and scrolling; preset/custom/cleared colors, independent daily fills, undo, and reload.
- 英文界面：七个星期、周与剩余指标、菜单、提示、预览和应用确认框；**This week** 清晰返回本周；**Clear day** 等宽悬停显示、默认取消，确认只影响一天。
  English UI: seven days, week/time metrics, menus, tooltips, preview, and app dialogs; **This week** clearly returns to the current week; **Clear day** appears on hover at column width, defaults to cancel, and affects one day only.
- 页面与流水线：七列、编号、完整背景和完成标记；密集任务单页无截断，硬边距适配；XPS 往返保留全部星期及文字轮廓，无字体/位图依赖，预览与文档共用矢量页面。
  Page/pipeline: seven columns, numbering, full fills, completion marks, dense single-page fit, and hard margins; XPS round-trip preserves every day header and text outline without font/bitmap dependencies; preview and document share the vector page.
- 导出其他周，核对周数与文件名对应所选周，月余/年余对应系统当天。查看 `artifacts/tests` 中生成的预览；不得随源码发布这些测试图。
  Export a different week: week number and filename follow selection while remaining percentages follow today. Inspect generated previews in `artifacts/tests`; keep them out of published source.

版本、构建大小、实际通过数量及未验证项目记录到 [VALIDATION.md](../VALIDATION.md)，不要沿用旧结果。自动检查不等于覆盖全部输入法、显示器或实体打印机；未实际出纸时明确说明。

Record the version, binary size, actual passing counts, and unverified areas in [VALIDATION.md](../VALIDATION.md), without reusing old results. Automated checks do not cover every IME, display, or physical printer; state when paper output has not been tested.

## 打包与隐私 / Package and privacy

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

- `dist/TaskBoard-1.4.0-portable.zip`：仅 `TaskBoard.exe`、`TaskBoard.exe.config`、`README.txt`、`LICENSE`。
  `dist/TaskBoard-1.4.0-portable.zip`: only `TaskBoard.exe`, `TaskBoard.exe.config`, `README.txt`, and `LICENSE`.
- `dist/TaskBoard-1.4.0-source.zip`：源码、脚本、图标、测试与中英文说明；排除 `.git`、`artifacts`、`qa-data`、`dist`、缓存、个人数据与截图。
  `dist/TaskBoard-1.4.0-source.zip`: source, scripts, icons, tests, and bilingual guides; exclude `.git`, `artifacts`, `qa-data`, `dist`, caches, personal data, and screenshots.
- 用 `Get-FileHash -Algorithm SHA256` 复核 `dist/SHA256SUMS.txt`；确认版本、嵌入图标及 ZIP 内容。检查源码及附件不含真实任务、备份或导出文件。
  Verify `dist/SHA256SUMS.txt` with `Get-FileHash -Algorithm SHA256`, then check the version, embedded icon, and archive contents. Ensure source and assets contain no real tasks, backups, or exports.

打包脚本不下载、不上传、不推送，也不向默认用户目录写测试数据。

Packaging does not download, upload, push, or write test data into the default user directory.

## 解压与升级 / Extract and upgrade

1. 解压至独立目录，保留配置文件；以 `TaskBoard.exe --data-dir "隔离目录"` 检查首次启动和重开。
   Extract into a separate directory with the configuration file. Use `TaskBoard.exe --data-dir "isolated-directory"` to verify first launch and reopening.
2. 核对约 70% 窗口、英文星期、周数和月余/年余；编辑中英文，空白新增应在末尾；完成、日常复制、颜色及顺序切周后保留。
   Check the roughly 70% window, English days, week number, and month/year remaining. Edit Chinese/English tasks and append via blank space; verify completion, daily copies, colors, and order survive navigation.
3. 打开新未来周只继承日常并重置完成；未安排的过去周为空。关闭重开保留归档并默认本周。
   A new future week inherits only daily tasks with completion reset; unplanned past weeks are empty. Reopening retains archives and defaults to this week.
4. PNG 包含周日末项与背景；预览核对七天、缩放和 **Fit page**，打印核对 A4 横向及实际边距/文字/颜色。
   PNG includes Sunday's last task and fills. Verify all days, zoom, and **Fit page** in preview; check A4 landscape and actual print margins/text/colors.
5. 用含旧 `recordFields` 的隔离数据验证升级保留原字段，但界面/输出不显示。任务与备份只能出现在所选目录，发布 ZIP 不含测试数据。
   With isolated legacy `recordFields` data, verify fields survive upgrade but stay absent from UI/output. Tasks and backups appear only in the chosen directory; release ZIPs contain no test data.

更新前关闭程序并备份整个数据目录。`deploy.ps1 -DestinationRoot "D:\TaskBoardRelease" -Update -Prune` 先备份程序与源码，再清理已知旧文件并保留一组回退备份，不删除 `%LOCALAPPDATA%\TaskBoard`。被占用文件先尝试移入备份，仍失败则另存 `TaskBoard-<version>.exe`，不结束运行进程。

Close the app and back up its entire data directory before updating. `deploy.ps1 -DestinationRoot "D:\TaskBoardRelease" -Update -Prune` backs up app/source, cleans known obsolete files, and retains one rollback group without deleting `%LOCALAPPDATA%\TaskBoard`. Locked files are first moved into backup if possible; otherwise the new binary uses `TaskBoard-<version>.exe`, without terminating the running process.
