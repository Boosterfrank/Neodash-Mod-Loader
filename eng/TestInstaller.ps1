#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $CompilerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if (!$CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $CompilerPath = $command.Source }
    else {
        foreach ($candidate in @(
            "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
            "$env:ProgramFiles/Inno Setup 7/ISCC.exe",
            "${env:ProgramFiles(x86)}/Inno Setup 7/ISCC.exe")) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $CompilerPath = $candidate; break }
        }
    }
}
if (!$CompilerPath) { throw 'Inno Setup compiler not found. Pass -CompilerPath with the path to ISCC.exe.' }
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).ProviderPath
& (Join-Path $PSScriptRoot 'VerifyInstaller.ps1')

$root = Join-Path $repository ('artifacts/installer-verification/run-' + [Guid]::NewGuid().ToString('N'))
$logs = Join-Path $root 'logs'
$build = Join-Path $repository 'artifacts/installer'
foreach ($target in @($root, $build)) {
    $ancestor = $target
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Installer verification output cannot traverse a linked directory: $ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}
New-Item -ItemType Directory -Path $root, $logs, $build -Force | Out-Null
$payload = Join-Path $repository 'artifacts/publish/Neodash Mod Loader.exe'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$source = Get-Content -LiteralPath (Join-Path $repository 'installer.iss') -Raw
$results = [Collections.Generic.List[string]]::new()
$script:MayUnlinkFixtures = $true

function Assert-Check {
    param([bool] $Condition, [string] $Message)
    if (!$Condition) { throw $Message }
}

function New-FixtureFile {
    param([string] $Path, [string] $Text = 'legacy runtime fixture')
    $absolute = [IO.Path]::GetFullPath($Path)
    Assert-Check ($absolute.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Fixture path escaped the isolated run directory.'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($absolute)) | Out-Null
    [IO.File]::WriteAllText($absolute, $Text)
}

function Invoke-Compile {
    param([string] $Script, [string] $Output, [string] $Name, [string] $LogName)
    $log = Join-Path $logs $LogName
    $process = Start-Process -FilePath $CompilerPath -ArgumentList @('/Qp', "/O`"$Output`"", "/F`"$Name`"", "`"$Script`"") -RedirectStandardOutput $log -RedirectStandardError "$log.error.log" -WindowStyle Hidden -PassThru
    try {
        Wait-VerificationProcess $process "compile $Name" 180
        if ($process.ExitCode -ne 0) {
            Get-Content -LiteralPath $log, "$log.error.log" | Write-Host
            throw "Inno Setup compilation failed: $Script (exit $($process.ExitCode))."
        }
    }
    finally { $process.Dispose() }
}

function Wait-VerificationProcess {
    param([Diagnostics.Process] $Process, [string] $Name, [int] $TimeoutSeconds = 60)
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$Process.WaitForExit(1000)) {
        if ($elapsed.Elapsed.TotalSeconds -lt $TimeoutSeconds) { continue }
        try {
            $Process.Kill($true)
            if (!$Process.WaitForExit(5000)) { throw 'Process did not terminate.' }
        }
        catch {
            $script:MayUnlinkFixtures = $false
            throw "Could not stop verification process tree for $Name (PID $($Process.Id)). Fixture links are retained: $_"
        }
        throw "Verification process timed out and its process tree was stopped: $Name."
    }
    $Process.Refresh()
}

function Get-Section {
    param([string] $Name)
    $match = [regex]::Match($source, "(?ms)^\[$Name\]\r?\n(.*?)(?=^\[|\z)")
    Assert-Check $match.Success "Missing installer section: $Name"
    $match.Groups[1].Value
}

