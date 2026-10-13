using System.Linq;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>A hand-written params DTO with defaults (its initialisers, R-3002) and a field kept out of the JSON contract.</summary>
[ParamsDto]
public struct CE2135_FieldSourceDto
{
    public float Speed = 1.5f;
    public int Loops;
    [System.Text.Json.Serialization.JsonIgnore] public int Scratch;
    public CE2135_Mode Mode = CE2135_Mode.Rush;
    public CE2135_FieldSourceDto() { }
}

public enum CE2135_Mode { Walk, Rush }

/// <summary>
/// ⭐ <c>CE-2135</c> P4 (R-2004 — docs/DESIGN_Orders_Params_Entity_Classes.md §13.10 P4-a/b/c) — the params DTO's fields, read the way
/// the compiler makes Parameters of them.
/// </summary>
public sealed class ParamsDtoFieldsTests
{
    /// <summary>⭐ The loaded type's public fields, in declaration order; a <c>[JsonIgnore]</c> field is not in the contract.
    /// 🔴 Red-proof: drop the JsonIgnore filter → <c>Scratch</c> appears. ⭐ CE-3194: no default rides the field list (R-3002).</summary>
    [Fact]
    public void CE2135_ReflectionSource_ReadsFieldsInOrder()
    {
        Assert.True(ReflectionParamsDtoFieldSource.Instance.TryGetFields(typeof(CE2135_FieldSourceDto).FullName!, out var fields));
        Assert.Equal(new[] { "Speed", "Loops", "Mode" }, fields.Select(f => f.Name).ToArray());
        Assert.Equal(new[] { "System.Single", "System.Int32", typeof(CE2135_Mode).FullName },
            fields.Select(f => f.TypeId).ToArray());
    }

    [Fact]
    public void CE2135_ReflectionSource_AnUnknownType_IsFalse()
        => Assert.False(ReflectionParamsDtoFieldSource.Instance.TryGetFields("No.Such.ParamsDto", out _));

    /// <summary>⭐ Materialize makes one Parameter per field on the asset it is given (the compiler passes its own copy), with ids that
    /// are the same on every compile — and a second call does not double them.</summary>
    [Fact]
    public void CE2135_Materialize_MakesOneParameterPerField_Deterministically()
    {
        BlueprintAsset Make()
        {
            var a = BlueprintAssetBuilder.AiPrimitive("CE2135Make").Build();
            a.ParamsDtoTypeId = typeof(CE2135_FieldSourceDto).FullName;
            return a;
        }
        var first = Make();
        Assert.True(ParamsDtoFields.Materialize(first, null, _ => Assert.Fail("no diagnostic expected")));
        Assert.True(ParamsDtoFields.Materialize(first, null, _ => Assert.Fail("no diagnostic expected")));
        var second = Make();
        Assert.True(ParamsDtoFields.Materialize(second, null, _ => { }));

        Assert.Equal(new[] { "Speed", "Loops", "Mode" }, first.Parameters.Select(p => p.Name).ToArray());
        Assert.Equal(first.Parameters.Select(p => p.Id), second.Parameters.Select(p => p.Id));
        Assert.All(first.Parameters, p => Assert.Null(p.DefaultValueJson));   // ⭐ CE-3194: no override ⇒ the DTO's own default at run time
    }

    /// <summary>⭐⭐ <c>CE-3194</c> (R-3001) — a made Parameter's default is the ASSET's override of that field (keys case-insensitive), and
    /// a field it does not override has none (the bake's <c>new T()</c> baseline supplies it). 🔴 Red-proof: read the default from the
    /// field source again → <c>Speed</c> is not "4".</summary>
    [Fact]
    public void CE3194_Materialize_TakesTheAssetsOverrides_Only()
    {
        var a = BlueprintAssetBuilder.AiPrimitive("CE3194Over").Build();
        a.ParamsDtoTypeId = typeof(CE2135_FieldSourceDto).FullName;
        a.ParamsDefaultJson = """{ "speed": 4, "Loops": 0 }""";
        Assert.True(ParamsDtoFields.Materialize(a, null, d => Assert.Fail(d.Message)));
        Assert.Equal("4", a.Parameters.Single(p => p.Name == "Speed").DefaultValueJson);
        Assert.Equal("0", a.Parameters.Single(p => p.Name == "Loops").DefaultValueJson);
        Assert.Null(a.Parameters.Single(p => p.Name == "Mode").DefaultValueJson);
    }

    /// <summary>⛔ <c>CE-3194</c> — an override naming no field of the DTO is an error (a typo would otherwise be silently ignored).</summary>
    [Fact]
    public void CE3194_AnOverrideOfNoSuchField_IsBP1689()
    {
        var a = BlueprintAssetBuilder.AiPrimitive("CE3194Typo").Build();
        a.ParamsDtoTypeId = typeof(CE2135_FieldSourceDto).FullName;
        a.ParamsDefaultJson = """{ "Sped": 4 }""";
        string? message = null;
        Assert.False(ParamsDtoFields.Materialize(a, null, d => message = d.Message));
        Assert.Contains("'Sped'", message);
    }

    /// <summary>⛔ The made Parameter's id is PINNED: converted <c>.bp.json</c> files reference it (a task's <c>ParamsVariable</c>, a
    /// <c>GetVariable</c>), so changing the formula silently orphans them. Value: <c>Demo_MissionPlan</c>'s <c>Move</c>.</summary>
    [Fact]
    public void CE2135_TheMadeParameterId_IsPinned()
        => Assert.Equal(System.Guid.Parse("b0162403-6926-ee54-b409-7895e8f568e7"),
            ParamsDtoFields.ParameterId(System.Guid.Parse("5b21a51c-99ec-44b0-94f2-86b66182d46e"), "Move"));
}
