using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.Params;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator;

/// <summary>
/// ⭐ <c>CE-2136</c> (B4a, docs/DESIGN_Orders_Params_Entity_Classes.md §12) — the params DTO asset's generator: a valid asset becomes a
/// <c>[ParamsDto]</c> sequential struct (plus <c>[Order]</c> when it names one); an invalid one is a diagnostic, never a source; the
/// layout a sibling generator uses matches the CLR's sequential rule.
/// </summary>
public sealed class ParamsDtoJsonGeneratorTests
{
    private const string Valid = """
        { "AssetId": "6f1c2b3a-0d4e-4f50-9a61-7b8c9d0e1f23", "Name": "MoveCarefully", "Namespace": "Demo.Params", "Comment": "a <test> DTO", "OrderName": "Creep", "Gesture": "GroundClick",
          "Fields": [
            { "Name": "Speed", "TypeId": "System.Single", "DefaultJson": "1.5", "Comment": "m/s" },
            { "Name": "Goal", "TypeId": "System.Numerics.Vector3", "DefaultJson": "[1, 2, 3]" },
            { "Name": "Hops", "TypeId": "System.Int32" },
            { "Name": "Quiet", "TypeId": "System.Boolean", "DefaultJson": "true" } ] }
        """;

    [Fact]
    public void CE2136_AValidAsset_BecomesAParamsDtoStruct_WithItsDefaultAndOrder()
    {
        var (source, hint, errors) = ParamsDtoJsonGenerator.Generate("/a/MoveCarefully.params.json", Valid);

        errors.Should().BeEmpty();
        hint.Should().Be("Demo.Params.MoveCarefully.params.g.cs");
        source.Should().Contain("namespace Demo.Params")
            .And.Contain("public struct MoveCarefully")
            .And.Contain("[global::Fdp.Core.ParamsDto(AssetId = \"6f1c2b3a-0d4e-4f50-9a61-7b8c9d0e1f23\", DefaultJson = \"{\\\"Speed\\\":1.5,\\\"Goal\\\":[1,2,3],\\\"Quiet\\\":true}\")]")
            .And.Contain("[global::Hrot.Map.Definitions.Behavior.Orders.Order(\"Creep\", Gesture = global::Hrot.Map.Definitions.Behavior.Orders.OrderGesture.GroundClick)]")
            .And.Contain("LayoutKind.Sequential")
            .And.Contain("public global::System.Numerics.Vector3 Goal;")
            .And.Contain("/// <summary>a &lt;test&gt; DTO</summary>");
    }

    [Theory]
    [InlineData("""{ "Name": "1Bad", "Fields": [] }""", "not a C# identifier")]
    [InlineData("""{ "Name": "Dup", "Fields": [ { "Name": "A", "TypeId": "System.Int32" }, { "Name": "A", "TypeId": "System.Int32" } ] }""", "declared twice")]
    [InlineData("""{ "Name": "NoType", "Fields": [ { "Name": "A", "TypeId": "" } ] }""", "names no type")]
    [InlineData("""{ "Name": "BadDefault", "Fields": [ { "Name": "A", "TypeId": "System.Int32", "DefaultJson": "{oops" } ] }""", "is not JSON")]
    [InlineData("""{ "Name": "Lonely", "Gesture": "GroundClick", "Fields": [] }""", "needs an OrderName")]
    [InlineData("""{ not json""", "does not parse")]
    [InlineData("""{ "Name": "X", "Fields": [] }""", "names no AssetId")]                                                    // CE-3185
    [InlineData("""{ "AssetId": "6f1c2b3a-0d4e-4f50-9a61-7b8c9d0e1f23", "Name": "Other", "Fields": [] }""", "file name is its Name")]   // CE-3185
    public void CE2136_AnInvalidAsset_IsADiagnostic_NeverASource(string json, string why)
    {
        var (source, _, errors) = ParamsDtoJsonGenerator.Generate("/a/X.params.json", json);
        source.Should().BeNull();
        errors.Should().Contain(e => e.Contains(why));
    }

    /// <summary>⭐ CE-3185 (§13.7 P1-g) — the "open asset" locator: a generated DTO's <c>[ParamsDto(AssetId)]</c> finds its
    /// <c>.params.json</c> wherever it moved; an unknown id finds nothing.</summary>
    [Fact]
    public void CE3185_FindByAssetId_LocatesTheAsset_WhereverItMoved()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ce3185-" + System.Guid.NewGuid().ToString("N"));
        string deep = System.IO.Path.Combine(root, "moved", "here");
        System.IO.Directory.CreateDirectory(deep);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(deep, "MoveCarefully.params.json"), Valid);
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Broken.params.json"), "{ not json");
            ParamsDtoJson.FindByAssetId(root, System.Guid.Parse("6f1c2b3a-0d4e-4f50-9a61-7b8c9d0e1f23"))
                .Should().Be(System.IO.Path.Combine(deep, "MoveCarefully.params.json"));
            ParamsDtoJson.FindByAssetId(root, System.Guid.NewGuid()).Should().BeNull();
        }
        finally { System.IO.Directory.Delete(root, recursive: true); }
    }

    /// <summary>The sequential layout a sibling generator sizes the DTO by: float@0, Vector3@4, int@16, bool@20, size 24 — the
    /// CLR rule (each field at its alignment, the whole at the largest). The runtime twin compares it with the real struct.</summary>
    [Fact]
    public void CE2136_TheLayout_FollowsTheSequentialRule()
    {
        var dto = ParamsDtoJson.Deserialize(Valid);
        var known = new Dictionary<string, (int, int)>
        {
            ["System.Single"] = (4, 4), ["System.Numerics.Vector3"] = (12, 4), ["System.Int32"] = (4, 4), ["System.Boolean"] = (1, 1),
        };
        ParamsDtoEmitCore.TryLayout(dto, t => known.TryGetValue(t, out var l) ? l : null, out var fields, out int size).Should().BeTrue();
        fields.Should().HaveCount(4);
        new[] { fields[0].Offset, fields[1].Offset, fields[2].Offset, fields[3].Offset }.Should().Equal(0, 4, 16, 20);
        size.Should().Be(24);

        ParamsDtoEmitCore.TryLayout(dto, _ => null, out _, out int none).Should().BeFalse("an unknown field type fails closed");
        none.Should().Be(-1);
    }
}
