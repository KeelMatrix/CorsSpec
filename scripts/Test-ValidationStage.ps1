[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.Reflection.Metadata

$failures = [System.Collections.Generic.List[string]]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $repositoryCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the repository commit for the compiler-produced symbol fixture.'
}

$buildProject = Join-Path $repositoryRoot 'src' 'KeelMatrix.CorsSpec' 'KeelMatrix.CorsSpec.csproj'
& dotnet build $buildProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Could not build the compiler-produced symbol fixture.'
}

$buildOutput = Join-Path $repositoryRoot 'src' 'KeelMatrix.CorsSpec' 'bin' 'Release' 'net8.0'
$builtAssembly = Join-Path $buildOutput 'KeelMatrix.CorsSpec.dll'
$builtDocumentation = Join-Path $buildOutput 'KeelMatrix.CorsSpec.xml'
$builtSymbols = Join-Path $buildOutput 'KeelMatrix.CorsSpec.pdb'
foreach ($artifact in @($builtAssembly, $builtDocumentation, $builtSymbols)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Compiler-produced fixture artifact is missing: $artifact"
    }
}

Invoke-ValidationStage -Name 'Successful command' -Failures $failures -Command {
    Invoke-NestedPwsh -NoProfile -Command 'exit 0'
}
if ($failures.Count -ne 0) {
    throw "A successful command was recorded as failed: $($failures -join ', ')."
}

Invoke-ValidationStage -Name 'Missing command' -Failures $failures -Command {
    Invoke-ValidationCommandThatDoesNotExist
} 2>$null
if ('Missing command' -notin $failures) {
    throw 'A command-not-found error was not recorded as a failed stage.'
}

Invoke-ValidationStage -Name 'Non-zero command' -Failures $failures -Command {
    Invoke-NestedPwsh -NoProfile -Command 'exit 7'
} 2>$null
if ('Non-zero command' -notin $failures) {
    throw 'A non-zero child-process exit was not recorded as a failed stage.'
}

$historyFixtureRoot = [IO.Path]::GetTempPath()
$historyFixture = Join-Path $historyFixtureRoot ("corsspec-history-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $historyFixture | Out-Null
try {
    & git -C $historyFixture init --quiet --initial-branch=main
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the commit-history fixture.' }

    Set-Content -LiteralPath (Join-Path $historyFixture 'README.md') -Value 'fixture' -Encoding utf8
    & git -C $historyFixture add README.md
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage the commit-history fixture.' }

    $badAuthor = 'Synthetic Automation'
    $badMessage = ('Generated' + ' by synthetic validation ' + ('Co-' + 'Authored-By') + ': Synthetic <noreply@example.test>')
    & git -C $historyFixture -c user.name=$badAuthor -c user.email=fixture@example.test commit --quiet -m $badMessage
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the commit-history fixture.' }

    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-CommitHistory.ps1') -RepositoryPath $historyFixture 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw 'Commit-history hygiene accepted an offending fixture.'
    }
}
finally {
    Remove-Item -LiteralPath $historyFixture -Recurse -Force -ErrorAction SilentlyContinue
}

