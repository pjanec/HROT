using System;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Inspector;

/// <summary>
/// AIE-024 tests for HSM field picker drawers.
/// All logic-level: no ImGui context required.
/// </summary>
public sealed class HsmPickerDrawerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    private static HsmAsset MakeAsset()
    {
        var b = new HsmBuilder("Test");
        b.Event("Fire",  1);
        b.Event("Reset", 2);
        b.State("Active").Final();
        b.State("Idle").Initial()
            .OnEntry("Ns.Actions.StartIdle")
            .OnExit("Ns.Actions.StopIdle")
            .On("Fire").GoTo("Active");
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Test", "", false, "");
    }

    // ── HsmEventPickerDrawer tests ────────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmEvent_ListsAssetEvents()
    {
        var asset  = MakeAsset();
        var drawer = new HsmEventPickerDrawer(asset);
        var items  = drawer.GetItems();

        items.Should().Contain("Fire");
        items.Should().Contain("Reset");
        items.Should().HaveCount(2, "exactly the two events defined in the builder");
    }

    [Fact]
    public void FieldPicker_HsmEvent_ItemsSorted()
    {
        var asset  = MakeAsset();
        var drawer = new HsmEventPickerDrawer(asset);
        drawer.GetItems().Should().BeInAscendingOrder();
    }

    // ── HsmStateSelectorDrawer tests ──────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmState_ListsAssetStates()
    {
        var asset  = MakeAsset();
        var drawer = new HsmStateSelectorDrawer(asset);
        var items  = drawer.GetItems();

        items.Should().Contain("Idle");
        items.Should().Contain("Active");
        // Compiler-internal states (names starting with __) must not appear.
        items.Should().NotContain(s => s.StartsWith("__"),
            "compiler-internal and synthetic root states must not appear in the picker");
    }

    [Fact]
    public void FieldPicker_HsmState_ItemsSorted()
    {
        var asset  = MakeAsset();
        var drawer = new HsmStateSelectorDrawer(asset);
        drawer.GetItems().Should().BeInAscendingOrder();
    }

    // ── HsmActionPickerDrawer tests ───────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmAction_ListsStateActions()
    {
        var asset  = MakeAsset();
        var drawer = new HsmActionPickerDrawer(asset);
        var items  = drawer.GetItems();

        // OnEntry and OnExit actions on "Idle" state must appear.
        items.Should().Contain("Ns.Actions.StartIdle");
        items.Should().Contain("Ns.Actions.StopIdle");
    }

    // ── HsmGuardPickerDrawer tests ────────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmGuard_EmptyAsset_ReturnsEmpty()
    {
        // Asset with no guard functions.
        var asset  = MakeAsset();
        var drawer = new HsmGuardPickerDrawer(asset);
        // No guards were set in the builder so list should be empty.
        drawer.GetItems().Should().BeEmpty("no guard functions were registered");
    }

    // ── CE-386: the action/guard pickers read the REGISTERED catalog ─────────

    private sealed class StubExporter : Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter
    {
        private readonly System.Collections.Generic.Dictionary<string,
            Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry> _map = new(StringComparer.Ordinal);

        public System.Collections.Generic.IReadOnlyDictionary<string,
            Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry> All => _map;
        public event Action? Changed { add { } remove { } }

        public StubExporter Add(string fqn, Hrot.Editor.AiShared.Blackboard.ActionHosting hosting)
        {
            _map[fqn] = new Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry(
                fqn, typeof(float), hosting,
                Hrot.Editor.AiShared.Blackboard.BlackboardAccess.ReadWrite);
            return this;
        }

        public Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry? Lookup(string fqn)
            => _map.GetValueOrDefault(fqn);
        public void Rebuild() { }
    }

    /// <summary>A catalog holding one HSM activity, one HSM guard, and one BTree-only action that
    /// must appear in NEITHER HSM picker.</summary>
    private static StubExporter ThreeKinds()
    {
        return new StubExporter()
            .Add("Ns.Catalog.Chase",
                 Hrot.Editor.AiShared.Blackboard.ActionHosting.Hsm
               | Hrot.Editor.AiShared.Blackboard.ActionHosting.HsmActivity)
            .Add("Ns.Catalog.InRange",
                 Hrot.Editor.AiShared.Blackboard.ActionHosting.Hsm
               | Hrot.Editor.AiShared.Blackboard.ActionHosting.HsmGuard)
            .Add("Ns.Catalog.BTreeOnly",
                 Hrot.Editor.AiShared.Blackboard.ActionHosting.BTree);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-386</c> — the FIRST binding is now makeable.</b> 📄 design §3.3, rail ⑧.
    ///
    /// <para>🔴 <b>The defect.</b> Both pickers built their list ONLY from names the asset already
    /// bound ⇒ an action could be picked once it was already in use, and the first binding was
    /// UNMAKEABLE from the inspector. ⛔ A picker that can only offer what you already chose is not
    /// a picker.</para>
    ///
    /// <para>⚠ Asserted on the CONSTRUCTED drawer with a catalog holding entries the asset does NOT
    /// mention — the shape rail ⑧ was re-aimed to, because the ONE production caller
    /// (<c>AiFacetPickerBinder.Rebuild:95</c>) was already forwarding the exporter.</para>
    /// </summary>
    [Fact]
    public void ActionPicker_OffersRegisteredHsmActivities_TheAssetDoesNotYetMention()
    {
        var items = new HsmActionPickerDrawer(MakeAsset(), ThreeKinds()).GetItems();

        items.Should().Contain("Ns.Catalog.Chase");
        items.Should().NotContain("Ns.Catalog.InRange", "a GUARD is not an activity");
        items.Should().NotContain("Ns.Catalog.BTreeOnly", "a BTree-only action is not HSM-hostable");
    }

    [Fact]
    public void GuardPicker_OffersRegisteredHsmGuards_TheAssetDoesNotYetMention()
    {
        var items = new HsmGuardPickerDrawer(MakeAsset(), ThreeKinds()).GetItems();

        items.Should().Contain("Ns.Catalog.InRange");
        items.Should().NotContain("Ns.Catalog.Chase", "an ACTIVITY is not a guard");
        items.Should().NotContain("Ns.Catalog.BTreeOnly");
    }

    /// <summary>
    /// ⭐⭐ <b>The asset's own names stay in the union, even WITH a catalog.</b> ⛔ Dropping a name
    /// the current assembly no longer exports would HIDE a dangling binding rather than show it —
    /// the same never-erase reasoning as the subtree reference.
    /// </summary>
    [Fact]
    public void TheAssetsOwnNamesSurviveAlongsideTheCatalog()
    {
        var items = new HsmActionPickerDrawer(MakeAsset(), ThreeKinds()).GetItems();

        items.Should().Contain("Ns.Actions.StartIdle", "the asset binds it, catalog or not");
        items.Should().Contain("Ns.Catalog.Chase");
    }

    /// <summary>⚠ The no-catalog FALLBACK is unchanged — a headless host still gets the
    /// self-referential list rather than an exception or an empty one.</summary>
    [Fact]
    public void WithNoExporter_ThePickersFallBackToTheAssetsOwnNames()
    {
        new HsmActionPickerDrawer(MakeAsset()).GetItems()
            .Should().Contain("Ns.Actions.StartIdle");
        new HsmGuardPickerDrawer(MakeAsset()).GetItems()
            .Should().BeEmpty("the fixture binds no guards, and there is no catalog to add any");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑧ proper: the drawer the FACTORY builds uses the exporter it is handed.</b>
    /// ⚠ Constructing the drawer directly (above) proves the drawer; this proves the WIRING, which
    /// is the half that went silently inert twice before (<c>HsmValidator._isStatefulSubtree</c>,
    /// <c>BlackboardAuthoringWindow._actionSchemaExporter</c>).
    /// </summary>
    [Fact]
    public void TheFactoryWiresTheExporterIntoBothPickers()
    {
        var drawers   = HsmPickerDrawerFactory.BuildDrawers(MakeAsset(), ThreeKinds());
        var composite = (HsmCompositeStringDrawer)drawers[typeof(string)];

        var action = composite.Resolve(NodeWithAttribute(new HsmActionPickerAttribute()));
        var guard  = composite.Resolve(NodeWithAttribute(new HsmGuardPickerAttribute()));

        ((Hrot.Editor.AiShared.Inspector.IPickerListSource)action!).GetItems()
            .Should().Contain("Ns.Catalog.Chase");
        ((Hrot.Editor.AiShared.Inspector.IPickerListSource)guard!).GetItems()
            .Should().Contain("Ns.Catalog.InRange");
    }

    // ── CE-396: [AiAssetPicker] dispatches on the attribute's KIND ────────────

    private sealed class CatalogAsset : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public Hrot.Editor.AiShared.AssetKind Kind { get; init; }
        public string SourceFilePath => "/x.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeCatalog : Hrot.Editor.AiShared.Catalog.IAssetCatalog
    {
        private readonly System.Collections.Generic.List<Hrot.Editor.AiShared.IEditableAsset> _a;
        public FakeCatalog(params Hrot.Editor.AiShared.IEditableAsset[] assets) => _a = assets.ToList();
        public System.Collections.Generic.IReadOnlyList<Hrot.Editor.AiShared.IEditableAsset> All => _a;
        public Hrot.Editor.AiShared.IEditableAsset? FindByAssetId(Guid id) => _a.FirstOrDefault(x => x.AssetId == id);
        public Hrot.Editor.AiShared.IEditableAsset? FindByName(string n) => _a.FirstOrDefault(x => x.Name == n);
        public System.Collections.Generic.IReadOnlyList<Hrot.Editor.AiShared.IEditableAsset> WhereDependsOn(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.IEditableAsset>();
#pragma warning disable 67
        public event Action<Hrot.Editor.AiShared.AssetKind>? Changed;
#pragma warning restore 67
    }

    private static StructEdit.Core.EditNode NodeWithAttribute(Attribute attr)
        => new StructEdit.Core.EditNode(
               id:       new StructEdit.Core.EditNodeId(0),
               name:     "F",
               jsonPath: "$.F",
               kind:     StructEdit.Core.EditNodeKind.String,
               clrType:  typeof(string),
               metadata: new StructEdit.Core.EditNodeMetadata { CustomAttributes = new[] { attr } });

    private static System.Collections.Generic.IReadOnlyList<string> ItemsFor(
        Hrot.Editor.AiShared.Catalog.IAssetCatalog catalog, Hrot.Editor.AiShared.AssetKind kind)
    {
        var drawers   = HsmPickerDrawerFactory.BuildDrawers(MakeAsset(), catalog: catalog);
        var composite = (HsmCompositeStringDrawer)drawers[typeof(string)];
        var resolved  = composite.Resolve(
            NodeWithAttribute(new Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute(kind)));

        resolved.Should().NotBeNull($"[AiAssetPicker({kind})] must resolve to a drawer");
        return ((Hrot.Editor.AiShared.Inspector.IPickerListSource)resolved!).GetItems();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-396</c> — <c>[AiAssetPicker]</c> offers the kind the FIELD asks for.</b>
    ///
    /// <para>🔴 <b>The defect this pins.</b> <c>HsmCompositeStringDrawer</c> keyed its registry by
    /// attribute TYPE and <c>BuildDrawers</c> registered ONE drawer hard-wired to
    /// <c>AssetKind.BTree</c>. With only <c>StateFacet.SubtreeName</c> in the tree that was
    /// invisible; <c>CE-385</c>'s blueprint fields would have silently drawn the BTREE list.
    /// ⛔ A picker offering the wrong KIND is worse than one offering nothing — the names are
    /// plausible, so nothing looks broken until the emitted id addresses the wrong asset.</para>
    ///
    /// <para>⚠ Asserted on the CONSTRUCTED drawer, exactly as design rail ⑧ requires: the
    /// registration existing is not the claim — what it hands back is.</para>
    /// </summary>
    [Fact]
    public void AiAssetPicker_OffersTheKindTheFieldAsksFor_NotWhicheverWasRegisteredFirst()
    {
        var catalog = new FakeCatalog(
            new CatalogAsset { Name = "PatrolTree",  Kind = Hrot.Editor.AiShared.AssetKind.BTree },
            new CatalogAsset { Name = "ChaseTarget", Kind = Hrot.Editor.AiShared.AssetKind.Blueprint });

        ItemsFor(catalog, Hrot.Editor.AiShared.AssetKind.BTree)
            .Should().Equal("PatrolTree");

        // ⚠ BeEquivalentTo, not Equal(params) — the params overload swallows the reason string as a
        //   second expected item, which is how the first draft of this rail failed on a CORRECT
        //   production result.
        ItemsFor(catalog, Hrot.Editor.AiShared.AssetKind.Blueprint)
            .Should().BeEquivalentTo(new[] { "ChaseTarget" },
                "the blueprint field must not be offered the BTree list");
    }

    /// <summary>⚠ No catalogue ⇒ no asset-picker drawer at all, so an empty dropdown can never be
    /// mistaken for "there are no assets of this kind". ⭐ The pre-existing contract, pinned because
    /// <c>CE-396</c> rewrote the registration around it.</summary>
    [Fact]
    public void WithNoCatalogue_TheAssetPickerIsNotRegisteredAtAll()
    {
        var drawers   = HsmPickerDrawerFactory.BuildDrawers(MakeAsset());
        var composite = (HsmCompositeStringDrawer)drawers[typeof(string)];

        composite.Resolve(NodeWithAttribute(
            new Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute(
                Hrot.Editor.AiShared.AssetKind.Blueprint)))
            .Should().BeNull();
    }
}
