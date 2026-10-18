param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'tools\BuildEnvironment.ps1')
$build = Get-TaskBoardBuildEnvironment $projectRoot
$properties = $build.Properties

# The project file owns production dependencies; tests use narrower layers.
$referenceNames = $build.ReferenceNames
$references = Get-TaskBoardReferenceArguments $build $referenceNames
$flags = $build.Flags
$sources = @(Get-TaskBoardSources $build)
$output = Join-Path $projectRoot $properties.OutputPath
New-Item -ItemType Directory -Path $output -Force | Out-Null
$binary = Join-Path $output ($properties.AssemblyName + '.exe')
$icon = Join-Path $projectRoot $properties.ApplicationIcon
if (-not (Test-Path -LiteralPath $icon)) { throw 'Application icon is missing.' }
& $build.DotNet $build.Compiler @flags @references /target:winexe ('/out:' + $binary) ('/win32manifest:' + (Join-Path $projectRoot $properties.ApplicationManifest)) ('/win32icon:' + $icon) @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot $properties.AppConfig) -Destination ($binary + '.config') -Force

if ($Test) {
    $testOutput = Join-Path $projectRoot 'artifacts\tests'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $coreReferences = @('mscorlib','System','System.Core','System.Xml','System.Runtime.Serialization')
    $renderReferences = $coreReferences + @('WindowsBase','PresentationCore')
    $suites = @(
        @{ Name = 'Core'; File = 'CoreTests.cs'; Entry = 'CoreTests'; Sources = @(Get-TaskBoardSources $build @('Core','Persistence')); References = $coreReferences },
        @{ Name = 'Rendering'; File = 'PrintLayoutTests.cs'; Entry = 'TaskBoard.Tests.PrintLayoutTests'; Sources = @(Get-TaskBoardSources $build @('Core','Rendering')); References = $renderReferences },
        @{ Name = 'Interaction'; File = 'InteractionTests.cs'; Entry = 'TaskBoard.Tests.InteractionTests'; Sources = $sources; References = $referenceNames },
        @{ Name = 'InboxInteraction'; File = 'InboxInteractionTests.cs'; Entry = 'TaskBoard.Tests.InboxInteractionTests'; Sources = $sources; References = $referenceNames },
        @{ Name = 'PrintPipeline'; File = 'PrintPipelineTests.cs'; Entry = 'TaskBoard.Tests.PrintPipelineTests'; Sources = $sources; References = $referenceNames }
    )
    foreach ($suite in $suites) {
        $testBinary = Join-Path $testOutput ('TaskBoard.' + $suite.Name + 'Tests.exe')
        $testReferences = Get-TaskBoardReferenceArguments $build $suite.References
        & $build.DotNet $build.Compiler @flags @testReferences /target:exe ('/main:' + $suite.Entry) ('/out:' + $testBinary) @($suite.Sources) (Join-Path $projectRoot ('tests\' + $suite.File))
        if ($LASTEXITCODE -ne 0) { throw ($suite.Name + ' test build failed.') }
        & $testBinary
        if ($LASTEXITCODE -ne 0) { throw ($suite.Name + ' tests failed.') }
    }
    & (Join-Path $projectRoot 'tests\CleanupTests.ps1')
}
Get-Item -LiteralPath $binary | Select-Object FullName, Length
