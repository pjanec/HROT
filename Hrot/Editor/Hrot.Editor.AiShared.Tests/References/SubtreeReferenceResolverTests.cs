using System;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.References;

/// <summary>
/// ⭐⭐⭐ <b>The heal rule — the ONE decision BTree and HSM both make about a stored asset reference.</b>
/// 📄 <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a *(its sequence diagram is this class's spec)*.
///
/// <para>🔒 User, <c>2026-09-26</c>: *"go with keep both and heal from guid; same would be good for
/// btree … no differences, consistency."*</para>
///
/// <para>⛔⛔ <b>The rail that matters most is <see cref="ADanglingReference_KeepsBOTHFields"/>.</b>
/// The shipped <c>BTreeSubtreeResolver</c> ran <c>SubtreeAssetId = Guid.Empty</c> on a missed name —
/// and a missed name IS the rename case, so it destroyed the only field that could still identify
/// the asset. That defect passed every existing test because nothing asserted what a FAILED
/// resolution must PRESERVE.</para>
/// </summary>
public sealed class SubtreeReferenceResolverTests
{
    private sealed class Asset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "A";
        public AssetKind Kind { get; init; } = AssetKind.BTree;
        public string SourceFilePath => "/a.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private static AssetCatalog CatalogOf(params IEditableAsset[] assets)
    {
        var c = new AssetCatalog();
        c.AddContributor(new StubContributor(assets));
        return c;
    }

    private sealed class StubContributor : IAssetCatalogContributor
    {
        private readonly IEditableAsset[] _assets;
        public StubContributor(IEditableAsset[] assets, AssetKind kind = AssetKind.BTree)
        {
            _assets = assets;
            Kind    = kind;
        }
        public AssetKind Kind { get; }
        public System.Collections.Generic.IReadOnlyList<IEditableAsset> Enumerate() => _assets;
        public event Action? ContributorChanged { add { } remove { } }
    }

    // ── ① the name wins when it resolves ──────────────────────────────────────

    [Fact]
    public void AResolvingName_KeepsTheNameAndRefreshesTheGuid()
    {
        var a = new Asset { Name = "Patrol" };
        var r = SubtreeReferenceResolver.Resolve(CatalogOf(a), "Patrol", Guid.NewGuid(), AssetKind.BTree);

        Assert.True(r.IsResolved);
        Assert.False(r.Healed);
        Assert.Equal("Patrol", r.Name);
        Assert.Equal(a.AssetId, r.AssetId);   // ⭐ refreshed from the catalogue, not the stale input
    }

    // ── ② the Guid heals the name ─────────────────────────────────────────────

    [Fact]
    public void ARenamedAsset_HealsTheNameFromTheGuid_AndSaysSo()
    {
        var a = new Asset { Name = "PatrolV2" };

        // the file still says "Patrol"; the asset was renamed to "PatrolV2"
        var r = SubtreeReferenceResolver.Resolve(CatalogOf(a), "Patrol", a.AssetId, AssetKind.BTree);

        Assert.True(r.IsResolved);
        Assert.True(r.Healed);                // ⭐⭐ the caller MUST mark the document dirty
        Assert.Equal("PatrolV2", r.Name);     // ⭐ healed
        Assert.Equal(a.AssetId, r.AssetId);
    }

    // ── ③ ⛔⛔ never erase ─────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔⛔ <b>THE DEFECT RAIL.</b> Neither the name nor the Guid resolves — and <b>both must
    /// survive</b>. 🔒 A reference that cannot resolve TODAY is still a reference: the asset may be
    /// absent from this session's catalogue (unloaded project, partial checkout) and present in the
    /// next. ⛔ Erasing it turns a recoverable state into data loss.
    /// </summary>
    [Fact]
    public void ADanglingReference_KeepsBOTHFields()
    {
        var strangerId = Guid.NewGuid();
        var r = SubtreeReferenceResolver.Resolve(
            CatalogOf(new Asset { Name = "Something else" }), "Patrol", strangerId, AssetKind.BTree);

        Assert.False(r.IsResolved);
        Assert.False(r.Healed);
        Assert.Equal("Patrol", r.Name);        // ⛔ NOT cleared
        Assert.Equal(strangerId, r.AssetId);   // ⛔ NOT Guid.Empty — the shipped BTree defect
    }

