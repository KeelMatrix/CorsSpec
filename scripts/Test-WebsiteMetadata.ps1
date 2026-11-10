[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WebsiteMetadata.ps1')
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-WebsiteProjectSelection.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Website project-selection regression failed.' }
$manifest = '{"schemaVersion":1,"packages":{"KeelMatrix.CorsSpec":{"visibility":"public-product","role":"primary"}}}'
$url = 'https://github.com/KeelMatrix/CorsSpec'
$tags = 'cors;keelmatrix-public-product;keelmatrix-primary'
function New-Package([string]$Tags = $tags) {
    [pscustomobject]@{ PackageId = 'KeelMatrix.CorsSpec'; PackageTags = $Tags; PackageProjectUrl = $url; RepositoryUrl = $url; RepositoryType = 'git' }
}
$checks = 0
function Assert-Rejected([string]$Name, [scriptblock]$Check) {
    $rejected = $false
    try { & $Check } catch { $rejected = $true }
    if (-not $rejected) { throw "Website metadata accepted $Name." }
    $script:checks++
}
Assert-WebsiteMetadata $manifest @(New-Package)
foreach ($bad in @(
    '', '{}', 'null', '[]', '{',
    $manifest.Replace('"schemaVersion":1', '"schemaVersion":2'),
    $manifest.Replace('"schemaVersion":1', '"schemaVersion":"1"'),
    $manifest.Replace('"schemaVersion":1', '"schemaVersion":1.0'),
    $manifest.Replace('"schemaVersion":1', '"schemaVersion":true'),
    $manifest.Replace('"schemaVersion":1', '"schemaVersion":1,"schemaVersion":1'),
    $manifest.Replace('"schemaVersion"', '"SchemaVersion"'),
    $manifest.Replace('"packages":', '"extra":1,"packages":'),
    $manifest.Replace('"packages":{', '"packages":[],"duplicate":{'),
    '{"schemaVersion":1,"packages":[]}',
    '{"schemaVersion":1,"packages":null}',
    $manifest.Replace('"KeelMatrix.CorsSpec"', '"keelmatrix.corsspec"'),
    $manifest.Replace('"KeelMatrix.CorsSpec"', '"KeelMatrix.Other"'),
    $manifest.Replace('"visibility":', '"extra":true,"visibility":'),
    $manifest.Replace('"visibility":"public-product",', ''),
    $manifest.Replace('"role":"primary"', '"role":null'),
    $manifest.Replace('"role":"primary"', '"role":"primary","role":"primary"'),
    $manifest.Replace('"public-product"', '"Public-Product"'),
    $manifest.Replace('"public-product"', '"unknown"'),
    $manifest.Replace('"primary"', '"Primary"'),
    $manifest.Replace('"primary"', '"unknown"'),
    $manifest.Replace('"primary"', '"component"'),
    $manifest.Replace('"public-product"', '"internal-package"'),
    '{"schemaVersion":1,"packages":{}}',
    '{"schemaVersion":1,"packages":{"KeelMatrix.CorsSpec":{"visibility":"public-product","role":"primary"},"KeelMatrix.CorsSpec":{"visibility":"public-product","role":"primary"}}}'
)) { Assert-Rejected 'malformed/schema/ID/classification manifest' { Assert-WebsiteMetadata $bad @(New-Package) } }
Assert-Rejected 'missing source package' { Assert-WebsiteMetadata $manifest @() }
Assert-Rejected 'duplicate source package' { Assert-WebsiteMetadata $manifest @((New-Package), (New-Package)) }
$sibling = New-Package
$sibling.PackageId = 'KeelMatrix.CorsSpec.Adapter'
$twoPrimaries = $manifest.Replace('}}}', '},"KeelMatrix.CorsSpec.Adapter":{"visibility":"public-product","role":"primary"}}}')
Assert-Rejected 'two public primaries' { Assert-WebsiteMetadata $twoPrimaries @((New-Package), $sibling) }
$componentManifest = $twoPrimaries.Replace('"KeelMatrix.CorsSpec.Adapter":{"visibility":"public-product","role":"primary"}', '"KeelMatrix.CorsSpec.Adapter":{"visibility":"public-product","role":"component"}')
$sibling.PackageTags = 'keelmatrix-public-product;keelmatrix-component'
Assert-WebsiteMetadata $componentManifest @((New-Package), $sibling)
Assert-Rejected 'orphan manifest entry' { Assert-WebsiteMetadata $componentManifest @(New-Package) }

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporaryRoot ('corsspec-website [metadata] ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $packagePath = Join-Path $fixture 'KeelMatrix.CorsSpec.0.1.0.nupkg'
    function Write-Archive($Package, [switch]$DuplicateTags, [string]$Destination = $packagePath) {
        $archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $archive.CreateEntry('KeelMatrix.CorsSpec.nuspec')
            $writer = [IO.StreamWriter]::new($entry.Open())
            $extra = if ($DuplicateTags) { '<tags>cors</tags>' } else { '' }
            try { $writer.Write("<package><metadata><id>$($Package.PackageId)</id><tags>$($Package.PackageTags)</tags>$extra<projectUrl>$($Package.PackageProjectUrl)</projectUrl><repository type='$($Package.RepositoryType)' url='$($Package.RepositoryUrl)' /></metadata></package>") }
            finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    function Reset-Archive { if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath } }
    Write-Archive (New-Package ($tags.Replace(';', ' ')))
    Assert-WebsiteArchive $packagePath @(New-Package) $manifest
    Reset-Archive
    foreach ($sentinel in @('keelmatrix-public-product', 'keelmatrix-primary')) {
        $opposite = if ($sentinel -ceq 'keelmatrix-primary') { 'keelmatrix-component' } else { 'keelmatrix-internal-package' }
        foreach ($badTags in @(
            $tags.Replace($sentinel, ''),
            "$tags;$sentinel",
            "$tags;$opposite",
            $tags.Replace($sentinel, $sentinel.ToUpperInvariant()),
            "$tags;$($sentinel.ToUpperInvariant())",
            $tags.Replace($sentinel, $opposite)
        )) {
            Assert-Rejected 'missing/duplicate/conflicting/wrong-case source sentinel' { Assert-WebsiteMetadata $manifest @(New-Package $badTags) }
            Write-Archive (New-Package $badTags)
            Assert-Rejected 'missing/duplicate/conflicting/wrong-case archive sentinel' { Assert-WebsiteArchive $packagePath @(New-Package) $manifest }
            Reset-Archive
        }
    }
    foreach ($field in @('PackageId', 'PackageProjectUrl', 'RepositoryUrl', 'RepositoryType')) {
        foreach ($value in @('', 'wrong', $url.ToUpperInvariant(), "$url/", "$url.git", "$url?ref=main", "$url#readme", 'https://github.com.evil.test/KeelMatrix/CorsSpec', 'http://github.com/KeelMatrix/CorsSpec', 'https://github.com/Other/CorsSpec')) {
            $badPackage = New-Package
            $badPackage.$field = $value
            Assert-Rejected "source $field drift" { Assert-WebsiteMetadata $manifest @($badPackage) }
            Write-Archive $badPackage
            Assert-Rejected "archive $field drift" { Assert-WebsiteArchive $packagePath @(New-Package) $manifest }
            Reset-Archive
        }
    }
    Write-Archive (New-Package ($tags.Replace('cors;', '')))
    Assert-Rejected 'lost existing search tag' { Assert-WebsiteArchive $packagePath @(New-Package) $manifest }
    Reset-Archive
    Write-Archive (New-Package) -DuplicateTags
    Assert-Rejected 'duplicate nuspec metadata' { Assert-WebsiteArchive $packagePath @(New-Package) $manifest }
    Reset-Archive

    # Evaluate real inherited/conditional MSBuild properties without restore/build.
    [IO.File]::WriteAllText((Join-Path $fixture 'keelmatrix.website.json'), $manifest)
    [IO.File]::WriteAllText((Join-Path $fixture 'Shipping.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
    $props = "<Project><PropertyGroup><IsPackable>true</IsPackable><PackageId>KeelMatrix.CorsSpec</PackageId><PackageProjectUrl>$url</PackageProjectUrl><RepositoryUrl>$url</RepositoryUrl><RepositoryType>git</RepositoryType><PackageTags Condition=`"'`$(Configuration)' == 'Release'`">$tags</PackageTags></PropertyGroup></Project>"
    $propsPath = Join-Path $fixture 'Directory.Build.props'
    [IO.File]::WriteAllText($propsPath, $props)
    $testProject = Join-Path $fixture 'Tests.csproj'
    [IO.File]::WriteAllText($testProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup></Project>')
    $gate = Join-Path $PSScriptRoot 'Validate-WebsiteMetadata.ps1'
    Invoke-NestedPwsh -NoProfile -File $gate -RepositoryRoot $fixture *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Website gate rejected inherited Release metadata in a literal path.' }
    [IO.File]::WriteAllText($propsPath, $props.Replace('keelmatrix-primary', 'keelmatrix-component'))

    # Execute the actual local validation stage blocks; CI and release use this same entry point.
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Validate.ps1'), [ref]$tokens, [ref]$errors)
    $metadataScriptDirectory = $PSScriptRoot
    foreach ($name in @('Website source metadata', 'Website archive metadata')) {
        if ($name -ceq 'Website archive metadata') {
            [IO.File]::WriteAllText($propsPath, $props)
            Write-Archive (New-Package ($tags.Replace('keelmatrix-primary', 'keelmatrix-component')))
            Write-Archive (New-Package) -Destination (Join-Path $fixture 'KeelMatrix.CorsSpec.0.1.0.snupkg')
        }
        $stage = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-ValidationStage' }, $true) |
            Where-Object { $_.CommandElements[2].Value -ceq $name })
        if ($stage.Count -ne 1) { throw "Missing actual validation stage '$name'." }
        & {
            $root = $fixture
            $packages = $fixture
            $failures = [Collections.Generic.List[string]]::new()
            $stageText = $stage[0].Extent.Text.Replace('$PSScriptRoot', '$metadataScriptDirectory')
            $stageOutput = & ([scriptblock]::Create($stageText)) 2>&1
            if ($failures.Count -ne 1 -or $failures[0] -cne $name) { throw "Validation lost the website failure for '$name'." }
            if (($stageOutput -join "`n") -notmatch 'Website sentinels') { throw "Validation did not reach metadata rejection for '$name': $stageOutput" }
        }
        $checks++
    }
    [IO.File]::WriteAllText($propsPath, $props)
    Remove-Item -LiteralPath (Join-Path $fixture 'keelmatrix.website.json')
    Invoke-NestedPwsh -NoProfile -File $gate -RepositoryRoot $fixture *> $null
    if ($LASTEXITCODE -eq 0) { throw 'Website gate accepted a missing manifest.' }
    $checks++

    # Execute both actual workflow validation blocks with success/failure exits.
    foreach ($workflowName in @('ci.yml', 'release.yml')) {
        $workflow = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "../.github/workflows/$workflowName")
        $block = [regex]::Match($workflow, '(?m)^        run: \|\r?\n(?<body>          pwsh -NoProfile -File ./scripts/Validate\.ps1[^\r\n]*\r?\n          if \(\$LASTEXITCODE[^\r\n]*)').Groups['body'].Value
        if ([string]::IsNullOrWhiteSpace($block)) { throw "Missing workflow metadata validation/failure propagation in $workflowName." }
        $block = [regex]::Replace($block, '\$\{\{[^}]+\}\}', '0.1.0')
        $probe = Join-Path $fixture 'workflow-probe.ps1'
        foreach ($exitCode in @(7, 0)) {
            [IO.File]::WriteAllText($probe, "function pwsh { `$global:LASTEXITCODE = $exitCode }`n" + $block + "`nexit 0")
            Invoke-NestedPwsh -NoProfile -File $probe *> $null
            if ($LASTEXITCODE -ne $exitCode) { throw "Workflow $workflowName lost validator exit $exitCode." }
        }
        $checks++
    }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedFixture -eq $temporaryRoot) { throw 'Unsafe website fixture cleanup path.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
Write-Host "Website metadata regression passed ($checks negative controls)."
