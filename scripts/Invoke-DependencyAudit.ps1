[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..' 'artifacts' 'dependency-audit.json'),
    [string]$DotnetCommand = 'dotnet'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' 'src' 'KeelMatrix.CorsSpec' 'KeelMatrix.CorsSpec.csproj'))
$config = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' 'NuGet.config'))
$expectedProjectPath = [IO.Path]::GetFullPath($project).Replace('\', '/')
$expectedRelativeProjectPath = 'src/KeelMatrix.CorsSpec/KeelMatrix.CorsSpec.csproj'
$expectedSources = @(([xml](Get-Content -Raw -LiteralPath $config)).configuration.packageSources.add | ForEach-Object { [string]$_.value })
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("corsspec-dependency-audit-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

function Invoke-JsonAudit([string[]]$Arguments, [string]$Label) {
    $stderrPath = Join-Path $temporaryRoot "$Label.stderr"
    if ($DotnetCommand.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase)) {
        $stdout = Invoke-NestedPwsh -NoProfile -File $DotnetCommand @Arguments 2> $stderrPath | Out-String
    }
    else {
        $stdout = & $DotnetCommand @Arguments 2> $stderrPath | Out-String
    }
    $exitCode = $LASTEXITCODE
    $stderr = Get-Content -Raw -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    if ($exitCode -ne 0) { throw "$Label did not complete successfully (exit $exitCode): $stderr" }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) { throw "$Label produced warnings or non-machine-readable diagnostics: $stderr" }
    if ([string]::IsNullOrWhiteSpace($stdout)) { throw "$Label produced empty audit output." }
    try { return $stdout | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "$Label did not produce valid JSON: $($_.Exception.Message)" }
}

function Require-Properties($Value, [string[]]$Names, [string]$Label) {
    if ($null -eq $Value -or $Value -is [string]) {
        throw "$Label is not a JSON object."
    }

    $actual = @($Value.PSObject.Properties.Name)
    if ($actual.Count -ne $Names.Count -or @($Names | Where-Object { $_ -notin $actual }).Count -ne 0 -or @($actual | Where-Object { $_ -notin $Names }).Count -ne 0) {
        throw "$Label has an unrecognized or incomplete JSON shape."
    }
}

function Test-ExpectedProjectPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $normalized = $Path.Replace('\', '/').TrimEnd('/')
    $comparison = if ([IO.Path]::DirectorySeparatorChar -eq '\') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    return $normalized.Equals($expectedProjectPath, $comparison) -or $normalized.Equals($expectedRelativeProjectPath, $comparison)
}

function Assert-ReportEnvelope($Report, [string]$Label, [string]$ExpectedParameters, [string[]]$ProjectProperties, [bool]$RequireSources = $true) {
    $reportProperties = if ($RequireSources) { @('version', 'parameters', 'sources', 'projects') } else { @('version', 'parameters', 'projects') }
    Require-Properties $Report $reportProperties $Label
    if ($Report.version -ne 1 -or $Report.parameters -ne $ExpectedParameters) {
        throw "$Label has an unsupported schema version or command scope."
    }

    if ($RequireSources) {
        $sources = @($Report.sources)
        if ($sources.Count -ne $expectedSources.Count -or @($sources | Where-Object { $_ -notin $expectedSources }).Count -ne 0) {
            throw "$Label did not report the configured package source set."
        }
    }

    $projects = @($Report.projects)
    if ($projects.Count -ne 1 -or -not (Test-ExpectedProjectPath ([string]$projects[0].path))) {
        throw "$Label did not cover exactly the expected shipping project."
    }

    Require-Properties $projects[0] $ProjectProperties "$Label project"
    return $projects[0]
}

function Assert-GraphReport($Report) {
    $projectEntry = Assert-ReportEnvelope $Report 'Dependency graph audit' '--include-transitive' @('path', 'frameworks') $false
    $frameworks = @($projectEntry.frameworks)
    if ($frameworks.Count -ne 1) {
        throw 'Dependency graph audit did not cover exactly one framework.'
    }

    Require-Properties $frameworks[0] @('framework', 'topLevelPackages', 'transitivePackages') 'Dependency graph framework'
    if ($frameworks[0].framework -ne 'net8.0') {
        throw 'Dependency graph audit did not cover exactly the expected net8.0 framework.'
    }

    foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
        $packages = @($frameworks[0].$kind)
        if ($packages.Count -eq 0) {
            throw "Dependency graph audit has no $kind coverage."
        }

        foreach ($package in $packages) {
            if ($null -eq $package -or [string]::IsNullOrWhiteSpace([string]$package.id) -or [string]::IsNullOrWhiteSpace([string]$package.resolvedVersion)) {
                throw 'Dependency graph audit has incomplete package identity or version coverage.'
            }
            if ($kind -eq 'topLevelPackages' -and [string]::IsNullOrWhiteSpace([string]$package.requestedVersion)) {
                throw 'Dependency graph audit has incomplete top-level package coverage.'
            }
        }
    }

    $telemetry = @($frameworks[0].topLevelPackages | Where-Object id -eq 'KeelMatrix.Telemetry')
    if ($telemetry.Count -ne 1 -or $telemetry[0].resolvedVersion -ne '0.1.1') {
        throw 'Dependency graph audit does not contain the expected KeelMatrix.Telemetry dependency.'
    }
}

function Get-VulnerabilityFindings($Report) {
    $projectEntry = Assert-ReportEnvelope $Report 'Vulnerability audit' '--vulnerable --include-transitive' @('path')
    $frameworksProperty = $projectEntry.PSObject.Properties['frameworks']
    if ($null -eq $frameworksProperty) {
        # The .NET SDK represents a completed clean vulnerability scan with no
        # frameworks property on the project entry. This is the only supported
        # no-findings shape; the envelope above proves its command and source scope.
        return @()
    }

    Require-Properties $projectEntry @('path', 'frameworks') 'Vulnerability audit project'

    $frameworks = @($frameworksProperty.Value)
    if ($frameworks.Count -ne 1) {
        throw 'Vulnerability audit did not cover exactly one framework.'
    }

    Require-Properties $frameworks[0] @('framework', 'topLevelPackages', 'transitivePackages') 'Vulnerability audit framework'
    if ($frameworks[0].framework -ne 'net8.0') {
        throw 'Vulnerability audit did not cover exactly the expected net8.0 framework.'
    }

    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
        $packages = @($frameworks[0].$kind)
        foreach ($package in $packages) {
            if ($null -eq $package -or [string]::IsNullOrWhiteSpace([string]$package.id) -or [string]::IsNullOrWhiteSpace([string]$package.resolvedVersion)) {
                throw 'Vulnerability audit has incomplete package identity or version coverage.'
            }

            $vulnerabilitiesProperty = $package.PSObject.Properties['vulnerabilities']
            if ($null -eq $vulnerabilitiesProperty) {
                throw 'Vulnerability audit has incomplete vulnerability coverage.'
            }

            $vulnerabilities = @($vulnerabilitiesProperty.Value)
            if ($vulnerabilities.Count -eq 0) {
                throw 'Vulnerability audit contains a package without a vulnerability finding.'
            }

            foreach ($finding in $vulnerabilities) {
                if ($null -eq $finding -or [string]::IsNullOrWhiteSpace([string]$finding.severity) -or [string]::IsNullOrWhiteSpace([string]$finding.advisoryurl)) {
                    throw 'Vulnerability audit contains an unrecognized vulnerability finding shape.'
                }
                [void]$findings.Add($finding)
            }
        }
    }

    if ($findings.Count -eq 0) {
        throw 'Vulnerability audit contained framework metadata without findings or a recognized clean shape.'
    }

    return $findings.ToArray()
}

try {
    $common = @('list', $project, 'package', '--include-transitive', '--format', 'json', '--output-version', '1', '--configfile', $config)
    $graph = Invoke-JsonAudit $common 'dependency graph audit'
    $vulnerabilities = Invoke-JsonAudit (@('list', $project, 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--output-version', '1', '--configfile', $config)) 'vulnerability audit'

    Assert-GraphReport $graph
    $findings = Get-VulnerabilityFindings $vulnerabilities
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
