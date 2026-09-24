[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\dependency-audit.txt')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\KeelMatrix.CorsSpec\KeelMatrix.CorsSpec.csproj'
dotnet list $project package --vulnerable --include-transitive --configfile (Join-Path $PSScriptRoot '..\NuGet.config') *> $OutputPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Select-String -LiteralPath $OutputPath -Pattern 'has the following vulnerable packages|Severity\s*:\s*(Critical|High|Moderate|Low)' -Quiet) {
    Write-Error 'DEPENDENCY_GATE_FAILED: vulnerable package advisory detected.'
    Get-Content -LiteralPath $OutputPath
    exit 1
}
Write-Output 'Dependency audit passed.'
