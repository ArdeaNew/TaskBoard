# 更新记录 / Changelog

## 1.4.1 — 2026-10-15

- 修正小屏幕环境下拖拽检查对滚动位置的假设，覆盖窄列中滚动至任务末尾后跨天放置的情况。
  Correct the drag check's scroll-position assumption on small displays, covering cross-day placement after scrolling to the task list's end in narrow columns.

## 1.4.0 — 2026-10-15

- 修复空白点击新增位置不可预测：始终追加到当天末尾；待办保留用户顺序，日常任务不再持续强制置底，完成任务仍置顶。
  Fix unpredictable blank-space insertion: always append to the day's end. Pending tasks retain user order, daily tasks are no longer continually forced to the bottom, and completed tasks remain at the top.
- 应用界面统一为英文，包含星期、菜单、清空按钮、预览与确认框；用 **This week** 替代房屋图标，清空按钮显示 **Clear day**。
  Use English throughout the app, including day labels, menus, clear actions, preview, and confirmations. Replace the home icon with **This week** and label the footer **Clear day**.
- 界面、打印及图片共用日期标签；突出 ISO 周数和按系统当天计算、包含今天的月余/年余百分比。使用说明与开发文档改为中英文对照。
  Share date labels across screen, print, and images. Highlight ISO week numbers and month/year remaining based on today, including today. Provide bilingual user and developer documentation.

## 1.3.0 — 2026-10-15

- 添加整页打印预览、缩放、适合页面与打印机选择；文字采用矢量轮廓，修复星期标题缺失，保留 A4 横向与硬边距适配。
  Add full-page preview, zoom, fit, and printer selection. Use vector text outlines to fix missing day headers, retaining A4 landscape and hard-margin handling.
- 任务右击支持整块背景预设、自定义和清除；编辑、拖拽、撤销、归档及输出保持一致，各天日常背景独立。
  Add full-block background presets, custom colors, and clearing. Keep fills consistent through editing, dragging, undo, archives, and output, with independent daily fills.
- 提取共享调色板、按钮、页面视图和打印适配，增加 XPS 往返检查，无新增第三方依赖。
  Extract shared palettes, buttons, page views, and print adapters; add XPS round-trip checks without third-party dependencies.

## 1.2.1 — 2026-10-15

- 拆分 Core、Persistence、Rendering 和 UI，独立拖拽控制器与打印/导出适配；统一依赖声明和分层检查。
  Split Core, Persistence, Rendering, and UI with a dedicated drag controller and print/export adapters; centralize dependencies and layer checks.
- 删除废弃记录模型与窗口偏好，旧 JSON 字段保留为不透明兼容数据；整理重复入口、构建输出与安全清理。
  Remove obsolete record models and window preferences, retaining old JSON fields as opaque compatibility data. Simplify duplicate entry points, build output, and safe cleanup.

## 1.2.0 — 2026-10-15

- 统一跨天拖拽的列宽、换行、序号与完成标记；添加悬停显示、确认后清空当天并可撤销的底部按钮。
  Unify column width, wrapping, numbering, and completion during cross-day dragging. Add a hover footer to clear one day after confirmation, with undo.
- 放大任务栏图标主体，减少留白并改善小尺寸描边。
  Enlarge the taskbar icon's main grid, reducing padding and improving small-size strokes.

## 1.1.0 — 2026-10-15

- 移除下方记录区；添加历史/未来周导航、日期选择及本周返回，按周独立保存并继承日常任务，保留旧字段。
  Remove the records area. Add past/future navigation, date selection, and return to this week, with independent archives, daily inheritance, and legacy-field retention.

## 1.0.1 — 2026-10-15

- 当时的记录区改为任务方块样式并添加单位，打印与导出同步；此区域在 1.1.0 移除。
  Style the former records area as task blocks with units and matching print/export output; the area was removed in 1.1.0.

## 1.0.0 — 2026-10-15

- 首版七天看板、日常任务、完成置顶、序号、本地保存、自定义记录、A4 打印、PNG 导出及便携包。
  Initial seven-day board with daily tasks, completed-first order, numbering, local saves, custom records, A4 print, PNG export, and portable packages.
