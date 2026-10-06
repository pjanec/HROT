using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Replication.Components;
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
/// ⭐ <c>CE-3078</c> N2 — <c>ReadSensorResult(kind, Index)</c>: the editor bakes the kind's types from the toolkit's
/// <see cref="SensorKindRegistry"/>; the compiler lowers only from the baked decl. 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.10a.
/// </summary>
[Collection("DebugProbe")]
public sealed class ReadSensorResultTests
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

    // ── the bake (D1) ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CE3078_Bake_DangerArea_CarriesTheAreaFamilysTypes_AndEveryElementField()
    {
        var d = SensorKindBaker.Bake(SensorModality.DangerArea)!;
        Assert.Equal((byte)SensorModality.DangerArea, d.Kind);
        Assert.Equal("Area", d.Family);
        Assert.Equal(typeof(DangerAreaCognitiveBuffer).FullName, d.ResultComponentFqn);
        Assert.Equal(typeof(DangerAreaDescriptor).FullName, d.ElementTypeFqn);
        Assert.Contains(d.ElementFields, f => f.Name == nameof(DangerAreaDescriptor.NearSideHandle) && f.TypeId == "System.Numerics.Vector3");
        Assert.Contains(d.ElementFields, f => f.Name == nameof(DangerAreaDescriptor.ThreatRating) && f.TypeId == "System.Single");
        Assert.Contains(d.ElementFields, f => f.Name == nameof(DangerAreaDescriptor.Kind) && f.TypeId == "global::" + typeof(DangerAreaKind).FullName);   // an enum pin: the global:: rule
        Assert.Equal(typeof(DangerAreaDescriptor).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length,
            d.ElementFields.Count);   // every public instance field, nothing dropped
    }

    [Fact]
    public void CE3078_Palette_OffersOneReadEntryPerRegisteredUnitKind()
    {
        var kinds = SensorKindRegistry.All.Where(i => i.Kind != SensorKindRegistry.EqsQuery).Select(i => SensorKindBaker.KindName(i.Kind)).ToList();
        var entries = SensorPaletteEntries.ReadEntries().ToList();
        Assert.Equal(kinds.Count, entries.Count);
        foreach (var k in kinds) Assert.Contains(entries, e => e.DisplayName == $"Read Sensor Result: {k}");
        var node = Assert.IsType<ReadSensorResultNode>(entries.Single(e => e.DisplayName.EndsWith("DangerArea")).CreateInstance());
        Assert.Equal((byte)SensorModality.DangerArea, node.Decl!.Kind);
    }

    // ── Stage0: the pins come from the baked decl — the SAME projection the editor uses ─────────────────────────────

    [Fact]
    public void CE3078_Stage0_ProjectsIndex_TheHeader_AndOnePinPerElementField()
    {
        var asset = BlueprintAssetBuilder.Instance("RsrPins").WithGraph("Tick", g => g.Entry().Return()).Build();
        var node = new ReadSensorResultNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.DangerArea) };
        asset.Graphs[0].Nodes.Add(node);

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        var expected = ReadSensorResultNode.DataPins(node.Decl).Select(p => (p.Name, p.Direction, p.TypeId)).ToList();
        var actual = node.Pins.Select(p => (p.Name, p.Direction, p.TypeRef!.TypeId)).ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(("Index", "In", "System.Int32"), actual[0]);
        Assert.Contains(("NearSideHandle", "Out", "System.Numerics.Vector3"), actual);
        Assert.DoesNotContain(node.Pins, p => p.IsExec);   // pure
    }

    // ── Stage2: no silent no-op ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    [CoversDiagnosticCode("BP2074")]
    public void CE3078_Validate_ANodeWithNoBakedKind_BP2074()
    {
        var asset = BlueprintAssetBuilder.Instance("RsrNoDecl").WithGraph("Tick", g => g.Entry().Return()).Build();
        asset.Graphs[0].Nodes.Add(new ReadSensorResultNode { Id = Guid.NewGuid() });
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2074);
    }

    [Fact]
    [CoversDiagnosticCode("BP2073")]
    public void CE3078_Validate_LibraryDispatch_BP2073()
    {
        var asset = BlueprintAssetBuilder.Library("RsrLib").WithGraph("Main", g => g.Entry().Return()).Build();
        asset.Graphs[0].Nodes.Add(new ReadSensorResultNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.DangerArea) });
        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2073);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2074);
    }

    // ── runtime: a blueprint reads a hand-filled danger-area sensor ─────────────────────────────────────────────────

    internal static (BlueprintAsset asset, Guid readId) BuildReadAsset(int index)
    {
        var readyVar  = new VariableDecl { Id = Guid.NewGuid(), Name = "Ready",  Type = new BlueprintTypeRef { TypeId = "bool" } };
        var countVar  = new VariableDecl { Id = Guid.NewGuid(), Name = "Areas",  Type = new BlueprintTypeRef { TypeId = "int" } };
        var nearVar   = new VariableDecl { Id = Guid.NewGuid(), Name = "Near",   Type = new BlueprintTypeRef { TypeId = "System.Numerics.Vector3" } };
        var threatVar = new VariableDecl { Id = Guid.NewGuid(), Name = "Threat", Type = new BlueprintTypeRef { TypeId = "float" } };

        var read = new ReadSensorResultNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(SensorModality.DangerArea) };
        foreach (var (name, dir, typeId, _) in ReadSensorResultNode.DataPins(read.Decl))
            read.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } });
        Pin Out(string n) => read.Pins.Single(p => p.Name == n);

        var idx = new LiteralNode { Id = Guid.NewGuid(), ValueJson = index.ToString(), TypeId = "System.Int32" };
        var idxOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        idx.Pins.Add(idxOut);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret.Pins.Add(retIn);

        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function, Nodes = { entry, read, idx, ret } };
        graph.Links.Add(new Link { FromNodeId = idx.Id, FromPinId = idxOut.Id, ToNodeId = read.Id, ToPinId = Out("Index").Id });
        Guid prevNode = entry.Id; Guid prevPin = entryOut.Id;
        foreach (var (v, pin) in new[] { (readyVar, "IsReady"), (countVar, "Count"), (nearVar, "NearSideHandle"), (threatVar, "ThreatRating") })
        {
            var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = v.Id.ToString() };
            var i = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
            var o = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
            var val = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new() };
            set.Pins.AddRange(new[] { i, o, val });
            graph.Nodes.Add(set);
            graph.Links.Add(new Link { FromNodeId = prevNode, FromPinId = prevPin, ToNodeId = set.Id, ToPinId = i.Id });
            graph.Links.Add(new Link { FromNodeId = read.Id, FromPinId = Out(pin).Id, ToNodeId = set.Id, ToPinId = val.Id });
            prevNode = set.Id; prevPin = o.Id;
        }
        graph.Links.Add(new Link { FromNodeId = prevNode, FromPinId = prevPin, ToNodeId = ret.Id, ToPinId = retIn.Id });

        return (new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ReadDangerTest", Dispatch = BlueprintDispatchKind.Instance,
            Variables = { readyVar, countVar, nearVar, threatVar }, Graphs = { graph },
        }, read.Id);
    }

    private static T Slot<T>(BlueprintTestFixture fixture, BlueprintAsset asset, Entity entity, string field) where T : unmanaged
    {
        Assert.True(fixture.Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def));
        var state = fixture.GetBlueprintState(asset, entity);
        var offset = (int)Marshal.OffsetOf(def!.StateClrType!, field);
        return MemoryMarshal.Read<T>(state!.Value.AsSpan().Slice(offset, Unsafe.SizeOf<T>()));
    }

    private static Entity DangerSensorOf(BlueprintTestFixture fixture, Entity unit)
    {
        var child = fixture.CreateEntity();
        fixture.World.AddComponent(child, new SensorTag { Kind = SensorModality.DangerArea });
        fixture.World.AddComponent(child, new PartMetadata { ParentEntity = unit, InstanceId = 1000 });
        return child;
    }

    private static DangerAreaDescriptor Area(uint id, float threat, Vector3 near)
        => new() { FeatureId = id, ThreatRating = threat, Kind = DangerAreaKind.StreetCrossing, NearSideHandle = near };

    /// <summary>🔴 the acceptance read (§7.10a): entry Index of the unit's danger sensor through the blueprint — IsReady,
    /// Count, and the element's own fields; not ready / no sensor ⇒ IsReady false.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CE3078_ABlueprintReads_TheUnitsDangerSensor_EntryIndex(int index)
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<SensorTag>();
        fixture.World.RegisterComponent<PartMetadata>();
        fixture.World.RegisterComponent<DangerAreaCognitiveBuffer>();
        var (asset, _) = BuildReadAsset(index);
        fixture.CompileAndLoad(asset);
        var unit = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, unit);

        fixture.TickFrame(0.016f);
        Assert.False(Slot<bool>(fixture, asset, unit, "Ready"), "no sensor yet");

        var child = DangerSensorOf(fixture, unit);
        fixture.World.AddComponent(child, new DangerAreaCognitiveBuffer());   // there, no answer
        fixture.TickFrame(0.016f);
        Assert.False(Slot<bool>(fixture, asset, unit, "Ready"), "no answer yet");

        var buf = new DangerAreaCognitiveBuffer { Count = 2, LastUpdateTick = 5 };
        buf.GetSpanRW()[0] = Area(1, 0.8f, new Vector3(101f, 100f, 0f));
        buf.GetSpanRW()[1] = Area(2, 0.2f, new Vector3(150f, 120f, 0f));
        fixture.World.SetComponent(child, buf);
        fixture.TickFrame(0.016f);

        Assert.True(Slot<bool>(fixture, asset, unit, "Ready"));
        Assert.Equal(2, Slot<int>(fixture, asset, unit, "Areas"));
        Assert.Equal(index == 0 ? new Vector3(101f, 100f, 0f) : new Vector3(150f, 120f, 0f), Slot<Vector3>(fixture, asset, unit, "Near"));
        Assert.Equal(index == 0 ? 0.8f : 0.2f, Slot<float>(fixture, asset, unit, "Threat"));
    }
}
