using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Regression for the editor "Compile / Reload Blueprint" crash:
/// "An item with the same key has already been added. Key: 00000000-0000-0000-0000-000000000000".
///
/// Root cause: <see cref="BlueprintSignatureParser"/> read camelCase property names ("assetId",
/// "name", "dispatch") but the on-disk <c>.bp.json</c> is serialized PascalCase. Case-sensitive
/// <c>JsonElement.TryGetProperty</c> meant every disk-parsed sibling fell back to
/// <c>AssetId = Guid.Empty</c>, and <c>ValidationContext</c>'s <c>ToDictionary(s =&gt; s.AssetId)</c>
/// threw on the second one. JSON parsing must be case-insensitive.
/// </summary>
public sealed class BlueprintSignatureParserCasingTests
{
    private const string PascalCaseBp = """
        {
          "$meta": { "docType": "Hrot.Blueprints", "schemaVersion": 1 },
          "AssetId": "00000099-0000-0000-0000-000000000001",
          "Name": "SampleWiredDemo",
          "Dispatch": 2
        }
        """;

    private const string CamelCaseBp = """
        {
          "assetId": "00000099-0000-0000-0000-000000000002",
          "name": "LegacyCamel",
          "dispatch": "instance"
        }
        """;

    [Fact]
    public void Parse_PascalCaseJson_ReadsAssetIdNameDispatch()
    {
        var sig = BlueprintSignatureParser.Parse("/x/SampleWiredDemo.bp.json", PascalCaseBp);

        Assert.Equal(Guid.Parse("00000099-0000-0000-0000-000000000001"), sig.AssetId);
        Assert.Equal("SampleWiredDemo", sig.Name);
        Assert.Equal(BlueprintDispatchKind.Instance, sig.Dispatch); // Dispatch 2 == Instance
    }

    [Fact]
    public void Parse_CamelCaseJson_StillReadsAssetId()
    {
        var sig = BlueprintSignatureParser.Parse("/x/LegacyCamel.bp.json", CamelCaseBp);

        Assert.Equal(Guid.Parse("00000099-0000-0000-0000-000000000002"), sig.AssetId);
        Assert.Equal("LegacyCamel", sig.Name);
        Assert.Equal(BlueprintDispatchKind.Instance, sig.Dispatch);
    }

    [Fact]
    public void Parse_MultiplePascalCaseSiblings_DistinctAssetIds_NoToDictionaryCollision()
    {
        // The exact crash scenario: several sibling blueprints, all PascalCase. Before the fix every
        // one parsed to Guid.Empty and the second collided in ToDictionary(s => s.AssetId).
        var a = BlueprintSignatureParser.Parse("/x/A.bp.json",
            """{ "AssetId": "00000099-0000-0000-0000-00000000000a", "Name": "A", "Dispatch": 0 }""");
        var b = BlueprintSignatureParser.Parse("/x/B.bp.json",
            """{ "AssetId": "00000099-0000-0000-0000-00000000000b", "Name": "B", "Dispatch": 0 }""");

        Assert.NotEqual(Guid.Empty, a.AssetId);
        Assert.NotEqual(Guid.Empty, b.AssetId);

        var ex = Record.Exception(() => new[] { a, b }.ToDictionary(s => s.AssetId));
        Assert.Null(ex); // distinct non-empty AssetIds must not collide
    }
}
