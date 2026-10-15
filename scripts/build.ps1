#Requires -Version 7.2
<#
.SYNOPSIS
Builds the Windows x64 app; Release publishes the self-contained single EXE.
.PARAMETER Debug
Build Debug instead of Release. Both -Debug and --debug are supported.
.EXAMPLE
./scripts/build.ps1
.EXAMPLE
./scripts/build.ps1 --debug
#>
param(
    [switch] $Debug
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

# PowerShell scripts use a single dash for parameters; also accept the requested CLI spelling.
foreach ($argument in $args) {
    if ($argument -eq '--debug') { $Debug = $true }
    else { throw "Unknown argument '$argument'. Usage: ./scripts/build.ps1 [--debug]" }
}

$configuration = if ($Debug) { 'Debug' } else { 'Release' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logs = Join-Path $repository 'artifacts/logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$binlog = Join-Path $logs ("build-$($configuration.ToLowerInvariant())-{}.binlog")

Push-Location $repository
try {
    Write-Host "Building $configuration (Windows x64)…"
    & dotnet build 'Neodash.ModLoader.slnx' -c $configuration -p:Platform=x64 -p:TreatWarningsAsErrors=true "-bl:$binlog"
    if ($LASTEXITCODE -ne 0) { throw "The $configuration build failed (exit $LASTEXITCODE). See $logs." }

    if ($Debug) {
        Write-Host ('Debug EXE: ' + (Join-Path $repository 'src/Neodash.ModLoader/bin/x64/Debug/net10.0-windows/win-x64/Neodash Mod Loader.exe'))
    }
    else {
        Write-Host ('Release EXE: ' + (Join-Path $repository 'artifacts/publish/Neodash Mod Loader.exe'))
    }
}
finally { Pop-Location }
