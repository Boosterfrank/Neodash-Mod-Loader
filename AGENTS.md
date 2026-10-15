# Repository Guidelines

## Project Structure & Module Organization

The Windows x64 app manages Neodash UE4SS mods using WinUI 3, .NET 10, and C# 14.

- `src/Neodash.ModLoader/`: WinUI application project; only app/window entry points and project manifests belong at its root.
- `Core/`: UI-free operations grouped by configuration, downloads, filesystem, game, mods, profiles, settings, storage, and UE4SS.
- `Services/`: contracts, workflow models, DI composition, backend adapters, and feature controllers. `UI/`: desktop dialogs, navigation, window services, controls, and view models. `Views/`: XAML pages and their rendering/event code.
- `Models/`: one named data type per file. `Assets/Branding/`: app icon and logos. `Assets/Package/`: Windows package artwork. `Properties/`: resources and launch/publish profiles.
- `tests/Neodash.ModLoader.Integration/`: `Scenarios/` contains filesystem regressions, `Host/` provides isolation/fixtures, and `Core/`, `Services/`, and `Presentation/` group focused tests.
- `eng/Verify.ps1` and reusable `.github/workflows/ci.yml`: local verification and Windows CI, called by `main-ci.yml` and `pull-request-ci.yml`. See `docs/ci.md` for gates and dependency/tool maintenance. Generated outputs belong in ignored `artifacts/`, `bin/`, and `obj/` directories.

## Build, Test, and Development Commands

Run from the repository root on Windows with PowerShell 7.2+ and the exact .NET 10 SDK selected by `global.json`. Nullable reference checking and C# 14 are enabled centrally in `Directory.Build.props`. Commit dependency lockfiles; verification uses locked restore. The app is not published to NuGet.

- `./eng/Verify.ps1`: restore, rebuild with warnings as errors, run regressions, collect core coverage, and validate a self-contained single-EXE publish.
- `dotnet build 'Neodash.ModLoader.slnx' -c Debug -p:Platform=x64`: build the development solution.
- `dotnet test --project 'tests/Neodash.ModLoader.Integration/Neodash.ModLoader.Integration.csproj' -c Release`: run regressions through native Microsoft Testing Platform.
- `& './artifacts/publish/Neodash Mod Loader.exe'`: launch the app after verification publishes it.

## Coding Style & Naming Conventions

Match surrounding C# and XAML: four-space indentation, file-scoped namespaces, and braces on separate lines for C# blocks. Use PascalCase for types, methods, and properties; camelCase for parameters and locals; and `_camelCase` for private instance fields. Keep nullable annotations accurate. Follow `.editorconfig`, which disables S3267 to retain explicit loops. No dedicated formatter is configured. Preserve existing service boundaries and branding.

Put each top-level type in a matching file, keep partial declarations together by feature, and keep page code-behind paired with its XAML. Preserve the existing `Neodash_Mod_Loader` namespace/resource identities and shipped `Neodash Mod Loader.exe` name; source folders do not redefine persistence or deployment contracts. See `docs/architecture.md` for ownership and dependency rules.

Use constructor injection for services and controllers. Register shared core adapters in `CoreServiceRegistration` and window-scoped UI services in `DesktopServiceRegistration`. Keep XAML code-behind focused on rendering, focus, animation, and event translation; do not add direct filesystem calls, static manager access, global window lookups, or page-to-page dependencies. `PageActivation` alone bridges the scoped container to parameterless Frame-created pages. Reuse `ModEditSession` and `NavigationGuard` for pending changes. Dispatch background notifications through `IUiDispatcher` and unsubscribe when a page unloads or its scope is disposed.

## Testing Guidelines

Use xUnit v3 `[Fact]` methods with descriptive PascalCase names such as `CancelledInstallLeavesGameUnchanged`. Keep native .NET 10 Microsoft Testing Platform selection in `global.json`; the test project is an executable, not a VSTest library. Register filesystem scenarios in `IntegrationTests.cs`; place implementations in `Scenarios/IntegrationTests.<Feature>.cs` and reuse `Host/` fixtures and process isolation. Keep filesystem fixtures under `tests/.scratch/`. Pure application-service tests use injected fakes and must not touch real settings or game files.

Add regressions for behavior changes, especially rollback, unsafe paths, ownership, and settings compatibility. Coverlet measures linked core sources; verification requires valid, nonempty coverage but sets no percentage threshold. Tests require junction creation permissions and exclude live WinUI/game checks; document manual verification separately.

## Commit & Pull Request Guidelines

History contains one release-style subject, `Neodash Mod Loader 1.3.0`; no broader convention is established. Use concise subjects and focused changes. PRs should explain the problem, behavior change, validation results, and relevant issues. Include screenshots for UI changes and run `eng/Verify.ps1` before submission.

## Data Safety & Configuration

Close Neodash before manual mod mutation checks. Preserve path/junction validation, package ownership checks, rollback backups, and legacy settings/profile compatibility. Keep real game files, application data under `%LOCALAPPDATA%\Neodash Mod Loader\`, and signing keys out of commits.
