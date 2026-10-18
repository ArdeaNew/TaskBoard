param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $projectRoot 'tools\SourceFiles.ps1')
$testRoot = Join-Path $projectRoot ('artifacts\tests\cleanup-' + [Guid]::NewGuid().ToString('N'))
$junction = $null
$checks = 0

function Write-Fixture([string]$Path, [string]$Contents = 'synthetic fixture') {
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Contents)
}

function New-Fixture([string]$Name) {
    $root = Join-Path $testRoot $Name
    Write-Fixture (Join-Path $root 'src\TaskBoard\Properties\AssemblyInfo.cs') '[assembly: AssemblyInformationalVersion("0.0.0")]'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'clean.ps1') -Destination $root
    $root
}

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

try {
    $root = New-Fixture 'generated'
    foreach ($name in @('artifacts\app\TaskBoard.exe.config', 'bin\old.dll', 'obj\cache',
        'qa-data\board.json', 'dist\TaskBoard-0.0.0-portable.zip', 'dist\TaskBoard-0.0.0-source.zip',
        'dist\TaskBoard-0.1.0-portable.zip', 'dist\TaskBoard-personal-backup.zip', 'dist\SHA256SUMS.txt', 'dist\notes.txt')) {
        Write-Fixture (Join-Path $root $name)
    }
    & (Join-Path $root 'clean.ps1')
    foreach ($name in @('artifacts', 'bin', 'obj', 'dist\TaskBoard-0.1.0-portable.zip', 'dist\SHA256SUMS.txt')) {
        Assert-Check (-not (Test-Path -LiteralPath (Join-Path $root $name))) ('Generated file was kept: ' + $name)
    }
    foreach ($name in @('qa-data\board.json', 'dist\TaskBoard-0.0.0-portable.zip', 'dist\TaskBoard-0.0.0-source.zip', 'dist\TaskBoard-personal-backup.zip', 'dist\notes.txt')) {
        Assert-Check (Test-Path -LiteralPath (Join-Path $root $name)) ('Retained file was removed: ' + $name)
    }

    $root = New-Fixture 'records'
    $records = @('artifacts\app\data\board.json', 'obj\board.backup.json', 'bin\nested\board.corrupt-example.json', 'dist\board.json')
    foreach ($name in $records) { Write-Fixture (Join-Path $root $name) }
    Write-Fixture (Join-Path $root 'dist\TaskBoard-0.1.0-source.zip')
    & (Join-Path $root 'clean.ps1') -WarningAction SilentlyContinue
    foreach ($name in $records) {
        Assert-Check ([IO.File]::ReadAllText((Join-Path $root $name)) -eq 'synthetic fixture') ('Task data changed: ' + $name)
    }
    Assert-Check (Test-Path -LiteralPath (Join-Path $root 'dist\TaskBoard-0.1.0-source.zip')) 'Protected distribution was changed.'

    $root = New-Fixture 'preview'
    Write-Fixture (Join-Path $root 'artifacts\cache.tmp')
    & (Join-Path $root 'clean.ps1') -WhatIf
    Assert-Check (Test-Path -LiteralPath (Join-Path $root 'artifacts\cache.tmp')) 'WhatIf changed generated files.'

    $root = New-Fixture 'links'
    $outside = Join-Path $testRoot 'outside-project'
    Write-Fixture (Join-Path $outside 'keep.txt')
    New-Item -ItemType Directory -Path (Join-Path $root 'artifacts') -Force | Out-Null
    $junction = Join-Path $root 'artifacts\link'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    & (Join-Path $root 'clean.ps1') -WarningAction SilentlyContinue
    Assert-Check (Test-Path -LiteralPath $junction) 'Cleanup removed a directory containing a link.'
    Assert-Check ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -eq 'synthetic fixture') 'Cleanup changed files outside the project.'

    # Public source collection must reject a directory link before entering it.
    New-Item -ItemType Directory -Path (Join-Path $root 'tools') -Force | Out-Null
    [IO.Directory]::Delete($junction)
    $junction = Join-Path $root 'tools\link'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $rejected = $false
    try { @(Get-TaskBoardSourceFiles $root) | Out-Null }
    catch { $rejected = $_.Exception.Message -eq 'Source packages cannot contain filesystem links.' }
    Assert-Check $rejected 'Source packaging accepted a directory link.'
    Write-Output ('Cleanup and source safety: ' + $checks + ' checks passed.')
}
finally {
    # Remove the test junction itself before recursively deleting isolated fixtures.
    if ($junction -and (Test-Path -LiteralPath $junction)) { [IO.Directory]::Delete($junction) }
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\tests')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar + 'cleanup-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside isolated safety fixtures.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
