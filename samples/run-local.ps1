$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot\RefreshAhead.MemoryCache.Sample
try {
    dotnet run
}
finally {
    Pop-Location
}
