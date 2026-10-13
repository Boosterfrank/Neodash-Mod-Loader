using System.Text;
using Neodash_Mod_Loader;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static async Task Ue4SsStatusDetectsVersionFromMetadataOrStartupHeaderCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-status-version");
        string dll = Path.Combine(paths.Game, "UE4SS.dll");
        string proxy = Path.Combine(paths.Game, "dwmapi.dll");
        string log = Path.Combine(paths.Game, "UE4SS.log");
        await File.WriteAllTextAsync(dll, "runtime-without-version-resources");
        await File.WriteAllTextAsync(proxy, "proxy");
        await File.WriteAllTextAsync(log, "[2026-09-23 00:15:58] Console created\r\n[2026-09-23 00:15:58] UE4SS - v3.0.1 Beta #0 - Git SHA #d935b5b\r\n[2026-09-23 00:16:00] [Lua] [SomeMod] Loaded for UE4SS v9.9.9\r\n");
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 3.0.1 Beta #0 (last run) — loader files present", "metadata-free DLL uses the UE4SS startup header, preserving beta details and identifying the version as the last run");

        await File.WriteAllTextAsync(log, "[00:15:58.123] UE4SS - v3.1.0 PreRelease #2 Beta #? - Git SHA #abc123\n", Encoding.Unicode);
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 3.1.0 PreRelease #2 Beta #? (last run) — loader files present", "BOM-encoded logs and prerelease details are detected");

        await File.WriteAllTextAsync(log, "UE4SS - v3.0.1\n");
        using (new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 3.0.1 (last run) — loader files present", "a readable log remains usable while another process holds it open");
        }
        File.Delete(proxy);
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 3.0.1 (last run) — proxy DLL missing; repair recommended", "version detection retains the missing-proxy repair state");

        // The test assembly has the SDK's default 1.0.0.0 file-version resource.
        File.Copy(typeof(IntegrationTests).Assembly.Location, dll, overwrite: true);
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 1.0.0.0 — proxy DLL missing; repair recommended", "native file-version metadata takes precedence over a different logged version");
    }

    private static async Task Ue4SsStatusIgnoresMissingStaleLockedAndInvalidLogsCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-status-fallback");
        string dll = Path.Combine(paths.Game, "UE4SS.dll");
        string proxy = Path.Combine(paths.Game, "dwmapi.dll");
        string log = Path.Combine(paths.Game, "UE4SS.log");
        const string installedStatus = "UE4SS installed — loader files present";
        const string header = "[2026-09-23 00:15:58] UE4SS - v3.0.1 Beta #0 - Git SHA #d935b5b\n";
        await File.WriteAllTextAsync(log, header);
        Assert(Ue4SsService.GetStatus(string.Empty) == "Choose your Neodash folder first." && Ue4SsService.GetStatus(paths.Game) == "UE4SS is not installed.", "a log alone cannot make an absent runtime appear installed");
        await File.WriteAllTextAsync(dll, "runtime-without-version-resources");
        await File.WriteAllTextAsync(proxy, "proxy");
        File.Delete(log);
        Assert(Ue4SsService.GetStatus(paths.Game) == installedStatus, "an installation without a log has a neutral installed state");

        foreach (string invalid in new[] { string.Empty, "[Lua] UE4SS - v9.9.9\n", "[2026-09-23 00:15:58] [Lua] [SomeMod] Loaded for UE4SS v9.9.9\n", "UE4SS - vunknown\n", "UE4SS - v3.0\n" })
        {
            await File.WriteAllTextAsync(log, invalid);
            Assert(Ue4SsService.GetStatus(paths.Game) == installedStatus, "empty, unrelated, and malformed log lines cannot invent a runtime version");
        }

        await File.WriteAllTextAsync(log, header);
        File.SetLastWriteTimeUtc(log, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(dll, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert(Ue4SsService.GetStatus(paths.Game) == installedStatus, "a startup log older than the runtime is ignored");
        File.SetLastWriteTimeUtc(log, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 3.0.1 Beta #0 (last run) — loader files present", "equal timestamps do not discard a matching startup log");
        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert(Ue4SsService.GetStatus(paths.Game) == installedStatus, "an unreadable log does not break the settings status display");
        }

        await File.WriteAllTextAsync(log, new string('x', 16_384) + "\n" + header);
        Assert(Ue4SsService.GetStatus(paths.Game) == installedStatus, "only the bounded startup prefix is searched, avoiding scans of an entire large log");
        const string partialHeader = "UE4SS - v3.0.1";
        await File.WriteAllTextAsync(log, new string('x', 16_384 - partialHeader.Length - 1) + "\n" + partialHeader + " Beta #0 - Git SHA #abc123\n");
        Assert(Ue4SsService.GetStatus(paths.Game) == installedStatus, "a truncated startup header cannot discard prerelease details and falsely report a release version");
    }

    private static async Task Ue4SsStatusDetectsFutureVersionNumbersAndNestedLayoutsCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-status-future");
        string dll = Path.Combine(paths.Game, "UE4SS.dll");
        string log = Path.Combine(paths.Game, "UE4SS.log");
        await File.WriteAllTextAsync(dll, "legacy-runtime");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll"), "proxy");
        foreach (string version in new[] { "2.5.2", "4.0.0", "42.17.123", "2026.10.13", "4.1.0.52", "4.0.0-rc.2+build.123", "4.0.0 Alpha #1", "4.0.0 Preview 2", "4.0.0 Experimental" })
        {
            await File.WriteAllTextAsync(log, $"[2026-10-13 12:00:00] UE4SS - v{version} - Git SHA #abc123\n");
            Assert(Ue4SsService.GetStatus(paths.Game) == $"UE4SS {version} (last run) — loader files present", "version detection preserves arbitrary release numbers, prerelease channels, and build metadata without a hard-coded release list");
        }

        await File.WriteAllTextAsync(log, "UE4SS - v2.5.2\n");
        string nested = Path.Combine(paths.Game, "ue4ss");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "UE4SS.dll"), "newer-runtime");
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS installed — loader files present", "a nested runtime cannot inherit the other runtime's logged version");
        await File.WriteAllTextAsync(Path.Combine(nested, "UE4SS.log"), "UE4SS - v4.0.0-rc.2+build.123\n");
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 4.0.0-rc.2+build.123 (last run) — loader files present", "nested runtime and its own log take precedence over the legacy flat installation");
        File.Delete(dll);
        Assert(Ue4SsService.GetStatus(paths.Game) == "UE4SS 4.0.0-rc.2+build.123 (last run) — loader files present", "a nested-only installation is detected without a legacy root DLL");
    }
}
