[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$SymbolsPath,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [switch]$RequireIcon,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
    else {
        $dependencies = @($groups[0].dependency)
        if ($dependencies.Count -ne 1 -or $dependencies[0].id -ne 'KeelMatrix.Telemetry' -or $dependencies[0].version -ne '0.1.1' -or $dependencies[0].exclude -ne 'Build,Analyzers') {
            Add-Failure("$Label nuspec has an unexpected dependency graph.")
        }
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

function Read-ArchiveBytes([System.IO.Compression.ZipArchiveEntry]$Entry) {
    $stream = $Entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return $memory.ToArray() }
    finally { $memory.Dispose(); $stream.Dispose() }
}

function Test-BytesEqual([byte[]]$Left, [byte[]]$Right) {
    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

function Test-PngIcon([byte[]]$Bytes, [string]$Label) {
    if ($null -eq $Bytes -or $Bytes.Length -gt 204800 -or $Bytes.Length -lt 33) {
        Add-Failure("$Label must be a non-empty PNG no larger than 200 KB.")
        return
    }

    $signature = [byte[]](137,80,78,71,13,10,26,10)
    if (-not (Test-BytesEqual ([byte[]]$Bytes[0..7]) $signature)) {
        Add-Failure("$Label is not a PNG image.")
        return
    }

    $offset = 8
    $seenHeader = $false
    $seenEnd = $false
    while ($offset + 12 -le $Bytes.Length) {
        $length = [uint32](([uint32]$Bytes[$offset] -shl 24) -bor ([uint32]$Bytes[$offset + 1] -shl 16) -bor ([uint32]$Bytes[$offset + 2] -shl 8) -bor $Bytes[$offset + 3])
        $offset += 4
        if ($length -gt [uint32]($Bytes.Length - $offset - 8)) { Add-Failure("$Label contains a truncated PNG chunk."); return }
        $type = [Text.Encoding]::ASCII.GetString($Bytes[$offset..($offset + 3)])
        $offset += 4
        if ($type -eq 'IHDR') {
            if ($seenHeader -or $length -ne 13) { Add-Failure("$Label has an invalid PNG header chunk."); return }
            $width = [uint32](([uint32]$Bytes[$offset] -shl 24) -bor ([uint32]$Bytes[$offset + 1] -shl 16) -bor ([uint32]$Bytes[$offset + 2] -shl 8) -bor $Bytes[$offset + 3])
            $height = [uint32](([uint32]$Bytes[$offset + 4] -shl 24) -bor ([uint32]$Bytes[$offset + 5] -shl 16) -bor ([uint32]$Bytes[$offset + 6] -shl 8) -bor $Bytes[$offset + 7])
            if ($width -ne 512 -or $height -ne 512) { Add-Failure("$Label must be exactly 512x512 pixels.") }
            $seenHeader = $true
        }
        $offset += [int]$length + 4
        if ($type -eq 'IEND') { $seenEnd = $true; break }
    }
    if (-not $seenHeader -or -not $seenEnd -or $offset -ne $Bytes.Length) { Add-Failure("$Label is not a complete PNG image.") }
}

function Get-SourceLinkPath([string]$DocumentName, [object]$Documents) {
    $normalized = $DocumentName.Replace('\', '/')
    $candidate = $null
    foreach ($property in @($Documents.PSObject.Properties)) {
        $pattern = [string]$property.Name
        $regex = '^' + [regex]::Escape($pattern).Replace('\*', '(.*)') + '$'
        if ($normalized -match $regex -and ($null -eq $candidate -or $pattern.Length -gt $candidate.Pattern.Length)) {
            $candidate = [pscustomobject]@{ Pattern = $pattern; Value = [string]$property.Value; Suffix = $Matches[1] }
        }
    }
    return $candidate
}

function Get-DocumentHash([Guid]$Algorithm, [byte[]]$Bytes) {
    if ($Algorithm -eq [Guid]'8829d00f-11b8-4213-878b-770e8597ac16') { return [Security.Cryptography.SHA256]::HashData($Bytes) }
    if ($Algorithm -eq [Guid]'ff1816ec-aa5e-4d10-87f7-6f4963833460') { return [Security.Cryptography.SHA1]::HashData($Bytes) }
    if ($Algorithm -eq [Guid]'406ea660-64cf-4c82-b6f0-42d48172a799') { return [Security.Cryptography.MD5]::HashData($Bytes) }
    return $null
}

function Get-GitBlobBytes([string]$RepositoryRoot, [string]$Object) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    foreach ($argument in @('-C', $RepositoryRoot, 'cat-file', 'blob', $Object)) { [void]$startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { return $null }
    try {
        $memory = [System.IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($memory)
            $errorText = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { return $null }
            return $memory.ToArray()
        }
        finally { $memory.Dispose() }
    }
    finally { $process.Dispose() }
}

function Get-EmbeddedSourceBytes(
    [System.Reflection.Metadata.MetadataReader]$Reader,
    [System.Reflection.Metadata.EntityHandle]$DocumentHandle) {
    $embeddedSourceKind = [guid]'0e8a571b-6926-466e-b4ad-8ab04611f5fe'
    foreach ($handle in $Reader.GetCustomDebugInformation($DocumentHandle)) {
        $record = $Reader.GetCustomDebugInformation($handle)
        if ($Reader.GetGuid($record.Kind) -ne $embeddedSourceKind) { continue }

        $blob = [byte[]]$Reader.GetBlobBytes($record.Value)
        if ($blob.Length -lt 4) { return $null }
        $length = [BitConverter]::ToInt32($blob, 0)
        $payload = [System.IO.MemoryStream]::new($blob, 4, $blob.Length - 4, $false)
        $output = [System.IO.MemoryStream]::new()
        try {
            if ($length -eq -1 -or $length -eq 0) {
                $payload.CopyTo($output)
            }
            else {
                $deflate = [System.IO.Compression.DeflateStream]::new($payload, [System.IO.Compression.CompressionMode]::Decompress)
                try { $deflate.CopyTo($output) }
                finally { $deflate.Dispose() }
            }

            $bytes = $output.ToArray()
            if ($length -gt 0 -and $bytes.Length -ne $length) { return $null }
            return $bytes
        }
        finally {
            $output.Dispose()
            $payload.Dispose()
        }
    }

    return $null
}

function Test-SourceLinkDocuments(
    [System.Reflection.Metadata.MetadataReader]$Reader,
    [object]$SourceLink,
    [string]$ExpectedCommit,
    [string]$RepositoryRoot) {
    if ($null -eq $SourceLink.documents) { Add-Failure('Symbol archive PDB SourceLink metadata has no documents mapping.'); return }
    foreach ($handle in $Reader.Documents) {
        $document = $Reader.GetDocument($handle)
        $documentName = $Reader.GetString($document.Name)
        $mapping = Get-SourceLinkPath $documentName $SourceLink.documents
        if ($null -eq $mapping -or $mapping.Value -notmatch '^https://raw\.githubusercontent\.com/KeelMatrix/CorsSpec/[0-9a-f]{40}/\*$') {
            Add-Failure("SourceLink does not resolve PDB document '$documentName' through the expected exact-commit mapping.")
            continue
        }

        $relative = $mapping.Suffix.TrimStart('/') -replace '/', [IO.Path]::DirectorySeparatorChar
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') {
            Add-Failure("SourceLink resolved PDB document '$documentName' to an invalid repository path.")
            continue
        }

        if ($relative -match '(^|[\\/])obj([\\/]|$)') {
            $embedded = Get-EmbeddedSourceBytes $Reader $handle
            $expectedHash = [byte[]]$Reader.GetBlobBytes($document.Hash)
            $embeddedHash = if ($null -eq $embedded) { $null } else { Get-DocumentHash ($Reader.GetGuid($document.HashAlgorithm)) $embedded }
            if ($null -eq $embeddedHash -or -not (Test-BytesEqual $expectedHash $embeddedHash)) {
                Add-Failure("Generated PDB document '$documentName' is not backed by matching embedded source.")
            }
            continue
        }

        $sourcePath = Join-Path $RepositoryRoot $relative
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            Add-Failure("SourceLink resolved PDB document '$documentName' to missing source '$relative'.")
            continue
        }

        $gitObject = "$ExpectedCommit`:$($relative -replace '\\','/')"
        $candidateBytes = Get-GitBlobBytes $RepositoryRoot $gitObject
        if ($null -eq $candidateBytes) {
            Add-Failure("SourceLink resolved PDB document '$documentName' to an untracked source '$relative'.")
            continue
        }

        $expectedHash = [byte[]]$Reader.GetBlobBytes($document.Hash)
        $actualHash = Get-DocumentHash ($Reader.GetGuid($document.HashAlgorithm)) $candidateBytes
        if ($null -eq $actualHash -or -not (Test-BytesEqual $expectedHash $actualHash)) {
            Add-Failure("PDB checksum does not match SourceLink-resolved source '$relative'.")
        }
    }
}

function Test-Pdb(
    [System.IO.Compression.ZipArchiveEntry]$Entry,
    [byte[]]$AssemblyBytes,
    [string]$ExpectedCommit,
    [string]$RepositoryRoot) {
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

            if ($null -ne $sourceLink -and $null -ne $sourceLink.documents) {
                Test-SourceLinkDocuments $reader $sourceLink $ExpectedCommit $RepositoryRoot
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
        $duplicateEntries = @($packageArchive.Entries | Group-Object FullName | Where-Object Count -gt 1)
        if ($duplicateEntries.Count -ne 0) { Add-Failure("Package contains duplicate archive entries: $($duplicateEntries.Name -join ', ')") }
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
            $packageIconEntry = @($packageArchive.Entries | Where-Object FullName -eq 'icon.png')
            Test-PngIcon (Read-ArchiveBytes $packageIconEntry[0]) 'Package icon.png'
            $repositoryIcon = Join-Path $RepositoryRoot 'icon.png'
            if (-not (Test-Path -LiteralPath $repositoryIcon -PathType Leaf)) {
                Add-Failure('Package icon.png is present but repository-root icon.png is missing.')
            }
            else {
                $repositoryIconBytes = [IO.File]::ReadAllBytes($repositoryIcon)
                Test-PngIcon $repositoryIconBytes 'Repository icon.png'
                if (-not (Test-BytesEqual (Read-ArchiveBytes $packageIconEntry[0]) $repositoryIconBytes)) {
                    Add-Failure('Package icon.png does not have byte identity with repository-root icon.png.')
                }
            }
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

            $canonicalReadmePath = Join-Path $RepositoryRoot 'src' 'KeelMatrix.CorsSpec' 'README.md'
            $canonicalLicensePath = Join-Path $RepositoryRoot 'LICENSE'
            $packageReadmeEntry = @($packageArchive.Entries | Where-Object FullName -eq 'README.md')
            $packageLicenseEntry = @($packageArchive.Entries | Where-Object FullName -eq 'LICENSE')
            $readmeMatches = $false
            $licenseMatches = $false
            if ((Test-Path -LiteralPath $canonicalReadmePath -PathType Leaf) -and $packageReadmeEntry.Count -eq 1) {
                $readmeMatches = Test-BytesEqual -Left (Read-ArchiveBytes $packageReadmeEntry[0]) -Right ([IO.File]::ReadAllBytes($canonicalReadmePath))
            }
            if ((Test-Path -LiteralPath $canonicalLicensePath -PathType Leaf) -and $packageLicenseEntry.Count -eq 1) {
                $licenseMatches = Test-BytesEqual -Left (Read-ArchiveBytes $packageLicenseEntry[0]) -Right ([IO.File]::ReadAllBytes($canonicalLicensePath))
            }
            if (-not $readmeMatches) {
                Add-Failure('Package README.md does not match the packable project README source.')
            }
            if (-not $licenseMatches) {
                Add-Failure('Package LICENSE does not match the canonical repository license.')
            }
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
        $duplicateEntries = @($symbolArchive.Entries | Group-Object FullName | Where-Object Count -gt 1)
        if ($duplicateEntries.Count -ne 0) { Add-Failure("Symbol archive contains duplicate entries: $($duplicateEntries.Name -join ', ')") }
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
        if ($null -ne $symbolCommit -and $pdb.Count -eq 1 -and $null -ne $packageAssemblyBytes) { Test-Pdb $pdb[0] $packageAssemblyBytes $symbolCommit $RepositoryRoot }

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
