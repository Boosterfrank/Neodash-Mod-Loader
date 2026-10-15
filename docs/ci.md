# CI and build maintenance

This is a Windows desktop application. CI builds and tests the app, verifies its self-contained executable, and exercises its installer. It does not pack or publish NuGet packages, create GitHub releases, or deploy anything.

The workflow structure follows [CheatEngine.SDK](https://github.com/CheatEngineNet/CheatEngine.SDK/tree/main/.github/workflows) and [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client/tree/main/.github/workflows), with checks specific to this loader.

## Workflows and required checks

| Workflow or job | Purpose |
| --- | --- |
| `main-ci.yml` | Verify every push to `main`; also supports a manual run. Main runs are not cancelled by newer pushes. |
| `pull-request-ci.yml` | Verify pull requests and merge queue candidates; cancel superseded runs for the same request or queue ref. |
| `ci.yml` → Build and test | Independently restore, rebuild, and test Debug and Release on `windows-2025`. Release also publishes and tests the installer. |
| Workflow and script checks | Parse all `eng/` and `scripts/` PowerShell files, run actionlint, and run offline zizmor security checks. |
| Optional GitHub dependency review | When configured, review dependency changes on pull requests. Required high/critical package auditing runs in both Windows restores regardless of this optional integration. |
| `CI / Gate` | Require all three upstream jobs, including both matrix configurations, to succeed. Failed, cancelled, and skipped required jobs cannot pass. |

Use **CI / Gate** as the required branch-protection check if protection is configured. Workflow edits do not change repository protection settings. Fork pull requests use a read-only token, no inherited secrets, and no persisted checkout credentials. Actions are pinned to full commit hashes. CI does not use dependency or build caches.

GitHub's dependency review requires the repository's Dependency graph feature. A repository admin can enable it under **Settings → Advanced Security → Dependency graph**, then set the Actions repository variable **DEPENDENCY_REVIEW_ENABLED=true**. Without that variable, the optional job reports that the integration is disabled; it does not pretend to run a review. Once enabled, review errors and high/critical findings fail CI. The repository currently has this feature disabled, and push access alone cannot enable it. See [GitHub's setup instructions](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/enable-dependency-graph).

The zizmor configuration disables only `self-repository`: the pinned actionlint release does not support the newer `$/` reference syntax suggested by that audit. All other offline zizmor audits run normally.

## Reproducible builds

`global.json` selects the exact .NET SDK and native Microsoft Testing Platform runner. `Directory.Build.props` shares C# 14, nullable reference checking, implicit usings, and dependency lockfile policy across the app and tests. Both projects are non-packable.

Committed `packages.lock.json` files describe consumed dependencies. `eng/Verify.ps1` restores with `--locked-mode`, so changing a dependency without updating its lockfile fails before compilation. NuGet audits both direct and transitive packages during restore. High/critical vulnerabilities and audit-source errors fail validation independently of GitHub's optional dependency-review service. The policy is shared in `Directory.Build.props`; see [Microsoft's audit documentation](https://learn.microsoft.com/nuget/concepts/auditing-packages). CI builds treat warnings as errors. xUnit's built-in TRX reporter records every test, skipped tests fail, and verification rejects empty or incomplete runs.

Coverlet measures linked production sources in the isolated test harness. Reports exclude tests, substitutes, generated sources, WinUI, and production sources outside that harness. A valid nonempty report is required; no coverage percentage threshold is currently enforced. This is not whole-app coverage.

Release validation checks matching app/installer versions, exactly one self-contained x64 executable, embedded Git commit, and a SHA-256 manifest. It explicitly disables the project's automatic post-build publish while compiling, then publishes once after tests succeed.

## Installer toolchain

`eng/Install-InnoSetup.ps1` downloads **Inno Setup 6.7.3** from its [official release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3), checks its pinned SHA-256 and installer version, and provisions it in portable mode without administrator installation or PATH changes. CI uses `RUNNER_TEMP`; local use defaults to ignored `artifacts/tools/`. An existing Inno Setup 6.4+ compiler can also be supplied directly to `eng/TestInstaller.ps1`.

`TestInstaller.ps1` compiles the shipping installer and exercises an isolated variant with the same payload and cleanup implementation. Its scenarios cover fresh install, legacy DLL/language-folder cleanup, user-file preservation, reinstall, locked files, junction rejection, and uninstall. It never targets a real app or game installation. Production elevation, shortcuts, Restart Manager, and interactive wizard behavior still require manual testing.

## Local verification

Run these commands sequentially from a Windows PowerShell 7.2+ terminal:

```powershell
./eng/VerifyScripts.ps1
./eng/Verify.ps1 -Configuration Debug -SkipPublish
./eng/Verify.ps1
$compiler = ./eng/Install-InnoSetup.ps1
./eng/TestInstaller.ps1 -CompilerPath $compiler
```

The first portable compiler setup needs network access. Subsequent verification can reuse the returned `ISCC.exe` path without rerunning setup.

CI uploads configuration-specific TRX results, linked-core coverage, and diagnostics for seven days, including when checks fail. Successful Release runs upload the single EXE and installer for fourteen days. The publish manifest is under `artifacts/logs/publish-manifest.json`; installer results and payload hashes are under `artifacts/installer-verification/run-*/results.json`. These are development artifacts, not signed releases.

## Updating tools and dependencies

Dependabot checks the dependencies the app consumes, the pinned SDK, and Actions monthly. Minor/patch dependency updates are grouped; major .NET upgrades remain a deliberate migration. This dependency maintenance does not publish the app to NuGet.

When changing a package or SDK pin, update the SDK error message if needed and regenerate the affected lockfiles using the selected SDK:

```powershell
dotnet restore Neodash.ModLoader.slnx -p:Platform=x64 --force-evaluate '-bl:artifacts/logs/update-locks-{}.binlog'
./eng/Verify.ps1
```

Review the lockfile diff and run installer verification before merging distribution changes. SDK updates can change implicit runtime packages as well as direct dependencies. For actionlint or Inno Setup updates, change the version and SHA-256 together after checking the upstream release. Keep the workflow action SHAs and their version comments aligned.