function New-FixtureSet([string]$Root, [hashtable]$Options = @{}) {
    $packageId = if ($Options.ContainsKey('PackageId')) { $Options.PackageId } else { 'KeelMatrix.CorsSpec' }
    $packageVersion = if ($Options.ContainsKey('PackageVersion')) { $Options.PackageVersion } else { '0.1.0' }
    $symbolId = if ($Options.ContainsKey('SymbolId')) { $Options.SymbolId } else { $packageId }
    $symbolVersion = if ($Options.ContainsKey('SymbolVersion')) { $Options.SymbolVersion } else { $packageVersion }
    $packageTfm = if ($Options.ContainsKey('PackageTfm')) { $Options.PackageTfm } else { 'net8.0' }
    $symbolTfm = if ($Options.ContainsKey('SymbolTfm')) { $Options.SymbolTfm } else { 'net8.0' }
    $packageCommit = if ($Options.ContainsKey('PackageCommit')) { $Options.PackageCommit } else { $repositoryCommit }
    $symbolCommit = if ($Options.ContainsKey('SymbolCommit')) { $Options.SymbolCommit } else { $repositoryCommit }
    $dependencyId = if ($Options.ContainsKey('DependencyId')) { $Options.DependencyId } else { 'KeelMatrix.Telemetry' }
    $dependencyVersion = if ($Options.ContainsKey('DependencyVersion')) { $Options.DependencyVersion } else { '0.1.1' }
    $dependencyExclude = if ($Options.ContainsKey('DependencyExclude')) { $Options.DependencyExclude } else { 'Build,Analyzers' }
    $packageRoot = Join-Path $Root 'package'
    $symbolRoot = Join-Path $Root 'symbols'
    New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot "lib/$packageTfm"), (Join-Path $symbolRoot "lib/$symbolTfm"), (Join-Path $packageRoot '_rels'), (Join-Path $symbolRoot '_rels'), (Join-Path $packageRoot 'package/services/metadata/core-properties'), (Join-Path $symbolRoot 'package/services/metadata/core-properties') | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src' 'KeelMatrix.CorsSpec' 'README.md') -Destination (Join-Path $packageRoot 'README.md')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE')
    if ($Options.ContainsKey('ReadmeMutation')) { Set-Content -LiteralPath (Join-Path $packageRoot 'README.md') -Value $Options.ReadmeMutation -Encoding utf8 }
    if ($Options.ContainsKey('LicenseMutation')) { Set-Content -LiteralPath (Join-Path $packageRoot 'LICENSE') -Value $Options.LicenseMutation -Encoding utf8 }
    Copy-Item -LiteralPath $builtAssembly -Destination (Join-Path $packageRoot "lib/$packageTfm/KeelMatrix.CorsSpec.dll")
    Copy-Item -LiteralPath $builtDocumentation -Destination (Join-Path $packageRoot "lib/$packageTfm/KeelMatrix.CorsSpec.xml")
    Set-Content -LiteralPath (Join-Path $packageRoot '_rels/.rels') -Value 'relationships' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot '[Content_Types].xml') -Value 'content types' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot 'package/services/metadata/core-properties/00000000000000000000000000000000.psmdcp') -Value 'metadata' -Encoding utf8

    $packageNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>$packageId</id><version>$packageVersion</version><license type="expression">MIT</license><readme>README.md</readme><repository type="git" url="https://github.com/KeelMatrix/CorsSpec" branch="refs/heads/main" commit="$packageCommit" /><dependencies><group targetFramework="$packageTfm"><dependency id="$dependencyId" version="$dependencyVersion" exclude="$dependencyExclude" /></group></dependencies></metadata></package>
"@
    Set-Content -LiteralPath (Join-Path $packageRoot "$packageId.nuspec") -Value $packageNuspec -Encoding utf8

    Set-Content -LiteralPath (Join-Path $symbolRoot '_rels/.rels') -Value 'relationships' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $symbolRoot '[Content_Types].xml') -Value 'content types' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $symbolRoot 'package/services/metadata/core-properties/00000000000000000000000000000000.psmdcp') -Value 'metadata' -Encoding utf8

    $symbolNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>$symbolId</id><version>$symbolVersion</version><packageTypes><packageType name="SymbolsPackage" /></packageTypes><repository type="git" url="https://github.com/KeelMatrix/CorsSpec" branch="refs/heads/main" commit="$symbolCommit" /><dependencies><group targetFramework="$symbolTfm"><dependency id="$dependencyId" version="$dependencyVersion" exclude="$dependencyExclude" /></group></dependencies></metadata></package>