function Invoke-SetupCheck {
    param([string] $Directory, [string] $Name, [bool] $ShouldSucceed = $true, [string] $Executable = $testInstaller)
    Assert-Check ([IO.Path]::GetFullPath($Directory).StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Install target escaped the isolated run directory.'
    $log = Join-Path $logs "$Name.log"
    $process = Start-Process -FilePath $Executable -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', "/DIR=`"$Directory`"", "/LOG=`"$log`"") -WindowStyle Hidden -PassThru
    try {
        Wait-VerificationProcess $process $Name
        Assert-Check (Test-Path -LiteralPath $log) "Installer did not create a log: $Name"
        Assert-Check (($process.ExitCode -eq 0) -eq $ShouldSucceed) "Unexpected installer exit $($process.ExitCode) for $Name. See $log."
    }
    finally { $process.Dispose() }
    $log
}

function Assert-Payload {
    param([string] $Directory)
    $installed = Join-Path $Directory 'Neodash Mod Loader.exe'
    Assert-Check ((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -eq $payloadHash) 'Installed executable differs from the verified publish payload.'
}

function Get-Uninstaller {
    param([string] $Directory)
    $executables = @(Get-ChildItem -LiteralPath $Directory -File -Filter 'unins*.exe')
    $records = @(Get-ChildItem -LiteralPath $Directory -File -Filter 'unins*.dat')
    Assert-Check ($executables.Count -eq 1 -and $records.Count -eq 1) 'Upgrade must retain exactly one uninstall executable and record pair.'
    Assert-Check ($executables[0].BaseName -eq $records[0].BaseName) 'Uninstall executable and record names differ.'
    $executables[0].FullName
}

function Complete-Check {
    param([string] $Name)
    $results.Add($Name)
    Write-Host "Passed: $Name"
}

# Compile the shipping script unchanged. The runtime variant below reuses its exact
# files and Pascal code, but does not register, launch, or close any user application.
Invoke-Compile (Join-Path $repository 'installer.iss') $build 'ModLoaderSetup' 'compile-production.log'
$productionInstaller = Join-Path $build 'ModLoaderSetup.exe'
$appVersion = [regex]::Match($source, '(?m)^AppVersion=(.+)$').Groups[1].Value.Trim()
$versionInfo = (Get-Item -LiteralPath $productionInstaller).VersionInfo
Assert-Check ($versionInfo.ProductVersion.Trim() -eq $appVersion -and $versionInfo.FileVersion.Trim() -eq $appVersion) 'Installer metadata version differs from AppVersion.'
Complete-Check 'Production installer compilation and version metadata'

$sections = @([regex]::Matches($source, '(?m)^\[(\w+)\]') | ForEach-Object { $_.Groups[1].Value })
Assert-Check (@($sections | Where-Object { $_ -notin @('Setup', 'Tasks', 'Files', 'Icons', 'Run', 'Code') }).Count -eq 0) 'Review the runtime harness before testing an installer with new sections.'
$setup = (Get-Section 'Setup') -replace '(?m)^CloseApplications=.+$', 'CloseApplications=no'
$setup += @"
SourceDir=$repository
AppId=Neodash Mod Loader Installer Verification $([Guid]::NewGuid().ToString('N'))
PrivilegesRequired=lowest
CreateUninstallRegKey=no
RestartApplications=no
UsePreviousAppDir=no
UsePreviousGroup=no
UsePreviousTasks=no
"@
$code = Get-Section 'Code'
$testCode = $code.Replace('#include "eng\', '#include "' + $repository + '\eng\')
$testSource = "[Setup]`n$setup`n[Files]`n$(Get-Section 'Files')`n[Code]`n$testCode"
Assert-Check ($testCode.Replace($repository + '\eng\', 'eng\') -eq $code) 'The isolated installer must use unchanged production Pascal code.'
$scriptPath = Join-Path $root 'isolated-installer.iss'
[IO.File]::WriteAllText($scriptPath, $testSource)
Invoke-Compile $scriptPath $root 'IsolatedSetup' 'compile-isolated.log'
$testInstaller = Join-Path $root 'IsolatedSetup.exe'

$fresh = Join-Path $root 'fresh'
$null = Invoke-SetupCheck $fresh 'fresh-install'
Assert-Payload $fresh
Assert-Check (Test-Path -LiteralPath (Join-Path $fresh 'unins000.exe')) 'Fresh install did not create an uninstaller.'
Complete-Check 'Fresh install contains the exact published EXE and an uninstaller'

$entries = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LegacyRuntimeCleanup.iss') | ForEach-Object {
    if ($_ -match "^\s*'([^']+)'(?:,|\];)$") { $Matches[1] }
})
$upgrade = Join-Path $root 'upgrade'
# Build a minimal 1.3 fixture package with the same isolated AppId. This gives the
# upgrade real legacy uninstall records, instead of only loose pre-existing files.
$legacyPayload = Join-Path $root 'legacy-payload'
foreach ($entry in $entries) { New-FixtureFile (Join-Path $legacyPayload $entry) }
New-FixtureFile (Join-Path $legacyPayload 'Neodash Mod Loader.exe') 'previous application executable'
$legacySetup = $setup -replace '(?m)^AppVersion=.+$', 'AppVersion=1.3.0.0'
$legacySource = "[Setup]`n$legacySetup`n[Files]`nSource: `"$legacyPayload\*`"; DestDir: `"{app}`"; Flags: ignoreversion recursesubdirs createallsubdirs`n"
$legacyScript = Join-Path $root 'legacy-fixture.iss'
[IO.File]::WriteAllText($legacyScript, $legacySource)
Invoke-Compile $legacyScript $root 'LegacyFixtureSetup' 'compile-legacy-fixture.log'
$null = Invoke-SetupCheck $upgrade 'legacy-fixture-install' -Executable (Join-Path $root 'LegacyFixtureSetup.exe')
Assert-Check (Test-Path -LiteralPath (Join-Path $upgrade 'unins000.dat')) 'Legacy fixture did not create uninstall records.'
foreach ($entry in $entries) { Assert-Check (Test-Path -LiteralPath (Join-Path $upgrade $entry)) "Legacy fixture did not install $entry." }
Complete-Check 'Legacy 1.3 fixture installs runtime files and real uninstall records'
$protected = @(
    'settings.json',
    'profiles.json',
    'operations.log',
    'user-data.json',
    'unknown-plugin.dll',
    'Mods\RunwayMP\dlls\main.dll',
    'custom\UE4SS.dll',
    'sv-SE\user-language-file.txt',
    'publish\sv-SE\user-language-file.txt'
)
foreach ($entry in $protected) { New-FixtureFile (Join-Path $upgrade $entry) ('user data: ' + $entry) }
$null = Invoke-SetupCheck $upgrade 'legacy-upgrade'
Assert-Payload $upgrade
foreach ($entry in $entries) { Assert-Check (!(Test-Path -LiteralPath (Join-Path $upgrade $entry))) "Legacy runtime file survived upgrade: $entry" }
foreach ($entry in $protected) { Assert-Check ([IO.File]::ReadAllText((Join-Path $upgrade $entry)) -eq ('user data: ' + $entry)) "User file changed during upgrade: $entry" }
$legacyDirectories = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LegacyRuntimeDirectories.iss') | ForEach-Object {
    if ($_ -match "^\s*'([^']+)'(?:,|\];)$") { $Matches[1] }
})
foreach ($directory in $legacyDirectories) {
    $containsUserData = @($protected | Where-Object { $_.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    if ($containsUserData) {
        Assert-Check (Test-Path -LiteralPath (Join-Path $upgrade $directory) -PathType Container) "Installer removed a legacy directory containing user data: $directory"
    }
    else {
        Assert-Check (!(Test-Path -LiteralPath (Join-Path $upgrade $directory))) "Installer left an empty legacy runtime directory: $directory"
    }
}
$null = Get-Uninstaller $upgrade
Complete-Check 'Upgrade removes legacy files and empty runtime directories while preserving user data and language folders with user files'
$null = Invoke-SetupCheck $upgrade 'reinstall'
Assert-Payload $upgrade
$uninstaller = Get-Uninstaller $upgrade
Complete-Check 'Reinstall succeeds without leftover legacy runtime files'

$blocked = Join-Path $root 'blocked-upgrade'
$blockedExe = Join-Path $blocked 'Neodash Mod Loader.exe'
$blockedLegacy = Join-Path $blocked 'coreclr.dll'
New-FixtureFile $blockedExe 'existing executable is locked'
New-FixtureFile $blockedLegacy
[IO.Directory]::CreateDirectory((Join-Path $blocked 'sv-SE')) | Out-Null
$handle = [IO.File]::Open($blockedExe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try { $null = Invoke-SetupCheck $blocked 'blocked-exe-upgrade' $false }
finally { $handle.Dispose() }
Assert-Check ([IO.File]::ReadAllText($blockedExe) -eq 'existing executable is locked') 'A failed install damaged the previous executable.'
Assert-Check ([IO.File]::ReadAllText($blockedLegacy) -eq 'legacy runtime fixture') 'Legacy cleanup ran before the replacement executable was installed.'
Assert-Check (Test-Path -LiteralPath (Join-Path $blocked 'sv-SE') -PathType Container) 'Legacy directory cleanup ran before the replacement executable was installed.'
Complete-Check 'Failed EXE replacement leaves the old executable, runtime files, and legacy directories intact'

$locked = Join-Path $root 'locked-legacy'
$lockedFile = Join-Path $locked 'coreclr.dll'
New-FixtureFile $lockedFile
$handle = [IO.File]::Open($lockedFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try { $lockedLog = Invoke-SetupCheck $locked 'locked-legacy-upgrade' }
finally { $handle.Dispose() }
Assert-Payload $locked
Assert-Check ([IO.File]::ReadAllText($lockedFile) -eq 'legacy runtime fixture') 'The locked legacy fixture was unexpectedly changed.'
Assert-Check ((Get-Content -LiteralPath $lockedLog -Raw).Contains('Could not remove legacy runtime file:')) 'Failed legacy deletion was not logged.'
Complete-Check 'Locked legacy DLL is logged without failing the successful EXE install'

$outside = Join-Path $root 'protected-target'
New-FixtureFile (Join-Path $outside 'coreclr.dll') 'protected junction target'
foreach ($scenario in @('app-root', 'ancestor', 'runtime-child')) {
    $case = Join-Path $root "junction-$scenario"
    [IO.Directory]::CreateDirectory($case) | Out-Null
    if ($scenario -eq 'runtime-child') {
        $directory = Join-Path $case 'app'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $link = Join-Path $directory 'Assets'
    }
    else {
        $link = Join-Path $case 'linked'
        $directory = if ($scenario -eq 'ancestor') { Join-Path $link 'app' } else { $link }
    }
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    try {
        $log = Invoke-SetupCheck $directory "reject-junction-$scenario" $false
        Assert-Check ((Get-Content -LiteralPath $log -Raw).Contains('linked directory:')) "Junction rejection did not run for $scenario."
        Assert-Check ([IO.File]::ReadAllText((Join-Path $outside 'coreclr.dll')) -eq 'protected junction target') 'Installer modified a junction target.'
        Assert-Check (!(Test-Path -LiteralPath (Join-Path $outside 'Neodash Mod Loader.exe'))) 'Installer copied its EXE through a junction.'
        Complete-Check "Rejects junction in $scenario before installing or deleting files"
    }
    finally {
        # Remove the link itself, without recursion or traversing its target.
        if ($script:MayUnlinkFixtures) {
            Assert-Check ((Get-Item -LiteralPath $link).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) 'Refusing to unlink a fixture that is no longer a junction.'
            Remove-Item -LiteralPath $link -Force
        }
    }
}

$uninstallLog = Join-Path $logs 'uninstall.log'
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$uninstallLog`"") -WindowStyle Hidden -PassThru
try {
    Wait-VerificationProcess $process 'uninstall'
    Assert-Check ($process.ExitCode -eq 0) "Uninstall failed with exit $($process.ExitCode)."
}
finally { $process.Dispose() }
Assert-Check (!(Test-Path -LiteralPath (Join-Path $upgrade 'Neodash Mod Loader.exe'))) 'Uninstall left the installed app executable.'
foreach ($entry in $protected) { Assert-Check ([IO.File]::ReadAllText((Join-Path $upgrade $entry)) -eq ('user data: ' + $entry)) "Uninstall changed a user file: $entry" }
# Windows uninstaller self-cleanup can finish just after the launching process exits.
$cleanupWait = [Diagnostics.Stopwatch]::StartNew()
do {
    $records = @(Get-ChildItem -LiteralPath $upgrade -File | Where-Object { $_.Name -match '^unins.*\.(exe|dat)$' })
    if ($records.Count -eq 0) { break }
    Start-Sleep -Milliseconds 100
} while ($cleanupWait.Elapsed.TotalSeconds -lt 5)
Assert-Check ($records.Count -eq 0) 'Uninstall left an uninstall executable or record.'
foreach ($directory in $legacyDirectories) {
    $containsUserData = @($protected | Where-Object { $_.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    if (!$containsUserData) {
        Assert-Check (!(Test-Path -LiteralPath (Join-Path $upgrade $directory))) "Upgraded uninstall records did not remove an empty legacy directory: $directory"
    }
}
Complete-Check 'Uninstall after legacy upgrade and reinstall removes the app while preserving user files'

[ordered]@{
    installer = $productionInstaller
    version = $appVersion
    installerSha256 = (Get-FileHash -LiteralPath $productionInstaller -Algorithm SHA256).Hash
    payloadSha256 = $payloadHash
    checks = $results.ToArray()
    runtimeVariant = 'Same production Pascal code and EXE payload; per-user scratch directory, no uninstall registry, shortcuts, app launch, or application closing.'
    limitations = 'Production admin elevation, Start menu/desktop shortcuts, Restart Manager, and interactive UI were not exercised.'
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
Write-Host "Installer verification passed: $($results.Count) checks. Results: $root"
