[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\artifacts\packages')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$smoke = Join-Path $root 'tests\PackageSmoke\PackageSmoke.csproj'
$feed = (Resolve-Path $PackageDirectory).Path
$config = Join-Path $feed 'NuGet.config'
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
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project $smoke --configuration Release --no-restore
exit $LASTEXITCODE
