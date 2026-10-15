using static Neodash.ModLoader.Integration.TestFixtures;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static void VersionOrdering()
    {
        Assert(ModVersion.Compare("1.2.0", "1.1.9") > 0, "new patch wins");
        Assert(ModVersion.Compare("v1.2.0", "1.2.0") == 0, "leading v ignored");
        Assert(ModVersion.Compare("1.0.0-alpha", "1.0.0") < 0, "prerelease before release");
        Assert(ModVersion.Compare("1.0.0-alpha.2", "1.0.0-alpha.10") < 0, "numeric prerelease ordering");
        Assert(ModVersion.Compare("1.0.0+build.2", "1.0.0+build.9") == 0, "build metadata ignored");
        Assert(ModVersion.Compare("1.0.0", "bad") is null, "invalid version does not imply direction");
        Assert(ModVersion.Compare("1.2", "1.2.0.0") == 0, "missing numeric components compare as zero");
        Assert(ModVersion.Compare("1.2", "1.2.0.1") < 0, "additional nonzero numeric component wins");
        Assert(ModVersion.Compare("2.0-alpha", "1.9") > 0, "numeric version takes precedence over release status");
        Assert(ModVersion.Compare("1.0.0", "1.0.0-rc") > 0, "release after prerelease in the opposite direction");
        Assert(ModVersion.Compare("1.0.0-alpha", "1.0.0-alpha") == 0, "identical prerelease identifiers compare equally");
        Assert(ModVersion.Compare("1.0.0-1", "1.0.0-alpha") < 0, "numeric prerelease identifier before text");
        Assert(ModVersion.Compare("1.0.0-alpha", "1.0.0-1") > 0, "text prerelease identifier after numeric");
        Assert(ModVersion.Compare("1.0.0-Beta", "1.0.0-alpha") < 0, "text prerelease comparison remains ordinal and case sensitive");
        Assert(ModVersion.Compare("1.0.0-alpha", "1.0.0-alpha.1") < 0, "shorter equal prerelease prefix comes first");
        Assert(ModVersion.Compare("bad", "1.0.0") is null, "invalid left version does not imply direction");
        Assert(ModVersion.Compare(null, "1.0.0") is null, "missing version does not imply direction");
    }

    private static void CatalogAndPathBehavior(string scratch)
    {
        var valid = new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Valid", Version = "1.0.0", DownloadURL = "https://example.invalid/mod.zip", DeveloperName = null!, Subtitle = null!, FolderNames = [], Sha256 = new string('a', 64) }] };
        valid.Validate();
        Assert(valid.Mods[0].DeveloperName == string.Empty && valid.Mods[0].Subtitle == string.Empty, "catalog validation normalizes optional text");
        Assert(valid.Mods[0].FolderNames is null && valid.Mods[0].Sha256 == new string('a', 64), "empty folder lists normalize to null and valid checksums remain intact");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = null }.Validate(), "missing mods list is rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [null!] }.Validate(), "null catalog entries are rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "", Version = "1.0.0", DownloadURL = "https://example.invalid/mod.zip" }] }.Validate(), "invalid library item is rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Missing version", Version = " ", DownloadURL = "https://example.invalid/mod.zip" }] }.Validate(), "missing catalog versions are rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Missing address", Version = "1", DownloadURL = " " }] }.Validate(), "missing catalog download addresses are rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "One", Id = "same", Version = "1", DownloadURL = "https://example.invalid/a" }, new ModLibraryItem { Name = "Two", Id = "SAME", Version = "1", DownloadURL = "https://example.invalid/b" }] }.Validate(), "case-insensitive duplicate catalog identities are rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Http", Version = "1", DownloadURL = "http://example.invalid/mod.zip" }] }.Validate(), "HTTP catalog download is rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Hash", Version = "1", DownloadURL = "https://example.invalid/mod.zip", Sha256 = "not-a-hash" }] }.Validate(), "invalid catalog SHA-256 is rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Hash digits", Version = "1", DownloadURL = "https://example.invalid/mod.zip", Sha256 = new string('z', 64) }] }.Validate(), "non-hexadecimal catalog checksum digits are rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Unsafe folder", Version = "1", DownloadURL = "https://example.invalid/mod.zip", FolderNames = [".."] }] }.Validate(), "catalog traversal folder names are rejected");
        AssertThrows<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Name = "Reserved folder", Version = "1", DownloadURL = "https://example.invalid/mod.zip", FolderNames = ["SHARED"] }] }.Validate(), "catalog reserved folder names are rejected regardless of case");
        JsonException duplicateFolder = AssertThrowsAndReturn<JsonException>(() => new ModLibraryResponse { Mods = [new ModLibraryItem { Id = "one", Name = "One", Version = "1", DownloadURL = "https://example.invalid/one.zip", FolderNames = ["OwnedMod"] }, new ModLibraryItem { Id = "two", Name = "Two", Version = "1", DownloadURL = "https://example.invalid/two.zip", FolderNames = ["ownedmod"] }] }.Validate(), "catalog rejects duplicate explicit folder ownership regardless of case");
        Assert(duplicateFolder.Message == "The mod library assigns the same folder to more than one entry.", "duplicate folder ownership is reported independently of reserved-name validation");
        string root = Path.Combine(scratch, "paths");
        Directory.CreateDirectory(root);
        Assert(SafePaths.Child(root, "nested/file.txt").StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "safe child resolves nested path");
        AssertThrows<IOException>(() => SafePaths.Child(root, "..\\escape.txt"), "safe path rejects traversal");
        string gameRoot = Path.Combine(root, "game");
        string win64 = Path.Combine(gameRoot, "Runway", "Binaries", "Win64");
        Directory.CreateDirectory(win64);
        File.WriteAllText(Path.Combine(win64, "Runway-Win64-Shipping.exe"), string.Empty);
        Assert(string.Equals(GamePathResolver.NormalizeGameDirectory(gameRoot), win64, StringComparison.OrdinalIgnoreCase), "game directory resolver finds nested Win64 executable");
    }

    private static async Task CatalogOwnershipIsolation(string scratch)
    {
        var paths = NewGame(scratch, "catalog-ownership");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        string sharedScript = CreateLiveMod(paths.Game, "Collision", true);
        await File.WriteAllTextAsync(sharedScript, "package-a");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.Current.Installations.RemoveAll(record => ModOperations.SameGame(record.GamePath, paths.Game));
        SettingsManager.Current.Installations.Add(new InstalledModRecord { Id = "package-a", Name = "Package A", Version = "1", GamePath = paths.Game, FolderNames = ["Collision"] });
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Catalog ownership fixture", paths.Game);
        var packageB = new ModLibraryItem { Id = "package-b", Name = "Package B", Version = "1", DownloadURL = "https://example.invalid/package-b.zip", FolderNames = ["Collision"] };
        Assert(ModOperations.GetInstalledFolders(packageB).Count == 0, "a catalog item cannot treat a folder owned by a different managed package as its own installation");
        string metadataBefore = JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings);
        _ = await AssertThrowsAsyncAndReturn<InvalidOperationException>(() => ModOperations.UninstallCatalogAsync(packageB), "catalog uninstall rejects a folder owned by another package");
        Assert(await File.ReadAllTextAsync(sharedScript) == "package-a" && ModManager.IsModInstalled(paths.Game, "Collision"), "catalog uninstall rejection leaves the owning package files intact");
        Assert(JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings) == metadataBefore && ModOperations.FindRecordForFolder("Collision")?.Id == "package-a", "catalog uninstall rejection leaves the owning metadata intact");
    }
}
