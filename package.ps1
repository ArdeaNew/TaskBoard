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

Windows 10/11 + .NET Framework 4.8 或更新版本。
解压完整文件夹，双击 TaskBoard.exe 即可运行，无需安装。

软件完全本地运行，无账号、广告、遥测、自动更新或网络请求。
各周任务保存在 %LOCALAPPDATA%\TaskBoard，移动或更新程序不会清空数据。

单击格子空白处新增任务，单击任务编辑。Enter 保存，Shift+Enter 换行。
拖动任务可排序或移动到另一天；日常任务在各天末尾，完成任务自动置顶。
任务右键“应用到每天”创建日常任务；末尾方块切换完成状态。
顶部箭头切换周，点击周数选择日期，房屋图标返回本周。
历史周和未来周独立保存，新建未来周会带入日常任务并重置完成状态。
鼠标在每天底部停留后显示整列宽的清空按钮；核对日期和任务数，确认后清空该天，可用 Ctrl+Z 撤销。

Ctrl+N：今天新增任务
Ctrl+Z：撤销任务操作
Ctrl+P：A4 整页打印
Ctrl+Shift+S：导出完整 PNG 图片

源码包包含构建、测试、图标源文件和 GitHub 自动构建配置。
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
