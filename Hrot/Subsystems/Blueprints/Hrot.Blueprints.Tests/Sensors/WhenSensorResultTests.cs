using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Perception.Components;
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
/// ⭐ <c>CE-3078</c> N4 — <c>When SensorResult(kind, trigger)</c>, decided by the baked trigger's SHAPE (Q3, D4); field
/// triggers compare every tick (the area family is re-rated between answers). 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.10a/b.
/// </summary>
[Collection("DebugProbe")]
public sealed class WhenSensorResultTests
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

    private static WhenNode When(string trigger, SensorKindDecl? decl, float threshold = 0f)
        => new() { Id = Guid.NewGuid(), Mode = WhenMode.SensorResult, Edges = WhenEdge.RisingEdge | WhenEdge.FallingEdge,
                   SensorResult = new SensorResultPayload { Decl = decl, Trigger = trigger, Threshold = threshold } };

    // ── the bake and the editor offer (Q3) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CE3078_Bake_CarriesTheTriggersAsData_AndOffersBecomesStaleOnlyWithAnAnswerTime()
    {
        var area = SensorKindBaker.Bake(SensorModality.DangerArea)!;
        Assert.Contains(area.Triggers!, t => t.Name == "ThreatCrossed" && t.ElementField == "ThreatRating" && t.Shape == "FieldCrossed");
        Assert.Contains(area.Triggers!, t => t.Name == "NextAreaChanged" && t.ElementField == "FeatureId" && t.Shape == "FieldChanged");
        Assert.True(area.HasAnswerTime);                                                          // Q4 (backend, lean (a)): DangerAreaCognitiveBuffer carries one now
        Assert.Contains(SensorPaletteEntries.UsableTriggers(area), t => t.Name == "BecomesStale");
        var timeless = SensorKindBaker.Bake(SensorModality.DangerArea)!;
        timeless.HasAnswerTime = false;                                                           // a family WITHOUT an answer time is still not offered it
        Assert.DoesNotContain(SensorPaletteEntries.UsableTriggers(timeless), t => t.Name == "BecomesStale");
        var visual = SensorKindBaker.Bake(SensorModality.Visual)!;
        Assert.True(visual.HasAnswerTime);                                                         // EqsCognitiveBuffer has one
        Assert.Contains(SensorPaletteEntries.UsableTriggers(visual), t => t.Name == "BecomesStale");

        var entry = SensorPaletteEntries.WhenEntries().Single(e => e.DisplayName == "When Sensor Result: DangerArea");
        var node = Assert.IsType<WhenNode>(entry.CreateInstance());
        Assert.Equal(WhenMode.SensorResult, node.Mode);
        Assert.Equal("NextAreaChanged", node.SensorResult!.Trigger);                               // the kind's own first trigger
    }

    // ── Stage2: nothing undecidable compiles ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [CoversDiagnosticCode("BP2076")]
    [InlineData("NoSuchTrigger")]
    [InlineData("BecomesStale")]   // a decl whose answer carries no time (Q4 gave the area family one — cleared here)
    public void CE3078_Validate_AnUndecidableTrigger_BP2076(string trigger)
    {
        var asset = BlueprintAssetBuilder.Instance("WhenBad").WithGraph("Tick", g => g.Entry().Return()).Build();
        var decl = SensorKindBaker.Bake(SensorModality.DangerArea)!;
        decl.HasAnswerTime = false;
        asset.Graphs[0].Nodes.Add(When(trigger, decl));
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2076);
    }

    [Fact]
    public void CE3072_Q4_BecomesStale_OnTheAreaFamily_IsDecidable()
    {
        var asset = BlueprintAssetBuilder.Instance("WhenStale").WithGraph("Tick", g => g.Entry().Return()).Build();
        asset.Graphs[0].Nodes.Add(When("BecomesStale", SensorKindBaker.Bake(SensorModality.DangerArea), threshold: 5f));
        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP2076);
    }

    [Fact]
    public void CE3078_Validate_ANodeWithNoBakedKind_BP2074()
    {
        var asset = BlueprintAssetBuilder.Instance("WhenNoDecl").WithGraph("Tick", g => g.Entry().Return()).Build();
        asset.Graphs[0].Nodes.Add(When("ThreatCrossed", null));
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2074);
    }

    // ── runtime ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Tick: Entry → When(DangerArea, trigger) — OnFired → Fired = true, OnEnded → Ended = true; Out → Return.</summary>
    internal static BlueprintAsset BuildWhenAsset(string trigger, float threshold = 0f)
    {
        var fired = new VariableDecl { Id = Guid.NewGuid(), Name = "Fired", Type = new BlueprintTypeRef { TypeId = "bool" }, DefaultValueJson = "false" };
        var ended = new VariableDecl { Id = Guid.NewGuid(), Name = "Ended", Type = new BlueprintTypeRef { TypeId = "bool" }, DefaultValueJson = "false" };
        var when = When(trigger, SensorKindBaker.Bake(SensorModality.DangerArea), threshold);
        var wIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var wOut   = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var wFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        var wEnded = new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() };
        when.Pins.AddRange(new[] { wIn, wOut, wFired, wEnded });

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function, Nodes = { entry, when } };
        graph.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = when.Id, ToPinId = wIn.Id });

        void Branch(Pin from, VariableDecl v)
        {
            var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
            var litOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
            lit.Pins.Add(litOut);
            var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = v.Id.ToString() };
            var i = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
            var o = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
            var val = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new() };
            set.Pins.AddRange(new[] { i, o, val });
            var ret = new ReturnNode { Id = Guid.NewGuid() };
            var retIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
            ret.Pins.Add(retIn);
            graph.Nodes.Add(lit); graph.Nodes.Add(set); graph.Nodes.Add(ret);
            graph.Links.Add(new Link { FromNodeId = when.Id, FromPinId = from.Id, ToNodeId = set.Id, ToPinId = i.Id });
            graph.Links.Add(new Link { FromNodeId = lit.Id, FromPinId = litOut.Id, ToNodeId = set.Id, ToPinId = val.Id });
            graph.Links.Add(new Link { FromNodeId = set.Id, FromPinId = o.Id, ToNodeId = ret.Id, ToPinId = retIn.Id });
        }
        Branch(wFired, fired);
        Branch(wEnded, ended);
        var outRet = new ReturnNode { Id = Guid.NewGuid() };
        var outRetIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        outRet.Pins.Add(outRetIn);
        graph.Nodes.Add(outRet);
        graph.Links.Add(new Link { FromNodeId = when.Id, FromPinId = wOut.Id, ToNodeId = outRet.Id, ToPinId = outRetIn.Id });

        return new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "WhenDangerTest", Dispatch = BlueprintDispatchKind.Instance,
            Variables = { fired, ended }, Graphs = { graph },
        };
    }

    private sealed class Run : IDisposable
    {
        public readonly BlueprintTestFixture Fixture = new(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        public readonly BlueprintAsset Asset;
        public readonly Entity Unit, Sensor;

        public Run(BlueprintAsset asset)
        {
            Asset = asset;
            var w = Fixture.World;
            w.RegisterComponent<SensorTag>(); w.RegisterComponent<PartMetadata>(); w.RegisterComponent<DangerAreaCognitiveBuffer>();
            Fixture.CompileAndLoad(asset);
            Unit = Fixture.CreateEntity();
            Fixture.AttachBlueprint(asset, Unit);
            Sensor = Fixture.CreateEntity();
            w.AddComponent(Sensor, new SensorTag { Kind = SensorModality.DangerArea });
            w.AddComponent(Sensor, new PartMetadata { ParentEntity = Unit, InstanceId = 1000 });
            w.AddComponent(Sensor, new DangerAreaCognitiveBuffer());
        }

        /// <summary>Writes entry 0 (or no area) under answer stamp <paramref name="tick"/>, ticks once, returns (fired, ended) and clears both.</summary>
        public (bool Fired, bool Ended) Tick(uint tick, uint? feature, float threat = 0f)
        {
            var buf = new DangerAreaCognitiveBuffer { LastUpdateTick = tick, Count = feature is null ? 0 : 1 };
            if (feature is { } f) buf.GetSpanRW()[0] = new DangerAreaDescriptor { FeatureId = f, ThreatRating = threat };
            Fixture.World.SetComponent(Sensor, buf);
            Fixture.TickFrame(0.016f);
            var r = (Get("Fired"), Get("Ended"));
            Set("Fired"); Set("Ended");
            return r;
        }

        private int Offset(string field)
        {
            Assert.True(Fixture.Registry.TryGetById(BlueprintIdHash.Compute(Asset.AssetId), out var def));
            return (int)Marshal.OffsetOf(def!.StateClrType!, field);
        }

        private bool Get(string field)
            => MemoryMarshal.Read<bool>(Fixture.GetBlueprintState(Asset, Unit)!.Value.AsSpan().Slice(Offset(field), 1));

        private void Set(string field)
        {
            var span = Fixture.GetBlueprintState(Asset, Unit)!.Value.AsSpan();
            ref byte b = ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref b, Offset(field)), false);
        }

        public void Dispose() => Fixture.Dispose();
    }

    /// <summary>🔴 the acceptance trigger (§7.10a): <c>ThreatCrossed</c> fires once per crossing — up → OnFired, down → OnEnded —
    /// never while it stays, counts from "below" (an area threatened at first sight fires), and ⭐ sees a RE-RATE with no new
    /// answer stamp (the area family is re-rated every Brain tick).</summary>
    [Fact]
    public void CE3078_ThreatCrossed_FiresOncePerCrossing_BothWays_AndOnAReRateWithTheSameStamp()
    {
        using var run = new Run(BuildWhenAsset("ThreatCrossed", threshold: 0.5f));
        Assert.Equal((true, false),  run.Tick(1, 7, 0.8f));   // first sight, already threatened: fires
        Assert.Equal((false, false), run.Tick(1, 7, 0.8f));   // stays: nothing
        Assert.Equal((false, false), run.Tick(2, 7, 0.9f));   // a new answer, still above: nothing
        Assert.Equal((false, true),  run.Tick(2, 7, 0.3f));   // re-rated DOWN under the SAME stamp: OnEnded
        Assert.Equal((false, false), run.Tick(2, 7, 0.3f));
        Assert.Equal((true, false),  run.Tick(3, 7, 0.6f));   // up again: OnFired
        Assert.Equal((false, true),  run.Tick(4, null));      // no area ahead reads as threat 0: OnEnded
    }

    [Fact]
    public void CE3078_NextAreaChanged_FiresWhenEntry0IsADifferentArea()
    {
        using var run = new Run(BuildWhenAsset("NextAreaChanged"));
        Assert.True(run.Tick(1, 1).Fired);     // the first area seen
        Assert.False(run.Tick(2, 1).Fired);    // the same area on a new answer
        Assert.True(run.Tick(3, 2).Fired);     // the next one
        Assert.True(run.Tick(4, null).Fired);  // none ahead any more
    }

    [Fact]
    public void CE3078_Changed_FiresOnEveryNewAnswerAfterTheFirst()
    {
        using var run = new Run(BuildWhenAsset("Changed"));
        Assert.False(run.Tick(1, 1).Fired);   // the first answer is FirstReady's, not a change
        Assert.False(run.Tick(1, 1).Fired);   // same stamp
        Assert.True(run.Tick(2, 1).Fired);    // a new answer
    }
}