    // ── ④ kind is checked on BOTH lookups ─────────────────────────────────────

    [Fact]
    public void AnAssetOfTheWrongKind_IsNotAMatch_ByNameOrByGuid()
    {
        var hsm = new Asset { Name = "Patrol", Kind = AssetKind.Hsm };

        var byName = SubtreeReferenceResolver.Resolve(CatalogOf(hsm), "Patrol", Guid.Empty, AssetKind.BTree);
        Assert.False(byName.IsResolved);

        // ⭐ and the heal branch is kind-checked too: a renamed-AND-retyped asset must dangle,
        //   ⛔ not silently bind an HSM where a BTree was meant.
        var byGuid = SubtreeReferenceResolver.Resolve(CatalogOf(hsm), "stale", hsm.AssetId, AssetKind.BTree);
        Assert.False(byGuid.IsResolved);
        Assert.False(byGuid.Healed);
    }

    // ── ⑤ no reference at all is ORDINARY, not dangling ───────────────────────

    [Fact]
    public void NoReferenceAtAll_IsNotReportedAsUnresolved_Loudly()
    {
        var r = SubtreeReferenceResolver.Resolve(CatalogOf(), null, Guid.Empty, AssetKind.BTree);

        Assert.False(r.IsResolved);
        Assert.False(r.Healed);
        Assert.Equal(string.Empty, r.Name);
        Assert.Equal(Guid.Empty, r.AssetId);
    }

    [Fact]
    public void ANullCatalog_DanglesRatherThanThrowing()
    {
        var id = Guid.NewGuid();
        var r  = SubtreeReferenceResolver.Resolve(null, "Patrol", id, AssetKind.BTree);

        Assert.False(r.IsResolved);
        Assert.Equal("Patrol", r.Name);
        Assert.Equal(id, r.AssetId);   // ⛔ still never erased
    }

    // ── the picker's item list ────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The picker offers only assets of its kind. 📐 Measured before building: <b>no asset picker
    /// existed</b> — all eleven picker attributes picked a symbol — so this is the first rail of its
    /// kind in the repo.
    /// </summary>
    [Fact]
    public void TheAssetPicker_OffersOnlyItsOwnKind_Sorted()
    {
        var catalog = CatalogOf(
            new Asset { Name = "Zulu",  Kind = AssetKind.BTree },
            new Asset { Name = "Alpha", Kind = AssetKind.BTree },
            new Asset { Name = "AnHsm", Kind = AssetKind.Hsm });

        var items = new AiAssetPickerDrawer(catalog, AssetKind.BTree).GetItems();

        Assert.Equal(new[] { "Alpha", "Zulu" }, items);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The list is read PER CALL, not snapshotted</b> — 🔒 the <c>CE-343</c> lesson. Both hosts
    /// build their pickers during initialisation, before the contributors have finished populating;
    /// a cached list would leave the dropdown permanently empty.
    /// </summary>
    [Fact]
    public void TheAssetPicker_SeesAssetsAddedAfterItWasConstructed()
    {
        var catalog = new AssetCatalog();
        var drawer  = new AiAssetPickerDrawer(catalog, AssetKind.BTree);

        Assert.Empty(drawer.GetItems());

        catalog.AddContributor(new StubContributor(new IEditableAsset[]
        {
            new Asset { Name = "ArrivedLate", Kind = AssetKind.BTree },
        }));

        Assert.Equal(new[] { "ArrivedLate" }, drawer.GetItems());
    }
}
