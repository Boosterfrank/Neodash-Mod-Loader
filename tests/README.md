# Regression tests and CI

Run the same application checks as GitHub Actions from a Windows PowerShell 7.2+ terminal with the .NET SDK pinned in `global.json`:

```powershell
./eng/Verify.ps1
```

This restores locked dependencies and rebuilds the x64 solution with warnings treated as errors, runs the regression suite, publishes the self-contained app, and fails if the publish folder contains anything except `Neodash Mod Loader.exe`. Logs and the executable are written under `artifacts/`. Use `./eng/Verify.ps1 -Configuration Debug -SkipPublish` for CI's Debug build and test path.

To run only the tests:

```powershell
dotnet test --project tests/Neodash.ModLoader.Integration/Neodash.ModLoader.Integration.csproj -c Release
```

The xUnit v3 tests run through the native Microsoft Testing Platform runner and each scenario is individually discoverable. They link the actual installer, mod operations, settings, profiles, catalog, version, path, and mod-management source files. Test substitutes keep storage and process checks deterministic. Filesystem scenarios execute their core fixture in a filtered fresh test host, so static settings/profile state cannot leak between random test order or individual filtered runs. Filesystem fixtures stay under `tests/.scratch/`, which the runner validates before deleting; it never installs into your real game or uses your real settings.

`IntegrationTests.cs` lists the discoverable facts. `Scenarios/IntegrationTests.*.cs` contains implementations by feature, `Host/IntegrationTestHost.cs` handles process isolation, and `Host/TestFixtures.cs` contains shared filesystem and assertion helpers. Focused tests are grouped under `Core/`, `Services/`, and `Presentation/`.

## .NET 10 test project

The SDK-style project targets `net10.0-windows` with C# 14, nullable annotations, `OutputType=Exe`, `IsTestProject=true`, and `IsPackable=false`. It uses xUnit v3's `xunit.v3.mtp-v2` package and the native Microsoft Testing Platform runner selected by `global.json`. This follows the [.NET 10 MTP guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test#mtp-mode-of-dotnet-test) and [xUnit v3 project guidance](https://xunit.net/docs/getting-started/v3/getting-started). VSTest adapters and the .NET 8/9 bridge property are not needed for this repository's .NET 10 runner.

Linked production sources are declared by feature in the project file. The existing `AppStorage`, `GameRuntime`, and `ResourceStrings` boundaries are substituted by the isolated host, so tests cannot accidentally write to real application data, inspect a live game, or fetch the production resource URLs. Resource lookup is outside linked-source coverage. These links intentionally test the production implementations without loading WinUI. This is an existing harness boundary, not a claim that .NET prescribes linked-source tests or one mandatory folder layout.

Folder placement does not change test namespaces, test names, discovery, or child-process filters. The complete suite must discover the same cases before and after organization; `eng/Verify.ps1` runs them with coverage and validates the published app.

`ApplicationServicesTests.cs` uses the production `ModEditSession`, `NavigationGuard`, `StartupCoordinator`, and core DI registrations with injected fakes. These tests verify successful and failed saves, retained drafts, discard without I/O, reload and subscription cleanup, Save / Discard / Cancel decisions, corrupt-settings startup, existing-runtime setup detection, and independent window scopes sharing one backend. They do not need filesystem fixtures or process isolation.

`eng/Verify.ps1` restores the pinned Coverlet console tool from `.config/dotnet-tools.json`, instruments the test executable before starting `dotnet test`, and collects core-source coverage across its isolated child hosts. The Cobertura report, detailed JSON, and summary are saved under `artifacts/coverage/latest/`. Tests, substitutes, and generated code are excluded. UI, game-process enumeration, and other production services not linked into the test executable are outside this measured scope; this is not whole-app coverage. An empty report fails validation.

Verification uses xUnit's built-in TRX reporter through native MTP and saves `integration.trx` under `artifacts/test-results/<Configuration>/`. Skipped tests fail the run, and the TRX report must contain a nonempty, completely passing result. No VSTest adapter or additional reporting package is required.

The tests cover install/update/uninstall/restore, configuration and disabled-state preservation, archive and path validation, transaction rollback, profiles, legacy settings JSON, catalog validation, and version ordering. They also pin down cross-package ownership rejection before files change, catalog ownership isolation, stale-backup ownership rejection, partial-uninstall backup invalidation, cancellation before live-file replacement, a game starting between snapshot creation and install/restore/uninstall mutation, installation-specific process matching, invalid nested settings and semantic profile-data preservation, profile reconciliation of missing and disabled mods including package-folder renames, profile-bound mod-state rollback, mods.txt encoding/newline behavior for snapshot restore plus normal enable/uninstall changes, offline UE4SS archive install/rollback/uninstall preservation, and byte-preserving configuration edits (UTF-8 with and without BOM, UTF-16, UTF-32, legacy ANSI, external-edit detection, and rollback).

The runner uses a temporary NTFS junction to prove that linked game folders are rejected. On a restricted shell, run the command with the permission needed to create junctions; the scenario intentionally fails rather than being skipped. The tests do not launch WinUI, download real packages, or test a running game. PAK behavior is unchanged.

UE4SS status regressions cover DLL metadata precedence, startup banners with arbitrary version numbers and prerelease/build labels, UTF-16 logs, missing/stale/locked/malformed logs, bounded reads and truncated headers, and both flat and nested runtime layouts.

UI availability regressions exercise the real catalog and runtime adapters: absent, enabled, disabled, missing tracked, and partially installed packages; missing game folders; removal only for existing tracked runtime files; and log creation/deletion with nested UE4SS log preference. Availability queries must not create recovery directories. XAML bindings and page control states are checked through compilation and source review; these tests do not automate live WinUI interaction.

Native-mod regressions cover DLL and Lua entry-point display, mixed mods, disabled paths, deterministic fallback and unsafe paths. Update cleanup regressions verify removal of obsolete tracked runtime DLLs, preservation of user DLL mods/settings/untracked files, exact rollback on failure or cancellation after cleanup, and distinct backup paths for DLLs sharing a filename. `eng/VerifyInstaller.ps1` validates the explicit legacy app-file inventory and post-install cleanup wiring; `eng/Verify.ps1` also checks that the app, installer, package and published EXE versions agree.

After publishing, `eng/TestInstaller.ps1` uses Inno Setup 6.4 or newer to compile the shipping installer and run an isolated variant of its exact payload and Pascal code. It checks fresh install, removal of all 1,086 known legacy files, preservation of user data and unknown DLLs, reinstall, failed replacement of a locked EXE, a locked legacy DLL, rejection of app-root/ancestor/child-directory junctions, and uninstall. An installed 1.3 fixture package supplies real uninstall records; upgrade and reinstall must retain one record pair, and the final uninstall must remove both the app and empty legacy directories while retaining user files. Test registration, shortcuts, application launching, and application closing are disabled. The script never targets a real application or game installation. The shipping installer's elevation, shortcuts, Restart Manager, and interactive UI remain manual checks. CI runs this script after `eng/Verify.ps1` and uploads its logs and compiled installer.

The reusable `.github/workflows/ci.yml` runs Debug and Release checks on Windows Server 2025. `main-ci.yml` handles main pushes and manual runs; `pull-request-ci.yml` handles pull requests and merge queues. Workflow/security linting and dependency review join both build configurations in the stable `CI / Gate` check. Failed, cancelled, or skipped required jobs fail the gate. CI uploads test reports, validation logs, coverage, the Release single EXE, and the verified installer; it does not publish releases or NuGet packages. See [CI and build maintenance](../docs/ci.md).
