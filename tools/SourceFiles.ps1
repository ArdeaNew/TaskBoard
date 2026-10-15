function Get-TaskBoardSourceFiles([string]$ProjectRoot) {
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    # Public source is an explicit allowlist, independent of local folder contents.
    $entries = @('.github', 'assets', 'docs', 'src', 'tests', 'tools',
        '.gitattributes', '.gitignore', 'TaskBoard.csproj', 'build.ps1', 'clean.ps1',
        'deploy.ps1', 'package.ps1', 'README.md', 'CONTRIBUTING.md', 'SECURITY.md',
        'CHANGELOG.md', 'VALIDATION.md', 'LICENSE')
    foreach ($entry in $entries) {
        $path = Join-Path $root $entry
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path
        $files = if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $path -Recurse -File -Force } else { @($item) }
        foreach ($file in $files) {
            # Omit accidental local data even if copied into an allowed directory.
            if ($file.Name -match '^(board.*\.json|\.env(\..*)?)$' -or
                $file.Extension -in @('.log', '.tmp', '.bak', '.user', '.suo') -or
                $file.Name -in @('Thumbs.db', '.DS_Store')) { continue }
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source packages cannot contain file links.' }
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
