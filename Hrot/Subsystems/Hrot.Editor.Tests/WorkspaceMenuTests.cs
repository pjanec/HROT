using Fdp.Toolkit.DER;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Unit tests for <see cref="WorkspaceMenuBuilder"/> — MTB-P7-T3 success conditions.
/// Pure logic tests using real <see cref="AiDocumentManager"/> (headless)
/// and a fake <see cref="IEditorLogic"/>.
/// </summary>
public sealed class WorkspaceMenuTests
{
    // ── Fake IEditorLogic ────────────────────────────────────────────────────

    private sealed class FakeEditorLogic : IEditorLogic
    {
        public string? LoadedScenarioNameValue;
        public string? LoadedScenarioName => LoadedScenarioNameValue;

        public void Update() { }
        public void NewScenario() { }
        public void SaveScenario(string filePath) { }
        public void LoadScenario(string filePath) { }
        public void LoadScenarioByName(string scenarioName) { }
        public void SaveCurrentScenario() { }
        public void SaveScenarioAs(string scenarioName) { }
        public IReadOnlyList<string> AvailableScenarios => Array.Empty<string>();
        public void ActivateTool(EditorTool tool) { }
        public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents) { }
        public IDerRepo View => null!;
        public Task SwitchToExternalAsync() => Task.CompletedTask;
        public Task SwitchToInternalAsync() => Task.CompletedTask;
        public SimHostMode CurrentMode => SimHostMode.Internal;
        public void CenterOnEntity(long entityId) { }
        public void SelectEntity(long entityId) { }
        public void OpenRenameDialog(long entityId) { }
        public void RebuildAndReloadAI() { }
        public bool IsScenarioDegraded => false;
        public IReadOnlyList<Fdp.Core.Serialization.Migrations.SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()
            => Array.Empty<Fdp.Core.Serialization.Migrations.SidecarFileInfo>();
    }

    // ── Helper — create a fake document ──────────────────────────────────────

    private static AiDocument CreateDoc(AiDocumentManager mgr, string name, AssetKind kind, bool dirty = false)
    {
        var asset = new FakeAsset(name, kind);
        var doc = mgr.Open(asset);
        if (dirty) doc.MarkDirty();
        return doc;
    }

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(string name, AssetKind kind) { Name = name; Kind = kind; }
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; }
        public AssetKind Kind { get; }
        public string SourceFilePath => "/fake/" + Name;
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private static AiDocumentManager CreateDocManager()
    {
        // Use a no-op perspective switch callback.
        return new AiDocumentManager(_ => { });
    }

    // ── Lists_OpenDocuments_AndLoadedScenario ────────────────────────────────

    [Fact]
    public void Lists_OpenDocuments_AndLoadedScenario()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = "CombatScenario" };

        // Open two documents of different kinds.
        var blueprintDoc = CreateDoc(docManager, "MyBlueprint", AssetKind.Blueprint, dirty: true);
        var btreeDoc = CreateDoc(docManager, "MyBTree", AssetKind.BTree);

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        // 3 entries: 2 open docs + 1 loaded scenario.
        // Order follows OpenDocuments insertion order.
        Assert.Equal(3, entries.Count);

        // First entry: MyBlueprint (first opened, not active, dirty).
        var bpEntry = entries[0];
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint), bpEntry.IconKey);
        Assert.Equal("MyBlueprint", bpEntry.Label);
        Assert.False(bpEntry.IsActive);
        Assert.True(bpEntry.IsDirty);
        Assert.NotNull(bpEntry.OnSelect);

        // Second entry: MyBTree (opened second, now active).
        var btEntry = entries[1];
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.BTree), btEntry.IconKey);
        Assert.Equal("MyBTree", btEntry.Label);
        Assert.True(btEntry.IsActive);
        Assert.False(btEntry.IsDirty);
        Assert.NotNull(btEntry.OnSelect);

        // Third entry: loaded scenario.
        var scenarioEntry = entries[2];
        Assert.Equal(AssetKindIcons.ScenarioIconKey, scenarioEntry.IconKey);
        Assert.Equal("CombatScenario", scenarioEntry.Label);
        Assert.False(scenarioEntry.IsActive);
        Assert.False(scenarioEntry.IsDirty);
        Assert.Null(scenarioEntry.OnSelect);
    }

    [Fact]
    public void NoScenarioLoaded_OnlyDocumentsListed()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        CreateDoc(docManager, "Doc1", AssetKind.Hsm);

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        Assert.Single(entries);
        Assert.Equal("Doc1", entries[0].Label);
    }

    [Fact]
    public void NoDocuments_OnlyScenarioListed()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = "OnlyScenario" };

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        Assert.Single(entries);
        Assert.Equal(AssetKindIcons.ScenarioIconKey, entries[0].IconKey);
        Assert.Equal("OnlyScenario", entries[0].Label);
    }

    [Fact]
    public void Empty_WhenNothingLoadedOrOpen()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        Assert.Empty(entries);
    }

    // ── SelectDocument_CallsActivate ─────────────────────────────────────────

    [Fact]
    public void SelectDocument_CallsActivate()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        // Open two docs. First opened becomes inactive when second is opened.
        var doc1 = CreateDoc(docManager, "Doc1", AssetKind.Blueprint);
        var doc2 = CreateDoc(docManager, "Doc2", AssetKind.BTree);
        // doc2 is now active (most recently opened/activated).

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.Equal(2, entries.Count);

        // OpenDocuments order: [doc1, doc2].
        // entries[0] = doc1 (Blueprint, NOT active), entries[1] = doc2 (BTree, active).
        var doc1Entry = entries[0];
        Assert.Equal("Doc1", doc1Entry.Label);
        Assert.False(doc1Entry.IsActive);
        var doc2Entry = entries[1];
        Assert.Equal("Doc2", doc2Entry.Label);
        Assert.True(doc2Entry.IsActive);

        // Select doc1 (inactive) → should activate it.
        Assert.NotNull(doc1Entry.OnSelect);
        doc1Entry.OnSelect();

        Assert.Equal(doc1, docManager.Active);
    }

    // ── RebuiltFromLiveState_EachBuild ───────────────────────────────────────

    [Fact]
    public void RebuiltFromLiveState_EachBuild()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        // Build 1: one open doc.
        CreateDoc(docManager, "Doc1", AssetKind.Hsm);
        var entries1 = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.Single(entries1);
        Assert.Equal("Doc1", entries1[0].Label);

        // Build 2: add another doc — should reflect the new state.
        CreateDoc(docManager, "Doc2", AssetKind.Blueprint);
        var entries2 = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.Equal(2, entries2.Count);
        Assert.Contains(entries2, e => e.Label == "Doc1");
        Assert.Contains(entries2, e => e.Label == "Doc2");

        // Build 3: set loaded scenario — should appear.
        editorLogic.LoadedScenarioNameValue = "NewScenario";
        var entries3 = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.Equal(3, entries3.Count);
        Assert.Contains(entries3, e => e.Label == "NewScenario");
        Assert.Equal(AssetKindIcons.ScenarioIconKey,
            entries3.First(e => e.Label == "NewScenario").IconKey);

        // Build 4: remove scenario — should disappear.
        editorLogic.LoadedScenarioNameValue = null;
        var entries4 = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.Equal(2, entries4.Count);
        Assert.DoesNotContain(entries4, e => e.Label == "NewScenario");
    }

    [Fact]
    public void MarkDirty_ReflectedInSubsequentBuild()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        var doc = CreateDoc(docManager, "Doc", AssetKind.Blueprint);

        // Initially clean.
        var entries1 = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.False(entries1[0].IsDirty);

        // Mark dirty, rebuild.
        doc.MarkDirty();
        var entries2 = WorkspaceMenuBuilder.Build(docManager, editorLogic);
        Assert.True(entries2[0].IsDirty);
    }

    [Fact]
    public void ActiveMarker_ChangesWhenDifferentDocActivated()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        var doc1 = CreateDoc(docManager, "Doc1", AssetKind.BTree);
        var doc2 = CreateDoc(docManager, "Doc2", AssetKind.Hsm);
        // doc2 is active (most recently opened).

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        // OpenDocuments order: [doc1 (BTree), doc2 (Hsm)]; doc2 is most recently opened → active.
        var doc1Entry = entries.First(e => e.Label == "Doc1");
        var doc2Entry = entries.First(e => e.Label == "Doc2");
        Assert.False(doc1Entry.IsActive);
        Assert.True(doc2Entry.IsActive);

        // Activate doc1.
        docManager.Activate(doc1);
        var entries2 = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        var doc1Entry2 = entries2.First(e => e.Label == "Doc1");
        var doc2Entry2 = entries2.First(e => e.Label == "Doc2");
        Assert.True(doc1Entry2.IsActive);
        Assert.False(doc2Entry2.IsActive);
    }

    [Fact]
    public void IconKeys_MatchAssetKind()
    {
        var docManager = CreateDocManager();
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = "S" };

        CreateDoc(docManager, "bp", AssetKind.Blueprint);
        CreateDoc(docManager, "bt", AssetKind.BTree);
        CreateDoc(docManager, "hsm", AssetKind.Hsm);

        var entries = WorkspaceMenuBuilder.Build(docManager, editorLogic);

        // Should have 4 entries: bp (active), bt, hsm + scenario S.
        // Order follows OpenDocuments insertion order (bp opened first, then bt, then hsm).
        Assert.Equal(4, entries.Count);

        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint), entries[0].IconKey);
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.BTree), entries[1].IconKey);
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Hsm), entries[2].IconKey);
        Assert.Equal(AssetKindIcons.ScenarioIconKey, entries[3].IconKey);
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Build_NullDocManager_ThrowsArgumentNullException()
    {
        var editorLogic = new FakeEditorLogic();
        Assert.Throws<ArgumentNullException>(() =>
            WorkspaceMenuBuilder.Build(null!, editorLogic));
    }

    [Fact]
    public void Build_NullEditorLogic_ThrowsArgumentNullException()
    {
        var docManager = CreateDocManager();
        Assert.Throws<ArgumentNullException>(() =>
            WorkspaceMenuBuilder.Build(docManager, null!));
    }
}
