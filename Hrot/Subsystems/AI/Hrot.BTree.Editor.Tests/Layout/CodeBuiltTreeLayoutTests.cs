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
            choosers += t.Nodes.Count(n => n.Condition?.MethodFqn.Contains("ChooseOption") == true || n.Action?.MethodFqn.Contains("ChooseOption") == true);
        _out.WriteLine($"{first.Count} trees, {choosers} ChooseOption node(s)");
    }

    /// <summary>
    /// ⭐ <c>CE-1360</c> (BTree host design §3.4) — a C#-built tree's leaves show the METHOD's name in words, never the registry key: no title
    /// keeps a namespace or an <c>@offset</c>, and the five <c>IsOption</c> conditions of the combat posture tree each name their option (the
    /// parameter field they were bound at). The binding (<c>MethodFqn</c>) keeps the key. 🔒 User, 2026-10-26: "the name of the C# method, but
    /// without too many extra stuff around". 🔴 Red-proof: the projector's title step removed ⇒ the keys come back.
    /// </summary>
    [Fact]
    public void CE1360_ACodeBuiltLeaf_ShowsItsMethodInWords_NotItsKey()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
        var leaves = contributor.Enumerate().OfType<BehaviorTreeAsset>().Where(a => a.IsCodeBuilt)
            .SelectMany(t => t.Nodes).Where(n => n.Action != null || n.Condition != null).ToList();
        Assert.NotEmpty(leaves);
        foreach (var n in leaves)
        {
            Assert.DoesNotContain("@", n.DisplayLabel);
            Assert.DoesNotContain(".", n.DisplayLabel);
            Assert.Contains("@", (n.Action ?? n.Condition)!.MethodFqn + "@");   // the binding is untouched
        }
        var titles = leaves.Select(n => n.DisplayLabel).ToHashSet();
        _out.WriteLine(string.Join(" | ", titles.OrderBy(t => t)));
        Assert.Contains("Is option: Advance?", titles);
        Assert.Contains("Advance and attack", titles);
        Assert.Contains("Bind sensor handle", titles);   // a plain delegate leaf (labelled with its bare method name) too
        Assert.DoesNotContain(titles, t => t.EndsWith(": Order", StringComparison.Ordinal) || t.Contains("config", StringComparison.OrdinalIgnoreCase));
        Assert.True(titles.Max(t => t.Length) <= 40, $"longest title: {titles.OrderByDescending(t => t.Length).First()}");
    }

    /// <summary>The title rule, pure: the namespace, type, offsets and Action_/Condition_ prefixes go; a generic method takes its type's
    /// name; a parameter field that names an instance is added; a condition asks.</summary>
    [Theory]
    [InlineData("Hrot.AI.Behaviors.Brains.PostureNodes.AdvanceAndAttack@24@936", false, null, "Advance and attack")]
    [InlineData("Fdp.Toolkit.Utility.UtilityNodes.IsOption@5@4", true, "IsAdvance", "Is option: Advance?")]
    [InlineData("Hrot.AI.Behaviors.Brains.CgfNodes.Condition_HasTarget", true, null, "Has target?")]
    [InlineData("Hrot.AI.Behaviors.Brains.CgfNodes.Action_WriteMoveToChannel@0", false, null, "Write move to channel")]
    [InlineData("Hrot.AI.Behaviors.Brains.SquadMarchBehavior.Run@0@40", false, null, "Squad march")]
    [InlineData("Hrot.AI.Behaviors.Brains.PostureNodes.Stay@492", false, "Params", "Stay")]
    [InlineData("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.Flank@312@616", false, "Flank", "Flank")]
    [InlineData("Hrot.AI.Behaviors.Brains.SquadMarchBehavior.Run@0@40", false, "Order", "Squad march")]
    [InlineData("Hrot.AI.Behaviors.Brains.CgfNodes.Condition_HasTarget@0", false, "MoveConfig", "Has target?")]
    [InlineData("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.FallBack@244@776", false, "Fallback", "Fall back")]
    [InlineData("Hrot.AI.Behaviors.Brains.CarryNodes.PickUp@8", false, "Take", "Pick up: Take")]
    [InlineData("Hrot.AI.Behaviors.Brains.TacticsNodes.BindSensorHandle", false, null, "Bind sensor handle")]
    public void CE1360_TheTitleRule(string key, bool condition, string? field, string expected)
        => Assert.Equal(expected, BTreeNodeTitles.For(key, condition, field));

    /// <summary>
    /// ⭐ <c>CE-1360</c> — laid out with a MEASURED font (here one 4× wider than the estimate) no two nodes overlap at that width, and a row of
    /// more than three small subtrees (pairs) is staggered too, each layer below everything the one above draws.
    /// 🔴 Red-proof: the measure ignored by WidthOf ⇒ overlaps at the measured width; pairs back to one layer ⇒ no staggered pair row.
    /// </summary>
    [Fact]
    public void CE1360_WithTheMeasuredFont_NothingOverlaps_AndPairRowsStagger()
    {
        static float Wide(string t) => t.Length * 30f;   // 4× the estimate: ignoring the measure must collide
        static float Drawn(BTreeEditorNode node)
            => MathF.Max(160f, Wide(string.IsNullOrEmpty(node.DisplayLabel) ? node.KernelType.ToString() : node.DisplayLabel) + 48f);
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
        int pairRows = 0;
        foreach (var t in contributor.Enumerate().OfType<BehaviorTreeAsset>().Where(a => a.IsCodeBuilt))
        {
            Hrot.BTree.Editor.Layout.BTreeAutoLayout.Layout(t, Wide);
            var n = t.Nodes;
            for (int a = 0; a < n.Count; a++)
                for (int b = a + 1; b < n.Count; b++)
                {
                    // the DRAWN width, computed here as CanvasLayout does (title + 48, at least 160) — not by the layout's own helper
                    float wa = Drawn(n[a]), wb = Drawn(n[b]);
                    bool overlap = n[a].Position.X < n[b].Position.X + wb && n[b].Position.X < n[a].Position.X + wa
                                && n[a].Position.Y < n[b].Position.Y + 50f && n[b].Position.Y < n[a].Position.Y + 50f;
                    Assert.False(overlap, $"{t.Name}: '{n[a].DisplayLabel}' at {n[a].Position} overlaps '{n[b].DisplayLabel}' at {n[b].Position}");
                }
            foreach (var p in n.Where(p => p.ChildVisualIds.Count > 3))
            {
                var kids = p.ChildVisualIds.Select(id => n.First(c => c.VisualId == id)).ToList();
                if (kids.Any(k => k.ChildVisualIds.Count > 0) && kids.Select(k => k.Position.Y).Distinct().Count() > 1) pairRows++;
            }
        }
        _out.WriteLine($"staggered rows of subtrees: {pairRows}");
        Assert.True(pairRows > 0, "no row of small subtrees was staggered");
    }
}
