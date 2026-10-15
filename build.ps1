param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'TaskBoard.csproj') -Raw
$properties = $project.Project.PropertyGroup
$referenceRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path -LiteralPath $referenceRoot)) { throw '.NET Framework 4.8 Developer Pack is required.' }
$sdkRoot = Join-Path (Split-Path (Get-Command dotnet.exe).Source) 'sdk'
$compiler = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
    ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw '.NET SDK with the C# compiler is required.' }

function Get-ReferenceArguments([string[]]$Names) {
    $Names | ForEach-Object { '/reference:' + (Join-Path $referenceRoot ($_ + '.dll')) }
}
function Get-Sources([string[]]$Directories) {
    foreach ($directory in $Directories) {
        Get-ChildItem -LiteralPath (Join-Path $projectRoot ('src\TaskBoard\' + $directory)) -Filter '*.cs' -Recurse |
            Select-Object -ExpandProperty FullName
    }
}

# The project file owns production dependencies; tests use narrower layers.
$referenceNames = @('mscorlib') + @($project.Project.ItemGroup.Reference | ForEach-Object Include)
$references = Get-ReferenceArguments $referenceNames
$flags = @('/nologo','/noconfig','/nostdlib+',('/langversion:' + $properties.LangVersion),'/optimize+','/deterministic+','/warn:4')
$sources = Get-Sources @('')
$output = Join-Path $projectRoot $properties.OutputPath
New-Item -ItemType Directory -Path $output -Force | Out-Null
$binary = Join-Path $output ($properties.AssemblyName + '.exe')
$icon = Join-Path $projectRoot $properties.ApplicationIcon
if (-not (Test-Path -LiteralPath $icon)) { throw 'Application icon is missing.' }
& dotnet $compiler @flags @references /target:winexe ('/out:' + $binary) ('/win32manifest:' + (Join-Path $projectRoot $properties.ApplicationManifest)) ('/win32icon:' + $icon) @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot $properties.AppConfig) -Destination ($binary + '.config') -Force

if ($Test) {
    $testOutput = Join-Path $projectRoot 'artifacts\tests'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $coreReferences = @('mscorlib','System','System.Core','System.Xml','System.Runtime.Serialization')
    $renderReferences = $coreReferences + @('WindowsBase','PresentationCore')
    $suites = @(
        @{ Name = 'Core'; File = 'CoreTests.cs'; Entry = 'CoreTests'; Sources = @(Get-Sources @('Core','Persistence')); References = $coreReferences },
        @{ Name = 'Rendering'; File = 'PrintLayoutTests.cs'; Entry = 'TaskBoard.Tests.PrintLayoutTests'; Sources = @(Get-Sources @('Core','Rendering')); References = $renderReferences },
        @{ Name = 'Interaction'; File = 'InteractionTests.cs'; Entry = 'TaskBoard.Tests.InteractionTests'; Sources = $sources; References = $referenceNames }
    )
    foreach ($suite in $suites) {
        $testBinary = Join-Path $testOutput ('TaskBoard.' + $suite.Name + 'Tests.exe')
        $testReferences = Get-ReferenceArguments $suite.References
        & dotnet $compiler @flags @testReferences /target:exe ('/main:' + $suite.Entry) ('/out:' + $testBinary) @($suite.Sources) (Join-Path $projectRoot ('tests\' + $suite.File))
        if ($LASTEXITCODE -ne 0) { throw ($suite.Name + ' test build failed.') }
        & $testBinary
        if ($LASTEXITCODE -ne 0) { throw ($suite.Name + ' tests failed.') }
    }
}
Get-Item -LiteralPath $binary | Select-Object FullName, Length
