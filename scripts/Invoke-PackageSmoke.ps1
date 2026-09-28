[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..' 'artifacts' 'packages')
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$smoke = Join-Path $root 'tests' 'PackageSmoke' 'PackageSmoke.csproj'
$feed = (Get-Item -LiteralPath $PackageDirectory -ErrorAction Stop).FullName
$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("keelmatrix-corsspec-package-smoke-" + [guid]::NewGuid().ToString('N'))
$cache = Join-Path $runRoot 'packages'
$httpCache = Join-Path $runRoot 'http-cache'
$consumerOutput = Join-Path $runRoot 'consumer-output'
$config = Join-Path $runRoot 'NuGet.config'
$consumerBin = Join-Path $root 'tests' 'PackageSmoke' 'bin'
$consumerObj = Join-Path $root 'tests' 'PackageSmoke' 'obj'
$oldPackages = $env:NUGET_PACKAGES
$oldHttpCache = $env:NUGET_HTTP_CACHE_PATH
$exitCode = 1

Remove-Item -LiteralPath $consumerBin, $consumerObj -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $runRoot, $cache, $httpCache, $consumerOutput | Out-Null

try {
    $env:NUGET_PACKAGES = $cache
    $env:NUGET_HTTP_CACHE_PATH = $httpCache

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create($config, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('configuration')
        $writer.WriteStartElement('packageSources')
        $writer.WriteStartElement('clear'); $writer.WriteEndElement()
        $writer.WriteStartElement('add'); $writer.WriteAttributeString('key', 'local-corsspec'); $writer.WriteAttributeString('value', $feed); $writer.WriteEndElement()
        $writer.WriteStartElement('add'); $writer.WriteAttributeString('key', 'nuget.org'); $writer.WriteAttributeString('value', 'https://api.nuget.org/v3/index.json'); $writer.WriteAttributeString('protocolVersion', '3'); $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteStartElement('packageSourceMapping')
        $writer.WriteStartElement('clear'); $writer.WriteEndElement()
        $writer.WriteStartElement('packageSource'); $writer.WriteAttributeString('key', 'local-corsspec')
        $writer.WriteStartElement('package'); $writer.WriteAttributeString('pattern', 'KeelMatrix.CorsSpec'); $writer.WriteEndElement(); $writer.WriteEndElement()
        $writer.WriteStartElement('packageSource'); $writer.WriteAttributeString('key', 'nuget.org')
        $writer.WriteStartElement('package'); $writer.WriteAttributeString('pattern', '*'); $writer.WriteEndElement()
        $writer.WriteStartElement('package'); $writer.WriteAttributeString('pattern', 'Microsoft.AspNetCore.TestHost'); $writer.WriteEndElement(); $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally { $writer.Dispose() }

    dotnet restore $smoke --configfile $config --force-evaluate --no-cache
    if ($LASTEXITCODE -eq 0) {
        dotnet run --project $smoke --configuration Release --no-restore --output $consumerOutput
        $exitCode = $LASTEXITCODE
    }
    else {
        $exitCode = $LASTEXITCODE
    }
}
finally {
    if ($null -eq $oldPackages) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_PACKAGES = $oldPackages
    }

    if ($null -eq $oldHttpCache) {
        Remove-Item Env:NUGET_HTTP_CACHE_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_HTTP_CACHE_PATH = $oldHttpCache
    }

    Remove-Item -LiteralPath $consumerBin, $consumerObj -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue
}

exit $exitCode
