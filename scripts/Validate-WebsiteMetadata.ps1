[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WebsiteMetadata.ps1')
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$manifest = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot 'keelmatrix.website.json')
$packages = @(Get-WebsiteSourcePackages $RepositoryRoot)
Assert-WebsiteMetadata $manifest $packages
if ($packages.Count -ne 1 -or $packages[0].PackageId -cne 'KeelMatrix.CorsSpec') {
    throw 'CorsSpec must declare exactly its shipping KeelMatrix.CorsSpec package.'
}
Assert-WebsitePackage $packages[0] ([pscustomobject]@{ visibility = 'public-product'; role = 'primary' })
if (-not [string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $archives = @(Get-ChildItem -LiteralPath $PackageDirectory -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') })
    if ($archives.Count -ne 2 -or @($archives | Where-Object Extension -eq '.nupkg').Count -ne 1 -or
        @($archives | Where-Object Extension -eq '.snupkg').Count -ne 1) {
        throw 'Website metadata requires the package and symbol archive pair.'
    }
    foreach ($archive in $archives) { Assert-WebsiteArchive $archive.FullName $packages $manifest }
}
Write-Host 'Website metadata passed.'
