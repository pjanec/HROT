using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>E5</c> item 7 — the <c>A</c> hosts <c>B</c> hosts <c>A</c> walk.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16.
///
/// <para>⛔⛔ <b>The rail that matters most is <see cref="Diamond_IsNotACycle"/>, not the cycle ones.</b>
/// 📐 A cycle detector written with a plain <c>visited</c> set passes every positive case here and
/// still FALSE-POSITIVES on a diamond — two hosts pulling in the same child, which is legitimate and
/// common. 🔒 That is the one mistake this rule could make that would be worse than not having it,
/// because it would red an asset the designer cannot fix.</para>
/// </summary>
public sealed class SubtreeCycleDetectorTests
{
    /// <summary>A catalogue-visible asset that hosts a fixed set of children.</summary>
    private sealed class HostAsset : IEditableAsset, ISubtreeHostingAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Asset";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/test.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public List<Guid> Hosts { get; } = new();
        public IReadOnlyCollection<Guid> GetHostedSubtreeAssetIds() => Hosts;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    /// <summary>⚠ An asset that is NOT an <c>ISubtreeHostingAsset</c> — e.g. a Blueprint. It must
    /// contribute no edges rather than throw.</summary>
    private sealed class PlainAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name => "Plain";
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => "/plain.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly Dictionary<Guid, IEditableAsset> _byId = new();
        public FakeCatalog(params IEditableAsset[] assets)
        {
            foreach (var a in assets) _byId[a.AssetId] = a;
        }
        public IReadOnlyList<IEditableAsset> All => _byId.Values.ToList();
        public IEditableAsset? FindByAssetId(Guid assetId) => _byId.GetValueOrDefault(assetId);
        public IEditableAsset? FindByName(string name) => _byId.Values.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    [Fact]
    public void SelfHostingAsset_IsACycle()
    {
        var a = new HostAsset { Name = "A" };
        a.Hosts.Add(a.AssetId);

        var cycle = SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(a), a.AssetId);

