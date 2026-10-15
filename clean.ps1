param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$allowedPrefix = $projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$versionFile = Join-Path $projectRoot 'src\TaskBoard\Properties\AssemblyInfo.cs'
$versionMatch = [regex]::Match([IO.File]::ReadAllText($versionFile), 'AssemblyInformationalVersion\("([^"]+)"\)')
if (-not $versionMatch.Success) { throw 'Release version is missing.' }
$version = $versionMatch.Groups[1].Value

# These folders contain only reproducible builds and isolated developer data.
foreach ($name in @('artifacts', 'qa-data', 'obj', 'bin')) {
    $target = [IO.Path]::GetFullPath((Join-Path $projectRoot $name))
    if (-not $target.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the project.'
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
$distribution = Join-Path $projectRoot 'dist'
if (Test-Path -LiteralPath $distribution) {
    $current = @(('TaskBoard-' + $version + '-portable.zip'), ('TaskBoard-' + $version + '-source.zip'))
    Get-ChildItem -LiteralPath $distribution -File -Filter 'TaskBoard-*.zip' |
        Where-Object { $_.Name -notin $current } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName }
    # Checksums are regenerated with the next package, never leave a stale list.
    $checksums = Join-Path $distribution 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $checksums) { Remove-Item -LiteralPath $checksums }
}
