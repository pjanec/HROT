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
}
