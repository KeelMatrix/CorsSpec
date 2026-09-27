[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
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
    pwsh -NoProfile -Command 'exit 0'
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
    pwsh -NoProfile -Command 'exit 7'
} 2>$null
if ('Non-zero command' -notin $failures) {
    throw 'A non-zero child-process exit was not recorded as a failed stage.'
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
    $packageRoot = Join-Path $Root 'package'
    $symbolRoot = Join-Path $Root 'symbols'
    New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot "lib/$packageTfm"), (Join-Path $symbolRoot "lib/$symbolTfm"), (Join-Path $packageRoot '_rels'), (Join-Path $symbolRoot '_rels'), (Join-Path $packageRoot 'package/services/metadata/core-properties'), (Join-Path $symbolRoot 'package/services/metadata/core-properties') | Out-Null

    Set-Content -LiteralPath (Join-Path $packageRoot 'README.md') -Value 'Package README' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot 'LICENSE') -Value 'MIT License' -Encoding utf8
    Copy-Item -LiteralPath $builtAssembly -Destination (Join-Path $packageRoot "lib/$packageTfm/KeelMatrix.CorsSpec.dll")
    Copy-Item -LiteralPath $builtDocumentation -Destination (Join-Path $packageRoot "lib/$packageTfm/KeelMatrix.CorsSpec.xml")
    Set-Content -LiteralPath (Join-Path $packageRoot '_rels/.rels') -Value 'relationships' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot '[Content_Types].xml') -Value 'content types' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot 'package/services/metadata/core-properties/00000000000000000000000000000000.psmdcp') -Value 'metadata' -Encoding utf8

    $packageNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>$packageId</id><version>$packageVersion</version><license type="expression">MIT</license><readme>README.md</readme><repository type="git" url="https://github.com/KeelMatrix/CorsSpec" branch="refs/heads/main" commit="$packageCommit" /><dependencies><group targetFramework="$packageTfm" /></dependencies></metadata></package>
"@
    Set-Content -LiteralPath (Join-Path $packageRoot "$packageId.nuspec") -Value $packageNuspec -Encoding utf8

    Set-Content -LiteralPath (Join-Path $symbolRoot '_rels/.rels') -Value 'relationships' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $symbolRoot '[Content_Types].xml') -Value 'content types' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $symbolRoot 'package/services/metadata/core-properties/00000000000000000000000000000000.psmdcp') -Value 'metadata' -Encoding utf8

    $symbolNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>$symbolId</id><version>$symbolVersion</version><packageTypes><packageType name="SymbolsPackage" /></packageTypes><repository type="git" url="https://github.com/KeelMatrix/CorsSpec" branch="refs/heads/main" commit="$symbolCommit" /><dependencies><group targetFramework="$symbolTfm" /></dependencies></metadata></package>
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
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $Set.Package -SymbolsPath $Set.Symbols
    if ($LASTEXITCODE -ne 0) { throw 'A valid package and symbol archive set was rejected.' }
}

function Assert-InspectionFails($Set, [string]$Reason) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $Set.Package -SymbolsPath $Set.Symbols 2>$null
    if ($LASTEXITCODE -eq 0) { throw "Archive inspection accepted $Reason." }
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("corsspec-package-inspection-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
try {
    $valid = New-FixtureSet (Join-Path $fixtureRoot 'valid')
    Assert-InspectionPasses $valid
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $valid.Package -SymbolsPath $valid.Symbols -RequireIcon 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Package inspection did not enforce the icon prerequisite.' }
    $extraArtifact = Join-Path (Split-Path -Parent $valid.Package) 'unexpected.nupkg'
    Set-Content -LiteralPath $extraArtifact -Value 'unexpected artifact' -Encoding utf8
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag v0.1.0 -ArtifactDirectory (Split-Path -Parent $valid.Package) 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Release contract accepted an unintended artifact.' }
    Remove-Item -LiteralPath $extraArtifact -Force

    $packageVersionDrift = New-FixtureSet (Join-Path $fixtureRoot 'package-version-drift') @{ PackageVersion = '9.9.9' }
    Assert-InspectionFails $packageVersionDrift 'package version drift'

    $packageIdDrift = New-FixtureSet (Join-Path $fixtureRoot 'package-id-drift') @{ PackageId = 'Wrong.Package' }
    Assert-InspectionFails $packageIdDrift 'package id drift'

    $symbolVersionDrift = New-FixtureSet (Join-Path $fixtureRoot 'symbol-version-drift') @{ SymbolVersion = '9.9.9' }
    Assert-InspectionFails $symbolVersionDrift 'symbol version drift'

    $symbolIdDrift = New-FixtureSet (Join-Path $fixtureRoot 'symbol-id-drift') @{ SymbolId = 'Wrong.Package' }
    Assert-InspectionFails $symbolIdDrift 'symbol id drift'

    $commitDrift = New-FixtureSet (Join-Path $fixtureRoot 'commit-drift') @{ PackageCommit = '1111111111111111111111111111111111111111' }
    Assert-InspectionFails $commitDrift 'package and symbol commit drift'

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

Write-Host 'Validation stage regression checks passed.'
