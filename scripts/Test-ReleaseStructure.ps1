[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('corsspec-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$validator = Join-Path $PSScriptRoot 'Validate-ReleaseContract.ps1'
$valid = "# Changelog`n`n## [Unreleased]`n`n## [0.1.0] - 2026-11-06`n`n### Added`n`n- Verifies CORS contracts.`n"
$cases = [System.Collections.Generic.List[object]]::new()
function Add-Case([string]$Name, [string]$Text, [bool]$Pass) { $cases.Add([pscustomobject]@{ Name = $Name; Text = $Text; Pass = $Pass }) }
Add-Case 'ordinary finalized entry' $valid $true
Add-Case 'normal release date edit' ($valid.Replace('2026-11-06', '2024-02-29')) $true
Add-Case 'closing heading hashes' ($valid.Replace('## [0.1.0] - 2026-11-06', '## [0.1.0] - 2026-11-06 ##').Replace('### Added', '### Added ###')) $true
Add-Case 'HTTP heading separators' ($valid.Replace('## [0.1.0]', "##`t[0.1.0]").Replace('### Added', "###`tAdded")) $true
Add-Case 'invalid calendar date' ($valid.Replace('2026-11-06', '2026-02-30')) $false
Add-Case 'wrong version' ($valid.Replace('[0.1.0]', '[0.2.0]')) $false
Add-Case 'missing date' ($valid.Replace(' - 2026-11-06', '')) $false
Add-Case 'nested heading container' ($valid.Replace('## [0.1.0]', '  ## [0.1.0]')) $false
Add-Case 'HTML category container' ($valid.Replace('### Added', '<div>`n### Added')) $false
Add-Case 'thematic break only content' ($valid.Replace('- Verifies CORS contracts.', '- - -')) $false
Add-Case 'whole entry comment' ("# Changelog`n## [Unreleased]`n<!--`n" + $valid.Substring($valid.IndexOf('## [0.1.0]')) + "-->`n") $false
Add-Case 'category comment' ($valid.Replace("### Added`n`n- Verifies CORS contracts.", "<!--`n### Added`n- Example.`n-->")) $false
Add-Case 'inline comment bullet' ($valid.Replace('- Verifies CORS contracts.', '- <!-- Example -->')) $false
Add-Case 'unclosed comment' ($valid + '<!--') $false
foreach ($marker in @('```', '````', '~~~', '~~~~~')) {
    foreach ($indent in @('', '   ', '    ', "`t")) {
        $example = $indent + $marker + "markdown`n### Added`n- Example.`n" + $indent + $marker
        Add-Case "code-only category $marker indent $($indent.Length)" ($valid.Replace("### Added`n`n- Verifies CORS contracts.", $example)) $false
    }
    Add-Case "whole entry fence $marker" ("# Changelog`n## [Unreleased]`n$marker`n" + $valid + "`n$marker") $false
    Add-Case "short/unclosed fence $marker" ($valid + "`n$marker`n## [0.1.0] - 2026-11-07`n~~") $false
}
foreach ($suffix in @('', ' ##', ' ###')) {
    Add-Case "duplicate version with closing hashes '$suffix'" ($valid + "`n## [0.1.0] - 2026-11-07$suffix`n### Added`n- Duplicate.`n") $false
}
Add-Case 'indented category example' ($valid.Replace("### Added`n`n- Verifies CORS contracts.", "    ### Added`n    - Example.")) $false
Add-Case 'indented bullet example' ($valid.Replace('- Verifies CORS contracts.', '    - Example.')) $false
Add-Case 'blockquote example' ($valid.Replace('- Verifies CORS contracts.', '> - Example.')) $false
Add-Case 'setext duplicate' ($valid + "`n[0.1.0] - 2026-11-07`n----------------------`n") $false
Add-Case 'unknown section boundary' ($valid.Replace('- Verifies CORS contracts.', "## Other`n### Added`n- Example.")) $false
Add-Case 'other category' ($valid.Replace('### Added', '### Changed')) $false
Add-Case 'duplicate category' ($valid + "`n### Added`n- Duplicate category.") $false
Add-Case 'empty category' ($valid.Replace('- Verifies CORS contracts.', 'Example text without an entry.')) $false

try {
    $path = Join-Path $fixture 'CHANGELOG.md'
    foreach ($case in $cases) {
        [IO.File]::WriteAllText($path, $case.Text)
        Invoke-NestedPwsh -NoProfile -File $validator -Tag v0.1.0 -ChangelogPath $path *> $null
        if (($LASTEXITCODE -eq 0) -ne $case.Pass) { throw "Release structure result differs for '$($case.Name)'." }
    }

    # Exercise normal stage failure propagation with the real validator.
    [IO.File]::WriteAllText($path, $valid + "`n<!-- hidden -->")
    $failures = [System.Collections.Generic.List[string]]::new()
    Invoke-ValidationStage -Name 'Invalid release structure' -Failures $failures -Command {
        Invoke-NestedPwsh -NoProfile -File $validator -Tag v0.1.0 -ChangelogPath $path
    } *> $null
    if ('Invalid release structure' -notin $failures) { throw 'Validation lost the release validator failure.' }

    # Execute the actual workflow's release-version shell block with a native
    # command failure. It must exit before publishing a version output.
    $workflow = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../.github/workflows/release.yml')
    $block = [regex]::Match($workflow, '(?ms)      - name: Validate release tag and changelog contract.*?        run: \|\r?\n(?<body>.*?)(?=\r?\n      - name:)').Groups['body'].Value
    if ([string]::IsNullOrWhiteSpace($block)) { throw 'Release-version workflow block was not found.' }
    $body = ($block -split '\r?\n' | ForEach-Object { $_ -replace '^          ', '' }) -join "`n"
    $probe = Join-Path $fixture 'workflow-probe.ps1'
    $output = Join-Path $fixture 'version-output'
    $savedOutput = $env:GITHUB_OUTPUT
    $savedTag = $env:RELEASE_TAG
    try {
        $env:GITHUB_OUTPUT = $output
        $env:RELEASE_TAG = 'v0.1.0'
        foreach ($exitCode in @(7, 0)) {
            [IO.File]::WriteAllText($probe, "function pwsh { `$global:LASTEXITCODE = $exitCode }`n" + $body)
            Invoke-NestedPwsh -NoProfile -File $probe *> $null
            if ($LASTEXITCODE -ne $exitCode) { throw 'Release workflow did not propagate the validator exit.' }
            if ($exitCode -ne 0 -and (Test-Path -LiteralPath $output)) { throw 'Release workflow wrote version output after failure.' }
        }
        if ((Get-Content -Raw -LiteralPath $output).Trim() -ne 'version=0.1.0') { throw 'Valid release workflow output was lost.' }
    }
    finally { $env:GITHUB_OUTPUT = $savedOutput; $env:RELEASE_TAG = $savedTag }
    Write-Output "Release structure passed: $($cases.Count) cases plus normal validation and release workflow failure propagation."
}
finally {
    if ([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
