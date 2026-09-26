[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')

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

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("corsspec-package-inspection-" + [guid]::NewGuid().ToString('N'))
$fixturePackageRoot = Join-Path $fixtureRoot 'package'
$fixturePackage = Join-Path $fixtureRoot 'KeelMatrix.CorsSpec.0.1.0.nupkg'
$fixtureSymbols = Join-Path $fixtureRoot 'KeelMatrix.CorsSpec.0.1.0.snupkg'
New-Item -ItemType Directory -Force -Path (Join-Path $fixturePackageRoot 'lib/net8.0') | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $fixturePackageRoot 'README.md') -Value 'Package README' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $fixturePackageRoot 'LICENSE') -Value 'MIT License' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $fixturePackageRoot 'lib/net8.0/KeelMatrix.CorsSpec.dll') -Value 'fixture' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $fixturePackageRoot 'lib/net8.0/KeelMatrix.CorsSpec.xml') -Value '<doc />' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $fixturePackageRoot 'KeelMatrix.CorsSpec.nuspec') -Value @'
<?xml version="1.0" encoding="utf-8"?>
<package>
  <metadata>
    <id>KeelMatrix.CorsSpec</id>
    <version>0.1.0</version>
    <authors>KeelMatrix</authors>
    <description>Fixture package.</description>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
  </metadata>
</package>
'@ -Encoding utf8
    Compress-Archive -Path (Join-Path $fixturePackageRoot '*') -DestinationPath $fixturePackage
    Set-Content -LiteralPath $fixtureSymbols -Value 'symbols fixture' -Encoding utf8

    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $fixturePackage -SymbolsPath $fixtureSymbols 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Package inspection rejected a package whose only missing prerequisite is the founder-owned icon.'
    }

    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $fixturePackage -SymbolsPath $fixtureSymbols -RequireIcon 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw 'Package inspection did not enforce the icon prerequisite when RequireIcon was requested.'
    }
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Validation stage regression checks passed.'
