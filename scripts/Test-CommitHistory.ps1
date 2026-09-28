[CmdletBinding()]
param(
    [string]$RepositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryPath).Path
$separator = [char]0x1f
$messageChecks = @(
    @{ Name = 'authorship trailer'; Pattern = ('(?im)^\s*' + ('Co-' + 'Authored-By') + '\s*:') },
    @{ Name = 'internal task reference'; Pattern = '\bKEE-[0-9]{4,}\b' },
    @{ Name = 'generated attribution'; Pattern = ('(?im)\b' + ('generated' + ' by') + '\b') }
)

$hashes = @(git -C $repository rev-list --all)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate reachable commits in '$repository'." }

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($hash in $hashes) {
    $record = (& git -C $repository show -s --format="%an$separator%ae$separator%cn$separator%ce$separator%B" $hash | Out-String).TrimEnd()
    if ($LASTEXITCODE -ne 0) { throw "Could not read commit '$hash'." }
    $fields = $record -split [Regex]::Escape([string]$separator), 5
    if ($fields.Count -ne 5) { throw "Could not parse commit metadata for '$hash'." }

    $authorName = $fields[0]
    $authorEmail = $fields[1]
    $committerName = $fields[2]
    $committerEmail = $fields[3]
    $message = $fields[4]
    $shortHash = $hash.Substring(0, [Math]::Min(12, $hash.Length))

    $dependabotAuthor = $authorName -in @('dependabot[bot]', 'dependabot')
    if (-not ($authorName -ceq 'KeelMatrix' -or $dependabotAuthor)) {
        [void]$violations.Add("$shortHash author name is '$authorName' (expected KeelMatrix or Dependabot)")
    }

    $allowedCommitter = $committerName -ceq 'KeelMatrix' -or
        ($authorName -ceq 'KeelMatrix' -and $committerName -ceq 'GitHub') -or
        ($dependabotAuthor -and $committerName -in @('dependabot[bot]', 'dependabot', 'GitHub'))
    if (-not $allowedCommitter) {
        [void]$violations.Add("$shortHash committer name is '$committerName' for author '$authorName'")
    }
    if ([string]::IsNullOrWhiteSpace($authorEmail) -or [string]::IsNullOrWhiteSpace($committerEmail)) {
        [void]$violations.Add("$shortHash is missing an author or committer email")
    }

    foreach ($check in $messageChecks) {
        if ($message -match $check.Pattern) { [void]$violations.Add("$shortHash contains $($check.Name)") }
    }
}

if ($violations.Count -ne 0) {
    Write-Error ('Commit history hygiene failed: ' + ($violations -join '; '))
    exit 1
}

Write-Output "Commit history hygiene passed for $($hashes.Count) reachable commit(s)."
