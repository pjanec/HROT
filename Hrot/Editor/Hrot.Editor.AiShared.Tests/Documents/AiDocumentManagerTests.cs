using Hrot.Editor.AiShared.Documents;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>
/// Tests for <see cref="AiDocumentManager"/> (AIE-012).
/// </summary>
public sealed class AiDocumentManagerTests
{
    // ── Fake asset ────────────────────────────────────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind = AssetKind.BTree, string name = "TestAsset")
        {
            AssetId = Guid.NewGuid();
            Kind    = kind;
            Name    = name;
        }

        public Guid AssetId { get; }
        public string Name  { get; }
        public AssetKind Kind { get; }
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AiDocumentManager Make(
        List<string>? switchLog   = null,
        List<AiDocument?>? focusLog = null)
    {
        var sw = switchLog ?? new List<string>();
        var fc = focusLog;

        return new AiDocumentManager(
            perspectiveSwitchCallback: kind => sw.Add(kind),
            focusCallback: fc != null ? doc => fc.Add(doc) : null);
    }

    // ── AIE-012 SC1: Open adds and activates ─────────────────────────────────

    [Fact]
    public void AiDocumentManager_Open_AddsDocument_AndActivates()
    {
        var manager = Make();
        var asset   = new FakeAsset(AssetKind.BTree, "MyTree");

        var doc = manager.Open(asset);

        Assert.NotNull(doc);
        Assert.Single(manager.OpenDocuments);
        Assert.Same(doc, manager.Active);
        Assert.Same(asset, doc.Asset);
        Assert.Equal(AssetKind.BTree, doc.Kind);
    }

    // ── AIE-012 SC2: Opening an already-open asset focuses it, no duplicate ──

    [Fact]
    public void AiDocumentManager_OpenAlreadyOpen_FocusesExisting_NoDuplicate()
    {
        var manager = Make();
        var asset   = new FakeAsset();

        var doc1 = manager.Open(asset);
        var doc2 = manager.Open(asset);   // same asset again

        // Must return the same instance.
        Assert.Same(doc1, doc2);
        // Only one document in the list.
        Assert.Single(manager.OpenDocuments);
        // It must be the active document.
        Assert.Same(doc1, manager.Active);
    }

    // ── AIE-012 SC3: Activate invokes perspective-switch with kind name ───────

    [Fact]
    public void AiDocumentManager_Activate_InvokesPerspectiveSwitchWithKind()
    {
        var switchLog = new List<string>();
        var manager   = Make(switchLog: switchLog);

        // Open a BTree asset.
        var bTreeAsset = new FakeAsset(AssetKind.BTree, "BTree1");
        var bTreeDoc   = manager.Open(bTreeAsset);

        // Open an HSM asset and activate it.
        var hsmAsset = new FakeAsset(AssetKind.Hsm, "Hsm1");
        var hsmDoc   = manager.Open(hsmAsset);

        // Re-activate the BTree doc.
        manager.Activate(bTreeDoc);

        // Expected switch sequence: "BTree" (Open BTree), "Hsm" (Open HSM), "BTree" (re-activate).
        Assert.Equal(3, switchLog.Count);
        Assert.Equal("BTree",     switchLog[0]);
        Assert.Equal("Hsm",       switchLog[1]);
        Assert.Equal("BTree",     switchLog[2]);
    }

    // ── AIE-012 SC4: Close removes doc and activates next-or-none ────────────

    [Fact]
    public void AiDocumentManager_Close_RemovesDocument_AndActivatesNextOrNone()
    {
        var manager = Make();
        var a1 = new FakeAsset(AssetKind.BTree, "A1");
        var a2 = new FakeAsset(AssetKind.Hsm,   "A2");
        var a3 = new FakeAsset(AssetKind.BTree,  "A3");

        var d1 = manager.Open(a1);
        var d2 = manager.Open(a2);
        var d3 = manager.Open(a3);   // d3 is now active

        // Close the active doc → should activate d2 (the new last doc).
        manager.Close(d3);
        Assert.Equal(2, manager.OpenDocuments.Count);
        Assert.Same(d2, manager.Active);
        Assert.DoesNotContain(d3, manager.OpenDocuments);

        // Close d1 (not active) → active stays d2.
        manager.Close(d1);
        Assert.Single(manager.OpenDocuments);
        Assert.Same(d2, manager.Active);

        // Close the last doc → active becomes null.
        manager.Close(d2);
        Assert.Empty(manager.OpenDocuments);
        Assert.Null(manager.Active);
    }

