param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'TaskBoard.csproj') -Raw
$referenceRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path -LiteralPath $referenceRoot)) { throw '.NET Framework 4.8 Developer Pack is required.' }
$sdkRoot = Join-Path (Split-Path (Get-Command dotnet.exe).Source) 'sdk'
$compiler = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
    ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw '.NET SDK with the C# compiler is required.' }
$references = @('mscorlib') + @($project.Project.ItemGroup.Reference | ForEach-Object Include)
$referenceArguments = $references | ForEach-Object { '/reference:' + (Join-Path $referenceRoot ($_ + '.dll')) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\TaskBoard') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName
$demoRoot = Join-Path $projectRoot 'artifacts\demo'
$dataRoot = Join-Path $demoRoot ('data-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $projectRoot 'docs\images'
New-Item -ItemType Directory -Path $demoRoot -Force | Out-Null
$binary = Join-Path $demoRoot 'TaskBoard.DemoImages.exe'
& dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:7.3 /optimize+ /deterministic+ /warn:4 @referenceArguments /target:exe /main:TaskBoard.Tools.DemoImages ('/out:' + $binary) @sources (Join-Path $PSScriptRoot 'DemoImages.cs')
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
