# Code organization

The solution is `Neodash.ModLoader.slnx`. The Windows application lives in `src/Neodash.ModLoader/`; its regression project lives in `tests/Neodash.ModLoader.Integration/`.

## Ownership

| Folder inside the app | Responsibility |
| --- | --- |
| Root | `App` and `MainWindow`, their XAML, project file, and Windows manifests. |
| `Core/Configuration/` | Byte-preserving configuration documents and transactional edits. |
| `Core/Mods/`, `Core/Profiles/`, `Core/Settings/`, `Core/Ue4ss/` | Mod/package operations, recovery, profile persistence, settings migration, and runtime installation. |
| `Core/Game/`, `Core/Downloads/`, `Core/FileSystem/`, `Core/Diagnostics/`, `Core/Storage/`, `Core/Serialization/` | Supporting process/path checks, transfers, safe writes, logging, application storage, and JSON metadata. |
| `Models/` | Persisted and transfer data types; one top-level type per matching file. |
| `Services/Contracts/`, `Services/Models/` | UI-free application interfaces and workflow state. |
| `Services/Adapters/` | Concrete implementations that expose core operations through the interfaces. |
| Other `Services/` feature folders | Home, library, settings, setup, updates, pending edits, navigation decisions, and feedback workflows. |
| `Services/Composition/` | DI registrations and lifetime ownership. |
| `UI/Contracts/` | Contracts that reference WinUI windows, frames, and dialogs. |
| Other `UI/` folders | Desktop dialogs/files, frame navigation, window context, dispatch, startup rendering, controls, collection synchronization, and view models. |
| `Views/` | XAML pages and paired code-behind for rendering, bindings, focus, animation, and event translation. |
| `Assets/Branding/`, `Assets/Package/` | App icon/logos and Windows package artwork. |
| `Properties/` | Resources and launch/publish configuration. |

## Dependencies and lifetimes

Pages call their injected feature controllers. Controllers coordinate through the service contracts. Adapters call the core operations; filesystem transactions and storage formats stay in the core. Desktop implementations provide dialogs, navigation, dispatch, and file picking. Core files do not depend on WinUI pages or controls.

`CoreServiceRegistration` owns shared backend adapters. `DesktopServiceRegistration` owns window-scoped UI services and workflows. `PageActivation` is the sole bridge for `Frame`-created pages. Keep service resolution there rather than adding global service locators or page-to-page calls.

`ModEditSession` owns pending checkbox state, and `NavigationGuard` owns Save / Discard / Cancel decisions. Dispatch background notifications through `IUiDispatcher`. Unsubscribe page listeners on unload and dispose scoped subscriptions when the window closes.

## Names and compatibility

Use a matching file for each top-level class, interface, record, or enum. Keep related partial declarations in the same feature folder. Keep nested implementation types with their owner. Keep XAML and its code-behind together. Put assets under `Assets/`; generated output belongs in ignored `bin/`, `obj/`, or repository `artifacts/`.

Physical source paths use `Neodash.ModLoader`. The existing `Neodash_Mod_Loader` C#/XAML/resource namespaces remain stable, including historical namespace casing. Namespace changes must account for XAML generation and resource lookup; moving a source file is not a reason to change runtime identity.

The explicit assembly name remains `Neodash Mod Loader`, so distribution still produces `Neodash Mod Loader.exe`. The package/installer identities and `%LOCALAPPDATA%\Neodash Mod Loader\` remain unchanged. Settings and profile schemas are independent of source layout and retain their existing migration rules.

## Tests and verification

Use the native .NET 10 Microsoft Testing Platform runner with xUnit v3. `Host/` contains process isolation and test substitutes; `Scenarios/` contains filesystem scenarios. Put focused tests in `Core/`, `Services/`, or `Presentation/` according to the behavior they exercise. Keep real settings and game files outside every test.

The harness compiles linked production sources because the existing static storage/process boundaries are replaced by isolated substitutes. `Host/Doubles/ResourceStrings` also substitutes resource URLs; production resource lookup is outside this coverage. Keep the inclusion/exclusion rules explicit in the test project. Do not load the WinUI executable just to test pure workflows.

Run `./eng/Verify.ps1` for build, regression tests, linked-source coverage, and single-EXE verification. Run `./eng/TestInstaller.ps1` when changing asset paths, installer inputs, or distribution layout. Native UI rendering, shortcuts, interactive installer behavior, and live-game behavior require separate manual evidence.
