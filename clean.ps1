[CmdletBinding(SupportsShouldProcess = $true)]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$allowedPrefix = $projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (((Get-Item -LiteralPath $projectRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Refusing cleanup from a linked project directory.'
}
$versionFile = Join-Path $projectRoot 'src\TaskBoard\Properties\AssemblyInfo.cs'
$versionMatch = [regex]::Match([IO.File]::ReadAllText($versionFile), 'AssemblyInformationalVersion\("([^"]+)"\)')
if (-not $versionMatch.Success) { throw 'Release version is missing.' }
$version = $versionMatch.Groups[1].Value

function Test-ProtectedDirectory([string]$Path) {
    # Inspect one level at a time: never traverse links, including junctions.
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $item = Get-Item -LiteralPath $directory -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
        foreach ($child in Get-ChildItem -LiteralPath $directory -Force) {
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
            if ($child.PSIsContainer) { $pending.Push($child.FullName) }
            elseif ($child.Name -like 'board*.json') { return $true }
        }
    }
    return $false
}

# qa-data and arbitrary --data-dir locations can contain real usage records.
# Keep a whole generated directory if task data or a filesystem link is present.
foreach ($name in @('artifacts', 'obj', 'bin')) {
    $target = [IO.Path]::GetFullPath((Join-Path $projectRoot $name))
    if (-not $target.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the project.'
    }
    if (-not (Test-Path -LiteralPath $target)) { continue }
    if (Test-ProtectedDirectory $target) {
        Write-Warning ('Preserving ' + $name + ': task data or filesystem links were found.')
        continue
    }
    if ($PSCmdlet.ShouldProcess($target, 'Remove reproducible build output')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
$distribution = Join-Path $projectRoot 'dist'
if (Test-Path -LiteralPath $distribution) {
    if (Test-ProtectedDirectory $distribution) {
        Write-Warning 'Preserving dist: task data or filesystem links were found.'
        return
    }
    $current = @(('TaskBoard-' + $version + '-portable.zip'), ('TaskBoard-' + $version + '-source.zip'))
    Get-ChildItem -LiteralPath $distribution -File -Filter 'TaskBoard-*.zip' |
        Where-Object { $_.Name -match '^TaskBoard-\d+\.\d+\.\d+-(portable|source)\.zip$' -and $_.Name -notin $current } |
        ForEach-Object {
            if ($PSCmdlet.ShouldProcess($_.FullName, 'Remove an obsolete release archive')) {
                Remove-Item -LiteralPath $_.FullName
            }
        }
    # Checksums are regenerated with the next package, never leave a stale list.
    $checksums = Join-Path $distribution 'SHA256SUMS.txt'
    if ((Test-Path -LiteralPath $checksums) -and $PSCmdlet.ShouldProcess($checksums, 'Remove generated checksums')) {
        Remove-Item -LiteralPath $checksums
    }
}
