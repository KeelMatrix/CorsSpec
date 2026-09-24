[CmdletBinding()]
param(
    [ValidateSet('Focused', 'Full')][string]$Mode = 'Full',
    [string]$Version = '0.1.0',
    [switch]$SkipPackage
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'KeelMatrix.CorsSpec.sln'
$project = Join-Path $root 'src\KeelMatrix.CorsSpec\KeelMatrix.CorsSpec.csproj'
$packages = Join-Path $root 'artifacts\packages'
$failures = [System.Collections.Generic.List[string]]::new()
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Invalid package version '$Version'. Expected X.Y.Z."
    exit 1
}

New-Item -ItemType Directory -Force -Path $packages | Out-Null

$env:KEELMATRIX_NO_TELEMETRY = '1'
Invoke-ValidationStage -Name 'Validation stage regression' -Failures $failures -Command {
    pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ValidationStage.ps1')
}
if ($Mode -eq 'Full') {
    Invoke-ValidationStage -Name 'Release contract' -Failures $failures -Command { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag "v$Version" }
}
Invoke-ValidationStage -Name 'Restore' -Failures $failures -Command { dotnet restore $solution --configfile (Join-Path $root 'NuGet.config') }
Invoke-ValidationStage -Name 'Release build' -Failures $failures -Command { dotnet build $solution --configuration Release --no-restore }
Invoke-ValidationStage -Name 'Formatting' -Failures $failures -Command { dotnet format $solution --no-restore --verify-no-changes }
Invoke-ValidationStage -Name 'Unit tests' -Failures $failures -Command { dotnet test (Join-Path $root 'tests\KeelMatrix.CorsSpec.Tests\KeelMatrix.CorsSpec.Tests.csproj') --configuration Release --no-build --no-restore }
Invoke-ValidationStage -Name 'Integration tests' -Failures $failures -Command { dotnet test (Join-Path $root 'tests\KeelMatrix.CorsSpec.IntegrationTests\KeelMatrix.CorsSpec.IntegrationTests.csproj') --configuration Release --no-build --no-restore }

if (-not $SkipPackage) {
    Get-ChildItem -LiteralPath $packages -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.nupkg', '.snupkg') } |
        Remove-Item -Force
    Invoke-ValidationStage -Name 'Package build' -Failures $failures -Command { dotnet pack $project --configuration Release --no-build --no-restore -p:Version=$Version --output $packages }
    $package = Join-Path $packages "KeelMatrix.CorsSpec.$Version.nupkg"
    $symbols = Join-Path $packages "KeelMatrix.CorsSpec.$Version.snupkg"
    Invoke-ValidationStage -Name 'Release artifact contract' -Failures $failures -Command { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag "v$Version" -ArtifactDirectory $packages }
    Invoke-ValidationStage -Name 'Package inspection (icon gate is fail-closed but non-blocking for later evidence)' -Failures $failures -Command { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $package -SymbolsPath $symbols }
    Invoke-ValidationStage -Name 'Package consumer smoke' -Failures $failures -Command { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-PackageSmoke.ps1') -PackageDirectory $packages }
}

if ($Mode -eq 'Full') {
    Invoke-ValidationStage -Name 'Dependency audit' -Failures $failures -Command { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-DependencyAudit.ps1') }
}

if ($failures.Count -ne 0) {
    Write-Error ('VALIDATION_FAILED: ' + ($failures -join ', '))
    exit 1
}
Write-Host 'Validation passed.'
