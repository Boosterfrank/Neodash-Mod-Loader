using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class ProfileJsonFormatTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"profiles\"")]
    [InlineData("{broken")]
    public void InvalidProfileDocumentIsRejected(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ProfileJsonFormat.Deserialize(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void UnsupportedProfileSchemaCannotBeWritten(int version)
    {
        Assert.Throws<JsonException>(() => ProfileJsonFormat.Serialize(new ModProfileData { SchemaVersion = version }));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"profiles\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[]}")]
    public void EmptyLegacyAndCurrentEnvelopesUseSchemaOne(string json)
    {
        ModProfileData data = ProfileJsonFormat.Deserialize(json);
        Assert.Equal(1, data.SchemaVersion);
        Assert.Empty(data.Profiles);
        using var saved = JsonDocument.Parse(ProfileJsonFormat.Serialize(data));
        Assert.Equal(1, saved.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Array, saved.RootElement.GetProperty("profiles").ValueKind);
    }
}
