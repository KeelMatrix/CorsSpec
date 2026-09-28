[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..' 'artifacts' 'dependency-audit.json')
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' 'src' 'KeelMatrix.CorsSpec' 'KeelMatrix.CorsSpec.csproj'))
$config = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' 'NuGet.config'))
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("corsspec-dependency-audit-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

function Invoke-JsonAudit([string[]]$Arguments, [string]$Label) {
    $stderrPath = Join-Path $temporaryRoot "$Label.stderr"
    $stdout = & dotnet @Arguments 2> $stderrPath | Out-String
    $exitCode = $LASTEXITCODE
    $stderr = Get-Content -Raw -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    if ($exitCode -ne 0) { throw "$Label did not complete successfully (exit $exitCode): $stderr" }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) { throw "$Label produced warnings or non-machine-readable diagnostics: $stderr" }
    if ([string]::IsNullOrWhiteSpace($stdout)) { throw "$Label produced empty audit output." }
    try { return $stdout | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "$Label did not produce valid JSON: $($_.Exception.Message)" }
}

try {
    $common = @('list', $project, 'package', '--include-transitive', '--format', 'json', '--output-version', '1', '--configfile', $config)
    $graph = Invoke-JsonAudit $common 'dependency graph audit'
    $vulnerabilities = Invoke-JsonAudit (@('list', $project, 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--output-version', '1', '--configfile', $config)) 'vulnerability audit'

    foreach ($report in @($graph, $vulnerabilities)) {
        if ($report.version -ne 1) { throw 'Dependency audit returned an unsupported JSON schema version.' }
        $projects = @($report.projects)
        if ($projects.Count -ne 1 -or [string]$projects[0].path -notlike '*src/KeelMatrix.CorsSpec/KeelMatrix.CorsSpec.csproj') {
            throw 'Dependency audit did not cover the expected shipping project.'
        }
    }

    $frameworks = @($graph.projects[0].frameworks)
    if ($frameworks.Count -ne 1 -or $frameworks[0].framework -ne 'net8.0') {
        throw 'Dependency graph audit did not cover exactly the expected net8.0 framework.'
    }
    $topLevel = @($frameworks[0].topLevelPackages)
    if ($topLevel.Count -eq 0 -or @($topLevel | Where-Object { [string]::IsNullOrWhiteSpace($_.id) -or [string]::IsNullOrWhiteSpace($_.resolvedVersion) }).Count -ne 0) {
        throw 'Dependency graph audit has incomplete package coverage.'
    }
    $telemetry = @($topLevel | Where-Object id -eq 'KeelMatrix.Telemetry')
    if ($telemetry.Count -ne 1 -or $telemetry[0].resolvedVersion -ne '0.1.1') {
        throw 'Dependency graph audit does not contain the expected KeelMatrix.Telemetry dependency.'
    }

    $vulnerableFrameworks = @($vulnerabilities.projects[0].frameworks)
    $findings = foreach ($framework in $vulnerableFrameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            foreach ($finding in @($package.vulnerabilities)) { if ($null -ne $finding) { $finding } }
        }
    }
    if (@($findings).Count -ne 0) {
        throw 'DEPENDENCY_GATE_FAILED: vulnerability findings were reported.'
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    @{ schemaVersion = 1; graph = $graph; vulnerabilities = $vulnerabilities } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    Write-Output 'Dependency audit passed: machine-readable graph coverage completed and no vulnerability findings were reported.'
}
finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
