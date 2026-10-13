# Regression tests and CI

Run the same checks as GitHub Actions from a Windows PowerShell terminal with the .NET 10 SDK:

```powershell
./eng/Verify.ps1
```

This restores and rebuilds the x64 solution with warnings treated as errors, runs the regression suite, publishes the self-contained app, and fails if the publish folder contains anything except `Neodash Mod Loader.exe`. Logs and the executable are written under `artifacts/`.

To run only the tests:

```powershell
dotnet test --project tests/Neodash.ModLoader.Integration/Neodash.ModLoader.Integration.csproj -c Release
```

The xUnit v3 tests run through the native Microsoft Testing Platform runner and each scenario is individually discoverable. They link the actual installer, mod operations, settings, profiles, catalog, version, path, and mod-management source files. Test substitutes keep storage and process checks deterministic. Every fact executes its core fixture in a filtered fresh test host, so static settings/profile state cannot leak between random test order or individual filtered runs. Filesystem fixtures stay under `tests/.scratch/`, which the runner validates before deleting; it never installs into your real game or uses your real settings.

`IntegrationTests.cs` lists the discoverable facts. Scenario implementations are split into `IntegrationTests.*.cs` files by feature, `IntegrationTestHost.cs` handles process isolation, and `TestFixtures.cs` contains shared filesystem and assertion helpers.

`eng/Verify.ps1` restores the pinned Coverlet console tool from `.config/dotnet-tools.json`, instruments the test executable before starting `dotnet test`, and collects core-source coverage across its isolated child hosts. The Cobertura report, detailed JSON, and summary are saved under `artifacts/coverage/latest/`. Tests, substitutes, and generated code are excluded. UI, game-process enumeration, and other production services not linked into the test executable are outside this measured scope; this is not whole-app coverage. An empty report fails validation.

The tests cover install/update/uninstall/restore, configuration and disabled-state preservation, archive and path validation, transaction rollback, profiles, legacy settings JSON, catalog validation, and version ordering. They also pin down cross-package ownership rejection before files change, catalog ownership isolation, stale-backup ownership rejection, partial-uninstall backup invalidation, cancellation before live-file replacement, a game starting between snapshot creation and install/restore/uninstall mutation, installation-specific process matching, invalid nested settings and semantic profile-data preservation, profile reconciliation of missing and disabled mods including package-folder renames, profile-bound mod-state rollback, mods.txt encoding/newline behavior for snapshot restore plus normal enable/uninstall changes, offline UE4SS archive install/rollback/uninstall preservation, and byte-preserving configuration edits (UTF-8 with and without BOM, UTF-16, UTF-32, legacy ANSI, external-edit detection, and rollback).

The runner uses a temporary NTFS junction to prove that linked game folders are rejected. On a restricted shell, run the command with the permission needed to create junctions; the scenario intentionally fails rather than being skipped. The tests do not launch WinUI, download real packages, or test a running game. PAK behavior is unchanged.

UE4SS status regressions cover DLL metadata precedence, startup banners with arbitrary version numbers and prerelease/build labels, UTF-16 logs, missing/stale/locked/malformed logs, bounded reads and truncated headers, and both flat and nested runtime layouts.

Native-mod regressions cover DLL and Lua entry-point display, mixed mods, disabled paths, deterministic fallback and unsafe paths. Update cleanup regressions verify removal of obsolete tracked runtime DLLs, preservation of user DLL mods/settings/untracked files, exact rollback on failure or cancellation after cleanup, and distinct backup paths for DLLs sharing a filename. `eng/VerifyInstaller.ps1` validates the explicit legacy app-file inventory and post-install cleanup wiring; `eng/Verify.ps1` also checks that the app, installer, package and published EXE versions agree.

After publishing, `eng/TestInstaller.ps1` uses Inno Setup 6.4 or newer to compile the shipping installer and run an isolated variant of its exact payload and Pascal code. It checks fresh install, removal of all 1,086 known legacy files, preservation of user data and unknown DLLs, reinstall, failed replacement of a locked EXE, a locked legacy DLL, rejection of app-root/ancestor/child-directory junctions, and uninstall. An installed 1.3 fixture package supplies real uninstall records; upgrade and reinstall must retain one record pair, and the final uninstall must remove both the app and empty legacy directories while retaining user files. Test registration, shortcuts, application launching, and application closing are disabled. The script never targets a real application or game installation. The shipping installer's elevation, shortcuts, Restart Manager, and interactive UI remain manual checks. CI runs this script after `eng/Verify.ps1` and uploads its logs and compiled installer.

`.github/workflows/ci.yml` runs these checks on Windows for pushes, pull requests, and manual runs. It uploads validation logs, coverage, and the single EXE as workflow artifacts; it does not create releases or deploy the app.
