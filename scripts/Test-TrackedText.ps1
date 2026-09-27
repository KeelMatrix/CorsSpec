[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$textExtensions = @('.cs', '.csproj', '.md', '.ps1', '.props', '.targets', '.txt', '.yml', '.yaml', '.json', '.xml')
$encodedTerms = @(
    'Zm91bmRlci1vd25lZA==',
    'ZnJvbnRpZXIgcmV2aWV3',
    'cGFwZXJjbGlw',
    'Y29kZXg=',
    'cmV2aWV3LXByb2Nlc3M=',
    'dGFzayBkZWxlZ2F0b3I=',
    'YWdlbnQgaW5zdHJ1Y3Rpb25z',
    'dGFzayBpZA==',
    'aW50ZXJuYWwgcmVsZWFzZQ=='
)
$terms = @($encodedTerms | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) })
$matches = [System.Collections.Generic.List[string]]::new()

@(git -C $root ls-files) | ForEach-Object {
    $relative = $_
    $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
    if ($extension -notin $textExtensions) { return }

    $path = Join-Path $root ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
    $found = Select-String -LiteralPath $path -Pattern $terms -AllMatches
    foreach ($match in @($found)) {
        [void]$matches.Add("${relative}:$($match.LineNumber): $($match.Line.Trim())")
    }
}

if ($matches.Count -ne 0) {
    Write-Error ('Tracked-text hygiene failed: ' + ($matches -join '; '))
    exit 1
}

Write-Output 'Tracked-text hygiene passed.'
