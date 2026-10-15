using System.Reflection;
using Neodash_Mod_Loader.Services.Core;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class BuildIdentityTests
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
    private const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void EmbeddedCommitSupportsSdkBuildMetadata()
    {
        Assert.Equal(Sha1, AppDiagnostics.GetCommitHash("1.4.0.0+" + Sha1));
        Assert.Equal(Sha1, AppDiagnostics.GetCommitHash("1.4.0-preview+custom.build." + Sha1.ToUpperInvariant()));
        Assert.Equal(Sha256, AppDiagnostics.GetCommitHash("1.4.0.0+" + Sha256));
    }

    [Fact]
    public void MissingAndInvalidCommitMetadataReturnsUnavailable()
    {
        foreach (string? version in new[] { null, "", "1.4.0.0", Sha1, "1.4.0.0+", "1.4.0.0+custom",
            "1.4.0.0+" + Sha1[..39], "1.4.0.0+" + Sha1 + "0", "1.4.0.0+" + Sha1[..39] + "g", "1.4.0.0+" + Sha1 + ".dirty" })
        {
            Assert.Null(AppDiagnostics.GetCommitHash(version));
        }
    }

    [Fact]
    public void DiagnosticsReadsCommitFromItsOwnAssembly()
    {
        string? informationalVersion = typeof(AppDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string? hash = new AppDiagnostics().CommitHash;
        if (informationalVersion?.Contains('+') == true)
        {
            Assert.NotNull(hash);
            Assert.EndsWith(hash, informationalVersion, StringComparison.OrdinalIgnoreCase);
            Assert.True(hash.Length is 40 or 64);
        }
        else
        {
            Assert.Null(hash);
        }
    }
}
