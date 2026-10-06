using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Compiler;
using Hrot.Blueprints.Tests.Runtime;
using Xunit;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Sensors;

/// <summary>
/// ⭐ <c>CE-3078</c> N3 — <c>SpawnSensor(kind)</c>: one in-pin per settings field, the kind's own Ensure method (Q2, D3).
/// 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.10a.
/// </summary>
[Collection("DebugProbe")]
public sealed class SpawnSensorTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    [Fact]
    public void CE3078_Bake_DangerArea_CarriesItsSettings_Default_AndEnsureMethod()
    {
        var d = SensorKindBaker.Bake(SensorModality.DangerArea)!;
        Assert.Equal("Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure", d.EnsureMethodFqn);
        Assert.Equal(typeof(DangerAreaSettings).FullName, d.SettingsTypeFqn);
        Assert.Equal(typeof(DangerAreaSettings).FullName + ".Default", d.SettingsDefaultFqn);
        Assert.Contains(d.SettingsFields!, f => f.Name == nameof(DangerAreaSettings.RoutePoint) && f.TypeId == "System.Numerics.Vector3");
        Assert.Contains(d.SettingsFields!, f => f.Name == nameof(DangerAreaSettings.RouteSource) && f.TypeId == "global::" + typeof(DangerRouteSource).FullName);
        Assert.Null(SensorKindBaker.Bake(SensorModality.Visual)!.EnsureMethodFqn);   // a TKB perception kind is read, not spawned
    }

    [Fact]
    public void CE3078_Palette_OffersSpawn_OnlyForKindsABehaviourSpawns()
    {
        var spawnable = SensorKindRegistry.All.Where(i => !string.IsNullOrEmpty(i.EnsureMethod)).Select(i => SensorKindBaker.KindName(i.Kind)).ToList();
        var entries = SensorPaletteEntries.SpawnEntries().ToList();
        Assert.Equal(spawnable.Count, entries.Count);
        Assert.Contains(entries, e => e.DisplayName == "Spawn Sensor: DangerArea");
        Assert.DoesNotContain(entries, e => e.DisplayName == "Spawn Sensor: Visual");
    }

    [Fact]
    public void CE3078_Stage0_ProjectsExec_OnePinPerSettingsField_Key_AndSensor()
    {
        var asset = BlueprintAssetBuilder.Instance("SpawnPins").WithGraph("Tick", g => g.Entry().Return()).Build();
        var node = new SpawnSensorNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.DangerArea) };
        asset.Graphs[0].Nodes.Add(node);
        Stage0_Rehydrate.Run(asset, DefaultOptions());

        Assert.Equal(new[] { ("In", "In"), ("Out", "Out") }, node.Pins.Where(p => p.IsExec).Select(p => (p.Name, p.Direction)));
        var data = node.Pins.Where(p => !p.IsExec).Select(p => (p.Name, p.Direction, p.TypeRef!.TypeId)).ToList();
        Assert.Equal(SpawnSensorNode.DataPins(node.Decl).ToList(), data);
        Assert.Contains(("RoutePoint", "In", "System.Numerics.Vector3"), data);
        Assert.Equal(("Sensor", "Out", "Fdp.Core.Entity"), data[^1]);
    }

    [Fact]
    [CoversDiagnosticCode("BP2075")]
    public void CE3078_Validate_SpawningAKindNoBehaviourSpawns_BP2075()
    {
        var asset = BlueprintAssetBuilder.Instance("SpawnVisual").WithGraph("Tick", g => g.Entry().Return()).Build();
        asset.Graphs[0].Nodes.Add(new SpawnSensorNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.Visual) });
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2075);
    }

    [Fact]
    [CoversDiagnosticCode("BP2074")]
    [CoversDiagnosticCode("BP2073")]
    public void CE3078_Validate_SpawnSensor_NoDecl_BP2074_AndLibraryDispatch_BP2073()
    {
        var a = BlueprintAssetBuilder.Instance("SpawnNoDecl").WithGraph("Tick", g => g.Entry().Return()).Build();
        a.Graphs[0].Nodes.Add(new SpawnSensorNode { Id = Guid.NewGuid() });
        Assert.Contains(Validate(a), d => d.Code == DiagnosticCodes.BP2074);

        var lib = BlueprintAssetBuilder.Library("SpawnLib").WithGraph("Main", g => g.Entry().Return()).Build();
        lib.Graphs[0].Nodes.Add(new SpawnSensorNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.DangerArea) });
        Assert.Contains(Validate(lib), d => d.Code == DiagnosticCodes.BP2073);
    }

    // ── runtime ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Tick: Entry → SpawnSensor(DangerArea, CorridorHalfWidth = 33) → SetVariable(Sensor) → Return.</summary>
    internal static BlueprintAsset BuildSpawnAsset()
    {
        var sensorVar = new VariableDecl { Id = Guid.NewGuid(), Name = "Sensor", Type = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var spawn = new SpawnSensorNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.DangerArea) };
        var sIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var sOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        spawn.Pins.AddRange(new[] { sIn, sOut });
        foreach (var (name, dir, typeId) in SpawnSensorNode.DataPins(spawn.Decl))
            spawn.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } });
        Pin P(string n) => spawn.Pins.Single(p => !p.IsExec && p.Name == n);

        var width = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "33", TypeId = "System.Single" };
        var widthOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        width.Pins.Add(widthOut);

        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = sensorVar.Id.ToString() };
        var setIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setVal = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        set.Pins.AddRange(new[] { setIn, setOut, setVal });

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, spawn, width, set, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = spawn.Id, ToPinId = sIn.Id },
                new Link { FromNodeId = spawn.Id, FromPinId = sOut.Id,     ToNodeId = set.Id,   ToPinId = setIn.Id },
                new Link { FromNodeId = set.Id,   FromPinId = setOut.Id,   ToNodeId = ret.Id,   ToPinId = retIn.Id },
                new Link { FromNodeId = width.Id, FromPinId = widthOut.Id, ToNodeId = spawn.Id, ToPinId = P("CorridorHalfWidth").Id },
                new Link { FromNodeId = spawn.Id, FromPinId = P("Sensor").Id, ToNodeId = set.Id, ToPinId = setVal.Id },
            },
        };
        return new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "SpawnDangerTest", Dispatch = BlueprintDispatchKind.Instance,
            Variables = { sensorVar }, Graphs = { graph },
        };
    }

    private static T Slot<T>(BlueprintTestFixture fixture, BlueprintAsset asset, Entity entity, string field) where T : unmanaged
    {
        Assert.True(fixture.Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def));
        var state = fixture.GetBlueprintState(asset, entity);
        var offset = (int)Marshal.OffsetOf(def!.StateClrType!, field);
        return MemoryMarshal.Read<T>(state!.Value.AsSpan().Slice(offset, Unsafe.SizeOf<T>()));
    }

    /// <summary>🔴 the acceptance spawn (§7.10a): the blueprint creates the unit's danger sensor through
    /// <c>DangerAreaChildSensor.Ensure</c> — the wired pin over <c>Default</c>, the rest kept — once (find-or-create).</summary>
    [Fact]
    public void CE3078_ABlueprintSpawns_TheDangerSensor_ThroughTheKindsEnsure_Once()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var w = fixture.World;
        w.RegisterComponent<SensorTag>(); w.RegisterComponent<PartMetadata>(); w.RegisterComponent<EqsSensor>();
        w.RegisterComponent<EqsCognitiveBuffer>(); w.RegisterComponent<ScenarioIgnoreTag>(); w.RegisterComponent<BehaviorOwnedPart>();
        w.RegisterComponent<DangerAreaSensor>(); w.RegisterComponent<DangerAreaCognitiveBuffer>();
        var asset = BuildSpawnAsset();
        fixture.CompileAndLoad(asset);
        var unit = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, unit);

        fixture.TickFrame(0.016f);
        var child = UnitSensors.Of(w, unit, SensorModality.DangerArea);
        Assert.False(child.IsNull, "the blueprint must have created the unit's danger sensor");
        Assert.Equal(child, Slot<Entity>(fixture, asset, unit, "Sensor"));
        var settings = w.GetComponentRO<DangerAreaSensor>(child).Settings;
        Assert.Equal(33f, settings.CorridorHalfWidth);                                   // the wired pin
        Assert.Equal(DangerAreaSettings.Default.RefreshSeconds, settings.RefreshSeconds);  // unwired: the Default kept
        Assert.Equal(DangerAreaSettings.Default.MaxAreas, settings.MaxAreas);

        fixture.TickFrame(0.016f);
        Assert.Equal(child, UnitSensors.Of(w, unit, SensorModality.DangerArea));
        int sensors = 0;
        foreach (var _ in w.Query().With<DangerAreaSensor>().Build()) sensors++;
        Assert.Equal(1, sensors);                                                          // find-or-create: still one
    }
}