    // ── AIE-012 SC5: ViewState is preserved per document ─────────────────────

    [Fact]
    public void AiDocumentManager_PreservesViewStatePerDocument()
    {
        var manager = Make();
        var assetA  = new FakeAsset(AssetKind.BTree, "A");
        var assetB  = new FakeAsset(AssetKind.Hsm,   "B");

        var docA = manager.Open(assetA);
        var docB = manager.Open(assetB);

        // Assign a ViewState to docA (simulating what the canvas would do).
        var stateA = new object();
        docA.ViewState = stateA;

        // Assign a ViewState to docB.
        var stateB = new object();
        docB.ViewState = stateB;

        // Switch to docA and back to docB.
        manager.Activate(docA);
        manager.Activate(docB);

        // Both ViewState objects must survive the switching.
        Assert.Same(stateA, docA.ViewState);
        Assert.Same(stateB, docB.ViewState);
    }

    // ── AIE-012 SC6: ActiveChanged fires on Activate ──────────────────────────

    [Fact]
    public void AiDocumentManager_ActiveChanged_FiresOnActivate()
    {
        var manager = Make();
        var asset   = new FakeAsset();

        int fireCount = 0;
        manager.ActiveChanged += () => fireCount++;

        var doc = manager.Open(asset);   // fires once (Open → Activate)
        manager.Activate(doc);           // fires again (explicit Activate)

        Assert.Equal(2, fireCount);
    }

    // ── Additional edge cases ─────────────────────────────────────────────────

    /// <summary>ActiveChanged fires when Close sets active to null.</summary>
    [Fact]
    public void AiDocumentManager_Close_LastDoc_ActiveChangedFires()
    {
        var manager = Make();
        var asset   = new FakeAsset();

        int fireCount = 0;
        manager.Open(asset);             // resets counter baseline
        manager.ActiveChanged += () => fireCount++;

        manager.Close(manager.OpenDocuments[0]);

        Assert.Equal(1, fireCount);
        Assert.Null(manager.Active);
    }

    /// <summary>Activate of a doc not in the open list is a no-op (guard).</summary>
    [Fact]
    public void AiDocumentManager_Activate_UnknownDoc_IsNoOp()
    {
        var manager = Make();
        var assetA  = new FakeAsset();
        var docA    = manager.Open(assetA);

        // Build a doc that is NOT registered with the manager.
        var orphan = new AiDocument(new FakeAsset(), AssetKind.Blueprint);

        int fires = 0;
        manager.ActiveChanged += () => fires++;

        // Must not throw, must not change active, must not fire.
        manager.Activate(orphan);

        Assert.Same(docA, manager.Active);
        Assert.Equal(0, fires);
    }

    /// <summary>Close of a doc not in the open list is a no-op (guard).</summary>
    [Fact]
    public void AiDocumentManager_Close_UnknownDoc_IsNoOp()
    {
        var manager = Make();
        var assetA  = new FakeAsset();
        manager.Open(assetA);

        var orphan = new AiDocument(new FakeAsset(), AssetKind.Blueprint);

        // Must not throw, must not change state.
        var exception = Record.Exception(() => manager.Close(orphan));
        Assert.Null(exception);
        Assert.Single(manager.OpenDocuments);
    }

    /// <summary>
    /// The perspective-switch callback receives the exact enum name
    /// ("BTree", "Hsm", "Blueprint") — confirming AssetKind.ToString() is the convention.
    /// </summary>
    [Fact]
    public void AiDocumentManager_SwitchCallback_ReceivesKindName()
    {
        var log = new List<string>();
        var mgr = Make(switchLog: log);

        mgr.Open(new FakeAsset(AssetKind.Blueprint, "Bp1"));
        mgr.Open(new FakeAsset(AssetKind.Hsm,       "Hsm1"));
        mgr.Open(new FakeAsset(AssetKind.BTree,     "Bt1"));

        Assert.Equal(new[] { "Blueprint", "Hsm", "BTree" }, log);
    }
}
