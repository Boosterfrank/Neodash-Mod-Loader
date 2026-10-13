[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $repository 'installer.iss'
$cleanup = Join-Path $PSScriptRoot 'LegacyRuntimeCleanup.iss'
$directories = Join-Path $PSScriptRoot 'LegacyRuntimeDirectories.iss'
$legacyPublishRoots = @(
    (Join-Path $repository 'Neodash Mod Loader/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64'),
    (Join-Path $repository 'Neodash Mod Loader/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64')
)
$expectedCleanupEntryCount = 1086
$expectedCleanupEntryHash = '7DE7203FDC81E80964797C607C70CAAABC7359A89D2634B3A288F89A5A60FE2D'

function Get-RelativeFiles {
    param([Parameter(Mandatory)][string] $Root)

    $resolvedRoot = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).ProviderPath
    @(Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force |
        ForEach-Object { $_.FullName.Substring($resolvedRoot.Length + 1) } |
        Sort-Object)
}

foreach ($path in @($installer, $cleanup, $directories)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Required installer validation input is missing: $path" }
}

$cleanupSource = Get-Content -LiteralPath $cleanup -Raw
$cleanupEntries = @(
    Get-Content -LiteralPath $cleanup |
        ForEach-Object {
            if ($_ -match "^\s*'([^']+)'(?:,|\];)$") { $Matches[1] }
        }
)

if ($cleanupEntries.Count -eq 0) { throw 'The legacy cleanup manifest is empty.' }
if (@($cleanupEntries | Select-Object -Unique).Count -ne $cleanupEntries.Count) { throw 'The legacy cleanup manifest contains duplicate paths.' }
if ($cleanupSource -notmatch 'LegacyRuntimeFileCount = (\d+);') { throw 'The legacy cleanup manifest is missing its count constant.' }
if ([int]$Matches[1] -ne $cleanupEntries.Count) { throw 'The legacy cleanup count constant does not match its entries.' }
if ($cleanupEntries.Count -ne $expectedCleanupEntryCount) { throw "The cleanup manifest must contain $expectedCleanupEntryCount entries." }
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($cleanupEntries -join "`n")))) -ne $expectedCleanupEntryHash) {
    throw 'The cleanup manifest no longer matches the reviewed legacy file inventory.'
}

foreach ($entry in $cleanupEntries) {
    if ([IO.Path]::IsPathRooted($entry) -or $entry.Contains('..') -or $entry.Contains('/') -or $entry.IndexOfAny([char[]]'*?') -ge 0) {
        throw "Legacy cleanup entry is not an explicit relative file path: $entry"
    }
}

foreach ($legacyPublish in $legacyPublishRoots) {
    if (Test-Path -LiteralPath $legacyPublish -PathType Container) {
        $expectedEntries = @(Get-RelativeFiles $legacyPublish | Where-Object { $_ -ne 'Neodash Mod Loader.exe' })
        $difference = @($expectedEntries | Where-Object { $_ -notin $cleanupEntries })
        if ($difference.Count -ne 0) {
            throw "The cleanup manifest omits files from retained legacy publish evidence at ${legacyPublish}:`n$($difference -join [Environment]::NewLine)"
        }
    }
}

$expectedDirectories = @($cleanupEntries |
    ForEach-Object {
        $parts = $_.Split('\')
        for ($depth = 1; $depth -lt $parts.Count; $depth++) {
            [string]::Join('\', $parts[0..($depth - 1)])
        }
    } |
    Sort-Object -Unique)
$directoryEntries = @(
    Get-Content -LiteralPath $directories |
        ForEach-Object {
            if ($_ -match "^\s*'([^']+)'(?:,|\];)$") { $Matches[1] }
        }
)
if ((Get-Content -LiteralPath $directories -Raw) -notmatch 'LegacyRuntimeDirectoryCount = (\d+);') {
    throw 'The linked-directory checks are missing their count constant.'
}
if ([int]$Matches[1] -ne $directoryEntries.Count) {
    throw 'The linked-directory count constant does not match its entries.'
}
if (@(Compare-Object -ReferenceObject $expectedDirectories -DifferenceObject $directoryEntries).Count -ne 0) {
    throw 'The linked-directory checks do not match the cleanup manifest child directories.'
}

$protectedNames = @('settings.json', 'profiles.json', 'operations.log', 'user-data.json')
foreach ($protectedName in $protectedNames) {
    if ($cleanupEntries -contains $protectedName) { throw "The cleanup manifest must not delete user data: $protectedName" }
}

$installerSource = Get-Content -LiteralPath $installer -Raw
foreach ($requiredText in @(
    'Source: "artifacts\publish\Neodash Mod Loader.exe"; DestDir: "{app}";',
    '#include "eng\LegacyRuntimeCleanup.iss"',
    '#include "eng\LegacyRuntimeDirectories.iss"',
    'function PrepareToInstall(var NeedsRestart: Boolean): String;',
    'ValidateLegacyCleanupPath(AppDirectory);',
    'RootPath := AddBackslash(ExtractFileDrive(PathToCheck));',
    'ParentPath := ExtractFileDir(PathToCheck);',
    'if CompareText(PathToCheck, RootPath) = 0 then',
    'ArchitecturesAllowed=x64compatible',
    'VersionInfoVersion={#SetupSetting("AppVersion")}',
    'procedure CurStepChanged(CurStep: TSetupStep);',
    'if CurStep = ssPostInstall then',
    'procedure RemoveLegacyRuntimeFiles();',
    'DeleteFile(LegacyFile)')) {
    if (!$installerSource.Contains($requiredText)) { throw "installer.iss is missing required cleanup wiring: $requiredText" }
}
if ($installerSource.Contains('[InstallDelete]')) { throw 'Legacy cleanup must run after the replacement executable is installed, not through [InstallDelete].' }

Write-Host "Installer cleanup validation passed: $($cleanupEntries.Count) explicit legacy files and $($directoryEntries.Count) checked child directories."
