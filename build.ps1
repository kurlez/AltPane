# Build AltPane and publish a self-contained single-file win-x64 executable.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet test tests/AltPane.Tests/AltPane.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish src/AltPane/AltPane.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish
exit $LASTEXITCODE
