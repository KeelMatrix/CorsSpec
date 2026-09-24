[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $root 'src\KeelMatrix.CorsSpec\KeelMatrix.CorsSpec.csproj'
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = Join-Path $root 'CHANGELOG.md'

if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$') {
    throw "Malformed release tag '$Tag'. Expected vX.Y.Z."
}

$version = $Matches.version
$project = [xml](Get-Content -Raw -LiteralPath $projectPath)
$packageId = $project.Project.PropertyGroup | Where-Object { $_.PackageId } | Select-Object -First 1 -ExpandProperty PackageId
if ($packageId -ne 'KeelMatrix.CorsSpec') {
    throw "Unexpected package id '$packageId'. Expected 'KeelMatrix.CorsSpec'."
}

$props = [xml](Get-Content -Raw -LiteralPath $propsPath)
$declaredVersions = @($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -ExpandProperty Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($declaredVersions.Count -ne 1 -or $declaredVersions[0] -ne $version) {
    throw "Release tag '$Tag' does not match the repository package version '$($declaredVersions -join ', ')'."
}

$changelog = Get-Content -Raw -LiteralPath $changelogPath
if ($changelog -notmatch '(?m)^## \[Unreleased\]\s*$') {
    throw 'CHANGELOG.md must contain a separate Unreleased section.'
}

$escapedVersion = [regex]::Escape($version)
$entryMatches = [regex]::Matches($changelog, "(?ms)^## \[$escapedVersion\] - (?<date>\d{4}-\d{2}-\d{2})\s*$.*?(?=^## |\z)")
if ($entryMatches.Count -eq 0) {
    throw "CHANGELOG.md has no dated entry for $version."
}
if ($entryMatches.Count -ne 1) {
    throw "CHANGELOG.md contains $($entryMatches.Count) entries for $version; exactly one is required."
}

$entry = $entryMatches[0].Value
if ($entry -match '(?im)\b(Planned|Unreleased|TBD|not yet published)\b') {
    throw "CHANGELOG.md marks $version as not finalized."
}
if ($entry -notmatch '(?m)^### Added\s*$') {
    throw "First release $version must contain an Added section."
}
if ($entry -match '(?im)^###\s+(?!Added\s*$)') {
    throw "First release $version may contain only an Added section."
}
$addedSection = [regex]::Match($entry, '(?ms)^### Added\s*$.*?(?=^### |\z)').Value
if ($addedSection -notmatch '(?m)^-\s+\S') {
    throw "First release $version must contain at least one Added entry."
}

$unpublishedTransitionMarkers = '(?im)(?<![A-Za-z])(now|no longer|previously|formerly|used to|fixed|fixes|corrected|resolved|addressed|this removes|this fixes|changed from)(?![A-Za-z])'
if ($entry -match $unpublishedTransitionMarkers) {
    throw "First release $version contains unpublished-transition wording '$($Matches[1])'."
}

if (-not [string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
    $expectedArtifacts = @(
        "$packageId.$version.nupkg",
        "$packageId.$version.snupkg"
    ) | Sort-Object
    $actualArtifacts = @(Get-ChildItem -LiteralPath $artifactRoot -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') } | Select-Object -ExpandProperty Name | Sort-Object)
    if ((Compare-Object -ReferenceObject $expectedArtifacts -DifferenceObject $actualArtifacts) -ne $null) {
        throw "Unexpected release artifact set. Expected: $($expectedArtifacts -join ', '). Actual: $($actualArtifacts -join ', ')."
    }
}

Write-Output "Release contract passed for $Tag ($version)."
