[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\artifacts\packages')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$smoke = Join-Path $root 'tests\PackageSmoke\PackageSmoke.csproj'
$feed = (Resolve-Path $PackageDirectory).Path
$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("keelmatrix-corsspec-package-smoke-" + [guid]::NewGuid().ToString('N'))
$cache = Join-Path $runRoot 'packages'
$httpCache = Join-Path $runRoot 'http-cache'
$consumerOutput = Join-Path $runRoot 'consumer-output'
$config = Join-Path $runRoot 'NuGet.config'
$consumerBin = Join-Path $root 'tests\PackageSmoke\bin'
$consumerObj = Join-Path $root 'tests\PackageSmoke\obj'
$oldPackages = $env:NUGET_PACKAGES
$oldHttpCache = $env:NUGET_HTTP_CACHE_PATH
$exitCode = 1

Remove-Item -LiteralPath $consumerBin, $consumerObj -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $runRoot, $cache, $httpCache, $consumerOutput | Out-Null

try {
    $env:NUGET_PACKAGES = $cache
    $env:NUGET_HTTP_CACHE_PATH = $httpCache

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-corsspec" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local-corsspec">
      <package pattern="KeelMatrix.CorsSpec" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
      <package pattern="Microsoft.AspNetCore.TestHost" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8

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
