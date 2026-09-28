[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$textExtensions = @('.cs', '.csproj', '.md', '.ps1', '.props', '.targets', '.txt', '.yml', '.yaml', '.json', '.xml')
$patterns = @(
    '\bKEE-[0-9]{4,}\b',
    ('(?im)^\s*' + ('Co-' + 'Authored-By') + '\s*:'),
    ('(?im)\b' + ('generated' + ' by') + '\b')
)
$matches = [System.Collections.Generic.List[string]]::new()

function Get-TrackedPaths([string]$RepositoryRoot) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    foreach ($argument in @('-C', $RepositoryRoot, 'ls-files', '-z', '--')) { [void]$startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Could not start git tracked-file enumeration.' }
    try {
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "git tracked-file enumeration failed: $errorText" }
        return $output.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
    }
    finally { $process.Dispose() }
}

foreach ($relative in Get-TrackedPaths $root) {
    $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
    if ($extension -notin $textExtensions) { continue }

    $path = Join-Path $root ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Tracked file is missing from the checkout: $relative" }
    $content = [IO.File]::ReadAllText($path)
    foreach ($pattern in $patterns) {
        $match = [regex]::Match($content, $pattern)
        if ($match.Success) {
            $lineNumber = 1 + ([regex]::Matches($content.Substring(0, $match.Index), "`r?`n").Count)
            [void]$matches.Add("${relative}:$($lineNumber): $($match.Value.Trim())")
        }
    }
}

if ($matches.Count -ne 0) {
    Write-Error ('Tracked-text hygiene failed: ' + ($matches -join '; '))
    exit 1
}

Write-Output 'Tracked-text hygiene passed.'
