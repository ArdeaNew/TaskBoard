param()
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'tools\SourceFiles.ps1')
$binary = Join-Path $projectRoot 'artifacts\app\TaskBoard.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Run build.ps1 -Test before packaging.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = [Reflection.AssemblyName]::GetAssemblyName($binary).Version.ToString(3)
$distribution = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$stage = Join-Path $artifactsRoot ('package-' + [Guid]::NewGuid().ToString('N'))
$portableStage = Join-Path $stage 'portable'
$sourceStage = Join-Path $stage 'source'
New-Item -ItemType Directory -Path $portableStage, $sourceStage -Force | Out-Null
try {
    Copy-Item -LiteralPath $binary -Destination $portableStage
    Copy-Item -LiteralPath ($binary + '.config') -Destination $portableStage
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $portableStage
    $portableReadme = @'
TaskBoard

ISO 周数、月余和年余百分比，让计划与时间进度始终可见；剩余天数包含今天。
ISO week number, month remaining, and year remaining keep time in view; remaining days include today.

Windows 10/11 + .NET Framework 4.8 或更新版本。完整解压，双击 TaskBoard.exe，无需安装。
Windows 10/11 + .NET Framework 4.8 or later. Extract all files and open TaskBoard.exe; no installation.

完全本地运行，无账号、遥测、自动更新或网络请求。任务在 %LOCALAPPDATA%\TaskBoard。
Fully local: no account, telemetry, updater, or network requests. Data: %LOCALAPPDATA%\TaskBoard.
关闭应用后复制整个数据目录以备份；更新程序不会清空任务。
Close the app and copy the entire data directory to back up. Updating app files does not clear tasks.

英文界面，支持中文任务；Windows 打印/保存对话框跟随系统语言。
English interface with Chinese task support; native print/save dialogs follow Windows language.
点击空白总是在当天末尾新增，点击任务编辑。Enter 保存，Shift+Enter 换行，Esc 取消。
Click blank space to append at the day's end, or a task to edit. Enter saves; Shift+Enter adds a line; Esc cancels.
拖动排序，普通任务可跨天移动，日常任务仅当天排序。完成项置顶，待办保持用户顺序。
Drag to reorder; ordinary tasks move across days, daily tasks stay within their day. Completed tasks move to the top; pending tasks keep your order.
右击 Apply to every day 补齐日常副本并将未完成副本放到每天末尾；末尾方块切换完成。
Right-click Apply to every day to create missing copies and place pending copies at each day's end; the trailing square toggles completion.
圆点设置文字色；右击 Background color 填充整块，支持预设、#RRGGBB 和 No fill；每天背景独立。
The dot sets text color; right-click Background color for presets, #RRGGBB, or No fill. Daily fills are independent.
顶部箭头切换周，点击周数选日期，This week 返回本周；月余/年余始终对应系统当天。
Use header arrows for weeks, click the week number for dates, or This week to return. Month/year remaining always use today.
历史与未来周独立保存；新未来周继承日常任务并重置完成。
Past and future plans save separately. New future weeks inherit daily tasks with completion reset.
系统跨周或重启时，过去周中未完成项自动移到本周周日末尾；完成历史与未来计划保留。
At a system week change or startup, overdue pending tasks move to the current Sunday's end; completed history and future plans remain.
未完成日常副本移为独立待办；只延续实际存在的任务，不补造错过周的日常副本。
Missed daily occurrences become independent tasks. Only existing tasks move; skipped weeks do not generate invented daily copies.
右击 Move to next week 将单项移到所选周的下一周周日；颜色、完成状态保留，日常解除当前项关联，Ctrl+Z 恢复两周。
Right-click Move to next week to move one task to the following Sunday; colors and completion stay intact, daily occurrences detach, and Ctrl+Z restores both weeks.
在某天底部停留显示 Clear day；核对日期/任务数后确认，只清空该天，Ctrl+Z 撤销。
Hover at a day's bottom for Clear day. Confirm its date/count to clear only that day; Ctrl+Z undoes it.

Ctrl+N：当天对应列末尾新增 / Append to today's weekday in the selected week
Ctrl+Z：撤销 / Undo
Ctrl+P：打印预览，缩放或 Fit page，再 Print；默认 A4 横向
Ctrl+P: Preview, zoom or Fit page, then Print; A4 landscape by default
Ctrl+Shift+S：导出完整 PNG / Export the full week as PNG

源码包包含构建、测试、图标和自动构建配置；完整中英文说明见仓库。
The source package includes build scripts, tests, icons, and CI; find the full bilingual guide in the repository.
https://github.com/ArdeaNew/TaskBoard
'@
    [IO.File]::WriteAllText((Join-Path $portableStage 'README.txt'), $portableReadme, [Text.UTF8Encoding]::new($true))
    Copy-TaskBoardSource $projectRoot $sourceStage
    $portableZip = Join-Path $distribution ('TaskBoard-' + $version + '-portable.zip')
    $sourceZip = Join-Path $distribution ('TaskBoard-' + $version + '-source.zip')
    foreach ($entry in @(@{ Directory = $portableStage; Archive = $portableZip }, @{ Directory = $sourceStage; Archive = $sourceZip })) {
        $temporary = $entry.Archive + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.Compression.ZipFile]::CreateFromDirectory($entry.Directory, $temporary, [IO.Compression.CompressionLevel]::Optimal, $false)
            if (Test-Path -LiteralPath $entry.Archive) { [IO.File]::Replace($temporary, $entry.Archive, [NullString]::Value) }
            else { [IO.File]::Move($temporary, $entry.Archive) }
        }
        finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
    }
    $checksums = foreach ($file in @($portableZip, $sourceZip)) {
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file)
    }
    [IO.File]::WriteAllLines((Join-Path $distribution 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
    Get-ChildItem -LiteralPath $distribution -File | Select-Object Name, Length
}
finally {
    $resolved = [IO.Path]::GetFullPath($stage)
    $allowedPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar + 'package-'
    if (-not $resolved.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the package staging directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
