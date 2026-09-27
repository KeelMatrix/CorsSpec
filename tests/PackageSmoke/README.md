# Package consumer smoke test

This non-packable project references `KeelMatrix.CorsSpec` only through the built package. `scripts/Invoke-PackageSmoke.ps1` restores it against an isolated local package feed and disposable NuGet cache/output directories, then runs simple allowed/denied requests plus an allowed custom-method/requested-header preflight flow and a denied preflight that sends no actual request against a real ASP.NET Core `TestServer` pipeline.
