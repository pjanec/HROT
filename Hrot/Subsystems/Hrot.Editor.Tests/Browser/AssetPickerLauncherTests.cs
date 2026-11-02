using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using System.IO;
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

    // ═══ CE-4141 (AQ108 D25) — the scenario picker as the first source of the generic side pane ═══════════════════════════════════
    // Red-proof (compiles): the go-live branch of Open dropped (the action id ignored) ⇒ CE4141_OpenAndGoLive_… fails.

    private sealed class RecordingControl : Hrot.UI.Common.RunMode.IRunScenarioControl
    {
        public List<string> LiveLoads { get; } = new();
        public List<string> EditOpens { get; } = new();
        public string? LoadedScenarioName => "s";
        public void LoadForLive(string scenarioName) => LiveLoads.Add(scenarioName);
        public List<(string Name, string? Args)> LiveLoadArgs { get; } = new();
        public void LoadForLive(string scenarioName, string? argsJson) { LiveLoads.Add(scenarioName); LiveLoadArgs.Add((scenarioName, argsJson)); }   // ⭐ AQ108 D24
        public void OpenForEdit(string scenarioName) => EditOpens.Add(scenarioName);
        public void OpenRecording(Guid exerciseId) { }
        public void SaveCurrent() { }
        public void TakeCheckpoint() { }
    }

    private static Hrot.UI.Common.RunMode.RunModeChoice? RunChoiceOver(RecordingControl control)
        => Hrot.UI.Common.RunMode.NodeRunControls.For(new Fdp.Core.FdpEventBus(), () => null, () => 0.0, null,
               new Hrot.UI.Common.RunMode.NodeRunProfile(Hrot.UI.Common.RunMode.RunHostKind.Editor, control)).RunChoice;

    private static string TempScenario(out string json, string name = "alpha", bool withPreview = false, string headerJson = "\"terrainName\":\"Hill\",\"description\":\"A hill attack.\",\"subsystemType\":\"CGF\"")
    {
        string root = Path.Combine(Path.GetTempPath(), "pk4141-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, name));
        json = Path.Combine(root, name, "scenario.json");
        File.WriteAllText(json, "{\"header\":{" + headerJson + "},\"entities\":{\"a\":{},\"b\":{},\"c\":{}}}");
        if (withPreview) File.WriteAllBytes(Path.Combine(root, name, "preview.png"), new byte[] { 1 });
        return root;
    }

    [Fact]
    public void CE4141_AScenarioRequest_HasTheSidePane_TheTwoActions_AndThePreviewKeyOnlyWhenTheFileExists()
    {
        string root = TempScenario(out var json, "with", withPreview: true);
        TempScenarioAt(root, "without", out var json2);
        try
        {
            var catalog = new FakeCatalog(new StubAsset { Kind = AssetKind.Scenario, Name = "with", SourceFilePath = json },
                                          new StubAsset { Kind = AssetKind.Scenario, Name = "without", SourceFilePath = json2 });
            var picker = new FakeOpenPicker();
            new AssetPickerLauncher(picker.OpenPicker, catalog, MakeRouter(), baseFolderResolver: _ => "").Open(AssetKindFilter.Scenario);

            var req = picker.CapturedRequest!;
            Assert.True(req.ShowSidePane);
            Assert.Equal(new System.Numerics.Vector2(1000f, 640f), req.DefaultSize);   // D25's wide picker: the preview must be readable
            Assert.Equal(560f, req.SidePaneWidth);
            Assert.NotNull(req.DrawPane);
            Assert.Equal(new[] { "open", "go-live" }, req.Actions!.Select(a => a.Id).ToArray());
            Assert.True(req.Actions![0].IsDefault);
            var entries = req.ItemsProvider().ToList();
            Assert.EndsWith("preview.png", entries.Single(e => e.Name == "with").ImageKey);
            Assert.Null(entries.Single(e => e.Name == "without").ImageKey);

            // a picker of other kinds is the classic one: no pane, no actions
            var other = new FakeOpenPicker();
            new AssetPickerLauncher(other.OpenPicker, new FakeCatalog(new StubAsset { Kind = AssetKind.BTree, Name = "p" }), MakeRouter(), baseFolderResolver: _ => "").Open(AssetKindFilter.BTree);
            Assert.False(other.CapturedRequest!.ShowSidePane);
            Assert.Null(other.CapturedRequest.Actions);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TempScenarioAt(string root, string name, out string json)
    {
        Directory.CreateDirectory(Path.Combine(root, name));
        json = Path.Combine(root, name, "scenario.json");
        File.WriteAllText(json, "{\"header\":{},\"entities\":{}}");
    }

    [Fact]
    public void CE4141_TheFactsAreReadFromScenarioJson_LazilyAndCached_AndAbsentFieldsStayAbsent()
    {
        string root = TempScenario(out var json);
        try
        {
            int before = ScenarioPaneFacts.Parses;
            var f = ScenarioPaneFacts.Get(json, nowMs: 1_000)!;
            Assert.Equal(("A hill attack.", "Hill", "CGF", 3), (f.Description, f.TerrainName, f.SubsystemType, f.EntityCount));
            Assert.Same(f, ScenarioPaneFacts.Get(json, nowMs: 1_500));          // a frame later: the cache, no parse
            Assert.Equal(before + 1, ScenarioPaneFacts.Parses);
            Assert.Same(f, ScenarioPaneFacts.Get(json, nowMs: 9_000));          // re-checked, the write time unchanged ⇒ still no parse
            Assert.Equal(before + 1, ScenarioPaneFacts.Parses);

            string root2 = TempScenario(out var json2, headerJson: "");
            try
            {
                var bare = ScenarioPaneFacts.Get(json2)!;
                Assert.Null(bare.TerrainName); Assert.Null(bare.Description);   // only what the file has — no placeholder
            }
            finally { Directory.Delete(root2, true); }
            Assert.Null(ScenarioPaneFacts.Get(Path.Combine(root, "missing", "scenario.json")));
            Assert.Null(ScenarioPaneFacts.Get(""));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CE4141_OpenAndGoLive_RoutesToTheRunChoice_AndOpenStaysTheRouter()
    {
        var control = new RecordingControl();
        var choice = RunChoiceOver(control);
        var asset = new StubAsset { Kind = AssetKind.Scenario, Name = "alpha" };
        var picker = new FakeOpenPicker();
        var opened = new List<string>();
        new AssetPickerLauncher(picker.OpenPicker, new FakeCatalog(asset), MakeRouter(onLoad: opened.Add), baseFolderResolver: _ => "", runChoice: () => choice)
            .Open(AssetKindFilter.Scenario);

        picker.InvokeHandler(new PickerResult(new[] { EntryFor(asset) }) { ActionId = AssetPickerLauncher.GoLiveActionId });
        Assert.Equal(new[] { "alpha" }, control.LiveLoads);                      // the door loaded it for LIVE
        Assert.Empty(opened);                                                     // and the Open router did not run

        picker.InvokeHandler(new PickerResult(new[] { EntryFor(asset) }) { ActionId = AssetPickerLauncher.OpenActionId });
        Assert.Equal(new[] { "alpha" }, opened);                                  // Open = today's route
        Assert.Single(control.LiveLoads);
    }

    // ═══ AQ108 D24 (the ui half) — the picker's Run setup ═══════════════════════════════════════════════════════════════════════════
    // Red-proof (compiles): the facts reader returning UsesVariants always false ⇒ the variant-facts rail fails; OpenAndGoLive not passing RunArgsFor ⇒ the frame rail's args assert fails.

    [Fact]
    public void D24_TheFacts_CarryTheHeadersParams_AndWhetherTheFileUsesVariants()
    {
        string root = Path.Combine(Path.GetTempPath(), "pkd24-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "v")); Directory.CreateDirectory(Path.Combine(root, "plain"));
            File.WriteAllText(Path.Combine(root, "v", "scenario.json"),
                "{\"header\":{\"description\":\"d\",\"Params\":[{\"Name\":\"count\",\"TypeId\":\"int\",\"Default\":2},{\"Name\":\"side\",\"TypeId\":\"ForceId\",\"Default\":\"Hostile\"}]},\"entities\":{\"a\":{\"Presence\":0.6}}}");
            File.WriteAllText(Path.Combine(root, "plain", "scenario.json"), "{\"header\":{\"description\":\"d\"},\"entities\":{\"a\":{}}}");

            var v = ScenarioPaneFacts.Get(Path.Combine(root, "v", "scenario.json"))!;
            Assert.True(v.UsesVariants);
            Assert.Equal(new[] { "count", "side" }, v.Params!.Select(p => p.Name));
            var plain = ScenarioPaneFacts.Get(Path.Combine(root, "plain", "scenario.json"))!;
            Assert.False(plain.UsesVariants);                       // no variant row for it
            Assert.Null(plain.Params);                              // and no param rows
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void D24_AStartThatNeverOpenedTheExpander_PassesNothingExtra()
    {
        var control = new RecordingControl();
        var choice = RunChoiceOver(control);
        var asset = new StubAsset { Kind = AssetKind.Scenario, Name = "alpha" };
        var picker = new FakeOpenPicker();
        var launcher = new AssetPickerLauncher(picker.OpenPicker, new FakeCatalog(asset), MakeRouter(), baseFolderResolver: _ => "", runChoice: () => choice);
        launcher.Open(AssetKindFilter.Scenario);
        Assert.Null(launcher.RunArgsFor("alpha"));
        picker.InvokeHandler(new PickerResult(new[] { EntryFor(asset) }) { ActionId = AssetPickerLauncher.GoLiveActionId });
        Assert.Equal(new[] { "alpha" }, control.LiveLoads);
        Assert.Empty(control.LiveLoadArgs);                         // the plain load: no args, no interaction
    }

    [Fact]
    public void CE4141_GoLive_IsDisabledWithAReason_WhenTheHostHasNoRunChoice_OrPicksNoScenario()
    {
        var asset = new StubAsset { Kind = AssetKind.Scenario, Name = "alpha" };
        var launcher = new AssetPickerLauncher(new FakeOpenPicker().OpenPicker, new FakeCatalog(asset), MakeRouter(), baseFolderResolver: _ => "");
        Assert.Contains("no run controls", launcher.GoLiveDisabledReason(EntryFor(asset)));
        Assert.Equal("Pick a scenario first", launcher.GoLiveDisabledReason(null));

        var withChoice = new AssetPickerLauncher(new FakeOpenPicker().OpenPicker, new FakeCatalog(asset), MakeRouter(), baseFolderResolver: _ => "", runChoice: () => RunChoiceOver(new RecordingControl()));
        Assert.Null(withChoice.GoLiveDisabledReason(EntryFor(asset)));
        Assert.Equal("Pick a scenario first", withChoice.GoLiveDisabledReason(EntryFor(new StubAsset { Kind = AssetKind.BTree, Name = "p" })));
    }

    [Fact]
    public void CE4141_ARemoteHost_HasNoFileToRead_SoItsPaneSaysSo()
    {
        Assert.Equal("preview and description not available on this host", AssetPickerLauncher.NoPreviewNotice);
        Assert.Null(ScenarioPaneFacts.PreviewOf(""));                              // an empty SourceFilePath (no local root) ⇒ no preview, no facts
        Assert.Null(ScenarioPaneFacts.Get(""));
    }

    /// <summary>⭐ CE-4141 (the silent-default rule) — the hosts that build the launcher AND hold a run choice must pass it, or <i>Open and go live</i> is dead there.</summary>
    [Fact]
    public void CE4141_EveryHostThatBuildsTheLauncher_PassesItsRunChoice()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        while (!File.Exists(Path.Combine(root, "HROT.sln")) && Directory.GetParent(root) != null) root = Directory.GetParent(root)!.FullName;
        foreach (var rel in new[] { "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs", "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs", "Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs" })
        {
            var text = File.ReadAllText(Path.Combine(root, rel));
            int at = text.IndexOf("new Hrot.Editor.AiShared.Browser.AssetPickerLauncher(", StringComparison.Ordinal);
            Assert.True(at >= 0, rel);
            string call = text.Substring(at, Math.Min(900, text.Length - at));
            Assert.Contains("runChoice:", call);
        }
    }

    /// <summary>⭐ AQ108 D24 (the silent-default rule) — the Editor and CGF HOLD a component edit service and a scenario world: they must pass the former to the launcher (else the Run setup's param rows are dead there) and override
    /// <c>IProvidesRunControls.RunSetupLines</c> (else the indicator's menu never names the run's variant).</summary>
    [Fact]
    public void D24_TheEditorAndCgf_PassTheEditServiceToTheLauncher_AndOfferTheRunSetupLines()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        while (!File.Exists(Path.Combine(root, "HROT.sln")) && Directory.GetParent(root) != null) root = Directory.GetParent(root)!.FullName;
        foreach (var rel in new[] { "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs", "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs" })
        {
            var text = File.ReadAllText(Path.Combine(root, rel));
            int at = text.IndexOf("new Hrot.Editor.AiShared.Browser.AssetPickerLauncher(", StringComparison.Ordinal);
            Assert.Contains("paramEditService:", text.Substring(at, Math.Min(1500, text.Length - at)));
            Assert.Contains("IProvidesRunControls.RunSetupLines", text);
        }
        var local = File.ReadAllText(Path.Combine(root, "Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs"));
        Assert.Contains("runSetupLines:", local);   // the ONE toolbar registration forwards the owner's lines
    }

    // ── ⭐ CE-4210 MA-6 — the Macros group ────────────────────────────────────────────────────────

    private static string MakeMacrosRoot(params string[] paths)
    {
        string root = Path.Combine(Path.GetTempPath(), "macros-" + Guid.NewGuid().ToString("N"));
        foreach (var p in paths)
        {
            var dir = Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scenario.json"), "{}");
        }
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void TheMacrosGroup_ListsExactlyTheMacroFolders_AndNoneOfTheScenarios()
    {
        string root = MakeMacrosRoot("test/fire-team", "forces/squad");
        try
        {
            var scenario = new StubAsset { Kind = AssetKind.Scenario, Name = "Combat/mine" };
            var launcher = new AssetPickerLauncher(new FakeOpenPicker().OpenPicker, new FakeCatalog(scenario), MakeRouter(), _ => "",
                macroPaths: () => ScenarioEnumeration.EnumerateRelPaths(root), macrosRoot: () => root);

            var entries = launcher.Entries(AssetKindFilter.Scenario);
            var macros = entries.Where(e => e.Category is { } c && (c == "Macros" || c.StartsWith("Macros/"))).ToList();

            Assert.Equal(new[] { "macros/forces/squad", "macros/test/fire-team" }, macros.Select(e => ((IEditableAsset)e.Tag!).Name).OrderBy(n => n).ToArray());
            Assert.All(macros, e => Assert.Equal(AssetKind.Scenario, ((IEditableAsset)e.Tag!).Kind));   // a pick is the ordinary scenario open of "macros/<path>"
            Assert.Contains(entries, e => e.Tag == scenario);                                            // the scenario list is untouched
            Assert.DoesNotContain(macros, e => ((IEditableAsset)e.Tag!).Name.Contains("Combat"));
            var asset = (MacroPickerEntries.MacroAsset)macros.First(e => e.Name == "fire-team").Tag!;
            Assert.Equal(Path.Combine(root, "test", "fire-team", "scenario.json"), asset.SourceFilePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnEmptyMacrosRoot_SaysSo_AndNeverFallsBackToTheRepo()
    {
        string root = MakeMacrosRoot();
        try
        {
            var launcher = new AssetPickerLauncher(new FakeOpenPicker().OpenPicker, new FakeCatalog(), MakeRouter(), _ => "",
                macroPaths: () => ScenarioEnumeration.EnumerateRelPaths(root), macrosRoot: () => root);
            var hint = Assert.Single(launcher.Entries(AssetKindFilter.Scenario));
            Assert.Equal("No macros on the server — see macros/ seeding", hint.Name);
            Assert.False(hint.IsEnabled);
            Assert.Null(hint.Tag);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AHostWithNoMacroListing_OffersNoMacrosGroup()
    {
        var launcher = new AssetPickerLauncher(new FakeOpenPicker().OpenPicker, new FakeCatalog(), MakeRouter(), _ => "");
        Assert.Empty(launcher.Entries(AssetKindFilter.Scenario));
        Assert.False(launcher.OpenMacros(_ => { }));
    }

    [Fact]
    public void PickingAMacroInTheMacrosPicker_ReturnsItsPath()
    {
        string root = MakeMacrosRoot("test/fire-team");
        try
        {
            var picker = new FakeOpenPicker();
            var launcher = new AssetPickerLauncher(picker.OpenPicker, new FakeCatalog(), MakeRouter(), _ => "",
                macroPaths: () => ScenarioEnumeration.EnumerateRelPaths(root), macrosRoot: () => root);
            string? got = null;
            Assert.True(launcher.OpenMacros(p => got = p));
            var entry = picker.CapturedRequest!.ItemsProvider().Single();
            picker.InvokeHandler(ConfirmResult(entry));
            Assert.Equal("test/fire-team", got);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TheWindowTitle_NamesAMacro()
    {
        Assert.Equal("Macro: test/fire-team", EditorWindowTitle.Detail("macros/test/fire-team"));
        Assert.Equal("Combat/mine", EditorWindowTitle.Detail("Combat/mine"));
        Assert.Null(EditorWindowTitle.Detail(null));
    }
}
