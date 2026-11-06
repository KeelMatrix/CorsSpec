[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$ArtifactDirectory,
    [string]$ChangelogPath,
    [switch]$RequireIcon
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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

# Release notes use a deliberately bounded flat Markdown format. Reject constructs
# whose rendering could hide or reinterpret headings and list entries.
$changelogLines = @(Get-Content -LiteralPath $changelogPath)
$headings = [System.Collections.Generic.List[object]]::new()
$structure = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $changelogLines.Count; $index++) {
    $line = $changelogLines[$index]
    if ($line.Contains('<') -or $line.Contains('>') -or $line -match '`{3,}|~{3,}' -or
        (-not [string]::IsNullOrWhiteSpace($line) -and ($line -match '^[ \t]' -or $line -match '^[-*_= \t]+$'))) {
        throw "CHANGELOG.md uses unsupported comments, code blocks, HTML or container structure at line $($index + 1). Use flat headings and release bullets."
    }

    $heading = [regex]::Match($line, '^(?<level>#{1,6})(?:[ \t]+(?<text>.*?)|[ \t]*)$')
    if ($heading.Success) {
        $level = $heading.Groups['level'].Value.Length
        $text = [regex]::Replace($heading.Groups['text'].Value, '[ \t]+#+[ \t]*$', '').Trim(' ', "`t")
        if ($level -eq 1) {
            if ($text -ne 'Changelog' -or $structure.Count -ne 0) {
                throw 'CHANGELOG.md permits only its initial level-one Changelog heading.'
            }
        }
        elseif ($level -eq 2) {
            $release = [regex]::Match($text, '^\[(?<version>Unreleased|[0-9]+\.[0-9]+\.[0-9]+)\](?:[ \t]+-[ \t]+(?<date>\S+))?$')
            if (-not $release.Success) { throw "Unsupported release heading at line $($index + 1)." }
            $headingVersion = $release.Groups['version'].Value
            $headingDate = $release.Groups['date'].Value
            if ($headingVersion -eq 'Unreleased') {
                if ($headingDate.Length -ne 0) { throw 'Unreleased must not have a release date.' }
            }
            else {
                $parsedDate = [DateTime]::MinValue
                if (-not [DateTime]::TryParseExact($headingDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedDate)) {
                    throw "CHANGELOG.md contains an invalid calendar date '$headingDate'."
                }
            }
            $headings.Add([pscustomobject]@{ Version = $headingVersion; Date = $headingDate; Index = $index })
        }
        elseif ($level -ne 3) {
            throw "Unsupported heading depth at line $($index + 1)."
        }
        $structure.Add([pscustomobject]@{ Kind = 'Heading'; Level = $level; Text = $text; Index = $index })
    }
    elseif ($line -match '^ {0,3}#' -or $line -match '^\s*[-=]+\s*$' -or $line -match '^\s*[+*][ \t]') {
        throw "Unsupported Markdown structure at line $($index + 1)."
    }
    elseif ($line -match '^-[ \t]+\S') {
        $structure.Add([pscustomobject]@{ Kind = 'Bullet'; Level = 0; Text = $line; Index = $index })
    }
    elseif (-not [string]::IsNullOrWhiteSpace($line)) {
        $structure.Add([pscustomobject]@{ Kind = 'Text'; Level = 0; Text = $line; Index = $index })
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
if ($entryHeading.Count -ne 1) { throw "CHANGELOG.md must contain exactly one dated entry for $version." }
$entryStart = $entryHeading[0].Index
$entryEnd = $changelogLines.Count
$nextHeading = @($headings | Where-Object { $_.Index -gt $entryStart } | Sort-Object Index | Select-Object -First 1)
if ($nextHeading.Count -ne 0) { $entryEnd = $nextHeading[0].Index }
$entryNodes = @($structure | Where-Object { $_.Index -gt $entryStart -and $_.Index -lt $entryEnd })
$categories = @($entryNodes | Where-Object Kind -eq 'Heading')
if ($categories.Count -ne 1 -or $categories[0].Level -ne 3 -or $categories[0].Text -cne 'Added') {
    throw "First release $version must contain exactly one Added category."
}
if (@($entryNodes | Where-Object { $_.Kind -eq 'Bullet' -and $_.Index -gt $categories[0].Index }).Count -eq 0) {
    throw "First release $version must contain at least one Added entry."
}
$entry = ($entryNodes.Text -join [Environment]::NewLine)
if ($entry -match '(?im)\b(Planned|Unreleased|TBD|not yet published)\b') {
    throw "CHANGELOG.md marks $version as not finalized."
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
