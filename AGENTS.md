# Repository Guidelines

## Project Structure & Module Organization

The Windows x64 app manages Neodash UE4SS mods using WinUI 3, .NET 10, and C# 14.

- `Neodash Mod Loader/`: entry points, installation, persistence, and recovery services.
- `Views/`, `Models/`, and `Assets/` inside that directory: XAML pages with code-behind, data models, and branding assets. `Properties/` contains resources and launch/publish profiles.
- `Services/`: application contracts, feature controllers, and per-window workflows. `Services/Core/` adapts the tested installation and persistence managers without UI dependencies.
- `tests/Neodash.ModLoader.Integration/`: regression scenarios and isolated test host.
- `eng/Verify.ps1` and `.github/workflows/ci.yml`: local verification and Windows CI. Generated outputs belong in ignored `artifacts/`, `bin/`, and `obj/` directories.

## Build, Test, and Development Commands

Run from the repository root on Windows with PowerShell and the .NET 10 SDK selected by `global.json`.

- `./eng/Verify.ps1`: restore, rebuild with warnings as errors, run regressions, collect core coverage, and validate a self-contained single-EXE publish.
- `dotnet build 'Neodash Mod Loader.slnx' -c Debug -p:Platform=x64`: build the development solution.
- `dotnet test --project 'tests/Neodash.ModLoader.Integration/Neodash.ModLoader.Integration.csproj' -c Release`: run regressions through native Microsoft Testing Platform.
- `& './artifacts/publish/Neodash Mod Loader.exe'`: launch the app after verification publishes it.

## Coding Style & Naming Conventions

Match surrounding C# and XAML: four-space indentation, file-scoped namespaces, and braces on separate lines for C# blocks. Use PascalCase for types, methods, and properties; camelCase for parameters and locals; and `_camelCase` for private instance fields. Keep nullable annotations accurate. Follow `.editorconfig`, which disables S3267 to retain explicit loops. No dedicated formatter is configured. Preserve existing service boundaries and branding.

Use constructor injection for services and controllers. Register shared core adapters in `CoreServiceRegistration` and window-scoped UI services in `DesktopServiceRegistration`. Keep XAML code-behind focused on rendering, focus, animation, and event translation; do not add direct filesystem calls, static manager access, global window lookups, or page-to-page dependencies. `PageActivation` alone bridges the scoped container to parameterless Frame-created pages. Reuse `ModEditSession` and `NavigationGuard` for pending changes. Dispatch background notifications through `IUiDispatcher` and unsubscribe when a page unloads or its scope is disposed.

## Testing Guidelines

Use xUnit v3 `[Fact]` methods with descriptive PascalCase names such as `CancelledInstallLeavesGameUnchanged`. Register filesystem scenarios in `IntegrationTests.cs`; place implementations in `IntegrationTests.<Feature>.cs` and reuse shared fixtures and process isolation. Keep filesystem fixtures under `tests/.scratch/`. Pure application-service tests can live in standalone test classes with injected fakes; they must not touch real settings or game files.

Add regressions for behavior changes, especially rollback, unsafe paths, ownership, and settings compatibility. Coverlet measures linked core sources; verification requires valid, nonempty coverage but sets no percentage threshold. Tests require junction creation permissions and exclude live WinUI/game checks; document manual verification separately.

## Commit & Pull Request Guidelines

History contains one release-style subject, `Neodash Mod Loader 1.3.0`; no broader convention is established. Use concise subjects and focused changes. PRs should explain the problem, behavior change, validation results, and relevant issues. Include screenshots for UI changes and run `eng/Verify.ps1` before submission.

## Data Safety & Configuration

Close Neodash before manual mod mutation checks. Preserve path/junction validation, package ownership checks, rollback backups, and legacy settings/profile compatibility. Keep real game files, application data under `%LOCALAPPDATA%\Neodash Mod Loader\`, and signing keys out of commits.
