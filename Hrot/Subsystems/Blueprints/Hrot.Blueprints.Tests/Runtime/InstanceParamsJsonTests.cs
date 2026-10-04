using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// ⭐⭐ <c>CE-3044</c> (R-191) — an Instance blueprint's params persist as JSON BY NAME through the COMPILED pair
/// <c>ParseParams</c> / <c>FormatParams</c>: only the fields that differ from the declared defaults are written, and a
/// save → reload restores the region. 📄 <c>DESIGN_Blueprint_Param_Persistence.md</c> §10.
/// </summary>
public sealed unsafe class InstanceParamsJsonTests
{
    private static ParameterDecl Param(string name, string typeId, string? defaultJson = null)
        => new() { Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = typeId }, DefaultValueJson = defaultJson };

    private static readonly Guid AssetGuid = new("CE304400-0000-4000-8000-000000000044");

    private static BlueprintAsset ParamCarrier()
    {
        var asset = BlueprintAssetBuilder
            .Instance("JsonParamCarrier")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.AssetId    = AssetGuid;
        asset.Parameters = new List<ParameterDecl>
        {
            Param("Speed",  "System.Single", "2.5"),
            Param("Count",  "System.Int32",  "3"),
            Param("Armed",  "System.Boolean", "true"),
            Param("Offset", "System.Numerics.Vector3"),
        };
        return asset;
    }

    private static string? Format(EntityRepository world, Entity e, BlueprintDefinition def)
    {
        byte* mem = OccurrenceStoreAccess.TryGetStore(world, e, out _);
        Assert.True(mem != null, "the entity must carry a store");
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, BlueprintIdHash.Compute(def.AssetId), out int off));
        return def.FormatParams!(mem + off + def.ParamsOffset, def.ParamsSize);
    }

    [Fact]
    public void TheCompiledFormatParams_WritesOnlyTheNonDefaultFields_ByName_AndAReloadRestoresThem()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(ParamCarrier());
        int bpId = BlueprintIdHash.Compute(AssetGuid);
        Assert.True(fixture.Registry.TryGetById(bpId, out var def) && def != null);
        Assert.NotNull(def!.FormatParams);
        Assert.Equal(new[] { "Speed", "Count", "Armed", "Offset" }, def.ParamNames);

        var world = fixture.World;

        // A default attach formats to NOTHING — every field is at its declared default.
        var plain = world.CreateEntity();
        Assert.Equal(BlueprintAttachStatus.Attached, BlueprintInstanceService.AttachToEntity(world, fixture.Registry, bpId, plain).Status);
        Assert.Null(Format(world, plain, def));

        // Two fields changed ⇒ exactly those two, by name.
        var src = world.CreateEntity();
        Assert.Equal(BlueprintAttachStatus.Attached, BlueprintInstanceService.AttachToEntity(
            world, fixture.Registry, bpId, src, "{\"Speed\":7.5,\"Offset\":{\"X\":1,\"Y\":2,\"Z\":3}}").Status);
        var formatted = JsonNode.Parse(Format(world, src, def)!)!.AsObject();
        Assert.Equal(new[] { "Speed", "Offset" }, PropertyNames(formatted));
        Assert.Equal(7.5f, (float)formatted["Speed"]!);
        Assert.Equal(2f, (float)formatted["Offset"]!["Y"]!);

        // Save → reload through the scenario translator and materialization: the same params, by construction.
        var translator = new BlueprintStateTranslator(fixture.Registry);
        var saved = translator.Extract(world, src, null!);
        var dst = world.CreateEntity();
        translator.Inject(world, dst, saved, null!);
        Assert.True(world.HasManagedComponent<InitialBlueprintsIntent>(dst));
        new BlueprintMaterializationSystem(fixture.Registry).Execute(world, 0f);
        Assert.Equal(formatted.ToJsonString(), Format(world, dst, def));
    }

    private static List<string> PropertyNames(JsonObject o)
    {
        var names = new List<string>();
        foreach (var kv in o) names.Add(kv.Key);
        return names;
    }
}
