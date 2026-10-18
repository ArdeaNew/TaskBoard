param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'BuildEnvironment.ps1')
$build = Get-TaskBoardBuildEnvironment $projectRoot
$referenceArguments = Get-TaskBoardReferenceArguments $build $build.ReferenceNames
$sources = @(Get-TaskBoardSources $build)
$flags = $build.Flags
$demoRoot = Join-Path $projectRoot 'artifacts\demo'
$dataRoot = Join-Path $demoRoot ('data-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $projectRoot 'docs\images'
New-Item -ItemType Directory -Path $demoRoot -Force | Out-Null
$binary = Join-Path $demoRoot 'TaskBoard.DemoImages.exe'
& $build.DotNet $build.Compiler @flags @referenceArguments /target:exe /main:TaskBoard.Tools.DemoImages ('/out:' + $binary) @sources (Join-Path $PSScriptRoot 'DemoImages.cs')
if ($LASTEXITCODE -ne 0) { throw 'Demo image tool build failed.' }
try {
    & $binary $output $dataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Demo image rendering failed.' }
    Get-ChildItem -LiteralPath $output -Filter '*.png' | Select-Object Name, Length
}
finally {
    $resolvedData = [IO.Path]::GetFullPath($dataRoot)
    $allowed = [IO.Path]::GetFullPath($demoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar + 'data-'
    if (-not $resolvedData.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside isolated demo data.' }
    if (Test-Path -LiteralPath $resolvedData) { Remove-Item -LiteralPath $resolvedData -Recurse -Force }
}
