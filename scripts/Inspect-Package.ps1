[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$SymbolsPath,
    [switch]$RequireIcon
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Add-Failure([string]$Message) {
    [void]$failures.Add($Message)
}

function Open-Archive([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure("Missing $Label archive: $Path")
        return $null
    }

    try {
        return [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    }
    catch {
        Add-Failure("$Label archive is not a readable ZIP: $Path")
        return $null
    }
}

function Read-Nuspec([System.IO.Compression.ZipArchive]$Archive, [string]$Label) {
    $entries = @($Archive.Entries | Where-Object { $_.FullName -match '\.nuspec$' })
    if ($entries.Count -ne 1) {
        Add-Failure("$Label archive must contain exactly one nuspec.")
        return $null
    }

    try {
        $reader = [System.IO.StreamReader]::new($entries[0].Open())
        try {
            return [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    catch {
        Add-Failure("$Label archive contains a malformed nuspec.")
        return $null
    }
}

function Test-CommonMetadata([object]$Metadata, [string]$Label, [string]$ExpectedId, [string]$ExpectedVersion, [bool]$IsSymbols) {
    if ($null -eq $Metadata) { return $null }

    if ($Metadata.id -ne $ExpectedId) { Add-Failure("$Label nuspec has unexpected package id '$($Metadata.id)'.") }
    if ($Metadata.version -ne $ExpectedVersion) { Add-Failure("$Label nuspec has version '$($Metadata.version)' instead of '$ExpectedVersion'.") }

    $groups = @($Metadata.dependencies.group)
    if ($groups.Count -ne 1 -or $groups[0].targetFramework -ne 'net8.0') {
        Add-Failure("$Label nuspec must declare exactly one net8.0 dependency group.")
    }

    $repository = $Metadata.repository
    if ($null -eq $repository -or $repository.type -ne 'git' -or $repository.url -ne 'https://github.com/KeelMatrix/CorsSpec' -or $repository.branch -ne 'refs/heads/main' -or $repository.commit -notmatch '^[0-9a-f]{40}$') {
        Add-Failure("$Label nuspec is missing the expected repository provenance metadata.")
    }

    if ($IsSymbols) {
        $packageTypes = @($Metadata.packageTypes.packageType | ForEach-Object { $_.name })
        if ($packageTypes -notcontains 'SymbolsPackage') {
            Add-Failure('Symbol archive nuspec is missing packageType SymbolsPackage.')
        }
    }

    return $repository.commit
}

function Test-Pdb([System.IO.Compression.ZipArchiveEntry]$Entry, [string]$ExpectedCommit) {
    if ($null -eq $Entry -or $Entry.Length -eq 0) {
        Add-Failure('Symbol archive is missing a non-empty portable PDB.')
        return
    }

    $stream = $Entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        $bytes = $memory.ToArray()
        $read = $bytes.Length
        if ($read -lt 4 -or $bytes[0] -ne 0x42 -or $bytes[1] -ne 0x53 -or $bytes[2] -ne 0x4a -or $bytes[3] -ne 0x42) {
            Add-Failure('Symbol archive PDB is not a portable PDB.')
        }

        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        $sourceLink = "https://raw.githubusercontent.com/KeelMatrix/CorsSpec/$ExpectedCommit/*"
        if (-not $text.Contains($sourceLink, [System.StringComparison]::Ordinal)) {
            Add-Failure('Symbol archive PDB is missing the expected SourceLink provenance.')
        }
    }
    finally {
        $memory.Dispose()
        $stream.Dispose()
    }
}

$packageName = Split-Path -Leaf $PackagePath
$packageMatch = [regex]::Match($packageName, '^(?<id>KeelMatrix\.CorsSpec)\.(?<version>\d+\.\d+\.\d+)\.nupkg$')
if (-not $packageMatch.Success) {
    Add-Failure("Package filename '$packageName' does not match the expected package identity format.")
    $expectedId = 'KeelMatrix.CorsSpec'
    $expectedVersion = $null
}
else {
    $expectedId = $packageMatch.Groups['id'].Value
    $expectedVersion = $packageMatch.Groups['version'].Value
}

$symbolName = Split-Path -Leaf $SymbolsPath
$symbolMatch = [regex]::Match($symbolName, '^(?<id>KeelMatrix\.CorsSpec)\.(?<version>\d+\.\d+\.\d+)\.snupkg$')
if (-not $symbolMatch.Success) {
    Add-Failure("Symbol filename '$symbolName' does not match the expected symbol identity format.")
}
elseif ($symbolMatch.Groups['id'].Value -ne $expectedId -or $symbolMatch.Groups['version'].Value -ne $expectedVersion) {
    Add-Failure('Package and symbol filenames do not declare the same id and version.')
}

$packageArchive = Open-Archive $PackagePath 'package'
$symbolArchive = Open-Archive $SymbolsPath 'symbol'
$packageCommit = $null
$symbolCommit = $null

if ($null -ne $packageArchive) {
    try {
        $packageNames = @($packageArchive.Entries | ForEach-Object FullName)
        $required = @('README.md', 'LICENSE', "$expectedId.nuspec", 'lib/net8.0/KeelMatrix.CorsSpec.dll', 'lib/net8.0/KeelMatrix.CorsSpec.xml')
        foreach ($name in $required) {
            if ($packageNames -notcontains $name) { Add-Failure("Package is missing required entry: $name") }
        }

        $packageTfms = @($packageNames | Where-Object { $_ -match '^lib/([^/]+)/' } | ForEach-Object { $Matches[1] } | Sort-Object -Unique)
        if ($packageTfms.Count -ne 1 -or $packageTfms[0] -ne 'net8.0') { Add-Failure('Package must contain exactly the net8.0 target framework payload.') }

        $iconPresent = $packageNames -contains 'icon.png'
        if ($iconPresent) {
            Write-Output 'ICON_GATE: package-root icon.png is present; package metadata will be checked.'
        }
        else {
            Write-Warning 'ICON_GATE_PENDING: required repository-root icon.png is absent and was not packed.'
            if ($RequireIcon) { Add-Failure('Missing required package-root icon.png') }
        }

        $nuspec = Read-Nuspec $packageArchive 'Package'
        if ($null -ne $nuspec) {
            $packageCommit = Test-CommonMetadata $nuspec.package.metadata 'Package' $expectedId $expectedVersion $false
            $metadata = $nuspec.package.metadata
            if ($metadata.license.'#text' -ne 'MIT') { Add-Failure('Package license expression is not MIT.') }
            if ($metadata.readme -ne 'README.md') { Add-Failure('Package README metadata is not README.md.') }
            if ($iconPresent -and $metadata.icon -ne 'icon.png') { Add-Failure('Package icon metadata is not icon.png.') }
            if (-not $iconPresent -and $metadata.icon) { Add-Failure('Package declares icon metadata without a package-root icon.png.') }
        }

        $allowed = @('_rels/.rels', '[Content_Types].xml', 'README.md', 'LICENSE', "$expectedId.nuspec", 'lib/net8.0/KeelMatrix.CorsSpec.dll', 'lib/net8.0/KeelMatrix.CorsSpec.xml')
        if ($iconPresent) { $allowed += 'icon.png' }
        $unexpected = @($packageNames | Where-Object { $_ -notmatch '/$' -and $_ -notin $allowed -and $_ -notmatch '^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$' })
        if ($unexpected.Count -ne 0) { Add-Failure("Package contains unintended entries: $($unexpected -join ', ')") }
    }
    finally { $packageArchive.Dispose() }
}

if ($null -ne $symbolArchive) {
    try {
        $symbolNames = @($symbolArchive.Entries | ForEach-Object FullName)
        $expectedPdb = 'lib/net8.0/KeelMatrix.CorsSpec.pdb'
        foreach ($name in @('_rels/.rels', '[Content_Types].xml', "$expectedId.nuspec", $expectedPdb)) {
            if ($symbolNames -notcontains $name) { Add-Failure("Symbol archive is missing required entry: $name") }
        }

        $symbolTfms = @($symbolNames | Where-Object { $_ -match '^lib/([^/]+)/' } | ForEach-Object { $Matches[1] } | Sort-Object -Unique)
        if ($symbolTfms.Count -ne 1 -or $symbolTfms[0] -ne 'net8.0') { Add-Failure('Symbol archive must contain exactly the net8.0 target framework payload.') }

        $symbolNuspec = Read-Nuspec $symbolArchive 'Symbol'
        if ($null -ne $symbolNuspec) { $symbolCommit = Test-CommonMetadata $symbolNuspec.package.metadata 'Symbol' $expectedId $expectedVersion $true }
        if ($null -ne $packageCommit -and $null -ne $symbolCommit -and $packageCommit -ne $symbolCommit) { Add-Failure('Package and symbol archives do not declare the same repository commit.') }

        $pdb = $symbolArchive.Entries | Where-Object FullName -eq $expectedPdb | Select-Object -First 1
        if ($null -ne $symbolCommit) { Test-Pdb $pdb $symbolCommit }

        $allowedSymbols = @('_rels/.rels', '[Content_Types].xml', "$expectedId.nuspec", $expectedPdb)
        $unexpectedSymbols = @($symbolNames | Where-Object { $_ -notmatch '/$' -and $_ -notin $allowedSymbols -and $_ -notmatch '^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$' })
        if ($unexpectedSymbols.Count -ne 0) { Add-Failure("Symbol archive contains unintended entries: $($unexpectedSymbols -join ', ')") }
    }
    finally { $symbolArchive.Dispose() }
}

if ($failures.Count -ne 0) {
    Write-Error ('PACKAGE_GATE_FAILED: ' + ($failures -join '; '))
    exit 1
}

Write-Output 'Package and symbol archive inspection passed.'
