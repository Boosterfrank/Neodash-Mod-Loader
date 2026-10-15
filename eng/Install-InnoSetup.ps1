#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Destination
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$version = '6.7.3'
$installerUri = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$installerSha256 = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repository "artifacts/tools/innosetup-$version"
}

$Destination = [IO.Path]::GetFullPath($Destination)

function Assert-SafeDestination {
    param([string] $Path)

    $root = [IO.Path]::GetPathRoot($Path)
    if ([string]::Equals($Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Inno Setup destination cannot be a filesystem root: $Path"
    }

    $ancestor = $Path
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
                throw "Inno Setup destination cannot traverse a linked directory: $ancestor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($ancestor)
        if ($parent -eq $ancestor) { break }
        $ancestor = $parent
    }

    if (!(Test-Path -LiteralPath $Path)) { return }
    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Inno Setup destination must be a directory: $Path"
    }

    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($child in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $item = Get-Item -LiteralPath $child -Force
            if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
                throw "Inno Setup destination contains a linked item: $child"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}

function Wait-InstallProcess {
    param([Diagnostics.Process] $Process, [int] $TimeoutSeconds)

    if (!$Process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $Process.Kill($true)
            if (!$Process.WaitForExit(5000)) {
                throw 'The installer process did not exit after termination was requested.'
            }
        }
        catch {
            throw "Inno Setup installation timed out and its process tree could not be stopped (PID $($Process.Id)): $_"
        }
        throw "Inno Setup installation timed out after $TimeoutSeconds seconds and its process tree was stopped."
    }
    $Process.Refresh()
}

Assert-SafeDestination $Destination
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Assert-SafeDestination $Destination

$installerPath = Join-Path $Destination ".innosetup-$version.exe"
if (Test-Path -LiteralPath $installerPath) {
    throw "Refusing to overwrite an existing installer download: $installerPath"
}

Write-Host "Downloading Inno Setup $version from the official release."
Invoke-WebRequest -Uri $installerUri -OutFile $installerPath
$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
if (![string]::Equals($actualHash, $installerSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Inno Setup installer SHA-256 did not match the pinned release hash. Expected $installerSha256, got $actualHash."
}
$installerVersion = (Get-Item -LiteralPath $installerPath).VersionInfo.ProductVersion.Trim()
if ($installerVersion -ne $version) {
    throw "Unexpected Inno Setup installer version '$installerVersion'; expected $version."
}

Write-Host "Installing Inno Setup $version in portable mode at $Destination."
$process = Start-Process -FilePath $installerPath -ArgumentList @(
    '/PORTABLE=1',
    '/CURRENTUSER',
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/NOICONS',
    "/DIR=`"$Destination`""
) -WindowStyle Hidden -PassThru
try {
    Wait-InstallProcess $process 300
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup installation failed with exit code $($process.ExitCode)."
    }
}
finally {
    $process.Dispose()
}

Assert-SafeDestination $Destination
$compilerPath = Join-Path $Destination 'ISCC.exe'
if (!(Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw "Inno Setup portable installation did not produce ISCC.exe: $compilerPath"
}

$compilerPath = (Resolve-Path -LiteralPath $compilerPath).ProviderPath
$compilerHelp = @(& $compilerPath '/?' 2>&1)
if (($compilerHelp | Out-String) -notmatch 'Inno Setup 6 Command-Line Compiler') {
    throw "Portable installation did not produce a working Inno Setup 6 compiler: $compilerPath"
}

# ISCC.exe intentionally has a 0.0.0.0 file-version resource. The installer
# resource provides the patch version, while its pinned SHA-256 binds ISCC.exe
# to that exact 6.7.3 release.

Remove-Item -LiteralPath $installerPath -Force
Write-Output $compilerPath
# ISCC /? returns 1 even when it prints valid help. Report this script's own
# successful result so GitHub's PowerShell wrapper does not inherit that code.
exit 0
