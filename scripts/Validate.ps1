[CmdletBinding()]
param([ValidateSet('Focused', 'Full')][string]$Mode = 'Full', [switch]$SkipPackage)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'KeelMatrix.CorsSpec.sln'
$project = Join-Path $root 'src\KeelMatrix.CorsSpec\KeelMatrix.CorsSpec.csproj'
$packages = Join-Path $root 'artifacts\packages'
$failures = [System.Collections.Generic.List[string]]::new()

New-Item -ItemType Directory -Force -Path $packages | Out-Null

function Run-Stage([string]$Name, [scriptblock]$Command) {
    Write-Host "=== $Name ==="
    & $Command
    if ($LASTEXITCODE -ne 0) { $failures.Add($Name) }
}

$env:KEELMATRIX_NO_TELEMETRY = '1'
Run-Stage 'Restore' { dotnet restore $solution --configfile (Join-Path $root 'NuGet.config') }
Run-Stage 'Release build' { dotnet build $solution --configuration Release --no-restore }
Run-Stage 'Unit tests' { dotnet test (Join-Path $root 'tests\KeelMatrix.CorsSpec.Tests\KeelMatrix.CorsSpec.Tests.csproj') --configuration Release --no-build --no-restore }
Run-Stage 'Integration tests' { dotnet test (Join-Path $root 'tests\KeelMatrix.CorsSpec.IntegrationTests\KeelMatrix.CorsSpec.IntegrationTests.csproj') --configuration Release --no-build --no-restore }

if (-not $SkipPackage) {
    Run-Stage 'Package build' { dotnet pack $project --configuration Release --no-build --no-restore --output $packages }
    $package = Join-Path $packages 'KeelMatrix.CorsSpec.0.1.0.nupkg'
    $symbols = Join-Path $packages 'KeelMatrix.CorsSpec.0.1.0.snupkg'
    Run-Stage 'Package inspection (icon gate is fail-closed but non-blocking for later evidence)' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $package -SymbolsPath $symbols }
    Run-Stage 'Package consumer smoke' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-PackageSmoke.ps1') -PackageDirectory $packages }
}

if ($Mode -eq 'Full') {
    Run-Stage 'Dependency audit' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-DependencyAudit.ps1') }
}

if ($failures.Count -ne 0) {
    Write-Error ('VALIDATION_FAILED: ' + ($failures -join ', '))
    exit 1
}
Write-Host 'Validation passed.'
