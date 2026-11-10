function Assert-WebsiteObject($Element, [string[]]$Names) {
    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Website metadata requires a JSON object.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
        if (-not $seen.Add($property.Name)) { throw "Duplicate website property '$($property.Name)'." }
        if ($null -ne $Names -and $property.Name -cnotin $Names) { throw "Unexpected website property '$($property.Name)'." }
    }
    foreach ($name in $Names) {
        if (-not $seen.Contains($name)) { throw "Missing website property '$name'." }
    }
}

function Get-WebsiteTags([string]$Tags) {
    return @($Tags -split '[;\s]+' | Where-Object { $_.Length -gt 0 })
}

function Assert-WebsitePackage($Package, $Classification) {
    $canonicalUrl = 'https://github.com/KeelMatrix/CorsSpec'
    if ($Package.PackageProjectUrl -cne $canonicalUrl -or $Package.RepositoryUrl -cne $canonicalUrl -or $Package.RepositoryType -cne 'git') {
        throw "Website URLs/repository type for '$($Package.PackageId)' must identify the canonical CorsSpec GitHub repository."
    }
    $tags = @(Get-WebsiteTags $Package.PackageTags)
    $visibility = @($tags | Where-Object { $_ -imatch '^keelmatrix-(public-product|internal-package)$' })
    $roles = @($tags | Where-Object { $_ -imatch '^keelmatrix-(primary|component)$' })
    if ($visibility.Count -ne 1 -or $roles.Count -ne 1 -or
        $visibility[0] -cne "keelmatrix-$($Classification.visibility)" -or
        $roles[0] -cne "keelmatrix-$($Classification.role)") {
        throw "Website sentinels for '$($Package.PackageId)' must occur exactly once, in lowercase, and match the manifest."
    }
}

function Assert-WebsiteMetadata([string]$ManifestJson, [object[]]$Packages) {
    $document = [System.Text.Json.JsonDocument]::Parse($ManifestJson)
    try {
        $manifest = $document.RootElement
        Assert-WebsiteObject $manifest @('schemaVersion', 'packages')
        if ($manifest.GetProperty('schemaVersion').GetRawText() -cne '1') { throw 'Website schemaVersion must be the integer 1.' }
        $entries = $manifest.GetProperty('packages')
        Assert-WebsiteObject $entries $null
        $classifications = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $primaryCount = 0
        $publicCount = 0
        foreach ($entry in $entries.EnumerateObject()) {
            Assert-WebsiteObject $entry.Value @('visibility', 'role')
            $visibility = $entry.Value.GetProperty('visibility').GetString()
            $role = $entry.Value.GetProperty('role').GetString()
            if ($visibility -cnotin @('public-product', 'internal-package') -or $role -cnotin @('primary', 'component')) {
                throw "Invalid website classification for '$($entry.Name)'."
            }
            if ($visibility -ceq 'public-product') { $publicCount++ }
            if ($role -ceq 'primary') { $primaryCount++ }
            $classifications.Add($entry.Name, [pscustomobject]@{ visibility = $visibility; role = $role })
        }
        if ($primaryCount -gt 1 -or ($publicCount -gt 0 -and $primaryCount -ne 1)) { throw 'A public product family requires exactly one primary package.' }
        if ($Packages.Count -eq 0 -or $Packages.Count -ne $classifications.Count) { throw 'Website manifest must name exactly the packable package set.' }
        $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($package in $Packages) {
            if (-not $ids.Add($package.PackageId) -or -not $classifications.ContainsKey($package.PackageId)) {
                throw "Duplicate or undeclared website package ID '$($package.PackageId)'."
            }
            Assert-WebsitePackage $package $classifications[$package.PackageId]
        }
    }
    finally { $document.Dispose() }
}

function Get-WebsiteSourcePackages([string]$RepositoryRoot) {
    # Select all ordinary .NET SDK languages before evaluating packability;
    # neither solution membership nor a source/test/helper location limits it.
    $projects = @(Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Force |
        Where-Object { $_.Extension -in @('.csproj', '.vbproj', '.fsproj') -and
            [IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName) -notmatch '(^|[\\/])(bin|obj|artifacts|\.git)([\\/]|$)' })
    foreach ($project in $projects) {
        $output = & dotnet msbuild $project.FullName -nologo -p:Configuration=Release '-getProperty:IsPackable,PackageId,PackageTags,PackageProjectUrl,RepositoryUrl,RepositoryType'
        if ($LASTEXITCODE -ne 0) { throw "Website metadata evaluation failed for '$($project.Name)'." }
        $metadata = ($output -join "`n" | ConvertFrom-Json).Properties
        if ($metadata.IsPackable -ieq 'true') { $metadata }
    }
}

function Assert-WebsiteArchive([string]$PackagePath, [object[]]$Packages, [string]$ManifestJson) {
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecs = @($archive.Entries | Where-Object { $_.FullName -cmatch '^[^/]+\.nuspec$' })
        if ($nuspecs.Count -ne 1) { throw 'Website package inspection requires exactly one root nuspec.' }
        $reader = [IO.StreamReader]::new($nuspecs[0].Open())
        try { $xmlText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $xmlReader = [Xml.XmlReader]::Create([IO.StringReader]::new($xmlText), $settings)
        $xml = [Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        try { $xml.Load($xmlReader) } finally { $xmlReader.Dispose() }
        $nodes = @($xml.SelectNodes('/*[local-name()="package"]/*[local-name()="metadata"]'))
        if ($nodes.Count -ne 1) { throw 'Website package inspection requires one metadata element.' }
        $metadata = $nodes[0]
        $values = @{}
        foreach ($name in @('id', 'tags', 'projectUrl', 'repository')) {
            $fields = @($metadata.SelectNodes("*[local-name()='$name']"))
            if ($fields.Count -ne 1) { throw "Website nuspec requires exactly one '$name' field." }
            $values[$name] = $fields[0]
        }
        $built = [pscustomobject]@{
            PackageId = $values.id.InnerText
            PackageTags = $values.tags.InnerText
            PackageProjectUrl = $values.projectUrl.InnerText
            RepositoryUrl = $values.repository.GetAttribute('url')
            RepositoryType = $values.repository.GetAttribute('type')
        }
        $source = @($Packages | Where-Object { $_.PackageId -ceq $built.PackageId })
        if ($source.Count -ne 1) { throw "Archive website ID '$($built.PackageId)' does not match a source package." }
        # Reuse the full family check, substituting the archive's metadata for its source.
        $family = @($Packages | Where-Object { $_.PackageId -cne $built.PackageId }) + @($built)
        Assert-WebsiteMetadata $ManifestJson $family
        $sourceTags = @(Get-WebsiteTags $source[0].PackageTags | Sort-Object -CaseSensitive)
        $builtTags = @(Get-WebsiteTags $built.PackageTags | Sort-Object -CaseSensitive)
        if (($sourceTags -join ';') -cne ($builtTags -join ';')) { throw 'Built website/search tags disagree with evaluated source metadata.' }
    }
    finally { $archive.Dispose() }
}
