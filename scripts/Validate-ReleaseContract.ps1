[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Tag)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\KeelMatrix.CorsSpec\KeelMatrix.CorsSpec.csproj'
$version = ([xml](Get-Content -Raw -LiteralPath $project)).Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1 -ExpandProperty Version
if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.1.0' }
$tagVersion = $Tag -replace '^v', ''
if ($tagVersion -ne $version) { throw "Release tag '$Tag' does not match package version '$version'." }
$changelog = Get-Content -Raw -LiteralPath (Join-Path $root 'CHANGELOG.md')
if ($changelog -notmatch "## \[$([regex]::Escape($version))\] - \d{4}-\d{2}-\d{2}") { throw "CHANGELOG.md has no dated entry for $version." }
if ($changelog -match "(?ms)## \[$([regex]::Escape($version))\].*?(?=^## |\z)\b(Planned|Unreleased|TBD|not yet published)\b") { throw "CHANGELOG.md marks $version as not finalized." }
$entry = [regex]::Match($changelog, "(?ms)## \[$([regex]::Escape($version))\].*?(?=^## |\z)").Value
if ($entry -notmatch '(?m)^### Added\s*$') { throw "First release $version must contain an Added section." }
if ($entry -match '(?im)^### (?!Added\s*$)') { throw "First release $version may contain only an Added section." }
Write-Output "Release contract passed for $Tag ($version)."
