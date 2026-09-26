[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$SymbolsPath,
    [switch]$RequireIcon
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $PackagePath)) { $failures.Add("Missing package: $PackagePath") }
if (-not (Test-Path -LiteralPath $SymbolsPath)) { $failures.Add("Missing symbol package: $SymbolsPath") }

if ($failures.Count -eq 0) {
    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath))
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        $required = @('README.md', 'LICENSE', 'lib/net8.0/KeelMatrix.CorsSpec.dll', 'lib/net8.0/KeelMatrix.CorsSpec.xml')
        foreach ($name in $required) {
            if ($names -notcontains $name) { $failures.Add("Package is missing required entry: $name") }
        }

        $iconPresent = $names -contains 'icon.png'
        if ($iconPresent) {
            Write-Output 'ICON_GATE: package-root icon.png is present; dimensions and founder confirmation remain release evidence.'
        }
        else {
            Write-Warning 'ICON_GATE_PENDING: required founder-owned repository-root icon.png is absent and was not packed. Place it at the exact path before frontier review.'
            if ($RequireIcon) {
                $failures.Add('Missing required package-root icon.png')
            }
        }

        $unexpected = @($names | Where-Object { $_ -match '(^|/)(\.env(\..*)?|keelmatrix\.telemetry\.json)$' })
        if ($unexpected.Count -ne 0) { $failures.Add("Sensitive package entries found: $($unexpected -join ', ')") }
        if ($names | Where-Object { $_ -match '(^|/)(tests?|samples?|artifacts?)/' }) { $failures.Add('Test, sample, or artifact content was included in the package') }
        $nuspec = $archive.Entries | Where-Object FullName -match '\.nuspec$' | Select-Object -First 1
        if ($null -eq $nuspec) { $failures.Add('Package is missing its nuspec') }
        else {
            $reader = [System.IO.StreamReader]::new($nuspec.Open())
            try { $xml = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
            $metadata = $xml.package.metadata
            if ($metadata.id -ne 'KeelMatrix.CorsSpec') { $failures.Add("Unexpected package id: $($metadata.id)") }
            if ($metadata.license.'#text' -ne 'MIT') { $failures.Add('Package license expression is not MIT') }
            if ($metadata.readme -ne 'README.md') { $failures.Add('Package README metadata is not README.md') }
            if ($metadata.icon -ne 'icon.png') {
                if ($iconPresent -or $RequireIcon) {
                    $failures.Add('Package icon metadata is not icon.png')
                }
                else {
                    Write-Warning 'ICON_GATE_PENDING: package icon metadata is not icon.png until the founder-owned icon is placed.'
                }
            }
        }
    }
    finally { $archive.Dispose() }
}

if ($failures.Count -ne 0) {
    Write-Error ('PACKAGE_GATE_FAILED: ' + ($failures -join '; '))
    exit 1
}

Write-Output 'Package inspection passed.'
