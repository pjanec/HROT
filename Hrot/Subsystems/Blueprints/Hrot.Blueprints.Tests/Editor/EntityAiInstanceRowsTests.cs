using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Events;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Editor.Scenario;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐ <c>CE-2086</c> — the AI section's instance-blueprint rows: each attached instance is listed with its params as JSON
/// (the save's own <c>FormatParams</c>), and an author's edit is committed in place while paused (the load's own
/// <c>ApplyParams</c>) or as a replace event while running (a restart with the new params). 📄
/// <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6a.
/// </summary>
public sealed unsafe class EntityAiInstanceRowsTests
{
    private static readonly Guid AssetGuid = new("CE208600-0000-4000-8000-000000002086");

    private static BlueprintAsset Carrier()
    {
        var asset = BlueprintAssetBuilder.Instance("Ce2086Carrier").WithGraph("Tick", g => g.Entry().Return()).Build();
        asset.AssetId = AssetGuid;
        asset.Parameters = new List<ParameterDecl>
        {
            new() { Id = Guid.NewGuid(), Name = "Speed", Type = new BlueprintTypeRef { TypeId = "System.Single" }, DefaultValueJson = "2.5" },
            new() { Id = Guid.NewGuid(), Name = "Count", Type = new BlueprintTypeRef { TypeId = "System.Int32" },  DefaultValueJson = "3" },
        };
        return asset;
    }

    private static (BlueprintTestFixture F, EntityAiEditModel Model, Entity Unit, int BpId) Attached(string? json)
    {
        var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(Carrier());
        int bpId = BlueprintIdHash.Compute(AssetGuid);
        if (!fixture.World.Bus.IsRegisteredManaged<ReplaceInstanceBlueprintEvent>())
            fixture.World.Bus.RegisterManaged<ReplaceInstanceBlueprintEvent>();   // the running edit's event (production registers it)
        var unit = fixture.World.CreateEntity();
        fixture.World.AddComponent(unit, new BehaviorState());
        var r = BlueprintInstanceService.AttachToEntity(fixture.World, fixture.Registry, bpId, unit, json);
        Assert.True(r.Status == BlueprintAttachStatus.Attached, r.Message);
        var model = new EntityAiEditModel(() => fixture.World, () => fixture.BehaviorRegistry, () => fixture.Registry);
        return (fixture, model, unit, bpId);
    }

    /// <summary>⭐ One row per attached instance: its name, its generated <c>Params</c> type (what the one form edits) and its
    /// params as JSON — only the fields that differ from the defaults (the save's own rule).</summary>
    [Fact]
    public void CE2086_AnAttachedInstance_IsARow_WithItsParamsTypeAndJson()
    {
        var (f, model, unit, bpId) = Attached("{\"Speed\":7.5}");
        using var _ = f;

        var row = Assert.Single(model.InstanceRows(unit));
        Assert.Equal(bpId, row.BlueprintId);
        Assert.Equal("Ce2086Carrier", row.Name);
        Assert.Equal("Params", row.ParamsType?.Name);
        Assert.True(f.Registry.TryGetById(bpId, out var def));
        Assert.Equal(def!.ParamsSize, System.Runtime.InteropServices.Marshal.SizeOf(row.ParamsType!));
        Assert.Equal(7.5f, (float)JsonNode.Parse(row.ParamsJson)!["Speed"]!);
        Assert.Null(JsonNode.Parse(row.ParamsJson)!["Count"]);
    }

    /// <summary>🔴 Paused: the edit is written in place — the same slot, the new value, nothing published.</summary>
    [Fact]
    public void CE2086_Paused_AParamsEditIsAppliedInPlace()
    {
        var (f, model, unit, bpId) = Attached(null);
        using var _ = f;

        Assert.True(model.ApplyInstanceParams(unit, bpId, "{\"Count\":9}", running: false));

        var row = Assert.Single(model.InstanceRows(unit));
        Assert.Equal(9, (int)JsonNode.Parse(row.ParamsJson)!["Count"]!);
        f.World.Bus.SwapBuffers();
        Assert.Empty(f.World.Bus.ReadManaged<ReplaceInstanceBlueprintEvent>());
    }

    /// <summary>🔴 Running: the edit is a replace of the instance by itself with the new params (a restart), never a write
    /// under a running instance.</summary>
    [Fact]
    public void CE2086_Running_AParamsEditIsAReplaceEvent_NotAWrite()
    {
        var (f, model, unit, bpId) = Attached(null);
        using var _ = f;

        Assert.True(model.ApplyInstanceParams(unit, bpId, "{\"Count\":9}", running: true));

        Assert.Equal("{}", Assert.Single(model.InstanceRows(unit)).ParamsJson);   // untouched until the ingress runs
        f.World.Bus.SwapBuffers();
        var evt = Assert.Single(f.World.Bus.ReadManaged<ReplaceInstanceBlueprintEvent>());
        Assert.Equal(unit, evt.Entity);
        Assert.Equal(bpId, evt.OldBlueprintId);
        Assert.Equal(bpId, evt.NewBlueprintId);
        Assert.Equal("{\"Count\":9}", evt.ParamsJson);
    }
}