        Assert.Equal(new[] { a.AssetId, a.AssetId }, cycle);
    }

    /// <summary>⭐ The named case: <c>A</c> hosts <c>B</c> hosts <c>A</c>.</summary>
    [Fact]
    public void TwoHopRing_IsACycle_AndThePathNamesTheRing()
    {
        var a = new HostAsset { Name = "A" };
        var b = new HostAsset { Name = "B" };
        a.Hosts.Add(b.AssetId);
        b.Hosts.Add(a.AssetId);

        var catalog = new FakeCatalog(a, b);
        var cycle = SubtreeCycleDetector.FindCycleFrom(catalog, a.AssetId);

        Assert.Equal(new[] { a.AssetId, b.AssetId, a.AssetId }, cycle);
        Assert.Equal("A -> B -> A", SubtreeCycleDetector.DescribeCycle(catalog, cycle));
    }

    /// <summary>
    /// ⭐⭐ Detected from EITHER end of the ring — which is the whole reason the walk is shared
    /// between both validators rather than living on the HSM side alone.
    /// </summary>
    [Fact]
    public void ThreeHopRing_IsDetectedFromEveryMember()
    {
        var a = new HostAsset { Name = "A" };
        var b = new HostAsset { Name = "B" };
        var c = new HostAsset { Name = "C" };
        a.Hosts.Add(b.AssetId);
        b.Hosts.Add(c.AssetId);
        c.Hosts.Add(a.AssetId);
        var catalog = new FakeCatalog(a, b, c);

        Assert.NotEmpty(SubtreeCycleDetector.FindCycleFrom(catalog, a.AssetId));
        Assert.NotEmpty(SubtreeCycleDetector.FindCycleFrom(catalog, b.AssetId));
        Assert.NotEmpty(SubtreeCycleDetector.FindCycleFrom(catalog, c.AssetId));
    }

    /// <summary>
    /// 🔴🔴 <b>THE RED-PROOF THAT MATTERS.</b> <c>A</c> hosts <c>B</c> and <c>C</c>; both host <c>D</c>.
    /// ⛔ <c>D</c> is reached twice and the graph is ACYCLIC. A <c>visited</c>-set implementation
    /// reports a cycle here; the <c>onPath</c> one does not.
    /// </summary>
    [Fact]
    public void Diamond_IsNotACycle()
    {
        var a = new HostAsset { Name = "A" };
        var b = new HostAsset { Name = "B" };
        var c = new HostAsset { Name = "C" };
        var d = new HostAsset { Name = "D" };
        a.Hosts.Add(b.AssetId);
        a.Hosts.Add(c.AssetId);
        b.Hosts.Add(d.AssetId);
        c.Hosts.Add(d.AssetId);

        var cycle = SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(a, b, c, d), a.AssetId);

        Assert.Empty(cycle);
    }

    /// <summary>⚠ The same child hosted twice from ONE asset is also a diamond, not a cycle.</summary>
    [Fact]
    public void SameChildHostedTwice_IsNotACycle()
    {
        var a = new HostAsset { Name = "A" };
        var b = new HostAsset { Name = "B" };
        a.Hosts.Add(b.AssetId);
        a.Hosts.Add(b.AssetId);

        Assert.Empty(SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(a, b), a.AssetId));
    }

    /// <summary>
    /// ⛔ A dangling child id is <c>DanglingReferenceAfterReload</c>'s defect, not this rule's —
    /// one authoring mistake must not produce two unrelated diagnostics.
    /// </summary>
    [Fact]
    public void UnresolvableChild_IsNotACycle()
    {
        var a = new HostAsset { Name = "A" };
        a.Hosts.Add(Guid.NewGuid());   // nothing in the catalogue answers to it

        Assert.Empty(SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(a), a.AssetId));
    }

    /// <summary>⚠ An asset kind that hosts nothing contributes no edges rather than throwing.</summary>
    [Fact]
    public void NonHostingAssetKind_ContributesNoEdges()
    {
        var plain = new PlainAsset();
        var a = new HostAsset { Name = "A" };
        a.Hosts.Add(plain.AssetId);

        Assert.Empty(SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(a, plain), a.AssetId));
    }

    [Fact]
    public void NullCatalogOrEmptyId_IsNotACycle()
    {
        Assert.Empty(SubtreeCycleDetector.FindCycleFrom(null, Guid.NewGuid()));
        Assert.Empty(SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(), Guid.Empty));
    }

    /// <summary>⭐ A long chain terminates and reports nothing — the walk is linear, not exponential.</summary>
    [Fact]
    public void LongAcyclicChain_Terminates()
    {
        var assets = new List<HostAsset>();
        for (int i = 0; i < 200; i++) assets.Add(new HostAsset { Name = $"A{i}" });
        for (int i = 0; i < assets.Count - 1; i++) assets[i].Hosts.Add(assets[i + 1].AssetId);

        var catalog = new FakeCatalog(assets.ToArray());
        Assert.Empty(SubtreeCycleDetector.FindCycleFrom(catalog, assets[0].AssetId));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>NextHopInRing</c> — the edge a designer can cut, and the reason the diagnostic can
    /// badge a node at all.</b> 🔴 Added after the forwarding rail caught the first cut emitting a
    /// diagnostic with NO target: the canvas had nothing to highlight, so the rule was invisible there.
    /// </summary>
    [Fact]
    public void NextHopInRing_NamesTheEdgeToCut_ForEveryMemberOfTheRing()
    {
        var a = new HostAsset { Name = "A" };
        var b = new HostAsset { Name = "B" };
        a.Hosts.Add(b.AssetId);
        b.Hosts.Add(a.AssetId);
        var catalog = new FakeCatalog(a, b);

        var fromA = SubtreeCycleDetector.FindCycleFrom(catalog, a.AssetId);
        Assert.Equal(b.AssetId, SubtreeCycleDetector.NextHopInRing(fromA, a.AssetId));
        Assert.Equal(a.AssetId, SubtreeCycleDetector.NextHopInRing(fromA, b.AssetId));
    }

    /// <summary>
    /// ⛔⛔ <b>The APPROACH-PATH case — this is what stops the rule blaming an innocent asset.</b>
    /// <c>A→B→C→B</c> reached from <c>A</c>: the ring is <c>B→C→B</c> and <c>A</c> is merely how we
    /// got there. ⇒ nothing in <c>A</c> is at fault and there is no edge in <c>A</c> to cut.
    /// </summary>
    [Fact]
    public void NextHopInRing_ReturnsNull_ForAnAssetOnlyOnTheApproachPath()
    {
        var a = new HostAsset { Name = "A" };
        var b = new HostAsset { Name = "B" };
        var c = new HostAsset { Name = "C" };
        a.Hosts.Add(b.AssetId);
        b.Hosts.Add(c.AssetId);
        c.Hosts.Add(b.AssetId);          // ⭐ the ring excludes A

        var cycle = SubtreeCycleDetector.FindCycleFrom(new FakeCatalog(a, b, c), a.AssetId);

        Assert.NotEmpty(cycle);
        Assert.Null(SubtreeCycleDetector.NextHopInRing(cycle, a.AssetId));
        Assert.Equal(c.AssetId, SubtreeCycleDetector.NextHopInRing(cycle, b.AssetId));
        Assert.Equal(b.AssetId, SubtreeCycleDetector.NextHopInRing(cycle, c.AssetId));
    }

    [Fact]
    public void NextHopInRing_OnAnEmptyOrTrivialPath_IsNull()
    {
        Assert.Null(SubtreeCycleDetector.NextHopInRing(Array.Empty<Guid>(), Guid.NewGuid()));
        Assert.Null(SubtreeCycleDetector.NextHopInRing(new[] { Guid.NewGuid() }, Guid.NewGuid()));
    }

    /// <summary>⚠ An id the catalogue cannot name falls back to the raw Guid rather than an empty gap.</summary>
    [Fact]
    public void DescribeCycle_FallsBackToTheRawId_WhenTheAssetIsUnnamed()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id.ToString("D"), SubtreeCycleDetector.DescribeCycle(new FakeCatalog(), new[] { id }));
    }
}
