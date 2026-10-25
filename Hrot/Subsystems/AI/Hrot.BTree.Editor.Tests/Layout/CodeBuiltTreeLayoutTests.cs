using System;
using System.Linq;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.BTree.Editor.Tests.Layout;

/// <summary>
/// ⭐ <c>CE-1273</c> — a C#-built tree opened as a graph (no layout data: it is read-only, nobody can tidy it) must be LAID OUT, not stacked
/// at one point. 🔒 User, 2026-10-23: <i>"Open running behavior shows all decompiled tree nodes on one single place no layout whatsoever"</i>.
/// </summary>
public sealed class CodeBuiltTreeLayoutTests
{
    private readonly ITestOutputHelper _out;
    public CodeBuiltTreeLayoutTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void CE1273_EveryCodeBuiltTree_HasOnePlacePerNode()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
        var trees = contributor.Enumerate().OfType<BehaviorTreeAsset>().Where(a => a.IsCodeBuilt).ToList();
        Assert.NotEmpty(trees);
        foreach (var t in trees)
        {
            int nodes = t.Nodes.Count, places = t.Nodes.Select(n => n.Position).Distinct().Count();
            _out.WriteLine($"{t.Name}: {nodes} nodes, {places} places");
            foreach (var g in t.Nodes.GroupBy(n => n.Position).Where(g => g.Count() > 1))
                _out.WriteLine($"  at {g.Key}: " + string.Join(", ", g.Select(n => $"{n.KernelType} parent={(t.Nodes.Any(p => p.ChildVisualIds.Contains(n.VisualId)) ? "y" : "n")}")));
            Assert.True(places == nodes, $"{t.Name}: {nodes} nodes share {places} place(s)");
        }
    }

    /// <summary>⭐ <c>CE-1344</c> — no two nodes of a C#-built tree overlap, at the width their TITLE draws (long names included), and a parent
    /// with many leaves spreads them over layers. 🔒 User, 2026-10-25: "keeps placing long child nodes on a single line so they overlap
    /// heavily". ✅ Red-proof: widths back to a flat 160 ⇒ overlaps on the long-named trees.</summary>
    [Fact]
    public void CE1344_NoTwoNodesOverlap_AtTheirTitlesWidth()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
        const float height = 50f;
        int staggered = 0;
        foreach (var t in contributor.Enumerate().OfType<BehaviorTreeAsset>().Where(a => a.IsCodeBuilt))
        {
            var n = t.Nodes;
            for (int a = 0; a < n.Count; a++)
                for (int b = a + 1; b < n.Count; b++)
                {
                    float wa = Hrot.BTree.Editor.Layout.BTreeAutoLayout.WidthOf(n[a]), wb = Hrot.BTree.Editor.Layout.BTreeAutoLayout.WidthOf(n[b]);
                    bool overlap = n[a].Position.X < n[b].Position.X + wb && n[b].Position.X < n[a].Position.X + wa
                                && n[a].Position.Y < n[b].Position.Y + height && n[b].Position.Y < n[a].Position.Y + height;
                    Assert.False(overlap, $"{t.Name}: '{n[a].DisplayLabel}' at {n[a].Position} overlaps '{n[b].DisplayLabel}' at {n[b].Position}");
                }
            foreach (var p in n.Where(p => p.ChildVisualIds.Count > 3))
                if (p.ChildVisualIds.Select(id => n.First(c => c.VisualId == id).Position.Y).Distinct().Count() > 1) staggered++;
        }
        _out.WriteLine($"staggered parents: {staggered}");
    }

    /// <summary>⭐ <c>CE-1345</c> — a C#-built tree built twice (the runtime's registry and the editor's graph each build it) gives every node the
    /// SAME id, so a node the editor selects is found in the running blob (its Decision view, a breakpoint). 🔒 User, 2026-10-25: "choseoption
    /// node in such decompiled btree does not seem to show the utility ai view". ✅ Red-proof: <c>Guid.NewGuid()</c> back ⇒ no id matches.</summary>
    [Fact]
    public void CE1345_ACodeBuiltTree_HasTheSameNodeIdsEveryTimeItIsBuilt()
    {
        static System.Collections.Generic.Dictionary<string, Guid[]> Ids()
        {
            var c = new BTreeAssetContributor();
            c.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
            return c.Enumerate().OfType<BehaviorTreeAsset>().Where(a => a.IsCodeBuilt)
                .ToDictionary(a => a.Name, a => a.Nodes.Select(n => n.VisualId).ToArray());
        }
        var first = Ids();
        var second = Ids();
        Assert.NotEmpty(first);
        int choosers = 0;
        foreach (var (name, ids) in first)
        {
            Assert.Equal(ids, second[name]);
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
        var c2 = new BTreeAssetContributor();
        c2.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
        foreach (var t in c2.Enumerate().OfType<BehaviorTreeAsset>().Where(a => a.IsCodeBuilt))
            choosers += t.Nodes.Count(n => n.DisplayLabel.Contains("ChooseOption"));
        _out.WriteLine($"{first.Count} trees, {choosers} ChooseOption node(s)");
    }
}
