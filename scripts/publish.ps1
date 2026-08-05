[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repositoryRoot 'CodexUsageWidget.slnx'
$project = Join-Path $repositoryRoot 'src\CodexUsageWidget\CodexUsageWidget.csproj'
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $releaseRoot $Runtime))
$archivePath = Join-Path $releaseRoot "codex-usage-widget-$Runtime.zip"

if (-not $publishDirectory.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish directory: $publishDirectory"
}

if ([IO.Directory]::Exists($publishDirectory)) {
    [IO.Directory]::Delete($publishDirectory, $true)
}

[IO.Directory]::CreateDirectory($releaseRoot) | Out-Null

Push-Location $repositoryRoot
try {
    dotnet test $solution -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    dotnet publish $project `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $publishDirectory
    if ([IO.File]::Exists($archivePath)) {
        [IO.File]::Delete($archivePath)
    }

    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath
    Write-Host "Portable release created: $archivePath"
}
finally {
    Pop-Location
}
