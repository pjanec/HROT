using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fdp.Presentation.Icons;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Layout;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.BTree.Editor.Tests.Layout;

/// <summary>
/// ⭐ <c>CE-1368</c> (BTree host design §3.4) — the editor opens the JSON asset (it wins the AssetId collision), and its STORED positions were
/// never touched by the auto layout: BasicSquadSop drew 73 overlapping pairs. The open path now tidies a stored layout IN MEMORY when two
/// drawn rectangles overlap (and leaves a clean one exactly as authored), and "Reset layout" on the canvas menu tidies on demand.
/// 🔒 User, 2026-10-27: "the nodes still overlap so on opening a bit more complex btree it is just a big mess … BasicSquadSop".
/// ⛔ No <c>*.btree.json</c> file is rewritten: these rails only READ them.
/// </summary>
public sealed class Ce1368OpenAndResetLayoutTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public Ce1368OpenAndResetLayoutTests(ITestOutputHelper output) => _out = output;
    public void Dispose() => _atlas.Dispose();

    /// <summary>The trees the user's measurement named as overlapping when stored (CE-1368 brief).</summary>
    private static readonly string[] Overlapping =
    {
        // ⭐ CE-1410 (R-1042): the SOPs, tactics and hill-attack trees on this list are C#-built now (CodeBuiltTreeLayoutTests covers them)
        "T11_Aliasing", "BTreeRenderShowcase",
    };

    private static string BTreesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HROT.sln"))) dir = dir.Parent;
        dir.Should().NotBeNull("the repo root (HROT.sln) must be above the test binary");
        return Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "BTrees");
    }

    /// <summary>Every shipped <c>*.btree.json</c>, loaded by the contributor the editor's catalog uses.</summary>
    private static List<BehaviorTreeAsset> LoadShipped()
    {
        var c = new BTreeJsonAssetContributor();
        c.Refresh(rootDirectory: BTreesRoot());
        return c.Enumerate().OfType<BehaviorTreeAsset>().OrderBy(a => a.Name).ToList();
    }

    private AiEditorAdapterBundle Bundle() => new(_atlas);

    private static float Header => BTreeAutoLayout.DefaultHeaderHeight;

    private static T WithFont<T>(Func<Func<string, float>, T> body) => CodeBuiltTreeLayoutTests.WithImGuiFont(body);

    /// <summary>a) every shipped JSON tree has 0 overlapping drawn pairs after the open path, and the named ones had some before.
    /// 🔴 Red-proof: the factory's re-lay skipped behind a runtime condition ⇒ the named trees stay overlapping.</summary>
    [Fact]
    public void CE1368_EveryShippedJsonTree_HasNoOverlap_AfterTheOpenPath()
    {
        var trees = LoadShipped();
        trees.Count.Should().BeGreaterThan(20, "the JSON corpus is the samples and the authoring fixtures, 24 trees (CE-1410)");
        var rows = WithFont(measure =>
        {
            var list = new List<(string Name, int Before, int After)>();
            foreach (var t in trees)
            {
                int before = BTreeLayoutCheck.OverlappingPairs(t, measure, Header, out _);
                BTreeDocumentFactory.Build(t, Bundle());   // the open path
                int after = BTreeLayoutCheck.OverlappingPairs(t, measure, Header, out var first);
                list.Add((t.Name, before, after));
                if (after != 0) _out.WriteLine($"{t.Name} still overlaps: {first}");
            }
            return list;
        });
        foreach (var r in rows.Where(r => r.Before > 0)) _out.WriteLine($"{r.Name}: {r.Before} -> {r.After}");
        foreach (var name in Overlapping)
            rows.Single(r => r.Name == name).Before.Should().BeGreaterThan(0, $"{name} was stored overlapping (proves the open path acted)");
        rows.Where(r => r.After != 0).Select(r => r.Name).Should().BeEmpty("no shipped tree may draw overlapping once opened");
    }

    /// <summary>b) + c) a stored layout with no overlap is untouched by the open path; the open path never dirties a document.
    /// 🔴 Red-proof: RelayIfOverlapping re-laying unconditionally ⇒ a clean tree moves.</summary>
    [Fact]
    public void CE1368_ACleanStoredLayout_IsUnchanged_AndNothingIsDirtyAfterOpening()
    {
        var trees = LoadShipped();
        var clean = WithFont(measure =>
        {
            var list = new List<(BehaviorTreeAsset T, Dictionary<Guid, Vector2> Pos)>();
            foreach (var t in trees)
            {
                bool wasClean = BTreeLayoutCheck.OverlappingPairs(t, measure, Header, out _) == 0;
                var pos = t.Nodes.ToDictionary(n => n.VisualId, n => n.Position);
                BTreeDocumentFactory.Build(t, Bundle());
                t.IsDirty.Should().BeFalse($"{t.Name}: re-laying on open is in memory only");
                if (wasClean) list.Add((t, pos));
            }
            return list;
        });
        clean.Should().NotBeEmpty("some shipped trees are stored without overlap");
        foreach (var (t, pos) in clean)
            t.Nodes.ToDictionary(n => n.VisualId, n => n.Position).Should().Equal(pos, $"{t.Name} was clean, so it stays exactly as authored");

        // the synthetic pair: far apart = the author's layout; stacked = tidied, still not dirty
        var apart = Synthetic(overlapping: false);
        var before = apart.Nodes.Select(n => n.Position).ToArray();
        WithFont(_ => { BTreeDocumentFactory.Build(apart, Bundle()); return 0; });
        apart.Nodes.Select(n => n.Position).Should().Equal(before);
        apart.IsDirty.Should().BeFalse();

        var stacked = Synthetic(overlapping: true);
        WithFont(measure =>
        {
            BTreeDocumentFactory.Build(stacked, Bundle());
            BTreeLayoutCheck.OverlappingPairs(stacked, measure, Header, out _).Should().Be(0);
            return 0;
        });
        stacked.IsDirty.Should().BeFalse("the open path's re-lay is not a document change");
    }

    /// <summary>d) "Reset layout" re-lays a tree to 0 overlaps; on a writable document it marks it dirty and Ctrl+Z restores the positions;
    /// on a code-built graph it never marks dirty. 🔴 Red-proof: the command swallowed behind a runtime condition ⇒ overlaps stay.</summary>
    [Fact]
    public void CE1368_ResetLayout_RelaysToZeroOverlaps_DirtyOnWritable_NeverOnCodeBuilt()
    {
        WithFont(measure =>
        {
            // a writable JSON asset
            var writable = LoadShipped().Single(t => t.Name == "BTreeRenderShowcase");   // CE-1410: BasicSquadSop is C# now
            var ctx = BTreeDocumentFactory.Build(writable, Bundle());
            foreach (var n in writable.Nodes) n.Position = Vector2.Zero;   // a hopeless stored layout
            writable.IsDirty.Should().BeFalse();
            BTreeLayoutCheck.OverlappingPairs(writable, measure, Header, out _).Should().BeGreaterThan(0);
            var scrambled = writable.Nodes.Select(n => n.Position).ToArray();
            Reset(ctx);
            BTreeLayoutCheck.OverlappingPairs(writable, measure, Header, out var first).Should().Be(0, first);
            writable.IsDirty.Should().BeTrue("a layout change the author asked for on a writable asset");
            ctx.View.UndoLast();
            writable.Nodes.Select(n => n.Position).Should().Equal(scrambled, "Reset layout is undoable");

            // a code-built (read-only) graph
            var c = new BTreeAssetContributor();
            c.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
            var built = c.Enumerate().OfType<BehaviorTreeAsset>().First(a => a.IsCodeBuilt && a.Nodes.Count > 4);
            var bctx = BTreeDocumentFactory.Build(built, Bundle());
            foreach (var n in built.Nodes) n.Position = Vector2.Zero;
            BTreeLayoutCheck.OverlappingPairs(built, measure, Header, out _).Should().BeGreaterThan(0);
            Reset(bctx);
            BTreeLayoutCheck.OverlappingPairs(built, measure, Header, out first).Should().Be(0, first);
            built.IsDirty.Should().BeFalse("on a code-built graph Reset layout is view state (CE-1275)");
            return 0;
        });
    }

    private static void Reset(AiCanvasContext ctx)
    {
        var item = ctx.View.Host.CanvasContextMenu!.GetItems().Single(i => i.Label == "Reset layout");
        item.Enabled.Should().BeTrue();
        item.Execute();
    }

    /// <summary>A stored (not auto-laid-out) tree: a root over two leaves, either spread far apart or stacked on one spot.</summary>
    private static BehaviorTreeAsset Synthetic(bool overlapping)
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "Synthetic", "/s.btree.json", true, "BB", "Ctx",
            new Fbt.BehaviorTreeBlob { TreeName = "S", Nodes = Array.Empty<Fbt.NodeDefinition>(), MethodNames = Array.Empty<string>(),
                FloatParams = Array.Empty<float>(), IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>() });
        var a = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = Fbt.NodeType.Action, DisplayLabel = "A", Position = overlapping ? Vector2.Zero : new Vector2(-400, 400) };
        var b = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = Fbt.NodeType.Action, DisplayLabel = "B", Position = overlapping ? Vector2.Zero : new Vector2(400, 400) };
        var root = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = Fbt.NodeType.Root, DisplayLabel = "Root", Position = new Vector2(0, -500) };
        root.ChildVisualIds.Add(a.VisualId); root.ChildVisualIds.Add(b.VisualId);
        asset.AddNode(root); asset.AddNode(a); asset.AddNode(b);
        return asset;
    }
}
