function Get-TaskBoardSourceTree([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source packages cannot contain filesystem links.' }
    if ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $Path -Force | Sort-Object Name) {
            Get-TaskBoardSourceTree $child.FullName
        }
    }
    else { $item }
}

function Get-TaskBoardSourceFiles([string]$ProjectRoot) {
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    # Public source is an explicit allowlist, independent of local folder contents.
    $entries = @('.github', 'assets', 'docs', 'src', 'tests', 'tools',
        '.gitattributes', '.gitignore', 'TaskBoard.csproj', 'build.ps1', 'clean.ps1',
        'deploy.ps1', 'package.ps1', 'README.md', 'CONTRIBUTING.md', 'SECURITY.md',
        'CHANGELOG.md', 'LICENSE')
    foreach ($entry in $entries) {
        $path = Join-Path $root $entry
        if (-not (Test-Path -LiteralPath $path)) { continue }
        foreach ($file in Get-TaskBoardSourceTree $path) {
            # Omit accidental local data even if copied into an allowed directory.
            if ($file.Name -match '^(board.*\.json|\.env(\..*)?)$' -or
                $file.Extension -in @('.log', '.tmp', '.bak', '.user', '.suo') -or
                $file.Name -in @('Thumbs.db', '.DS_Store')) { continue }
            $file
        }
    }
}

function Copy-TaskBoardSource([string]$ProjectRoot, [string]$Destination) {
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-TaskBoardSourceFiles $ProjectRoot) {
        $relative = $file.FullName.Substring($root.Length)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
