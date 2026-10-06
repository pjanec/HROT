using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Hrot.Hsm.Editor.Renderers;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// CE-1003 — docs/blueprints/Architect_Question_84 §6 (A0 + B): regions as designed, ONE writer for "initial",
/// region-aware validation (HSM-001/002), region add/remove keeps children in their regions (HSM-005), the
/// top-level start marker, and the emitter handing declared regions to the builder as structure.
/// </summary>
public sealed class HsmRegionsAndInitialStateTests
{
    private static (HsmAsset asset, HsmCommandSink sink) Build()
    {
        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            new StateNode("__root__"),
            new List<StateNode>(), new List<TransitionNode>(), new List<GlobalTransitionNode>(),
            new List<RegionNode>(), new List<EventDefinition>());
        return (asset, new HsmCommandSink(asset));
    }

    private static StateNode Add(HsmAsset asset, HsmCommandSink sink, string kind = HsmKinds.Simple, StateNode? parent = null, int region = 0)
    {
        var id = Guid.NewGuid();
        sink.Apply(new GraphCommand.AddNode(new NodeId(id), new NodeKindKey(kind), Vector2.Zero, null));
        var s = asset.FindStateByStableId(id)!;
        if (parent != null)
            sink.Apply(new GraphCommand.ChangeParent(new NodeId(id), new NodeId(parent.StableId), region, Vector2.Zero));
        return s;
    }

    /// <summary>P (parallel) with two regions: R0 = {Worker, Done}, R1 = {Other}.</summary>
    private static (HsmAsset asset, HsmCommandSink sink, StateNode p, StateNode worker, StateNode done, StateNode other) Parallel()
    {
        var (asset, sink) = Build();
        var p = Add(asset, sink, HsmKinds.Parallel);
        sink.Apply(new GraphCommand.AddRegion(new NodeId(p.StableId), 0, "R0", 0));
        sink.Apply(new GraphCommand.AddRegion(new NodeId(p.StableId), 1, "R1", 0));
        var worker = Add(asset, sink, parent: p, region: 0);
        var done   = Add(asset, sink, parent: p, region: 0);
        var other  = Add(asset, sink, parent: p, region: 1);
        return (asset, sink, p, worker, done, other);
    }

    [Fact]
    public void CE1003_SetStartState_InAPlainComposite_ClearsTheSiblings()
    {
        var (asset, sink) = Build();
        var c = Add(asset, sink, HsmKinds.Composite);
        var a = Add(asset, sink, parent: c);
        var b = Add(asset, sink, parent: c);

        asset.SetStartState(a);
        asset.SetStartState(b);

        Assert.False(a.IsInitial);
        Assert.True(b.IsInitial);
        Assert.True(asset.IsStartState(b));
    }

    [Fact]
    public void CE1003_SetStartState_InAParallelRegion_WritesTheRegion_NotTheFlag()
    {
        var (asset, _, p, worker, done, other) = Parallel();
        asset.SetStartState(done);
        asset.SetStartState(other);

        Assert.Same(done,  p.RegionNodes[0].InitialChild);
        Assert.Same(other, p.RegionNodes[1].InitialChild);
        Assert.True(asset.IsStartState(done));
        Assert.False(asset.IsStartState(worker));
    }

    [Fact]
    public void CE1003_SetInitialFromTheMenu_IsUndoable_BackToThePreviousStartState()
    {
        var (asset, sink) = Build();
        var c = Add(asset, sink, HsmKinds.Composite);
        var a = Add(asset, sink, parent: c);
        var b = Add(asset, sink, parent: c);
        asset.SetStartState(a);

        var undo = new List<GraphCommand>();
        var menu = new HsmNodeContextMenuProvider(asset)
        {
            Recorder = (fwd, inv, _) => { sink.Apply(fwd); undo.Add(inv); },
        };
        var item = Assert.Single(menu.GetItemsFor(new NodeId(b.StableId), Array.Empty<NodeId>()));
        Assert.Equal("Set as Initial State", item.Label);
        item.Execute();
        Assert.True(b.IsInitial);
        Assert.False(a.IsInitial);

        sink.Apply(undo.Single());
        Assert.True(a.IsInitial);
        Assert.False(b.IsInitial);
    }

    [Fact]
    public void CE1003_HSM001_ACorrectTwoRegionParallel_IsClean()
    {
        var (asset, _, _, worker, _, other) = Parallel();
        asset.SetStartState(worker);
        asset.SetStartState(other);
        var codes = new HsmValidator().Validate(asset).Select(d => d.Code).ToList();
        Assert.DoesNotContain(HsmDiagnosticCode.MultipleInitialChildrenInSameParent, codes);
        Assert.DoesNotContain(HsmDiagnosticCode.RegionWithoutInitialState, codes);
    }

    [Fact]
    public void CE1003_HSM002_ARegionWithNoStartState_IsAnError()
    {
        var (asset, _, _, worker, _, _) = Parallel();
        asset.SetStartState(worker);                       // R1 (Other) has none
        var d = Assert.Single(new HsmValidator().Validate(asset), x => x.Code == HsmDiagnosticCode.RegionWithoutInitialState);
        Assert.Contains("R1", d.Message);
    }

    [Fact]
    public void CE1003_HSM005_RemovingTheMiddleRegion_ShiftsTheLaterRegionsChildren()
    {
        var (asset, sink) = Build();
        var p = Add(asset, sink, HsmKinds.Parallel);
        for (int i = 0; i < 3; i++)
            sink.Apply(new GraphCommand.AddRegion(new NodeId(p.StableId), i, "R" + i, 0));
        var a = Add(asset, sink, parent: p, region: 0);
        var c = Add(asset, sink, parent: p, region: 2);

        sink.Apply(new GraphCommand.RemoveRegion(new NodeId(p.StableId), 1, ChildRedistributionPolicy.MoveToFirstRegion));

        Assert.Equal(2, p.RegionNodes.Count);
        Assert.Equal(0, a.RegionIndex);
        Assert.Equal(1, c.RegionIndex);                    // was left at 2 — past the end
    }

    [Fact]
    public void CE1003_InsertingARegionInTheMiddle_ShiftsTheLaterRegionsChildren()
    {
        var (asset, sink) = Build();
        var p = Add(asset, sink, HsmKinds.Parallel);
        sink.Apply(new GraphCommand.AddRegion(new NodeId(p.StableId), 0, "R0", 0));
        sink.Apply(new GraphCommand.AddRegion(new NodeId(p.StableId), 1, "R1", 0));
        var b = Add(asset, sink, parent: p, region: 1);

        sink.Apply(new GraphCommand.AddRegion(new NodeId(p.StableId), 1, "New", 0));

        Assert.Equal(2, b.RegionIndex);
        Assert.Equal("R1", p.RegionNodes[b.RegionIndex].Name);
    }

    [Fact]
    public void CE1003_TheTopLevelStartState_GetsAStartMarker()
    {
        var (asset, sink) = Build();
        var idle = Add(asset, sink);
        asset.SetStartState(idle);
        var marker = Assert.Single(HsmInitialArrowRenderer.CollectInitialMarkers(asset));
        Assert.Same(idle, marker.InitialChild);
        Assert.Same(asset.RootState, marker.Container);
    }

    /// <summary>
    /// The asset that showed the defect: drawn as 2 regions (RegionZero = Worker → Done, RegionOne = Worker), run as 3.
    /// Its emitted builder code must now declare both regions and give each its own start state.
    /// </summary>
    [Fact]
    public void CE1003_TheEmitter_HandsDeclaredRegionsToTheBuilder_WithEachRegionsOwnStartState()
    {
        var dir = System.IO.Path.GetDirectoryName(typeof(HsmRegionsAndInitialStateTests).Assembly.Location)!;
        for (int i = 0; i < 7; i++) dir = System.IO.Path.GetDirectoryName(dir)!;
        var path = System.IO.Path.Combine(dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "HSMs", "HsmCuratedBindingDemo.hsm.json");
        var dto = Hrot.AiEditor.Persistence.Hsm.HsmJsonServices.Deserialize(System.IO.File.ReadAllText(path))!;

        var code = HsmEmitCore.EmitTopologyCore(dto);

        // The child's builder call runs from `.Child("Name"` to the next `.Child(` (or the end of the method).
        string Config(string name)
        {
            int i = code.IndexOf(".Child(\"" + name + "\"", StringComparison.Ordinal);
            Assert.True(i >= 0, $"state {name} not emitted as a child:\n" + code);
            int next = code.IndexOf(".Child(", i + 7, StringComparison.Ordinal);
            return code.Substring(i, (next < 0 ? code.Length : next) - i);
        }
        Assert.Contains(".InRegion(0)", Config("RegionZeroWorker"));
        Assert.Contains(".Initial()",   Config("RegionZeroWorker"));
        Assert.Contains(".InRegion(0)", Config("RegionZeroDone"));
        Assert.DoesNotContain(".Initial()", Config("RegionZeroDone"));
        Assert.Contains(".InRegion(1)", Config("RegionOneWorker"));
        Assert.Contains(".Initial()",   Config("RegionOneWorker"));
    }
}
