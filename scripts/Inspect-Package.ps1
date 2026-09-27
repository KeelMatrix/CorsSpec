[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$SymbolsPath,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [switch]$RequireIcon
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Reflection.Metadata

function Add-Failure([string]$Message) {
    [void]$failures.Add($Message)
}

if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
    Add-Failure("Expected repository commit '$ExpectedCommit' is not a 40-character hexadecimal SHA.")
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

function Test-Pdb(
    [System.IO.Compression.ZipArchiveEntry]$Entry,
    [byte[]]$AssemblyBytes,
    [string]$ExpectedCommit) {
    if ($null -eq $Entry -or $Entry.Length -eq 0) {
        Add-Failure('Symbol archive is missing a non-empty portable PDB.')
        return
    }

    if ($null -eq $AssemblyBytes -or $AssemblyBytes.Length -eq 0) {
        Add-Failure('Package is missing a non-empty assembly for PDB identity validation.')
        return
    }

    $pdbStream = $Entry.Open()
    $pdbMemory = [System.IO.MemoryStream]::new()
    $assemblyStream = [System.IO.MemoryStream]::new($AssemblyBytes, $false)
    $provider = $null
    $peReader = $null
    try {
        $pdbStream.CopyTo($pdbMemory)
        $pdbMemory.Position = 0
        try {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbMemory)
            $reader = $provider.GetMetadataReader()
        }
        catch {
            Add-Failure('Symbol archive PDB is not a structurally valid portable PDB.')
            return
        }

        $pdbId = [byte[]]$reader.DebugMetadataHeader.Id
        if ($pdbId.Length -ne 20) {
            Add-Failure('Symbol archive PDB has an invalid portable PDB identity length.')
        }

        try {
            $peReader = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
            $codeViewEntries = @($peReader.ReadDebugDirectory() | Where-Object { $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView -and $_.IsPortableCodeView })
            if ($codeViewEntries.Count -ne 1) {
                Add-Failure('Package assembly must contain exactly one portable CodeView PDB identity.')
            }
            else {
                $codeViewEntry = $codeViewEntries[0]
                $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViewEntry)
                $expectedPdbId = [byte[]]::new(20)
                $codeView.Guid.ToByteArray().CopyTo($expectedPdbId, 0)
                [BitConverter]::GetBytes([uint32]$codeViewEntry.Stamp).CopyTo($expectedPdbId, 16)
                if ([Convert]::ToHexString($pdbId) -ne [Convert]::ToHexString($expectedPdbId)) {
                    Add-Failure('Symbol archive PDB identity does not match the packaged assembly.')
                }
            }
        }
        catch {
            Add-Failure('Package assembly does not contain readable portable PDB identity metadata.')
        }

        $sourceLinkKind = [guid]'cc110556-a091-4d38-9fec-25ab9a351a6a'
        $sourceLinkRecords = [System.Collections.Generic.List[object]]::new()
        foreach ($handle in $reader.GetCustomDebugInformation([System.Reflection.Metadata.EntityHandle]::ModuleDefinition)) {
            $customDebugInformation = $reader.GetCustomDebugInformation($handle)
            if ($reader.GetGuid($customDebugInformation.Kind) -eq $sourceLinkKind) {
                [void]$sourceLinkRecords.Add($customDebugInformation)
            }
        }
        if ($sourceLinkRecords.Count -ne 1) {
            Add-Failure('Symbol archive PDB must contain exactly one SourceLink custom debug record.')
        }
        else {
            $sourceLinkText = [System.Text.Encoding]::UTF8.GetString($reader.GetBlobBytes($sourceLinkRecords[0].Value))
            try {
                $sourceLink = $sourceLinkText | ConvertFrom-Json -ErrorAction Stop
                $documentProperties = @($sourceLink.documents.PSObject.Properties)
                $expectedSourceLink = "https://raw.githubusercontent.com/KeelMatrix/CorsSpec/$ExpectedCommit/*"
                if ($null -eq $sourceLink.documents -or $documentProperties.Count -eq 0) {
                    Add-Failure('Symbol archive PDB SourceLink metadata has no documents mapping.')
                }
                else {
                    foreach ($property in $documentProperties) {
                        if ([string]::IsNullOrWhiteSpace($property.Name) -or $property.Value -isnot [string] -or $property.Value -cne $expectedSourceLink) {
                            Add-Failure('Symbol archive PDB SourceLink metadata has an unexpected commit mapping.')
                        }
                    }
                }
            }
            catch {
                Add-Failure('Symbol archive PDB SourceLink metadata is not valid JSON.')
            }
        }
    }
    finally {
        if ($null -ne $peReader) { $peReader.Dispose() }
        if ($null -ne $provider) { $provider.Dispose() }
        $assemblyStream.Dispose()
        $pdbMemory.Dispose()
        $pdbStream.Dispose()
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
$packageAssemblyBytes = $null

if ($null -ne $packageArchive) {
    try {
        $packageNames = @($packageArchive.Entries | ForEach-Object FullName)
        $required = @('README.md', 'LICENSE', "$expectedId.nuspec", 'lib/net8.0/KeelMatrix.CorsSpec.dll', 'lib/net8.0/KeelMatrix.CorsSpec.xml')
        foreach ($name in $required) {
            $count = @($packageArchive.Entries | Where-Object FullName -eq $name).Count
            if ($count -ne 1) { Add-Failure("Package must contain exactly one required entry: $name") }
        }
        $assemblyEntry = @($packageArchive.Entries | Where-Object FullName -eq 'lib/net8.0/KeelMatrix.CorsSpec.dll')
        if ($assemblyEntry.Count -eq 1) {
            $assemblyStream = $assemblyEntry[0].Open()
            $assemblyMemory = [System.IO.MemoryStream]::new()
            try {
                $assemblyStream.CopyTo($assemblyMemory)
                $packageAssemblyBytes = $assemblyMemory.ToArray()
            }
            finally {
                $assemblyMemory.Dispose()
                $assemblyStream.Dispose()
            }
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
            if ($packageCommit -ne $ExpectedCommit) { Add-Failure("Package repository commit '$packageCommit' does not match expected candidate '$ExpectedCommit'.") }
            $metadata = $nuspec.package.metadata
            if ($metadata.license.'#text' -ne 'MIT') { Add-Failure('Package license expression is not MIT.') }
            if ($metadata.readme -ne 'README.md') { Add-Failure('Package README metadata is not README.md.') }
            if ($iconPresent -and $metadata.icon -ne 'icon.png') { Add-Failure('Package icon metadata is not icon.png.') }
            if (-not $iconPresent -and $metadata.icon) { Add-Failure('Package declares icon metadata without a package-root icon.png.') }
        }

        $allowed = @('_rels/.rels', '[Content_Types].xml', 'README.md', 'LICENSE', "$expectedId.nuspec", 'lib/net8.0/KeelMatrix.CorsSpec.dll', 'lib/net8.0/KeelMatrix.CorsSpec.xml')
        if ($iconPresent) { $allowed += 'icon.png' }
        $unexpected = @($packageNames | Where-Object { $_ -notmatch '/$' -and $_ -notin $allowed -and $_ -notmatch '^package/services/metadata/core-properties/(?:[0-9a-f]{32}|nuget)\.psmdcp$' })
        if ($unexpected.Count -ne 0) { Add-Failure("Package contains unintended entries: $($unexpected -join ', ')") }
    }
    finally { $packageArchive.Dispose() }
}

if ($null -ne $symbolArchive) {
    try {
        $symbolNames = @($symbolArchive.Entries | ForEach-Object FullName)
        $expectedPdb = 'lib/net8.0/KeelMatrix.CorsSpec.pdb'
        foreach ($name in @('_rels/.rels', '[Content_Types].xml', "$expectedId.nuspec", $expectedPdb)) {
            $count = @($symbolArchive.Entries | Where-Object FullName -eq $name).Count
            if ($count -ne 1) { Add-Failure("Symbol archive must contain exactly one required entry: $name") }
        }

        $symbolTfms = @($symbolNames | Where-Object { $_ -match '^lib/([^/]+)/' } | ForEach-Object { $Matches[1] } | Sort-Object -Unique)
        if ($symbolTfms.Count -ne 1 -or $symbolTfms[0] -ne 'net8.0') { Add-Failure('Symbol archive must contain exactly the net8.0 target framework payload.') }

        $symbolNuspec = Read-Nuspec $symbolArchive 'Symbol'
        if ($null -ne $symbolNuspec) {
            $symbolCommit = Test-CommonMetadata $symbolNuspec.package.metadata 'Symbol' $expectedId $expectedVersion $true
            if ($symbolCommit -ne $ExpectedCommit) { Add-Failure("Symbol repository commit '$symbolCommit' does not match expected candidate '$ExpectedCommit'.") }
        }
        if ($null -ne $packageCommit -and $null -ne $symbolCommit -and $packageCommit -ne $symbolCommit) { Add-Failure('Package and symbol archives do not declare the same repository commit.') }

        $pdb = @($symbolArchive.Entries | Where-Object FullName -eq $expectedPdb)
        if ($null -ne $symbolCommit -and $pdb.Count -eq 1 -and $null -ne $packageAssemblyBytes) { Test-Pdb $pdb[0] $packageAssemblyBytes $symbolCommit }

        $allowedSymbols = @('_rels/.rels', '[Content_Types].xml', "$expectedId.nuspec", $expectedPdb)
        $unexpectedSymbols = @($symbolNames | Where-Object { $_ -notmatch '/$' -and $_ -notin $allowedSymbols -and $_ -notmatch '^package/services/metadata/core-properties/(?:[0-9a-f]{32}|nuget)\.psmdcp$' })
        if ($unexpectedSymbols.Count -ne 0) { Add-Failure("Symbol archive contains unintended entries: $($unexpectedSymbols -join ', ')") }
    }
    finally { $symbolArchive.Dispose() }
}

if ($failures.Count -ne 0) {
    Write-Error ('PACKAGE_GATE_FAILED: ' + ($failures -join '; '))
    exit 1
}

Write-Output 'Package and symbol archive inspection passed.'
