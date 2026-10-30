using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// Tests for <see cref="AssetPickerLauncher"/> (MTB-P8-T3).
/// </summary>
public sealed class AssetPickerLauncherTests
{
    // ── Stub IEditableAsset ─────────────────────────────────────────────

    private sealed class StubAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    // ── Fake IAssetCatalog ──────────────────────────────────────────────

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly IReadOnlyList<IEditableAsset> _assets;

        public FakeCatalog(params IEditableAsset[] assets)
        {
            _assets = assets.ToList().AsReadOnly();
        }

        public IReadOnlyList<IEditableAsset> All => _assets;
        public IEditableAsset? FindByAssetId(Guid assetId) =>
            _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) =>
            _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
            Array.Empty<IEditableAsset>();
#pragma warning disable CS0067
        public event Action<AssetKind>? Changed;
#pragma warning restore CS0067
    }

    // ── Fake openPicker helper ──────────────────────────────────────────

    /// <summary>
    /// Captures the <see cref="PickerRequest"/> and exposes a method to invoke
    /// the result handler with a crafted <see cref="PickerResult"/>.
    /// </summary>
    private sealed class FakeOpenPicker
    {
        public PickerRequest? CapturedRequest { get; private set; }
        private Action<PickerResult>? _handler;

        public void OpenPicker(PickerRequest request, Action<PickerResult> onChosen)
        {
            CapturedRequest = request;
            _handler = onChosen;
        }

        public void InvokeHandler(PickerResult result)
        {
            _handler?.Invoke(result);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static AssetPickActionRouter MakeRouter(
        Action<IEditableAsset>? onOpen = null,
        Action<string>? onLoad = null)
        => new(onOpen ?? (_ => { }), onLoad ?? (_ => { }));

    /// <summary>
    /// Builds a <see cref="PickerResult"/> with the given entry as the sole selection.
    /// </summary>
    private static PickerResult ConfirmResult(PickerEntry entry)
        => new(new[] { entry });

    /// <summary>
    /// Builds a cancelled <see cref="PickerResult"/> (empty selection).
    /// </summary>
    private static PickerResult CancelResult()
        => new(Array.Empty<PickerEntry>());

    /// <summary>
    /// Creates a <see cref="PickerEntry"/> whose Tag is the given asset.
    /// </summary>
    private static PickerEntry EntryFor(IEditableAsset asset)
        => new(
            Id: asset.AssetId.ToString(),
            Name: asset.Name,
            Description: null,
            Category: null,
            Keywords: null,
            IconTextureId: null,
            Tag: asset,
            IconKey: AssetKindIcons.GetIconKey(asset.Kind));

    // ── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Opening the launcher builds a Tree-layout PickerRequest from the asset source.
    /// Asserts Layout, SelectionMode, and that ItemsProvider() yields one entry per catalog
    /// asset with Tag identity and non-null IconKey.
    /// </summary>
    [Fact]
    public void Open_BuildsTreeLayoutRequest_FromAssetSource()
    {
        var assets = new IEditableAsset[]
        {
            new StubAsset { Kind = AssetKind.Blueprint, Name = "MyBlueprint" },
            new StubAsset { Kind = AssetKind.Scenario,  Name = "MyScenario" },
        };
        var catalog = new FakeCatalog(assets);
        var fakePicker = new FakeOpenPicker();
        var router = MakeRouter();

        var launcher = new AssetPickerLauncher(
            openPicker: fakePicker.OpenPicker,
            catalog: catalog,
            router: router,
            baseFolderResolver: _ => "");

        launcher.Open(AssetKindFilter.All);

        Assert.NotNull(fakePicker.CapturedRequest);
        Assert.Equal(PickerLayout.Tree, fakePicker.CapturedRequest!.Layout);
        Assert.Equal(PickerSelectionMode.Single, fakePicker.CapturedRequest.SelectionMode);
        Assert.Equal("Open Asset", fakePicker.CapturedRequest.Title);

        // ItemsProvider should yield one entry per asset with correct projection.
        var entries = fakePicker.CapturedRequest.ItemsProvider().ToList();
        Assert.Equal(assets.Length, entries.Count);

        for (int i = 0; i < assets.Length; i++)
        {
            Assert.Same(assets[i], entries[i].Tag);
            Assert.NotNull(entries[i].IconKey);
        }
    }

    /// <summary>
    /// Confirming with a file-kind asset routes through router.Route (openDocument),
    /// and does NOT call loadScenario.
    /// </summary>
    [Fact]
    public void Open_Confirm_RoutesPickedAssetTag_ViaRouter()
    {
        IEditableAsset? openedAsset = null;
        string? loadedScenario = null;

        var router = new AssetPickActionRouter(
            openDocument: a => openedAsset = a,
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset { Kind = AssetKind.Blueprint, Name = "TestBP" };
        var catalog = new FakeCatalog(asset);
        var fakePicker = new FakeOpenPicker();

        var launcher = new AssetPickerLauncher(
            openPicker: fakePicker.OpenPicker,
            catalog: catalog,
            router: router,
            baseFolderResolver: _ => "");

        launcher.Open(AssetKindFilter.All);

        // Simulate user confirming the pick.
        var entry = EntryFor(asset);
        fakePicker.InvokeHandler(ConfirmResult(entry));

        Assert.NotNull(openedAsset);
        Assert.Same(asset, openedAsset);
        Assert.Null(loadedScenario);
    }

    /// <summary>
    /// Cancelling the picker routes nothing — neither openDocument nor loadScenario.
    /// </summary>
    [Fact]
    public void Open_Cancel_RoutesNothing()
    {
        IEditableAsset? openedAsset = null;
        string? loadedScenario = null;

        var router = new AssetPickActionRouter(
            openDocument: a => openedAsset = a,
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset { Kind = AssetKind.Blueprint, Name = "TestBP" };
        var catalog = new FakeCatalog(asset);
        var fakePicker = new FakeOpenPicker();

        var launcher = new AssetPickerLauncher(
            openPicker: fakePicker.OpenPicker,
            catalog: catalog,
            router: router,
            baseFolderResolver: _ => "");

        launcher.Open(AssetKindFilter.All);

        // Simulate user cancelling.
        fakePicker.InvokeHandler(CancelResult());

        Assert.Null(openedAsset);
        Assert.Null(loadedScenario);
    }

    /// <summary>
    /// When an onPicked callback is supplied, confirm invokes the callback and does
    /// NOT call the router — the scenario-load contract path.
    /// </summary>
    [Fact]
    public void Open_WithOnPickedCallback_InvokesCallback_NotRouter()
    {
        IEditableAsset? openedAsset = null;
        string? loadedScenario = null;
        IEditableAsset? callbackAsset = null;

        var router = new AssetPickActionRouter(
            openDocument: a => openedAsset = a,
            loadScenario: s => loadedScenario = s);

        var asset = new StubAsset { Kind = AssetKind.Scenario, Name = "scenarios/Patrol" };
        var catalog = new FakeCatalog(asset);
        var fakePicker = new FakeOpenPicker();

        var launcher = new AssetPickerLauncher(
            openPicker: fakePicker.OpenPicker,
            catalog: catalog,
            router: router,
            baseFolderResolver: _ => "");

        launcher.Open(AssetKindFilter.Scenario, onPicked: a => callbackAsset = a);

        // Simulate user confirming.
        var entry = EntryFor(asset);
        fakePicker.InvokeHandler(ConfirmResult(entry));

        Assert.NotNull(callbackAsset);
        Assert.Same(asset, callbackAsset);
        Assert.Null(openedAsset);
        Assert.Null(loadedScenario);
    }

    /// <summary>
    /// With a mixed-kind catalog, Open(AssetKindFilter.Scenario) yields only Scenario
    /// entries — the AssetPickerSource is built with the scenario filter.
    /// </summary>
    [Fact]
    public void Open_ScenarioKinds_RequestQueriesOnlyScenarios()
    {
        var assets = new IEditableAsset[]
        {
            new StubAsset { Kind = AssetKind.Blueprint,  Name = "BP1" },
            new StubAsset { Kind = AssetKind.Scenario,   Name = "Scen1" },
            new StubAsset { Kind = AssetKind.BTree,      Name = "BT1" },
            new StubAsset { Kind = AssetKind.Scenario,   Name = "Scen2" },
        };
        var catalog = new FakeCatalog(assets);
        var fakePicker = new FakeOpenPicker();
        var router = MakeRouter();

        var launcher = new AssetPickerLauncher(
            openPicker: fakePicker.OpenPicker,
            catalog: catalog,
            router: router,
            baseFolderResolver: _ => "");

        launcher.Open(AssetKindFilter.Scenario);

        Assert.NotNull(fakePicker.CapturedRequest);
        var entries = fakePicker.CapturedRequest!.ItemsProvider().ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(AssetKind.Scenario, ((IEditableAsset)e.Tag!).Kind));
        Assert.Contains(entries, e => ((IEditableAsset)e.Tag!).Name == "Scen1");
        Assert.Contains(entries, e => ((IEditableAsset)e.Tag!).Name == "Scen2");
    }

    // ═════════════════════════════════════════════════════════════════
    //  CE-4098 (AQ108 D19) — the Open-Asset / Open-Scenario picker shows Recent first, per kind
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ CE-4098 success 1+2 — through the REAL <see cref="PickerRegistry"/>: a pick is pushed to ITS KIND's Recent list (so File › Open Scenario
    /// and File › Open Asset share the scenario list and kinds do not mix), the open picker's Recent section lists it first, and after a "restart"
    /// (fresh store from the file) the picked scenario is still first. 🔴 Red-proof: drop <c>RecentContext:</c> from
    /// <c>AssetPickerSource.ToEntry</c> ⇒ the pick lands under the picker's own <c>assets.open.*</c> key and the kind-list assertion fails.
    /// </summary>
    [Fact]
    public void CE4098_APick_IsPushedToItsKindsList_ShownFirstAfterARestart_AndKindsDoNotMix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "asset-recent-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "recent-picks.json");
        try
        {
            var s1 = new StubAsset { Kind = AssetKind.Scenario,  Name = "Scen1" };
            var s2 = new StubAsset { Kind = AssetKind.Scenario,  Name = "Scen2" };
            var bp = new StubAsset { Kind = AssetKind.Blueprint, Name = "BP1" };
            var catalog = new FakeCatalog(s1, s2, bp);

            AssetPickerLauncher Launcher(PickerRegistry r) => new(r.OpenPicker, catalog, MakeRouter(), baseFolderResolver: _ => "");

            var registry = new PickerRegistry { Recent = RecentStore.Load(file) };
            IEditableAsset? picked = null;
            Launcher(registry).Open(AssetKindFilter.Scenario, a => picked = a);
            Assert.Empty(registry.RecentSection());
            Assert.True(registry.ConfirmEntry(s2.AssetId.ToString()));
            Assert.Same(s2, picked);

            // File › Open Asset (all kinds) picks a blueprint: its own kind's list, not the scenario list
            Launcher(registry).Open(AssetKindFilter.All, _ => { });
            Assert.True(registry.ConfirmEntry(bp.AssetId.ToString()));

            var reloaded = RecentStore.Load(file);
            Assert.Equal(new[] { s2.AssetId.ToString() }, reloaded.GetRecent(AssetRecents.ContextFor(AssetKind.Scenario)));
            Assert.Equal(new[] { bp.AssetId.ToString() }, reloaded.GetRecent(AssetRecents.ContextFor(AssetKind.Blueprint)));

            // "restart": a fresh registry over the reloaded store — both the scenario picker and the all-kinds picker show s2 first
            var second = new PickerRegistry { Recent = reloaded };
            Launcher(second).Open(AssetKindFilter.Scenario);
            Assert.Equal(new[] { s2.AssetId.ToString() }, second.RecentSection().Select(e => e.Id));
            Launcher(second).Open(AssetKindFilter.All);
            Assert.Equal(new[] { s2.AssetId.ToString(), bp.AssetId.ToString() }.OrderBy(x => x),
                         second.RecentSection().Select(e => e.Id).OrderBy(x => x));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// ⭐ CE-4098 success 3 — a scenario that is no longer in the catalog (deleted, or moved by the content-folder change and not found there) is
    /// not shown as Recent: the picker lists only what resolves, so there is no ghost row and no crash.
    /// </summary>
    [Fact]
    public void CE4098_AScenarioThatNoLongerResolves_IsNotShownAsRecent()
    {
        var kept  = new StubAsset { Kind = AssetKind.Scenario, Name = "Kept" };
        var other = new StubAsset { Kind = AssetKind.Scenario, Name = "NeverPicked" };
        var gone  = new StubAsset { Kind = AssetKind.Scenario, Name = "Gone" };
        var store = new RecentStore();
        store.Push(AssetRecents.ContextFor(AssetKind.Scenario), gone.AssetId.ToString());
        store.Push(AssetRecents.ContextFor(AssetKind.Scenario), kept.AssetId.ToString());

        var registry = new PickerRegistry { Recent = store };
        new AssetPickerLauncher(registry.OpenPicker, new FakeCatalog(kept, other), MakeRouter(), baseFolderResolver: _ => "")
            .Open(AssetKindFilter.Scenario);

        // only what the picker lists AND was picked: not the deleted one, not the never-picked one
        Assert.Equal(new[] { kept.AssetId.ToString() }, registry.RecentSection().Select(e => e.Id));
    }

    /// <summary>
    /// ⭐ <c>CE-1442</c> (U1, §11.7 ⑤) — a picker that lists SCENARIOS (the cluster's list) leads with the disabled "waiting for the cluster's list…" row until the
    /// list has arrived, so an empty picker never reads as "no scenarios"; files on disk (a BTree-only picker) are unaffected, and once the list arrives the row is gone.
    /// 🔴 Mutation: the <c>_hasInventory()</c> check dropped from <c>Entries</c> ⇒ no waiting row ⇒ red.
    /// </summary>
    [Fact]
    public void CE1442_AScenarioPicker_SaysItIsWaitingForTheClustersList_UntilItArrives()
    {
        bool arrived = false;
        var catalog = new FakeCatalog(new StubAsset { Name = "Patrol", Kind = AssetKind.BTree });
        var launcher = new AssetPickerLauncher(new FakeOpenPicker().OpenPicker, catalog, new AssetPickActionRouter(_ => { }, _ => { }), hasInventory: () => arrived);

        var waiting = launcher.Entries(AssetKindFilter.Scenario);
        Assert.Equal(AssetPickerLauncher.WaitingEntryId, waiting[0].Id);
        Assert.False(waiting[0].IsEnabled);
        Assert.DoesNotContain(launcher.Entries(AssetKindFilter.BTree), e => e.Id == AssetPickerLauncher.WaitingEntryId);

        arrived = true;
        Assert.DoesNotContain(launcher.Entries(AssetKindFilter.Scenario), e => e.Id == AssetPickerLauncher.WaitingEntryId);
    }
}
