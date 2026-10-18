function Get-TaskBoardBuildEnvironment([string]$ProjectRoot) {
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'TaskBoard.csproj') -Raw
    $properties = $project.Project.PropertyGroup
    $referenceRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    if (-not (Test-Path -LiteralPath $referenceRoot)) { throw '.NET Framework 4.8 Developer Pack is required.' }
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    $sdkRoot = Join-Path (Split-Path $dotnet -Parent) 'sdk'
    $compiler = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
        ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) { throw '.NET SDK with the C# compiler is required.' }
    [pscustomobject]@{
        ProjectRoot = $root
        Properties = $properties
        DotNet = $dotnet
        Compiler = $compiler
        ReferenceRoot = $referenceRoot
        ReferenceNames = @('mscorlib') + @($project.Project.ItemGroup.Reference | ForEach-Object Include)
        Flags = @('/nologo', '/noconfig', '/nostdlib+', ('/langversion:' + $properties.LangVersion), '/optimize+', '/deterministic+', '/warn:4')
    }
}

function Get-TaskBoardReferenceArguments($Build, [string[]]$Names) {
    foreach ($name in $Names) {
        $path = Join-Path $Build.ReferenceRoot ($name + '.dll')
        if (-not (Test-Path -LiteralPath $path)) { throw ('Framework reference is missing: ' + $name) }
        '/reference:' + $path
    }
}

function Get-TaskBoardSources($Build, [string[]]$Directories = @('')) {
    foreach ($directory in $Directories) {
        Get-ChildItem -LiteralPath (Join-Path $Build.ProjectRoot ('src\TaskBoard\' + $directory)) -Filter '*.cs' -File -Recurse |
            Sort-Object FullName | Select-Object -ExpandProperty FullName
    }
}
