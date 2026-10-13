using static Neodash.ModLoader.Integration.TestFixtures;
using System.Text;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static void ModsListSnapshotTextFidelity(string scratch)
    {
        var paths = NewGame(scratch, "mods-txt-fidelity");
        CreateLiveMod(paths.Game, "Target", true);
        string modsTxt = Path.Combine(paths.Game, "Mods", "mods.txt");
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Target : 1\r\nOther : 1")];
        File.WriteAllBytes(modsTxt, original);
        string replacement = CreateFolderPackage(paths.Root, "Target", "Scripts/main.lua");
        File.WriteAllText(Path.Combine(replacement, "Scripts", "main.lua"), "replacement");
        ModInstallResult update = ModPackageInstaller.InstallFolder(paths.Game, replacement, paths.Backups, ["Target"]);
        Assert(update.Success && update.BackupPath is not null, "mods.txt fidelity fixture update succeeds");
        File.Delete(modsTxt);
        ModInstallResult exactRestore = ModPackageInstaller.RestoreBackup(paths.Game, update.BackupPath!, paths.Backups);
        byte[] recreated = File.ReadAllBytes(modsTxt);
        string recreatedText = Encoding.UTF8.GetString(recreated, 3, recreated.Length - 3);
        Assert(exactRestore.Success && recreated.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) && recreatedText == "Target : 1", "restore recreates missing mods.txt with the saved encoding and only the affected package entry");
        Assert(!recreatedText.Contains("Other : 1", StringComparison.Ordinal) && !recreatedText.EndsWith('\n'), "restore does not resurrect unrelated historical entries or invent a final newline");

        ModInstallResult secondUpdate = ModPackageInstaller.InstallFolder(paths.Game, replacement, paths.Backups, ["Target"]);
        Assert(secondUpdate.Success && secondUpdate.BackupPath is not null, "selective mods.txt restore fixture update succeeds");
        Encoding utf16 = new UnicodeEncoding(false, true, true);
        byte[] current = [.. utf16.GetPreamble(), .. utf16.GetBytes("Target : 1\r\nOther : 1\r\nLater : 1")];
        File.WriteAllBytes(modsTxt, current);
        ModInstallResult selectiveRestore = ModPackageInstaller.RestoreBackup(paths.Game, secondUpdate.BackupPath!, paths.Backups);
        byte[] after = File.ReadAllBytes(modsTxt);
        string afterText = utf16.GetString(after, utf16.GetPreamble().Length, after.Length - utf16.GetPreamble().Length);
        Assert(selectiveRestore.Success && after.AsSpan().StartsWith(utf16.GetPreamble()), "selective restore preserves the current UTF-16 BOM and encoding");
        Assert(afterText.Contains("Other : 1", StringComparison.Ordinal) && afterText.Contains("Later : 1", StringComparison.Ordinal) && afterText.Contains("Target : 1", StringComparison.Ordinal), "selective restore keeps unrelated current entries while restoring the package entry");
        Assert(!afterText.EndsWith('\n') && afterText.Contains("\r\n", StringComparison.Ordinal), "selective restore preserves CRLF style and no-final-newline convention");

        CreateLiveMod(paths.Game, "EnableFidelity", false);
        ModManager.SetModEnabled(paths.Game, "EnableFidelity", true);
        byte[] afterEnable = File.ReadAllBytes(modsTxt);
        string afterEnableText = utf16.GetString(afterEnable, utf16.GetPreamble().Length, afterEnable.Length - utf16.GetPreamble().Length);
        Assert(afterEnable.AsSpan().StartsWith(utf16.GetPreamble()) && afterEnableText.Contains("EnableFidelity : 1", StringComparison.Ordinal), "normal enable preserves the UTF-16 BOM while adding the enabled entry");
        Assert(afterEnableText.Contains("Target : 1", StringComparison.Ordinal) && afterEnableText.Contains("Other : 1", StringComparison.Ordinal) && afterEnableText.Contains("Later : 1", StringComparison.Ordinal), "normal enable preserves existing entries");
        Assert(afterEnableText.Contains("\r\n", StringComparison.Ordinal) && !afterEnableText.EndsWith('\n'), "normal enable preserves CRLF style and no-final-newline convention");

        ModPackageInstaller.Uninstall(paths.Game, ["Target"]);
        byte[] afterUninstall = File.ReadAllBytes(modsTxt);
        string afterUninstallText = utf16.GetString(afterUninstall, utf16.GetPreamble().Length, afterUninstall.Length - utf16.GetPreamble().Length);
        Assert(!Directory.Exists(ModManager.GetModPath(paths.Game, "Target", true)) && afterUninstall.AsSpan().StartsWith(utf16.GetPreamble()), "normal uninstall removes the target folder without rewriting the UTF-16 BOM");
        Assert(!afterUninstallText.Contains("Target : 1", StringComparison.Ordinal) && afterUninstallText.Contains("EnableFidelity : 1", StringComparison.Ordinal) && afterUninstallText.Contains("Other : 1", StringComparison.Ordinal) && afterUninstallText.Contains("Later : 1", StringComparison.Ordinal), "normal uninstall removes only the target entry");
        Assert(afterUninstallText.Contains("\r\n", StringComparison.Ordinal) && !afterUninstallText.EndsWith('\n'), "normal uninstall preserves CRLF style and no-final-newline convention");
    }

    private static void ConfigurationEncodingAndTransactions(string scratch)
    {
        string root = Path.Combine(scratch, "config");
        Directory.CreateDirectory(root);
        var formats = new (string Name, Encoding Encoding, bool Preamble)[]
        {
            ("utf8-bom", new UTF8Encoding(true, true), true),
            ("utf8-no-bom", new UTF8Encoding(false, true), false),
            ("utf16-le", new UnicodeEncoding(false, true, true), true),
            ("utf16-be", new UnicodeEncoding(true, true, true), true),
            ("utf32-le", new UTF32Encoding(false, true, true), true),
            ("utf32-be", new UTF32Encoding(true, true, true), true)
        };
        foreach ((string name, Encoding encoding, bool preamble) in formats)
        {
            string path = Path.Combine(root, name + ".ini");
            byte[] source = preamble ? [.. encoding.GetPreamble(), .. encoding.GetBytes("name=café\n")] : encoding.GetBytes("name=café\n");
            File.WriteAllBytes(path, source);
            ConfigFileDocument document = ConfigFileDocument.Load(path, source);
            Assert(document.Text == "name=café\n", name + " decodes text");
            byte[] edited = document.Encode("name=renamed\n");
            byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes("name=renamed\n")];
            Assert(edited.SequenceEqual(expected), name + " preserves encoding and BOM when saving");
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding ansi = Encoding.GetEncoding(0, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        string ansiPath = Path.Combine(root, "legacy.ini");
        if (ansi.CodePage == Encoding.UTF8.CodePage)
        {
            AssertThrows<IOException>(() => ConfigFileDocument.Load(ansiPath, [0xFF]), "invalid non-UTF-8 bytes are refused when no legacy ANSI code page is available");
        }
        else
        {
            byte[] ansiBytes = ansi.GetBytes("title=café\n");
            File.WriteAllBytes(ansiPath, ansiBytes);
            ConfigFileDocument ansiDocument = ConfigFileDocument.Load(ansiPath, ansiBytes);
            Assert(ansiDocument.Text == "title=café\n" && ansiDocument.Encode("title=olé\n").SequenceEqual(ansi.GetBytes("title=olé\n")), "legacy ANSI text remains editable without UTF-8 conversion");
            AssertThrows<IOException>(() => ansiDocument.Encode("title=🧪\n"), "legacy ANSI rejects edits it cannot represent");
        }

        string malformedPath = Path.Combine(root, "malformed.ini");
        AssertThrows<IOException>(() => ConfigFileDocument.Load(malformedPath, [0xFF, 0xFE, 0x41]), "malformed UTF-16 is refused rather than silently replaced");

        string firstPath = Path.Combine(root, "first.ini");
        string secondPath = Path.Combine(root, "second.ini");
        byte[] firstOriginal = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("one=original\n")];
        byte[] secondOriginal = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("two=original\n")];
        File.WriteAllBytes(firstPath, firstOriginal);
        File.WriteAllBytes(secondPath, secondOriginal);
        ConfigFileDocument first = ConfigFileDocument.Load(firstPath, firstOriginal);
        ConfigFileDocument second = ConfigFileDocument.Load(secondPath, secondOriginal);

        File.WriteAllText(firstPath, "external=true\n");
        AssertThrows<IOException>(() => ConfigFileTransactions.Apply([new(first, "one=changed\n")]), "an external edit prevents overwrite");
        Assert(File.ReadAllText(firstPath) == "external=true\n", "external configuration remains untouched after conflict");
        File.WriteAllBytes(firstPath, firstOriginal);

        AppStorage.FailWritePath = secondPath;
        try { AssertThrows<IOException>(() => ConfigFileTransactions.Apply([new(first, "one=changed\n"), new(second, "two=changed\n")]), "a write failure is surfaced"); }
        finally { AppStorage.FailWritePath = null; }
        Assert(File.ReadAllBytes(firstPath).SequenceEqual(firstOriginal) && File.ReadAllBytes(secondPath).SequenceEqual(secondOriginal), "a partial configuration save restores exact original bytes");

        if (ansi.CodePage != Encoding.UTF8.CodePage)
        {
            byte[] ansiBytes = ansi.GetBytes("legacy=ok\n");
            File.WriteAllBytes(ansiPath, ansiBytes);
            ConfigFileDocument ansiDocument = ConfigFileDocument.Load(ansiPath, ansiBytes);
            AppStorage.FailWritePath = firstPath;
            try
            {
                IOException encodingError = AssertThrowsAndReturn<IOException>(() => ConfigFileTransactions.Apply([new(first, "one=changed\n"), new(ansiDocument, "legacy=\uD800\n")]), "a non-representable later edit fails before any file write");
                Assert(!encodingError.Message.Contains("Injected file write failure", StringComparison.Ordinal), "encoding validation is reported instead of an earlier write fault: " + encodingError.Message);
            }
            finally { AppStorage.FailWritePath = null; }
            Assert(File.ReadAllBytes(firstPath).SequenceEqual(firstOriginal), "pre-write encoding validation leaves the first configuration untouched");
        }
    }
}
