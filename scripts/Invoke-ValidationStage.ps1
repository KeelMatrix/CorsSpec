function Invoke-ValidationStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )

    Write-Host "=== $Name ==="
    try {
        $ErrorActionPreference = 'Stop'
        $global:LASTEXITCODE = 0
        & $Command
        $exitCode = $LASTEXITCODE
        if ($null -eq $exitCode) {
            $exitCode = 0
        }
        if ($exitCode -ne 0) {
            throw "Stage command exited with code $exitCode."
        }
    }
    catch {
        [void]$Failures.Add($Name)
        Write-Error "Stage '$Name' failed: $($_.Exception.Message)" -ErrorAction Continue
    }
}
