[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-ValidationStage.ps1')

$failures = [System.Collections.Generic.List[string]]::new()

Invoke-ValidationStage -Name 'Successful command' -Failures $failures -Command {
    pwsh -NoProfile -Command 'exit 0'
}
if ($failures.Count -ne 0) {
    throw "A successful command was recorded as failed: $($failures -join ', ')."
}

Invoke-ValidationStage -Name 'Missing command' -Failures $failures -Command {
    Invoke-ValidationCommandThatDoesNotExist
} 2>$null
if ('Missing command' -notin $failures) {
    throw 'A command-not-found error was not recorded as a failed stage.'
}

Invoke-ValidationStage -Name 'Non-zero command' -Failures $failures -Command {
    pwsh -NoProfile -Command 'exit 7'
} 2>$null
if ('Non-zero command' -notin $failures) {
    throw 'A non-zero child-process exit was not recorded as a failed stage.'
}

Write-Host 'Validation stage regression checks passed.'
