[CmdletBinding()]
param(
    [ValidateSet('Focused', 'Full')][string]$Mode = 'Full',
    [string]$Version = '0.1.0',
    [switch]$RequireIcon,
    [switch]$SkipPackage
)

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$launchGuard = Join-Path $root 'build/Test-NestedPwshLaunch.ps1'
& $launchGuard -SelfTest
if ($LASTEXITCODE -ne 0) { throw 'Nested PowerShell launch guard self-test failed.' }
& $launchGuard
if ($LASTEXITCODE -ne 0) { throw 'Nested PowerShell launch guard failed.' }
$solution = Join-Path $root 'KeelMatrix.CorsSpec.sln'
$project = Join-Path $root 'src' 'KeelMatrix.CorsSpec' 'KeelMatrix.CorsSpec.csproj'
$packages = Join-Path $root 'artifacts' 'packages'
$failures = [System.Collections.Generic.List[string]]::new()
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')

$repositoryCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $repositoryCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the checked-out repository commit for artifact provenance validation.'
}
$expectedCommit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { $repositoryCommit } else { $env:GITHUB_SHA.Trim() }
if ($expectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Expected candidate commit '$expectedCommit' is not a 40-character hexadecimal SHA."
}
if ($expectedCommit -ine $repositoryCommit) {
    throw "Expected candidate commit '$expectedCommit' does not match checked-out HEAD '$repositoryCommit'."
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Invalid package version '$Version'. Expected X.Y.Z."
    exit 1
}

New-Item -ItemType Directory -Force -Path $packages | Out-Null

$env:KEELMATRIX_NO_TELEMETRY = '1'
Invoke-ValidationStage -Name 'Tracked-text hygiene' -Failures $failures -Command {
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-TrackedText.ps1')
}
Invoke-ValidationStage -Name 'Commit history hygiene' -Failures $failures -Command {
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-CommitHistory.ps1')
}
Invoke-ValidationStage -Name 'Validation stage regression' -Failures $failures -Command {
    Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ValidationStage.ps1')
}
if ($Mode -eq 'Full') {
    Invoke-ValidationStage -Name 'Release contract' -Failures $failures -Command { Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag "v$Version" }
}
Invoke-ValidationStage -Name 'Restore' -Failures $failures -Command { dotnet restore $solution --configfile (Join-Path $root 'NuGet.config') }
Invoke-ValidationStage -Name 'Release build' -Failures $failures -Command { dotnet build $solution --configuration Release --no-restore }
Invoke-ValidationStage -Name 'Formatting' -Failures $failures -Command { dotnet format $solution --no-restore --verify-no-changes }
Invoke-ValidationStage -Name 'Unit tests' -Failures $failures -Command { dotnet test (Join-Path $root 'tests' 'KeelMatrix.CorsSpec.Tests' 'KeelMatrix.CorsSpec.Tests.csproj') --configuration Release --no-build --no-restore }
Invoke-ValidationStage -Name 'Integration tests' -Failures $failures -Command { dotnet test (Join-Path $root 'tests' 'KeelMatrix.CorsSpec.IntegrationTests' 'KeelMatrix.CorsSpec.IntegrationTests.csproj') --configuration Release --no-build --no-restore }

if (-not $SkipPackage) {
    Get-ChildItem -LiteralPath $packages -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.nupkg', '.snupkg') } |
        Remove-Item -Force
    Invoke-ValidationStage -Name 'Package build' -Failures $failures -Command { dotnet pack $project --configuration Release --no-build --no-restore -p:Version=$Version --output $packages }
    $package = Join-Path $packages "KeelMatrix.CorsSpec.$Version.nupkg"
    $symbols = Join-Path $packages "KeelMatrix.CorsSpec.$Version.snupkg"
    Invoke-ValidationStage -Name 'Release artifact contract' -Failures $failures -Command { Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1') -Tag "v$Version" -ArtifactDirectory $packages -RequireIcon:$RequireIcon }
    Invoke-ValidationStage -Name 'Package inspection' -Failures $failures -Command { Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Inspect-Package.ps1') -PackagePath $package -SymbolsPath $symbols -ExpectedCommit $expectedCommit -RequireIcon:$RequireIcon }
    Invoke-ValidationStage -Name 'Package consumer smoke' -Failures $failures -Command { Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-PackageSmoke.ps1') -PackageDirectory $packages }
}

if ($Mode -eq 'Full') {
    Invoke-ValidationStage -Name 'Dependency audit' -Failures $failures -Command { Invoke-NestedPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-DependencyAudit.ps1') }
}

if ($failures.Count -ne 0) {
    Write-Error ('VALIDATION_FAILED: ' + ($failures -join ', '))
    exit 1
}
Write-Host 'Validation passed.'
