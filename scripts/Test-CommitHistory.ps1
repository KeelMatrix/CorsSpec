[CmdletBinding()]
param(
    [string]$RepositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$separator = [char]0x1f
$checks = @(
    @{ Name = 'issue identifier'; Pattern = '(?<![A-Za-z0-9])[A-Z][A-Z0-9]{1,9}-[0-9]+(?![A-Za-z0-9])' },
    @{ Name = 'authorship trailer'; Pattern = '(?im)^\s*Co-Authored-By\s*:' },
    @{ Name = 'release-process wording'; Pattern = '(?i)\b(?:frontier|review|convergence|remediation|whole-candidate|release-blocking|release\s+candidate|release\s+gate|acceptance\s+confirmation)\b' },
    @{ Name = 'family label'; Pattern = '\b[A-Z][0-9]+(?:\s*[-–]\s*[A-Z]?[0-9]+)?\b' }
)
$encodedNames = @(
    'cGFwZXJjbGlw',
    'Y29kZXg=',
    'bHVuYQ==',
    'c29s',
    'Zmxhc2g=',
    'ZGVlcHNlZWs=',
    'Y2xhdWRl',
    'Z3B0'
)
$nameAlternatives = @(
    $encodedNames | ForEach-Object {
        [Regex]::Escape([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)))
    }
)
$checks += @{ Name = 'agent or model name'; Pattern = '(?i)\b(?:' + ($nameAlternatives -join '|') + ')\b' }

$hashes = @(git -C $repository rev-list --all)
if ($LASTEXITCODE -ne 0) {
    throw "Could not enumerate reachable commits in '$repository'."
}

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($hash in $hashes) {
    $record = (& git -C $repository show -s --format="%an$separator%ae$separator%cn$separator%ce$separator%B" $hash | Out-String).TrimEnd()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read commit '$hash'."
    }

    $fields = $record -split [Regex]::Escape([string]$separator), 5
    if ($fields.Count -ne 5) {
        throw "Could not parse commit metadata for '$hash'."
    }

    $authorName = $fields[0]
    $authorEmail = $fields[1]
    $committerName = $fields[2]
    $committerEmail = $fields[3]
    $message = $fields[4]
    $shortHash = $hash.Substring(0, [Math]::Min(12, $hash.Length))

    if ($authorName -cne 'KeelMatrix') {
        [void]$violations.Add("$shortHash author name is '$authorName' (expected KeelMatrix)")
    }
    if ($committerName -cne 'KeelMatrix') {
        [void]$violations.Add("$shortHash committer name is '$committerName' (expected KeelMatrix)")
    }
    if ([string]::IsNullOrWhiteSpace($authorEmail) -or [string]::IsNullOrWhiteSpace($committerEmail)) {
        [void]$violations.Add("$shortHash is missing an author or committer email")
    }

    foreach ($check in $checks) {
        if ($message -match $check.Pattern) {
            [void]$violations.Add("$shortHash contains $($check.Name)")
        }
    }
}

if ($violations.Count -ne 0) {
    Write-Error ('Commit history hygiene failed: ' + ($violations -join '; '))
    exit 1
}

Write-Output "Commit history hygiene passed for $($hashes.Count) reachable commit(s)."
