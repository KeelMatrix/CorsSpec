[CmdletBinding()]
param([string]$IgnoreFile = (Join-Path $PSScriptRoot '../.gitignore'))

$ErrorActionPreference = 'Stop'
$lines = @(Get-Content -LiteralPath $IgnoreFile)
$required = [ordered]@{
    'bin/' = 'src/Nested/Product/bin/Debug/product.dll'
    'obj/' = 'tests/Nested/Product/obj/project.assets.json'
    'artifacts/' = 'artifacts/reports/report.xml'
    '.vs/' = '.vs/Product/cache.bin'
    '.idea/' = 'tests/Nested/.idea/workspace.xml'
    '.vscode/' = '.vscode/.browse.VC.db'
    '*.user' = 'src/Nested/Product.csproj.user'
    '*.suo' = 'src/Nested/Product.suo'
    '*.userprefs' = 'src/Nested/Product.userprefs'
    '**/Properties/launchSettings.json' = 'tests/Nested/Web/Properties/launchSettings.json'
    'TestResults/' = 'tests/Nested/TestResults/result.xml'
    'coverage/' = 'tests/Nested/coverage/results.xml'
    '*.log' = 'tests/Nested/build.log'
    '*.cache' = 'tests/Nested/ide.cache'
    '.DS_Store' = 'src/Nested/.DS_Store'
    'Thumbs.db' = 'tests/Nested/Thumbs.db'
}
$generated = @($required.Values) + @('src/Web/Properties/launchSettings.json', 'Properties/launchSettings.json')
$source = @('src/Nested/Product.cs', 'src/Nested/Product.csproj', 'tests/Nested/Test.cs', 'docs/settings.json', 'README.md', 'NuGet.config')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('corsspec-ignore-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize ignore fixture.' }
    $fixtureIgnore = Join-Path $fixture '.gitignore'
    $emptyExcludes = Join-Path $fixture 'empty-excludes'
    [IO.File]::WriteAllText($emptyExcludes, '')

    function Get-Ignored([string[]]$Paths) {
        foreach ($path in $Paths) {
            & git -C $fixture -c "core.excludesFile=$emptyExcludes" check-ignore --quiet --no-index -- $path
            if ($LASTEXITCODE -eq 0) { $path }
            elseif ($LASTEXITCODE -ne 1) { throw 'git check-ignore failed.' }
        }
    }

    [IO.File]::WriteAllLines($fixtureIgnore, $lines)
    $ignored = @(Get-Ignored $generated)
    $missing = @($generated | Where-Object { $_ -notin $ignored })
    if ($missing.Count -ne 0) { throw "Missing required generated-file coverage: $($missing -join ', ')." }
    $hidden = @(Get-Ignored $source)
    if ($hidden.Count -ne 0) { throw "Ignore rules hide intentional product files: $($hidden -join ', ')." }

    # Removing each required rule must make its representative path visible.
    foreach ($pattern in $required.Keys) {
        $without = @($lines | Where-Object { $_ -cne $pattern })
        [IO.File]::WriteAllLines($fixtureIgnore, $without)
        if ($required[$pattern] -in (Get-Ignored @($required[$pattern]))) {
            throw "Required ignore rule '$pattern' is absent or its removal negative control is masked."
        }
    }

    # An overbroad replacement must be detectable by the product-file probes.
    [IO.File]::WriteAllLines($fixtureIgnore, @($lines) + 'src/')
    if ('src/Nested/Product.cs' -notin (Get-Ignored $source)) { throw 'Broad-ignore negative control failed.' }

    [IO.File]::WriteAllLines($fixtureIgnore, $lines)
    $trackedPath = 'src/Web/Properties/launchSettings.json'
    $trackedFile = Join-Path $fixture $trackedPath
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($trackedFile)) -Force | Out-Null
    [IO.File]::WriteAllText($trackedFile, '{}')
    & git -C $fixture add --force -- $trackedPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage intentional tracked fixture.' }
    $tracked = @(& git -C $fixture ls-files -- $trackedPath)
    if ($LASTEXITCODE -ne 0 -or $trackedPath -notin $tracked) { throw 'Ignore rules lost an intentional tracked file.' }
    & git -C $fixture -c "core.excludesFile=$emptyExcludes" check-ignore --quiet -- $trackedPath
    if ($LASTEXITCODE -ne 1) { throw 'An intentional tracked file was treated as untracked ignored output.' }

    Write-Output "Ignore coverage passed: $($generated.Count) generated paths, $($source.Count) product paths, $($required.Count) removal controls and tracked-file preservation."
}
finally {
    if ([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
