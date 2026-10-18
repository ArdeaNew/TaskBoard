param([Parameter(Mandatory = $true)][string]$DestinationRoot, [switch]$Update, [switch]$Prune)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\SourceFiles.ps1')
$DestinationRoot = [IO.Path]::GetFullPath($DestinationRoot)
$application = Join-Path $DestinationRoot 'TaskBoard'
$source = Join-Path $DestinationRoot 'Source\TaskBoard'
foreach ($path in @($application, $source)) {
    if (Test-Path -LiteralPath $path) {
        if (-not $Update -and (Get-ChildItem -LiteralPath $path -Force | Measure-Object).Count -gt 0) {
            throw "Destination already contains files: $path"
        }
    }
}
$binary = Join-Path $PSScriptRoot 'artifacts\app\TaskBoard.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Run build.ps1 first.' }
if ($Update -and (Test-Path -LiteralPath (Join-Path $application 'TaskBoard.exe'))) {
    $backup = Join-Path $application ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $application 'TaskBoard.exe') -Destination $backup
    if (Test-Path -LiteralPath (Join-Path $application 'TaskBoard.exe.config')) {
        Copy-Item -LiteralPath (Join-Path $application 'TaskBoard.exe.config') -Destination $backup
    }
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $backup 'Source') -Recurse }
    Get-ChildItem -LiteralPath $application -File | Where-Object { $_.Name -match '^TaskBoard-\d+\.\d+\.\d+\.exe(\.config)?$' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $backup }
}
New-Item -ItemType Directory -Path $application, $source -Force | Out-Null
$deployedBinary = Join-Path $application 'TaskBoard.exe'
try {
    Copy-Item -LiteralPath $binary -Destination $deployedBinary -Force
}
catch {
    $copyFailure = $_.Exception
    while ($copyFailure.InnerException) { $copyFailure = $copyFailure.InnerException }
    if (($copyFailure.HResult -band 0xFFFF) -notin @(32, 33)) { throw }
    # Renaming a loaded executable can be allowed even when overwriting it is
    # blocked. Preserve it inside this update's backup before installing.
    $renamed = $false
    if ($backup) {
        $occupiedBinary = [IO.Path]::GetFullPath((Join-Path $backup 'TaskBoard.in-use.exe'))
        $allowedBackupPrefix = [IO.Path]::GetFullPath((Join-Path $application 'backups')) + [IO.Path]::DirectorySeparatorChar
        if (-not $occupiedBinary.StartsWith($allowedBackupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid executable backup path.' }
        try { Move-Item -LiteralPath $deployedBinary -Destination $occupiedBinary; $renamed = $true }
        catch { Write-Host 'Original executable cannot be renamed while in use.' }
    }
    if ($renamed) {
        try { Copy-Item -LiteralPath $binary -Destination $deployedBinary }
        catch {
            if (Test-Path -LiteralPath $deployedBinary) { Remove-Item -LiteralPath $deployedBinary }
            Move-Item -LiteralPath $occupiedBinary -Destination $deployedBinary
            throw
        }
        Remove-Item -LiteralPath (Join-Path $backup 'TaskBoard.exe')
    }
    else {
        $releaseVersion = [Reflection.AssemblyName]::GetAssemblyName($binary).Version.ToString(3)
        $deployedBinary = Join-Path $application ('TaskBoard-' + $releaseVersion + '.exe')
        Copy-Item -LiteralPath $binary -Destination $deployedBinary -Force
        Write-Host ('Original program is in use; new version saved as ' + $deployedBinary)
    }
}
Copy-Item -LiteralPath ($binary + '.config') -Destination ($deployedBinary + '.config') -Force
Copy-TaskBoardSource $PSScriptRoot $source
foreach ($obsolete in @('src\TaskBoard\UI\DailyRecordsPanel.cs', 'src\TaskBoard\UI\Typography.cs',
    'src\TaskBoard\Core\BoardStore.cs', 'src\TaskBoard\Core\RecordField.cs',
    'src\TaskBoard\Core\RecordPresentation.cs', 'tests\UiPreview.cs', 'assets\icon-preview.png',
    'VALIDATION.md', 'docs\RELEASE_CHECKLIST.md')) {
    $obsoletePath = Join-Path $source $obsolete
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $obsolete)) -and (Test-Path -LiteralPath $obsoletePath)) {
        Remove-Item -LiteralPath $obsoletePath
    }
}
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'dist')) {
    $releases = Join-Path $DestinationRoot 'Releases\TaskBoard'
    New-Item -ItemType Directory -Path $releases -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'dist') -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $releases -Force
    }
}
$expected = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
$actual = (Get-FileHash -LiteralPath $deployedBinary -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'Deployed executable checksum mismatch.' }
if ($Prune) {
    # Only generated TaskBoard versions and timestamped rollback folders are
    # eligible; unrelated files and the user's data directory are never scanned.
    Get-ChildItem -LiteralPath $application -File | Where-Object {
        $_.Name -match '^TaskBoard-\d+\.\d+\.\d+\.exe$' -and $_.FullName -ne $deployedBinary
    } | ForEach-Object {
        $oldBinary = $_.FullName
        try {
            Remove-Item -LiteralPath $oldBinary
            if (Test-Path -LiteralPath ($oldBinary + '.config')) { Remove-Item -LiteralPath ($oldBinary + '.config') }
        }
        catch [IO.IOException] { Write-Host ('Still in use, retained: ' + $oldBinary) }
    }
    $backupRoot = [IO.Path]::GetFullPath((Join-Path $application 'backups'))
    $backupPrefix = $backupRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (Test-Path -LiteralPath $backupRoot) {
        Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}-\d{3}$' } |
            Sort-Object Name -Descending | Select-Object -Skip 1 | ForEach-Object {
                $target = [IO.Path]::GetFullPath($_.FullName)
                if (-not $target.StartsWith($backupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid backup cleanup path.' }
                try { Remove-Item -LiteralPath $target -Recurse -Force }
                catch [IO.IOException] { Write-Host ('Backup still in use, retained: ' + $target) }
            }
    }
    if ($releases) {
        $currentPackages = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'dist') -File | ForEach-Object Name)
        Get-ChildItem -LiteralPath $releases -File | Where-Object {
            $_.Name -match '^TaskBoard-\d+\.\d+\.\d+-(portable|source)\.zip$' -and $_.Name -notin $currentPackages
        } | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
    }
}
Get-Item -LiteralPath $deployedBinary, (Join-Path $source 'TaskBoard.csproj') | Select-Object FullName, Length