"@
    if ($Options.ContainsKey('MalformedSymbolNuspec') -and $Options.MalformedSymbolNuspec) { $symbolNuspec = '<package><metadata>' }
    Set-Content -LiteralPath (Join-Path $symbolRoot "$symbolId.nuspec") -Value $symbolNuspec -Encoding utf8

    if (-not ($Options.ContainsKey('MissingPdb') -and $Options.MissingPdb)) {
        $pdbRelative = if ($Options.ContainsKey('PdbPath')) { $Options.PdbPath } else { "lib/$symbolTfm/KeelMatrix.CorsSpec.pdb" }
        $pdbPath = Join-Path $symbolRoot $pdbRelative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pdbPath) | Out-Null
        Copy-Item -LiteralPath $builtSymbols -Destination $pdbPath
        if ($Options.ContainsKey('PdbMutation')) {
            $bytes = [IO.File]::ReadAllBytes($pdbPath)
            switch ($Options.PdbMutation) {
                'FakeMagic' {
                    [IO.File]::WriteAllBytes($pdbPath, [Text.Encoding]::UTF8.GetBytes("BSJB https://raw.githubusercontent.com/KeelMatrix/CorsSpec/$symbolCommit/*"))
                }
                'Truncated' {
                    [IO.File]::WriteAllBytes($pdbPath, $bytes[0..31])
                }
                'MismatchedIdentity' {
                    $memory = [IO.MemoryStream]::new($bytes, $false)
                    $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($memory)
                    try {
                        $offset = $provider.GetMetadataReader().DebugMetadataHeader.IdStartOffset
                        $bytes[$offset] = $bytes[$offset] -bxor 1
                    }
                    finally {
                        $provider.Dispose()
                        $memory.Dispose()
                    }
                    [IO.File]::WriteAllBytes($pdbPath, $bytes)
                }
                'MismatchedSourceLink' {
                    $source = [Text.Encoding]::UTF8.GetBytes("https://raw.githubusercontent.com/KeelMatrix/CorsSpec/$symbolCommit/*")
                    $replacement = [Text.Encoding]::UTF8.GetBytes('https://raw.githubusercontent.com/KeelMatrix/CorsSpec/1111111111111111111111111111111111111111/*')
                    for ($i = 0; $i -le $bytes.Length - $source.Length; $i++) {
                        if ([System.Linq.Enumerable]::SequenceEqual([byte[]]$bytes[$i..($i + $source.Length - 1)], $source)) {
                            $replacement.CopyTo($bytes, $i)
                            break
                        }
                    }
                    [IO.File]::WriteAllBytes($pdbPath, $bytes)
                }
                'MalformedSourceLink' {
                    $source = [Text.Encoding]::UTF8.GetBytes('{"documents":')
                    $replacement = [Text.Encoding]::UTF8.GetBytes('["documents":')
                    for ($i = 0; $i -le $bytes.Length - $source.Length; $i++) {
                        if ([System.Linq.Enumerable]::SequenceEqual([byte[]]$bytes[$i..($i + $source.Length - 1)], $source)) {
                            $replacement.CopyTo($bytes, $i)
                            break
                        }
                    }
                    [IO.File]::WriteAllBytes($pdbPath, $bytes)
                }
            }
        }
    }

    if ($Options.ContainsKey('ExtraPackageEntry')) { Set-Content -LiteralPath (Join-Path $packageRoot $Options.ExtraPackageEntry) -Value 'unexpected' -Encoding utf8 }
    if ($Options.ContainsKey('ExtraSymbolEntry')) { Set-Content -LiteralPath (Join-Path $symbolRoot $Options.ExtraSymbolEntry) -Value 'unexpected' -Encoding utf8 }

    $packagePath = Join-Path $Root 'KeelMatrix.CorsSpec.0.1.0.nupkg'
    $symbolsPath = Join-Path $Root 'KeelMatrix.CorsSpec.0.1.0.snupkg'
    New-FixtureArchive $packageRoot $packagePath
    New-FixtureArchive $symbolRoot $symbolsPath
    if ($Options.ContainsKey('DuplicatePdb') -and $Options.DuplicatePdb) {
        Add-DuplicateArchiveEntry $symbolsPath "lib/$symbolTfm/KeelMatrix.CorsSpec.pdb" $pdbPath
    }
    return [pscustomobject]@{ Package = $packagePath; Symbols = $symbolsPath }
}

