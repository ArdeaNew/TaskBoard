# 验证记录 / Validation

2026-10-15，TaskBoard 1.4.1，Windows，.NET Framework 4.8。 / TaskBoard 1.4.1 on Windows, targeting .NET Framework 4.8.

| 检查 / Check | 结果 / Result |
| --- | --- |
| Release 构建 / Release build | 无警告；主程序 102,400 bytes，配置 347 bytes；零第三方依赖。 / No warnings; app 102,400 bytes, config 347 bytes; no third-party dependencies. |
| 核心与存储 / Core and storage | 32 项通过：日期、迁移、恢复、归档、完成置顶、混合待办顺序、日常副本与背景。 / 32 passed: dates, migration, recovery, archives, completed-first sorting, mixed pending order, daily copies, and backgrounds. |
| 页面绘制 / Page rendering | 787 项通过：英文星期与日期标签、ISO 跨年边界、剩余百分比、矢量轮廓、背景、硬边距及密集任务。 / 787 passed: English days/date labels, ISO year boundaries, remaining percentages, vector outlines, fills, hard margins, and dense tasks. |
| WPF 控件 / WPF controls | 189 项通过：默认及最小窗口下的真实空白区连续点击追加、取消草稿、保存重开、撤销、日常排序、滚动到末尾后的跨天拖拽、英文界面及默认取消的确认框。 / 189 passed: repeated blank-area appends at default and minimum sizes, draft cancellation, save/reopen, undo, daily ordering, cross-day dragging after scrolling to the end, English UI, and default-cancel confirmation. |
| 打印流程 / Print pipeline | 94 项通过：单页固定文档、预览缩放、纸张能力和默认队列失败回退；XPS 写入重开后文字与背景保留，无位图或字体资源依赖。 / 94 passed: fixed page, preview zoom, media capabilities, and default-queue failure fallback; XPS round-trip preserves text and fills without bitmap or font-resource dependencies. |

已检查虚构任务的软件渲染图，包括最小宽度看板、打印预览和拖拽。最小宽度下，`This week` 与剩余百分比无重叠，`Clear day` 适合列宽。检查不显示窗口、不捕获鼠标、不注入桌面输入，也不提交打印作业。

Software-rendered synthetic boards, including minimum width, print preview, and dragging, were inspected. `This week` does not overlap the percentages, and `Clear day` fits the columns. Checks show no windows, capture no mouse, inject no desktop input, and submit no print jobs.

实际打印机出纸、不同驱动、物理悬停与拖拽、各类输入法及任务栏缓存刷新仍需人工验收。打印请求高质量和 600 DPI，最终设置由设备能力校验；PNG 以 192 DPI 输出。

Physical printing, driver variations, live hover/drag behavior, input methods, and taskbar cache refresh still need manual acceptance. Printing requests high quality and 600 DPI, subject to device validation; PNG export uses 192 DPI.

应用自有文字与确认框为英文，Windows 原生打印、保存对话框使用系统语言。中文用户任务与宋体映射保留。Core 不引用 WPF；Persistence、Rendering 各自依赖 Core；共享标签防止屏幕和输出文案不一致。

App-owned text and confirmations are English; native Windows print/save dialogs use the system language. Chinese task text and SimSun mapping remain. Core has no WPF reference; Persistence and Rendering each depend on Core, and shared labels keep screen and output consistent.

发布源码按显式清单收集，并仅在内存中与本机数据核对，未发现真实任务或旧记录内容。发布包排除个人数据、截图、测试输出、`.git` 和缓存；所有写入测试使用隔离目录，不修改默认用户看板。

Public source uses an explicit allowlist and was compared with local data only in memory; no actual task or legacy-record content was found. Packages exclude personal data, screenshots, test output, `.git`, and caches. All write tests use isolated directories and leave the default user board untouched.
