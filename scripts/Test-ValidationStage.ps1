[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')
Add-Type -AssemblyName System.IO.Compression

$failures = [System.Collections.Generic.List[string]]::new()

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
    $commit = '0000000000000000000000000000000000000000'
    $packageRoot = Join-Path $Root 'package'
    $symbolRoot = Join-Path $Root 'symbols'
    New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot "lib/$packageTfm"), (Join-Path $symbolRoot "lib/$symbolTfm"), (Join-Path $packageRoot '_rels'), (Join-Path $symbolRoot '_rels'), (Join-Path $packageRoot 'package/services/metadata/core-properties'), (Join-Path $symbolRoot 'package/services/metadata/core-properties') | Out-Null

    Set-Content -LiteralPath (Join-Path $packageRoot 'README.md') -Value 'Package README' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot 'LICENSE') -Value 'MIT License' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot "lib/$packageTfm/KeelMatrix.CorsSpec.dll") -Value 'fixture' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot "lib/$packageTfm/KeelMatrix.CorsSpec.xml") -Value '<doc />' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot '_rels/.rels') -Value 'relationships' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot '[Content_Types].xml') -Value 'content types' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $packageRoot 'package/services/metadata/core-properties/00000000000000000000000000000000.psmdcp') -Value 'metadata' -Encoding utf8

    $packageNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>$packageId</id><version>$packageVersion</version><license type="expression">MIT</license><readme>README.md</readme><repository type="git" url="https://github.com/KeelMatrix/CorsSpec" branch="refs/heads/main" commit="$commit" /><dependencies><group targetFramework="$packageTfm" /></dependencies></metadata></package>
"@
    Set-Content -LiteralPath (Join-Path $packageRoot "$packageId.nuspec") -Value $packageNuspec -Encoding utf8

    Set-Content -LiteralPath (Join-Path $symbolRoot '_rels/.rels') -Value 'relationships' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $symbolRoot '[Content_Types].xml') -Value 'content types' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $symbolRoot 'package/services/metadata/core-properties/00000000000000000000000000000000.psmdcp') -Value 'metadata' -Encoding utf8

    $symbolNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata><id>$symbolId</id><version>$symbolVersion</version><packageTypes><packageType name="SymbolsPackage" /></packageTypes><repository type="git" url="https://github.com/KeelMatrix/CorsSpec" branch="refs/heads/main" commit="$commit" /><dependencies><group targetFramework="$symbolTfm" /></dependencies></metadata></package>
"@
    if ($Options.ContainsKey('MalformedSymbolNuspec') -and $Options.MalformedSymbolNuspec) { $symbolNuspec = '<package><metadata>' }
    Set-Content -LiteralPath (Join-Path $symbolRoot "$symbolId.nuspec") -Value $symbolNuspec -Encoding utf8

    if (-not ($Options.ContainsKey('MissingPdb') -and $Options.MissingPdb)) {
        $pdbRelative = if ($Options.ContainsKey('PdbPath')) { $Options.PdbPath } else { "lib/$symbolTfm/KeelMatrix.CorsSpec.pdb" }
        $pdbPath = Join-Path $symbolRoot $pdbRelative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pdbPath) | Out-Null
        $pdbText = "BSJB https://raw.githubusercontent.com/KeelMatrix/CorsSpec/$commit/*"
        [IO.File]::WriteAllBytes($pdbPath, [Text.Encoding]::UTF8.GetBytes($pdbText))
    }

    if ($Options.ContainsKey('ExtraPackageEntry')) { Set-Content -LiteralPath (Join-Path $packageRoot $Options.ExtraPackageEntry) -Value 'unexpected' -Encoding utf8 }
    if ($Options.ContainsKey('ExtraSymbolEntry')) { Set-Content -LiteralPath (Join-Path $symbolRoot $Options.ExtraSymbolEntry) -Value 'unexpected' -Encoding utf8 }

    $packagePath = Join-Path $Root 'KeelMatrix.CorsSpec.0.1.0.nupkg'
    $symbolsPath = Join-Path $Root 'KeelMatrix.CorsSpec.0.1.0.snupkg'
    New-FixtureArchive $packageRoot $packagePath
    New-FixtureArchive $symbolRoot $symbolsPath
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
