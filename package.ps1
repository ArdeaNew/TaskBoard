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

保留程序旁的 TaskBoard.exe.config。界面为英文，支持中文任务。
Keep TaskBoard.exe.config beside the app. English interface with Chinese task support.

点击空白在末尾新增，点击任务编辑。Enter 保存，Shift+Enter 换行，Esc 取消。
Click blank space to append or a task to edit. Enter saves, Shift+Enter adds a line, and Esc cancels.
拖动排序或跨天移动；点击末尾方块切换完成。圆点设置文字色，右击 Background color 设置背景色。
Drag to reorder or move between days; click the trailing square to toggle completion. Use the dot for text color and right-click Background color for fills.
右击 Apply to every day 创建日常任务，Move to next week 移至所选周的下一周周日。
Right-click Apply to every day for daily tasks, or Move to next week to move to the Sunday after the displayed week.
顶部箭头切换周，点击周数选日期，This week 返回本周。过去周未完成任务自动延续到本周周日。
Use header arrows to browse weeks, click the week number to choose a date, or This week to return. Unfinished past tasks roll over to the current Sunday.

顶部收集箱图标打开 Work 1 / Work 2 / Life。右击 Schedule for 安排到正在查看的周。
The header inbox icon opens Work 1 / Work 2 / Life. Right-click Schedule for to choose a date in the displayed week.
主看板右击 Move to inbox 移至收集箱；收集箱右击 Move to group 调整分组。
On the board, right-click Move to inbox to choose a group; in the inbox, use Move to group to regroup.

Ctrl+N：新增 / Add a task
Ctrl+Z：撤销 / Undo
Ctrl+P：A4 横向整页打印预览 / A4 landscape print preview
Ctrl+Shift+S：导出完整周 PNG / Export the full week as PNG

完全本地运行。数据自动保存在 %LOCALAPPDATA%\TaskBoard。
Fully local. Data saves automatically to %LOCALAPPDATA%\TaskBoard.
备份或更新前关闭程序并复制整个数据目录；更新时替换程序和随附的配置文件。
Before backup or update, close the app and copy the whole data directory. Replace the application and its included configuration file to update.

完整中英文使用说明与 MIT 许可证见仓库。
Find the full bilingual guide and MIT license in the repository.
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
