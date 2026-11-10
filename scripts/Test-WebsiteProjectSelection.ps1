[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WebsiteMetadata.ps1')
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporaryRoot ('corsspec-projects [selection] ' + [guid]::NewGuid().ToString('N'))
$manifest = '{"schemaVersion":1,"packages":{"KeelMatrix.CorsSpec":{"visibility":"public-product","role":"primary"}}}'
$url = 'https://github.com/KeelMatrix/CorsSpec'
$tags = 'cors;keelmatrix-public-product;keelmatrix-primary'
$checks = 0
function Write-FixtureFile([string]$RelativePath, [string]$Text) {
    $path = Join-Path $fixture $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Text)
    return $path
}
function Assert-Rejected([string]$Name, [scriptblock]$Check, [string]$Message) {
    try { & $Check } catch {
        if ($_.Exception.Message -notmatch $Message) { throw }
        $script:checks++
        return
    }
    throw "Website project selection accepted $Name."
}
function Assert-Gate([bool]$Reject, [string]$Message) {
    $output = Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-WebsiteMetadata.ps1') -RepositoryRoot $fixture 2>&1
    if (($LASTEXITCODE -ne 0) -ne $Reject -or ($Reject -and ($output -join "`n") -notmatch $Message)) {
        throw "Unexpected native website gate result ($LASTEXITCODE): $output"
    }
    $script:checks++
}
try {
    Write-FixtureFile 'global.json' (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../global.json')) | Out-Null
    Write-FixtureFile 'keelmatrix.website.json' $manifest | Out-Null
    Write-FixtureFile 'Directory.Build.props' "<Project><PropertyGroup><IsPackable>false</IsPackable><PackageTags>$tags</PackageTags><PackageProjectUrl>$url</PackageProjectUrl><RepositoryUrl>$url</RepositoryUrl><RepositoryType>git</RepositoryType></PropertyGroup></Project>" | Out-Null
    Write-FixtureFile 'src/Main/Main.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><IsPackable>True</IsPackable><PackageId>KeelMatrix.CorsSpec</PackageId></PropertyGroup></Project>' | Out-Null
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Validate.ps1'), [ref]$tokens, [ref]$errors)
    $sourceStage = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-ValidationStage' }, $true) |
        Where-Object { $_.CommandElements[2].Value -ceq 'Website source metadata' })
    if ($errors.Count -ne 0 -or $sourceStage.Count -ne 1) { throw 'Missing actual website source stage.' }
    # Extracted blocks have no script path. Bind that path to the real scripts
    # directory while preserving the stage command and failure handling.
    $metadataScriptDirectory = $PSScriptRoot
    $sourceStageText = $sourceStage[0].Extent.Text.Replace('$PSScriptRoot', '$metadataScriptDirectory')

    # All three SDK languages must obey the same evaluated Release boundary,
    # even outside the solution and ordinary source directories.
    foreach ($extension in @('csproj', 'vbproj', 'fsproj')) {
        $cases = @(
            @{ Name = 'true'; Body = '<IsPackable>true</IsPackable>'; Packable = $true; Location = 'tests' },
            @{ Name = 'True'; Body = '<IsPackable>True</IsPackable>'; Packable = $true; Location = 'samples' },
            @{ Name = 'TRUE'; Body = '<IsPackable>TRUE</IsPackable>'; Packable = $true; Location = 'helpers/deep [literal]' },
            @{ Name = 'false'; Body = '<IsPackable>false</IsPackable>'; Packable = $false; Location = 'tests' },
            @{ Name = 'FALSE'; Body = '<IsPackable>FALSE</IsPackable>'; Packable = $false; Location = 'samples' },
            @{ Name = 'ReleaseOnly'; Body = '<IsPackable>false</IsPackable><IsPackable Condition="''$(Configuration)'' == ''Release''">True</IsPackable>'; Packable = $true; Location = 'tools' },
            @{ Name = 'ReleaseDisabled'; Body = '<IsPackable>true</IsPackable><IsPackable Condition="''$(Configuration)'' == ''Release''">FALSE</IsPackable>'; Packable = $false; Location = 'tools' },
            @{ Name = 'Inherited'; Body = ''; Props = '<IsPackable>True</IsPackable>'; Packable = $true; Location = 'helpers' },
            @{ Name = 'TargetsEnabled'; Body = '<IsPackable>false</IsPackable>'; Targets = '<IsPackable>TRUE</IsPackable>'; Packable = $true; Location = 'helpers' },
            @{ Name = 'TargetsDisabled'; Body = '<IsPackable>TRUE</IsPackable>'; Targets = '<IsPackable>FALSE</IsPackable>'; Packable = $false; Location = 'helpers' },
            @{ Name = 'HiddenUpperExtension'; Body = '<IsPackable>True</IsPackable>'; Packable = $true; Location = '.helpers'; Upper = $true }
        )
        foreach ($case in $cases) {
            $directory = "$($case.Location)/$extension-$($case.Name)"
            $suffix = if ($case.Upper) { $extension.ToUpperInvariant() } else { $extension }
            $project = Write-FixtureFile "$directory/Sibling.$suffix" ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><PackageId>KeelMatrix.UnclassifiedSibling</PackageId>' + $case.Body + '</PropertyGroup></Project>')
            $props = Write-FixtureFile "$directory/Directory.Build.props" ('<Project><Import Project="' + [Security.SecurityElement]::Escape((Join-Path $fixture 'Directory.Build.props')) + '" /><PropertyGroup>' + $case.Props + '</PropertyGroup></Project>')
            $targets = Write-FixtureFile "$directory/Directory.Build.targets" ('<Project><PropertyGroup>' + $case.Targets + '</PropertyGroup></Project>')
            if ($case.Upper -and $IsWindows) { (Get-Item -LiteralPath $project).Attributes = [IO.FileAttributes]::Hidden }
            $packages = @(Get-WebsiteSourcePackages $fixture)
            $expectedCount = if ($case.Packable) { 2 } else { 1 }
            if ($packages.Count -ne $expectedCount -or @($packages | Where-Object PackageId -CEQ 'KeelMatrix.CorsSpec').Count -ne 1 -or
                @($packages | Where-Object PackageId -CEQ 'KeelMatrix.UnclassifiedSibling').Count -ne ($expectedCount - 1)) {
                throw "Wrong evaluated project set for $extension/$($case.Name)."
            }
            if ($case.Packable) {
                Assert-Rejected "$extension/$($case.Name) missing entry" { Assert-WebsiteMetadata $manifest $packages } 'exactly the packable package set'
            } else { Assert-WebsiteMetadata $manifest $packages; $checks++ }

            # Exercise native status and the actual local stage used by CI and release.
            if ($case.Name -in @('true', 'false')) {
                Assert-Gate $case.Packable 'exactly the packable package set'
                & {
                    $root = $fixture
                    $failures = [Collections.Generic.List[string]]::new()
                    $stageOutput = & ([scriptblock]::Create($sourceStageText)) 2>&1
                    if ($failures.Count -ne [int]$case.Packable -or ($case.Packable -and $failures[0] -cne 'Website source metadata')) {
                        throw "Source stage lost $extension/$($case.Name) outcome: $stageOutput"
                    }
                }
                $checks++
            }
            Remove-Item -LiteralPath $project, $props, $targets -Force
        }

        # A classified extra component still violates the intentional one-package
        # wrapper. Check its metadata through the same real project-selection seam.
        $componentManifest = $manifest.Replace('}}}', '},"KeelMatrix.UnclassifiedSibling":{"visibility":"public-product","role":"component"}}}')
        $componentTags = 'cors;keelmatrix-public-product;keelmatrix-component'
        $projectText = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><IsPackable>True</IsPackable><PackageId>KeelMatrix.UnclassifiedSibling</PackageId><PackageTags>{0}</PackageTags><PackageProjectUrl>{1}</PackageProjectUrl></PropertyGroup></Project>'
        $project = Write-FixtureFile "siblings/Sibling.$extension" ($projectText -f $componentTags, $url)
        Write-FixtureFile 'keelmatrix.website.json' $componentManifest | Out-Null
        Assert-Gate $true 'exactly its shipping KeelMatrix.CorsSpec package'
        foreach ($badTags in @(
            'cors;keelmatrix-component',
            'cors;keelmatrix-public-product',
            "$componentTags;keelmatrix-internal-package",
            "$componentTags;keelmatrix-primary",
            "$componentTags;keelmatrix-component",
            $componentTags.Replace('keelmatrix-public-product', 'KEELMATRIX-PUBLIC-PRODUCT'),
            $componentTags.Replace('keelmatrix-component', 'KEELMATRIX-COMPONENT')
        )) {
            [IO.File]::WriteAllText($project, ($projectText -f $badTags, $url))
            $packages = @(Get-WebsiteSourcePackages $fixture)
            Assert-Rejected "$extension sibling sentinels" { Assert-WebsiteMetadata $componentManifest $packages } 'Website sentinels'
        }
        [IO.File]::WriteAllText($project, ($projectText -f $componentTags, 'https://github.com/Other/CorsSpec'))
        $packages = @(Get-WebsiteSourcePackages $fixture)
        Assert-Rejected "$extension sibling URL" { Assert-WebsiteMetadata $componentManifest $packages } 'canonical CorsSpec GitHub repository'
        Remove-Item -LiteralPath $project
        Write-FixtureFile 'keelmatrix.website.json' $manifest | Out-Null
    }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedFixture -eq $temporaryRoot) { throw 'Unsafe project fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
Write-Host "Website project selection passed ($checks controls)."
