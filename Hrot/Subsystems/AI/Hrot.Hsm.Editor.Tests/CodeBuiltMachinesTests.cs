using System.Linq;
using System.Reflection;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// ⭐ <c>CE-1252</c> (BV-17…BV-19, R-1028) — the HSMs built in C# that were never a graph asset (their method returns a
/// <c>StateMachineGraph</c>) open in the HSM editor as graphs, in memory, and are read-only: an edit is refused, a breakpoint is not.
/// 📄 docs/DESIGN_Entity_Brain_View.md §9.
/// </summary>
public sealed class CodeBuiltMachinesTests
{
    private static readonly Assembly Behaviors = typeof(Hrot.AI.Behaviors.Machines.SampleGuard).Assembly;

    private static HsmAsset Load(string name)
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(Behaviors);
        return (HsmAsset)contributor.Enumerate().Single(a => a.Name == name);
    }

    /// <summary>🔴 Red-proof: drop the <c>StateMachineGraph</c> branch in <c>HsmAssetContributor.LoadFrom</c> ⇒ red (none listed).</summary>
    [Fact]
    public void TheTwelveCSharpMachines_AreListed_AsCodeBuiltGraphs()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(Behaviors);
        var codeBuilt = contributor.Enumerate().Where(AiDocument.IsCodeBuilt).Select(a => a.Name).ToList();

        Assert.True(codeBuilt.Count >= 12, $"{codeBuilt.Count} code-built machines: {string.Join(", ", codeBuilt)}");
        Assert.Contains("PlatoonDefend", codeBuilt);
        Assert.Contains("CivilianHsm", codeBuilt);
        Assert.NotEmpty(Load("PlatoonDefend").AllStates);
    }

    /// <summary>🔴 Red-proof: drop the read-only guard in <c>HsmCommandSink.Apply</c> ⇒ red (the move is applied).</summary>
    [Fact]
    public void ACodeBuiltMachine_RefusesAnEdit_ButTakesABreakpoint()
    {
        var asset = Load("PlatoonDefend");
        Assert.True(new AiDocument(asset, AssetKind.Hsm).IsReadOnly);
        var state = asset.AllStates.First();
        var sink = new HsmCommandSink(asset);

        var edit = sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(state.StableId) }));
        Assert.False(edit.Success);
        Assert.Contains("built in C#", edit.Message);
        Assert.Contains(asset.AllStates, s => s.StableId == state.StableId);
        Assert.False(asset.IsDirty);

        Assert.True(sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(state.StableId), "isBreakpoint", true)).Success);
    }

    /// <summary>⭐ <c>CE-1253</c> (BV-20) — the session takes the metadata of the graph the catalog shows for the unit's RUNNING machine
    /// (a C#-built machine draws new stable ids at every compile, so the registry's own would never match the canvas).
    /// 🔴 Red-proof: drop the <c>RegisterMachine</c> call in <c>HsmAssetContributor.LoadFrom</c> ⇒ red.</summary>
    [Fact]
    public void TheSession_NamesTheRunningMachine_WithTheCatalogsMetadata()
    {
        var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
        registry.Register(77, "PlatoonDefend", new Fdp.Toolkit.Behavior.BehaviorDefinition { Name = "PlatoonDefend", BrainTier = 0 });
        var session = new Hrot.Hsm.Editor.Debug.HsmDebugSession(null, registry);
        var contributor = new HsmAssetContributor(session);
        contributor.LoadFrom(Behaviors);
        var shown = contributor.Enumerate().Single(a => a.Name == "PlatoonDefend");

        using var repo = new Fdp.Core.EntityRepository();
        repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
        var unit = repo.CreateEntity();
        repo.AddComponent(unit, new Fdp.Toolkit.Behavior.Components.BehaviorState { ActiveBehaviorHash = 77 });
        session.Update(repo, unit);

        Assert.Equal(shown.AssetId, session.MetadataAssetId);
    }

    /// <summary>⭐ <c>CE-1255</c> (BV-23) — every C#-built machine is laid out readably: no two siblings (in one band, for a parallel
    /// state) overlap, and every child sits inside its parent's interior. 🔴 Red-proof: give children the parent's canvas position as
    /// their origin (the old absolute grid) ⇒ red.</summary>
    [Fact]
    public void EveryCodeBuiltMachine_IsLaidOut_WithNoOverlaps_AndChildrenInside()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(Behaviors);
        var problems = new System.Collections.Generic.List<string>();
        foreach (var asset in contributor.Enumerate().Where(AiDocument.IsCodeBuilt).Cast<HsmAsset>())
        {
            var sizes = new System.Collections.Generic.Dictionary<StateNode, System.Numerics.Vector2>();
            void Check(StateNode parent, bool root)
            {
                var parentSize = root ? default : Hrot.Hsm.Editor.Layout.HsmAutoLayout.Measure(parent, sizes);
                foreach (var band in parent.Children.GroupBy(c => parent.IsParallel ? c.RegionIndex : 0))
                {
                    var kids = band.ToList();
                    for (int a = 0; a < kids.Count; a++)
                    {
                        var sa = Hrot.Hsm.Editor.Layout.HsmAutoLayout.Measure(kids[a], sizes);
                        if (!root && (kids[a].Position.X < 0 || kids[a].Position.Y < 0 || kids[a].Position.X + sa.X > parentSize.X))
                            problems.Add($"{asset.Name}: {kids[a].Name} outside {parent.Name}");
                        for (int b = a + 1; b < kids.Count; b++)
                        {
                            var sb = Hrot.Hsm.Editor.Layout.HsmAutoLayout.Measure(kids[b], sizes);
                            var pa = kids[a].Position; var pb = kids[b].Position;
                            if (pa.X < pb.X + sb.X && pb.X < pa.X + sa.X && pa.Y < pb.Y + sb.Y && pb.Y < pa.Y + sa.Y)
                                problems.Add($"{asset.Name}: {kids[a].Name} overlaps {kids[b].Name}");
                        }
                    }
                }
                foreach (var c in parent.Children) if (c.IsContainer) Check(c, false);
            }
            Check(asset.RootState, true);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
    }

    /// <summary>⭐ <c>CE-1274</c> — the statechart lays out as a FLOW: the initial state in the first column, a column never taller than six
    /// states, and the states reached from the initial one to its right. 📷 With <c>HROT_RAIL_SHOTS</c> set it writes each machine's boxes and
    /// arrows (<c>hsm-layout.json</c>) for a picture. ✅ Red-proof: the old near-square grid ⇒ red (the platoon machines' first column holds
    /// more than the initial state's column).</summary>
    [Fact]
    public void CE1274_EveryCodeBuiltMachine_FlowsLeftToRight_InShortColumns()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(Behaviors);
        var problems = new System.Collections.Generic.List<string>();
        var dump = new System.Text.Json.Nodes.JsonArray();
        foreach (var asset in contributor.Enumerate().Where(AiDocument.IsCodeBuilt).Cast<HsmAsset>())
        {
            var sizes = new System.Collections.Generic.Dictionary<StateNode, System.Numerics.Vector2>();
            var boxes = new System.Text.Json.Nodes.JsonArray();
            var arrows = new System.Text.Json.Nodes.JsonArray();
            void Walk(StateNode parent, System.Numerics.Vector2 at, bool root)
            {
                foreach (var band in parent.Children.GroupBy(c => parent.IsParallel ? c.RegionIndex : 0))
                {
                    var cols = Hrot.Hsm.Editor.Layout.HsmAutoLayout.Columns(band.ToList());
                    if (cols.Any(c => c.Count > 6)) problems.Add($"{asset.Name}/{parent.Name}: a column of {cols.Max(c => c.Count)}");
                    var initial = band.FirstOrDefault(c => c.IsInitial);
                    if (initial != null && cols.Count > 0 && !cols[0].Contains(initial)) problems.Add($"{asset.Name}/{parent.Name}: initial not first");
                    // ⭐ the PLACED positions follow the flow: a transition to a later column points to the right on the canvas
                    int ColumnOf(StateNode n) => cols.FindIndex(c => c.Contains(n));
                    foreach (var from in band)
                        foreach (var t in from.OutgoingTransitions)
                            if (band.Contains(t.Target) && ColumnOf(t.Target) > ColumnOf(from) && t.Target.Position.X <= from.Position.X)
                                problems.Add($"{asset.Name}: {from.Name} -> {t.Target.Name} points left");
                }
                foreach (var c in parent.Children)
                {
                    var size = Hrot.Hsm.Editor.Layout.HsmAutoLayout.Measure(c, sizes);
                    var abs = root ? c.Position : at + c.Position + new System.Numerics.Vector2(8f, 32f);
                    boxes.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["name"] = c.Name, ["x"] = abs.X, ["y"] = abs.Y, ["w"] = size.X, ["h"] = size.Y, ["container"] = c.IsContainer,
                        ["initial"] = c.IsInitial, ["id"] = c.StableId.ToString(),
                    });
                    foreach (var t in c.OutgoingTransitions)
                        arrows.Add(new System.Text.Json.Nodes.JsonObject { ["from"] = c.StableId.ToString(), ["to"] = t.Target.StableId.ToString() });
                    if (c.IsContainer) Walk(c, abs, false);
                }
            }
            Walk(asset.RootState, default, true);
            dump.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = asset.Name, ["boxes"] = boxes, ["arrows"] = arrows });
        }
        if (System.Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hsm-layout.json"), dump.ToJsonString());
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
    }

    /// <summary>⭐ <c>CE-1275</c> (user: "nodes jump back on drag") — a state DROP and a <c>MoveNodes</c> on a code-built machine move the
    /// BOX: the position changes and stays, the asset is not dirty; the drop never re-parents (the hierarchy is the behaviour); an edit is
    /// still refused. 🔴 Red-proof: <c>IsViewStateCommand</c> without <c>ChangeParentMultiple</c> ⇒ the drop is refused (the bug); the
    /// <c>continue</c> before the re-parent in <c>ApplyChangeParentMultiple</c> removed ⇒ the state changes parent.</summary>
    [Fact]
    public void CE1275_ACodeBuiltMachine_TakesAStateMove_AsViewState_AndNeverReparents()
    {
        var asset = Load("PlatoonDefend");
        var sink = new HsmCommandSink(asset);
        var state = asset.AllStates.First(s => !ReferenceEquals(s, asset.RootState) && s.Parent != null);
        var parentBefore = state.Parent;
        var elsewhere = asset.AllStates.First(s => !ReferenceEquals(s, state) && !ReferenceEquals(s, parentBefore) && !ReferenceEquals(s, asset.RootState));
        var dropped = new System.Numerics.Vector2(321f, 123f);

        var drop = sink.Apply(new GraphCommand.ChangeParentMultiple(new[]
            { new ChangeParentMove(new NodeId(state.StableId), new NodeId(elsewhere.StableId), null, dropped) }));
        Assert.True(drop.Success, drop.Message);
        Assert.Equal(dropped, state.Position);
        Assert.Same(parentBefore, state.Parent);

        var moved = new System.Numerics.Vector2(7f, 8f);
        Assert.True(sink.Apply(new GraphCommand.MoveNodes(new[] { new NodeMove(new NodeId(state.StableId), moved) })).Success);
        Assert.Equal(moved, state.Position);
        Assert.False(asset.IsDirty);

        Assert.False(sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(state.StableId) })).Success);
        Assert.True(sink.IsReadOnly);
    }
}