function New-FixtureArchive([string]$SourceRoot, [string]$Destination) {
    $fileStream = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -File -Recurse -Force) {
            $relative = [IO.Path]::GetRelativePath($SourceRoot, $file.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
            $entry = $archive.CreateEntry($relative)
            $input = [IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally {
        $archive.Dispose()
        $fileStream.Dispose()
    }
}

function Add-DuplicateArchiveEntry([string]$ArchivePath, [string]$EntryName, [string]$SourcePath) {
    $fileStream = [IO.File]::Open($ArchivePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.CreateEntry($EntryName)
        $input = [IO.File]::OpenRead($SourcePath)
        $output = $entry.Open()
        try { $input.CopyTo($output) }
        finally { $output.Dispose(); $input.Dispose() }
    }
    finally {
        $archive.Dispose()
        $fileStream.Dispose()
    }
}

function Assert-InspectionPasses($Set) {
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $Set.Package -SymbolsPath $Set.Symbols -ExpectedCommit $repositoryCommit
    if ($LASTEXITCODE -ne 0) { throw 'A valid package and symbol archive set was rejected.' }
}

function Assert-InspectionFails($Set, [string]$Reason) {
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $Set.Package -SymbolsPath $Set.Symbols -ExpectedCommit $repositoryCommit 2>$null
    if ($LASTEXITCODE -eq 0) { throw "Archive inspection accepted $Reason." }
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("corsspec-package-inspection-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
try {
    $valid = New-FixtureSet (Join-Path $fixtureRoot 'valid')
    Assert-InspectionPasses $valid
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $valid.Package -SymbolsPath $valid.Symbols -ExpectedCommit $repositoryCommit -RequireIcon 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Package inspection did not enforce the icon prerequisite.' }
    $extraArtifact = Join-Path (Split-Path -Parent $valid.Package) 'unexpected.nupkg'
    Set-Content -LiteralPath $extraArtifact -Value 'unexpected artifact' -Encoding utf8
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag v0.1.0 -ArtifactDirectory (Split-Path -Parent $valid.Package) 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Release contract accepted an unintended artifact.' }
    Remove-Item -LiteralPath $extraArtifact -Force

    $packageVersionDrift = New-FixtureSet (Join-Path $fixtureRoot 'package-version-drift') @{ PackageVersion = '9.9.9' }
    Assert-InspectionFails $packageVersionDrift 'package version drift'

    $packageIdDrift = New-FixtureSet (Join-Path $fixtureRoot 'package-id-drift') @{ PackageId = 'Wrong.Package' }
    Assert-InspectionFails $packageIdDrift 'package id drift'

    $dependencyVersionDrift = New-FixtureSet (Join-Path $fixtureRoot 'dependency-version-drift') @{ DependencyVersion = '9.9.9' }
    Assert-InspectionFails $dependencyVersionDrift 'dependency version drift'

    $dependencyIdDrift = New-FixtureSet (Join-Path $fixtureRoot 'dependency-id-drift') @{ DependencyId = 'Other.Dependency' }
    Assert-InspectionFails $dependencyIdDrift 'dependency id drift'

    $readmeDrift = New-FixtureSet (Join-Path $fixtureRoot 'readme-drift') @{ ReadmeMutation = 'substituted package README' }
    Assert-InspectionFails $readmeDrift 'substituted package README'

    $licenseDrift = New-FixtureSet (Join-Path $fixtureRoot 'license-drift') @{ LicenseMutation = 'substituted package license' }
    Assert-InspectionFails $licenseDrift 'substituted package license'

    $changelogFixture = Join-Path $fixtureRoot 'changelog.md'
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Destination $changelogFixture
    $leapChangelog = (Get-Content -Raw -LiteralPath $changelogFixture).Replace('2026-09-24', '2024-02-29')
    Set-Content -LiteralPath $changelogFixture -Value $leapChangelog -Encoding utf8
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag v0.1.0 -ChangelogPath $changelogFixture 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Release contract rejected a valid leap-day changelog date.' }

    $invalidDateChangelog = (Get-Content -Raw -LiteralPath $changelogFixture).Replace('2024-02-29', '2023-02-29')
    Set-Content -LiteralPath $changelogFixture -Value $invalidDateChangelog -Encoding utf8
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag v0.1.0 -ChangelogPath $changelogFixture 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Release contract accepted an invalid calendar date.' }

    $fencedChangelog = (Get-Content -Raw -LiteralPath $changelogFixture).Replace('2023-02-29', '2026-09-24') + "`n" + '```text' + "`n## [0.1.0] - 2026-99-99`n" + '```' + "`n"
    Set-Content -LiteralPath $changelogFixture -Value $fencedChangelog -Encoding utf8
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag v0.1.0 -ChangelogPath $changelogFixture 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Release contract treated a fenced changelog lookalike as a real heading.' }

    $duplicateChangelog = (Get-Content -Raw -LiteralPath $changelogFixture) + "`n## [0.1.0] - 2026-09-25`n`n### Added`n`n- Duplicate fixture section.`n"
    Set-Content -LiteralPath $changelogFixture -Value $duplicateChangelog -Encoding utf8
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag v0.1.0 -ChangelogPath $changelogFixture 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Release contract accepted duplicate version sections.' }

    $symbolVersionDrift = New-FixtureSet (Join-Path $fixtureRoot 'symbol-version-drift') @{ SymbolVersion = '9.9.9' }
    Assert-InspectionFails $symbolVersionDrift 'symbol version drift'

    $symbolIdDrift = New-FixtureSet (Join-Path $fixtureRoot 'symbol-id-drift') @{ SymbolId = 'Wrong.Package' }
    Assert-InspectionFails $symbolIdDrift 'symbol id drift'

    $commitDrift = New-FixtureSet (Join-Path $fixtureRoot 'commit-drift') @{ PackageCommit = '1111111111111111111111111111111111111111' }
    Assert-InspectionFails $commitDrift 'package and symbol commit drift'

    $foreignCommit = New-FixtureSet (Join-Path $fixtureRoot 'foreign-commit') @{ PackageCommit = '2222222222222222222222222222222222222222'; SymbolCommit = '2222222222222222222222222222222222222222' }
    Assert-InspectionFails $foreignCommit 'package and symbol archives with the same foreign commit'

    $packageTfmDrift = New-FixtureSet (Join-Path $fixtureRoot 'package-tfm-drift') @{ PackageTfm = 'net7.0' }
    Assert-InspectionFails $packageTfmDrift 'package TFM drift'

    $symbolTfmDrift = New-FixtureSet (Join-Path $fixtureRoot 'symbol-tfm-drift') @{ SymbolTfm = 'net7.0' }
    Assert-InspectionFails $symbolTfmDrift 'symbol TFM drift'

    $corruptSymbols = New-FixtureSet (Join-Path $fixtureRoot 'corrupt-symbols')
    Set-Content -LiteralPath $corruptSymbols.Symbols -Value 'not a zip' -Encoding utf8
    Assert-InspectionFails $corruptSymbols 'a corrupt symbol archive'

    $corruptPackage = New-FixtureSet (Join-Path $fixtureRoot 'corrupt-package')
    Set-Content -LiteralPath $corruptPackage.Package -Value 'not a zip' -Encoding utf8
    Assert-InspectionFails $corruptPackage 'a corrupt package archive'

    $missingPdb = New-FixtureSet (Join-Path $fixtureRoot 'missing-pdb') @{ MissingPdb = $true }
    Assert-InspectionFails $missingPdb 'a missing PDB'

    $fakePdb = New-FixtureSet (Join-Path $fixtureRoot 'fake-pdb') @{ PdbMutation = 'FakeMagic' }
    Assert-InspectionFails $fakePdb 'a fake BSJB PDB payload'

    $truncatedPdb = New-FixtureSet (Join-Path $fixtureRoot 'truncated-pdb') @{ PdbMutation = 'Truncated' }
    Assert-InspectionFails $truncatedPdb 'a truncated PDB'

    $mismatchedIdentity = New-FixtureSet (Join-Path $fixtureRoot 'mismatched-identity') @{ PdbMutation = 'MismatchedIdentity' }
    Assert-InspectionFails $mismatchedIdentity 'a PDB with mismatched symbol identity'

    $mismatchedSourceLink = New-FixtureSet (Join-Path $fixtureRoot 'mismatched-sourcelink') @{ PdbMutation = 'MismatchedSourceLink' }
    Assert-InspectionFails $mismatchedSourceLink 'a mismatched SourceLink record'

    $malformedSourceLink = New-FixtureSet (Join-Path $fixtureRoot 'malformed-sourcelink') @{ PdbMutation = 'MalformedSourceLink' }
    Assert-InspectionFails $malformedSourceLink 'a malformed SourceLink record'

    $duplicatePdb = New-FixtureSet (Join-Path $fixtureRoot 'duplicate-pdb') @{ DuplicatePdb = $true }
    Assert-InspectionFails $duplicatePdb 'duplicate required PDB entries'

    $wrongPdb = New-FixtureSet (Join-Path $fixtureRoot 'wrong-pdb') @{ PdbPath = 'lib/net8.0/Other.pdb' }
    Assert-InspectionFails $wrongPdb 'a wrong PDB'

    $malformedNuspec = New-FixtureSet (Join-Path $fixtureRoot 'malformed-symbol-nuspec') @{ MalformedSymbolNuspec = $true }
    Assert-InspectionFails $malformedNuspec 'a malformed symbol nuspec'

    $extraPackage = New-FixtureSet (Join-Path $fixtureRoot 'extra-package-entry') @{ ExtraPackageEntry = 'unexpected.txt' }
    Assert-InspectionFails $extraPackage 'an extra package entry'

    $extraSymbols = New-FixtureSet (Join-Path $fixtureRoot 'extra-symbol-entry') @{ ExtraSymbolEntry = 'unexpected.txt' }
    Assert-InspectionFails $extraSymbols 'an extra symbol entry'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$dependencyFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("corsspec-dependency-audit-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $dependencyFixtureRoot | Out-Null
$savedFakeGraph = $env:FAKE_DEPENDENCY_GRAPH_OUTPUT
$savedFakeVulnerability = $env:FAKE_DEPENDENCY_VULNERABILITY_OUTPUT
$savedFakeStderr = $env:FAKE_DEPENDENCY_AUDIT_STDERR
try {
    $graphOutput = Join-Path $dependencyFixtureRoot 'graph.json'
    $vulnerabilityOutput = Join-Path $dependencyFixtureRoot 'vulnerability.json'
    $rawVulnerabilityOutput = Join-Path $dependencyFixtureRoot 'raw-vulnerability.txt'
    $auditOutput = Join-Path $dependencyFixtureRoot 'audit.json'
    $fakeDotnet = Join-Path $dependencyFixtureRoot 'dotnet.ps1'
    @'
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$reportPath = if ($Arguments -contains '--vulnerable') {
    $env:FAKE_DEPENDENCY_VULNERABILITY_OUTPUT
}
else {
    $env:FAKE_DEPENDENCY_GRAPH_OUTPUT
}

Get-Content -Raw -LiteralPath $reportPath
if (-not [string]::IsNullOrEmpty($env:FAKE_DEPENDENCY_AUDIT_STDERR)) {
    [Console]::Error.WriteLine($env:FAKE_DEPENDENCY_AUDIT_STDERR)
}
'@ | Set-Content -LiteralPath $fakeDotnet -Encoding utf8

    $cleanGraph = [ordered]@{
        version = 1
        parameters = '--include-transitive'
        projects = @([ordered]@{
            path = 'src/KeelMatrix.CorsSpec/KeelMatrix.CorsSpec.csproj'
            frameworks = @([ordered]@{
                framework = 'net8.0'
                topLevelPackages = @(
                    [ordered]@{ id = 'KeelMatrix.Telemetry'; requestedVersion = '0.1.1'; resolvedVersion = '0.1.1' },
                    [ordered]@{ id = 'Microsoft.CodeAnalysis.PublicApiAnalyzers'; requestedVersion = '3.3.4'; resolvedVersion = '3.3.4' }
                )
                transitivePackages = @(
                    [ordered]@{ id = 'System.IO.Hashing'; resolvedVersion = '10.0.12' }
                )
            })
        })
    }
    $cleanVulnerability = [ordered]@{
        version = 1
        parameters = '--vulnerable --include-transitive'
        sources = @('https://api.nuget.org/v3/index.json')
        projects = @([ordered]@{ path = 'src/KeelMatrix.CorsSpec/KeelMatrix.CorsSpec.csproj' })
    }
    $findingsVulnerability = [ordered]@{
        version = 1
        parameters = '--vulnerable --include-transitive'
        sources = @('https://api.nuget.org/v3/index.json')
        projects = @([ordered]@{
            path = 'src/KeelMatrix.CorsSpec/KeelMatrix.CorsSpec.csproj'
            frameworks = @([ordered]@{
                framework = 'net8.0'
                topLevelPackages = @([ordered]@{
                    id = 'KeelMatrix.Telemetry'
                    requestedVersion = '0.1.1'
                    resolvedVersion = '0.1.1'
                    vulnerabilities = @([ordered]@{ severity = 'High'; advisoryurl = 'https://example.test/advisory/direct' })
                })
                transitivePackages = @([ordered]@{
                    id = 'System.IO.Hashing'
                    resolvedVersion = '10.0.12'
                    vulnerabilities = @([ordered]@{ severity = 'Moderate'; advisoryurl = 'https://example.test/advisory/transitive' })
                })
            })
        })
    }

    $cleanGraphJson = $cleanGraph | ConvertTo-Json -Depth 20
    $cleanVulnerabilityJson = $cleanVulnerability | ConvertTo-Json -Depth 20
    $findingsVulnerabilityJson = $findingsVulnerability | ConvertTo-Json -Depth 20
    $cleanGraphJson | Set-Content -LiteralPath $graphOutput -Encoding utf8
    $cleanVulnerabilityJson | Set-Content -LiteralPath $vulnerabilityOutput -Encoding utf8
    $env:FAKE_DEPENDENCY_GRAPH_OUTPUT = $graphOutput
    $env:FAKE_DEPENDENCY_VULNERABILITY_OUTPUT = $vulnerabilityOutput
    $env:FAKE_DEPENDENCY_AUDIT_STDERR = $null

    function Invoke-DependencyFixture([string]$VulnerabilityJson = $null, [string]$RawVulnerability = $null, [string]$Stderr = $null, [string]$GraphJson = $null) {
        if ([string]::IsNullOrEmpty($VulnerabilityJson)) { $VulnerabilityJson = $cleanVulnerabilityJson }
        if ([string]::IsNullOrEmpty($GraphJson)) { $GraphJson = $cleanGraphJson }
        $GraphJson | Set-Content -LiteralPath $graphOutput -Encoding utf8
        $env:FAKE_DEPENDENCY_GRAPH_OUTPUT = $graphOutput
        $env:FAKE_DEPENDENCY_VULNERABILITY_OUTPUT = $vulnerabilityOutput
        if ([string]::IsNullOrEmpty($RawVulnerability)) {
            $VulnerabilityJson | Set-Content -LiteralPath $vulnerabilityOutput -Encoding utf8
        }
        else {
            $RawVulnerability | Set-Content -LiteralPath $rawVulnerabilityOutput -Encoding utf8
            $env:FAKE_DEPENDENCY_VULNERABILITY_OUTPUT = $rawVulnerabilityOutput
        }
        $env:FAKE_DEPENDENCY_AUDIT_STDERR = $Stderr
        Remove-Item -LiteralPath $auditOutput -Force -ErrorAction SilentlyContinue
        Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-DependencyAudit.ps1') -DotnetCommand $fakeDotnet -OutputPath $auditOutput 2>$null | Out-Null
        return $LASTEXITCODE
    }

    $exitCode = Invoke-DependencyFixture
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $auditOutput -PathType Leaf)) {
        throw 'The actual clean no-findings vulnerability report shape was rejected.'
    }
    $audit = Get-Content -Raw -LiteralPath $auditOutput | ConvertFrom-Json
    if ($audit.vulnerabilities.projects[0].PSObject.Properties['frameworks']) {
        throw 'The positive clean vulnerability fixture did not preserve the SDK no-framework shape.'
    }

    $findingsExitCode = Invoke-DependencyFixture -VulnerabilityJson $findingsVulnerabilityJson
    $findingsArtifact = Test-Path -LiteralPath $auditOutput
    if ($findingsExitCode -eq 0 -or $findingsArtifact) {
        throw 'A direct and transitive vulnerability findings report was accepted as clean.'
    }

    function Assert-DependencyFixtureFails([string]$Name, [scriptblock]$Mutate) {
        $fixture = $cleanVulnerabilityJson | ConvertFrom-Json
        & $Mutate $fixture
        $json = $fixture | ConvertTo-Json -Depth 20
        if ((Invoke-DependencyFixture -VulnerabilityJson $json) -eq 0 -or (Test-Path -LiteralPath $auditOutput)) {
            throw "Dependency audit accepted malformed fixture: $Name."
        }
    }

    Assert-DependencyFixtureFails 'missing command metadata' { param($fixture) $fixture.PSObject.Properties.Remove('parameters') }
    Assert-DependencyFixtureFails 'missing source metadata' { param($fixture) $fixture.PSObject.Properties.Remove('sources') }
    Assert-DependencyFixtureFails 'missing schema version' { param($fixture) $fixture.PSObject.Properties.Remove('version') }
    Assert-DependencyFixtureFails 'missing project path' { param($fixture) $fixture.projects[0].PSObject.Properties.Remove('path') }
    Assert-DependencyFixtureFails 'wrong project' { param($fixture) $fixture.projects[0].path = 'src/Other/Other.csproj' }
    Assert-DependencyFixtureFails 'extra project' { param($fixture) $fixture.projects += [pscustomobject]@{ path = 'src/Other/Other.csproj' } }
    Assert-DependencyFixtureFails 'unknown schema property' { param($fixture) $fixture.PSObject.Properties.Add([psnoteproperty]::new('unexpected', 'value')) }
    $partialGraph = $cleanGraphJson | ConvertFrom-Json
    $partialGraph.projects[0].frameworks[0].PSObject.Properties.Remove('topLevelPackages')
    if ((Invoke-DependencyFixture -GraphJson ($partialGraph | ConvertTo-Json -Depth 20)) -eq 0 -or (Test-Path -LiteralPath $auditOutput)) {
        throw 'A graph report with truncated framework coverage was accepted.'
    }
    $partialTransitiveGraph = $cleanGraphJson | ConvertFrom-Json
    $partialTransitiveGraph.projects[0].frameworks[0].PSObject.Properties.Remove('transitivePackages')
    if ((Invoke-DependencyFixture -GraphJson ($partialTransitiveGraph | ConvertTo-Json -Depth 20)) -eq 0 -or (Test-Path -LiteralPath $auditOutput)) {
        throw 'A graph report with truncated transitive coverage was accepted.'
    }
    Assert-DependencyFixtureFails 'empty vulnerability frameworks' {
        param($fixture)
        $fixture.PSObject.Properties.Add([psnoteproperty]::new('frameworks', @()))
    }
    Assert-DependencyFixtureFails 'wrong vulnerability framework' {
        param($fixture)
        $fixture.PSObject.Properties.Remove('frameworks')
        $fixture.PSObject.Properties.Add([psnoteproperty]::new('frameworks', @([pscustomobject]@{ framework = 'net7.0'; topLevelPackages = @(); transitivePackages = @() })))
    }
    Assert-DependencyFixtureFails 'multiple vulnerability frameworks' {
        param($fixture)
        $fixture.PSObject.Properties.Add([psnoteproperty]::new('frameworks', @(
            [pscustomobject]@{ framework = 'net8.0'; topLevelPackages = @(); transitivePackages = @() },
            [pscustomobject]@{ framework = 'net7.0'; topLevelPackages = @(); transitivePackages = @() }
        )))
    }
    Assert-DependencyFixtureFails 'missing package identity and version' {
        param($fixture)
        $fixture.PSObject.Properties.Remove('frameworks')
        $fixture.PSObject.Properties.Add([psnoteproperty]::new('frameworks', @([pscustomobject]@{
            framework = 'net8.0'
            topLevelPackages = @([pscustomobject]@{ resolvedVersion = '0.1.1'; vulnerabilities = @([pscustomobject]@{ severity = 'High'; advisoryurl = 'https://example.test/advisory' }) })
            transitivePackages = @()
        })))
    }
    Assert-DependencyFixtureFails 'missing vulnerability array' {
        param($fixture)
        $fixture.PSObject.Properties.Remove('frameworks')
        $fixture.PSObject.Properties.Add([psnoteproperty]::new('frameworks', @([pscustomobject]@{
            framework = 'net8.0'
            topLevelPackages = @([pscustomobject]@{ id = 'KeelMatrix.Telemetry'; resolvedVersion = '0.1.1' })
            transitivePackages = @()
        })))
    }
    Assert-DependencyFixtureFails 'unexpected finding shape' {
        param($fixture)
        $fixture.PSObject.Properties.Remove('frameworks')
        $fixture.PSObject.Properties.Add([psnoteproperty]::new('frameworks', @([pscustomobject]@{
            framework = 'net8.0'
            topLevelPackages = @([pscustomobject]@{ id = 'KeelMatrix.Telemetry'; resolvedVersion = '0.1.1'; vulnerabilities = @('localized finding') })
            transitivePackages = @()
        })))
    }
    if ((Invoke-DependencyFixture -RawVulnerability "localized output`n") -eq 0 -or (Test-Path -LiteralPath $auditOutput)) {
        throw 'Localized or non-JSON vulnerability output was accepted.'
    }
    if ((Invoke-DependencyFixture -RawVulnerability ($cleanVulnerabilityJson + "`nwarning")) -eq 0 -or (Test-Path -LiteralPath $auditOutput)) {
        throw 'Warnings mixed into machine-readable output were accepted.'
    }
    if ((Invoke-DependencyFixture -Stderr 'warning from audit source') -eq 0 -or (Test-Path -LiteralPath $auditOutput)) {
        throw 'Audit stderr was accepted as a clean result.'
    }
}
finally {
    $env:FAKE_DEPENDENCY_GRAPH_OUTPUT = $savedFakeGraph
    $env:FAKE_DEPENDENCY_VULNERABILITY_OUTPUT = $savedFakeVulnerability
    $env:FAKE_DEPENDENCY_AUDIT_STDERR = $savedFakeStderr
    Remove-Item -LiteralPath $dependencyFixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Validation stage regression checks passed.'
