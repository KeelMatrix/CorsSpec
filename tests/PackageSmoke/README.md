# Package consumer smoke test

This non-packable project references `KeelMatrix.CorsSpec` only through the built package. `scripts/Invoke-PackageSmoke.ps1` restores it against an isolated local package feed and runs one allowed and one denied request against a real ASP.NET Core `TestServer` pipeline.
