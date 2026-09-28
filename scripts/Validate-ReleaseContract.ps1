[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$ArtifactDirectory,
    [string]$ChangelogPath,
    [switch]$RequireIcon
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $root 'src' 'KeelMatrix.CorsSpec' 'KeelMatrix.CorsSpec.csproj'
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    Join-Path $root 'CHANGELOG.md'
}
else {
    (Resolve-Path -LiteralPath $ChangelogPath).Path
}
$repositoryCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $repositoryCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the checked-out repository commit for release artifact provenance validation.'
}
$expectedCommit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { $repositoryCommit } else { $env:GITHUB_SHA.Trim() }
if ($expectedCommit -notmatch '^[0-9a-fA-F]{40}$' -or $expectedCommit -ine $repositoryCommit) {
    throw "Expected candidate commit '$expectedCommit' does not match checked-out HEAD '$repositoryCommit'."
}

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

$changelogLines = @(Get-Content -LiteralPath $changelogPath)
$headings = [System.Collections.Generic.List[object]]::new()
$inFence = $false
for ($index = 0; $index -lt $changelogLines.Count; $index++) {
    $line = $changelogLines[$index]
    if ($line.TrimStart().StartsWith('```', [StringComparison]::Ordinal)) {
        $inFence = -not $inFence
        continue
    }
    if ($inFence) { continue }
    if ($line -match '^## \[(?<headingVersion>[^\]]+)\](?:\s+-\s+(?<date>\S+))?\s*$') {
        $headingVersion = $Matches.headingVersion
        $headingDate = $Matches.date
        if ($headingVersion -eq 'Unreleased') {
            $headings.Add([pscustomobject]@{ Version = $headingVersion; Date = $null; Index = $index })
            continue
        }

        if ($headingVersion -notmatch '^\d+\.\d+\.\d+$' -or [string]::IsNullOrWhiteSpace($headingDate)) {
            throw "CHANGELOG.md contains a release heading with an invalid version or missing date: '$line'."
        }
        $parsedDate = [DateTime]::MinValue
        if (-not [DateTime]::TryParseExact($headingDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedDate)) {
            throw "CHANGELOG.md contains an invalid calendar date '$headingDate' in '$line'."
        }
        $headings.Add([pscustomobject]@{ Version = $headingVersion; Date = $headingDate; Index = $index })
    }
}

if (@($headings | Where-Object Version -eq 'Unreleased').Count -ne 1) {
    throw 'CHANGELOG.md must contain exactly one separate Unreleased section.'
}

$versionHeadings = @($headings | Where-Object Version -ne 'Unreleased')
$duplicateVersions = @($versionHeadings | Group-Object Version | Where-Object Count -ne 1)
if ($duplicateVersions.Count -ne 0) {
    throw "CHANGELOG.md contains duplicate release sections: $($duplicateVersions.Name -join ', ')."
}

$entryHeading = @($versionHeadings | Where-Object Version -eq $version)
if ($entryHeading.Count -ne 1) {
    throw "CHANGELOG.md must contain exactly one dated entry for $version."
}
$entryStart = $entryHeading[0].Index
$entryEnd = $changelogLines.Count
$nextHeading = @($headings | Where-Object { $_.Index -gt $entryStart } | Sort-Object Index | Select-Object -First 1)
if ($nextHeading.Count -ne 0) { $entryEnd = $nextHeading[0].Index }
$entry = ($changelogLines[$entryStart..($entryEnd - 1)] -join [Environment]::NewLine)
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

    $inspectionScript = Join-Path $PSScriptRoot 'Inspect-Package.ps1'
    Invoke-NestedPwsh -NoProfile -File $inspectionScript `
        -PackagePath (Join-Path $artifactRoot "$packageId.$version.nupkg") `
        -SymbolsPath (Join-Path $artifactRoot "$packageId.$version.snupkg") `
        -ExpectedCommit $expectedCommit `
        -RequireIcon:$RequireIcon
    if ($LASTEXITCODE -ne 0) {
        throw 'Release artifacts failed content and identity inspection.'
    }
}

Write-Output "Release contract passed for $Tag ($version)."
