using Hrot.Editor.AiComposition;
using Hrot.Editor.AiShared.Documents;

namespace Hrot.Editor.AiShared.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-340</c> — the ONE binder both composition roots call.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.18.
///
/// <para>🔒 <b>Raised by the user:</b> <i>"you mention editor path is wired and then you go cgf, that
/// seems like editor is running different code, not unified, which is undesired."</i> 📐 Measured:
/// <c>EditorSubsystem</c> and <c>CgfSubsystem</c> each carried a structurally identical
/// <c>DocumentOpened</c> handler, differing in only TWO of a dozen inputs — the debug sessions and
/// the tail.</para>
///
/// <para>⚠ <b>What these rails can and cannot prove.</b> ⛔ They do NOT build a real canvas — that
/// needs an atlas-backed adapter bundle, and the per-kind factories already have their own suites
/// (<c>HsmDocumentFactoryTests</c>, <c>BTreeDocumentFactoryTests</c>,
/// <c>BlueprintDocumentFactoryTests</c>). ⭐ They pin the parts the BINDER owns and the two hosts used
/// to own separately: the subscription, the re-open guard, the non-document-kind no-op, and the host
/// tail. 🔒 <c>T-1</c> — the feature's own suites still own the canvas.</para>
/// </summary>
public sealed class AiDocumentViewStateBinderTests
{
    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "A";
        public AssetKind Kind { get; init; } = AssetKind.Scenario;
        public string SourceFilePath => "/a.json";
        public bool IsDirty { get; private set; }
        public bool IsEditorOwned => false;
        public event Action? Changed;
        public void RaiseChanged() { IsDirty = true; Changed?.Invoke(); }
    }

    private static AiDocumentHostServices Services(
        AiDocumentManager manager, Action<AiDocument>? tail = null) =>
        new()
        {
            // ⚠ A null adapter bundle is fine for the kinds these rails open: the binder only
            //   touches it inside a factory arm, and Scenario has none.
            Adapters         = null!,
            DocumentManager  = manager,
            OnDocumentOpened = tail,
        };

    /// <summary>
    /// ⭐⭐ <b>A kind with no canvas factory is a deliberate NO-OP, not a throw.</b>
    /// ⛔ Scenario / Blackboard / Utility are not document-backed, and a host must not have to know
    /// that — which is precisely the knowledge that used to be copied into both subsystems.
    /// </summary>
    [Fact]
    public void ANonDocumentKind_LeavesViewStateNull_AndDoesNotThrow()
    {
        var manager = new AiDocumentManager(_ => { });
        AiDocumentViewStateBinder.Bind(Services(manager));

        var doc = manager.Open(new FakeAsset { Kind = AssetKind.Scenario });

        Assert.Null(doc.ViewState);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The host tail runs — this is the ONE thing the two hosts genuinely do differently</b>
    /// (the editor also queues the asset into its regeneration scheduler; CGF only marks dirty).
    /// ⛔ It is a parameter for that reason, so it must actually be invoked.
    /// </summary>
    [Fact]
    public void TheHostTail_IsInvokedForANewlyOpenedDocument()
    {
        var manager = new AiDocumentManager(_ => { });
        var seen = new List<AiDocument>();
        AiDocumentViewStateBinder.Bind(Services(manager, doc => seen.Add(doc)));

        var doc = manager.Open(new FakeAsset());

        Assert.Single(seen);
        Assert.Same(doc, seen[0]);
    }

    /// <summary>
    /// ⭐⭐ <b>The MA-003 tail wires <c>Asset.Changed</c> → <c>MarkDirty</c>, as CGF needs.</b>
    /// 🔴 Measured `2026-08-25`: CGF could edit a graph and save NOTHING, reporting success, because
    /// nothing marked the document dirty. ⇒ the tail is load-bearing, not bookkeeping.
    /// </summary>
    [Fact]
    public void TheTailCanWireDirtyMarking_SoASaveActuallyReachesTheFile()
    {
        var manager = new AiDocumentManager(_ => { });
        AiDocumentViewStateBinder.Bind(Services(manager,
            doc => doc.Asset.Changed += () => doc.MarkDirty()));

        var asset = new FakeAsset();
        var doc = manager.Open(asset);
        Assert.False(doc.IsDirty);

        asset.RaiseChanged();

        Assert.True(doc.IsDirty);
    }

    /// <summary>
    /// ⚠ <b>Binding twice must not double-invoke the tail for one open.</b> ⛔ Each host calls
    /// <c>Bind</c> exactly once; this pins that the binder adds ONE subscription per call, so a
    /// future host that binds in two places sees the duplication rather than a subtle double-fire.
    /// </summary>
    [Fact]
    public void EachBind_AddsExactlyOneSubscription()
    {
        var manager = new AiDocumentManager(_ => { });
        int calls = 0;
        AiDocumentViewStateBinder.Bind(Services(manager, _ => calls++));

        manager.Open(new FakeAsset());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Bind_WithNullServices_Throws()
        => Assert.Throws<ArgumentNullException>(() => AiDocumentViewStateBinder.Bind(null!));
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-343</c> — the shared active-document binder.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.19.
/// </summary>
public sealed class AiActiveDocumentBinderTests
{
    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "A";
        public AssetKind Kind { get; init; } = AssetKind.BTree;
        public string SourceFilePath => "/a.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    /// <summary>
    /// ⭐⭐ <b>The active kind's store gets the asset and EVERY other store goes null.</b>
    /// ⛔ The null half is the load-bearing one: a store left pointing at the previous asset keeps
    /// rendering its schema, which is the defect the per-frame pull model would otherwise hide.
    /// </summary>
    [Fact]
    public void OnlyTheActiveKindsStore_HoldsTheAsset()
    {
        var manager = new AiDocumentManager(_ => { });
        var btree = new Hrot.Editor.AiShared.Selection.EditorSelectionStore();
        var hsm   = new Hrot.Editor.AiShared.Selection.EditorSelectionStore();
        var bp    = new Hrot.Editor.AiShared.Selection.EditorSelectionStore();

        Hrot.Editor.AiComposition.AiActiveDocumentBinder.Bind(
            new Hrot.Editor.AiComposition.AiActiveDocumentServices
            {
                DocumentManager = manager,
                BTreeStore = btree, HsmStore = hsm, BlueprintStore = bp,
            });

        var asset = new FakeAsset { Kind = AssetKind.BTree };
        manager.Open(asset);

        Assert.Same(asset, btree.ActiveAsset);
        Assert.Null(hsm.ActiveAsset);
        Assert.Null(bp.ActiveAsset);
    }

    /// <summary>
    /// 🔴🔴 <b>THE RAIL THAT PINS THE BUG THIS EXTRACTION NEARLY INTRODUCED.</b>
    /// 📐 <c>EditorSubsystem</c> wires <c>ActiveChanged</c> ~900 lines BEFORE it assigns
    /// <c>_blueprintMyBlueprintWindow</c>. ⇒ the outline must be resolved PER FIRE, not captured at
    /// bind time — capturing would have passed <see langword="null"/> forever and silently disabled
    /// the panel on the editor only. ⛔ That is exactly the split this whole programme is closing.
    /// </summary>
    [Fact]
    public void TheOutlineProvider_IsResolvedPerFire_NotCapturedAtBindTime()
    {
        var manager = new AiDocumentManager(_ => { });
        Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow? late = null;
        int resolved = 0;

        Hrot.Editor.AiComposition.AiActiveDocumentBinder.Bind(
            new Hrot.Editor.AiComposition.AiActiveDocumentServices
            {
                DocumentManager  = manager,
                BlueprintOutline = () => { resolved++; return late; },
            });

        // ⭐ Assigned AFTER Bind, exactly as the editor does.
        late = new Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow();

        manager.Open(new FakeAsset { Kind = AssetKind.BTree });

        Assert.Equal(1, resolved);   // ⛔ a captured value would never have asked
    }

    /// <summary>⭐ The host hook runs, and receives the active document.</summary>
    [Fact]
    public void TheHostHook_RunsAfterTheSharedRetarget()
    {
        var manager = new AiDocumentManager(_ => { });
        var store = new Hrot.Editor.AiShared.Selection.EditorSelectionStore();
        IEditableAsset? seenInHook = null;

        Hrot.Editor.AiComposition.AiActiveDocumentBinder.Bind(
            new Hrot.Editor.AiComposition.AiActiveDocumentServices
            {
                DocumentManager = manager,
                BTreeStore      = store,
                // ⭐ Asserts ORDER: the store is already retargeted when the hook runs.
                AfterRetarget   = _ => seenInHook = store.ActiveAsset,
            });

        var asset = new FakeAsset { Kind = AssetKind.BTree };
        manager.Open(asset);

        Assert.Same(asset, seenInHook);
    }

    [Fact]
    public void Bind_WithNullServices_Throws()
        => Assert.Throws<ArgumentNullException>(
               () => Hrot.Editor.AiComposition.AiActiveDocumentBinder.Bind(null!));
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-348</c> — the kernel adapter the design deferred at "Slice 3+".</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.22.
///
/// <para>🔒 <b>User:</b> <i>"zero callers for btreedebugsession and trace loop is again a sign of
/// under adoption, not un-necessity."</i> ⭐ The corpus agrees — <c>Hrot.BTree.Editor.md:294</c> says
/// the session's <c>Record*</c> methods exist <i>"for the FUTURE kernel adapter"</i>.</para>
///
/// <para>⚠ These rails pin the PUMP's contract — when it advances the session and when it must not.
/// ⛔ They do not drive a real kernel: <c>Update</c> needs a live <c>EntityRepository</c>, and the
/// session's own behaviour is its suite's business (<c>T-1</c>).</para>
/// </summary>
public sealed class AiDebugSessionPumpTests
{
    /// <summary>
    /// ⭐⭐ <b>No selected entity ⇒ the session is NOT advanced, and nothing throws.</b>
    /// 🔒 An empty selection is an ordinary state on a per-frame hook, not an error.
    /// </summary>
    [Fact]
    public void WithNoSelectedEntity_TheSessionIsNotAdvanced()
    {
        int worldAsked = 0;
        var pump = Hrot.Editor.AiComposition.AiDebugSessionPump.ForBTree(
            new Hrot.BTree.Editor.Debug.BTreeDebugSession(),
            new Hrot.Editor.AiComposition.AiDebugSessionPumpServices
            {
                World          = () => { worldAsked++; return null; },
                SelectedEntity = () => null,
            });

        var ex = Record.Exception(() => pump(null!));

        Assert.Null(ex);
        Assert.Equal(1, worldAsked);   // ⭐ asked, then declined to advance
    }

    /// <summary>
    /// ⭐⭐ <b>No session ⇒ a no-op that never touches the providers.</b> ⛔ A host without a session
    /// (as CGF was before <c>CE-345</c>) must not pay for the hook, and must not need to branch.
    /// </summary>
    [Fact]
    public void WithNoSession_ThePumpIsAPureNoOp()
    {
        int asked = 0;
        var pump = Hrot.Editor.AiComposition.AiDebugSessionPump.ForBTree(
            null,
            new Hrot.Editor.AiComposition.AiDebugSessionPumpServices
            {
                World          = () => { asked++; return null; },
                SelectedEntity = () => { asked++; return null; },
            });

        pump(null!);

        Assert.Equal(0, asked);
    }

    /// <summary>
    /// 🔴 <b>The providers are resolved PER FIRE — the <c>CE-343</c> lesson, applied.</b>
    /// 📐 Both hosts assign their world during initialisation, AFTER the canvas hook is built ⇒
    /// capturing it would pin <see langword="null"/> forever.
    /// </summary>
    [Fact]
    public void TheWorldProvider_IsResolvedPerFire_NotCapturedAtBuildTime()
    {
        int calls = 0;
        var pump = Hrot.Editor.AiComposition.AiDebugSessionPump.ForBTree(
            new Hrot.BTree.Editor.Debug.BTreeDebugSession(),
            new Hrot.Editor.AiComposition.AiDebugSessionPumpServices
            {
                World          = () => { calls++; return null; },
                SelectedEntity = () => null,
            });

        pump(null!);
        pump(null!);
        pump(null!);

        Assert.Equal(3, calls);
    }

    /// <summary>⭐ <c>Then</c> runs both actions, in order — the editor composes the pump onto a
    /// canvas hook that already carries a selection bridge, and neither may own the other.</summary>
    [Fact]
    public void Then_RunsBothActionsInOrder()
    {
        var order = new List<string>();
        var composed = Hrot.Editor.AiComposition.AiDebugSessionPump.Then(
            _ => order.Add("first"), _ => order.Add("second"));

        composed(null!);

        Assert.Equal(new[] { "first", "second" }, order);
    }

    /// <summary>⚠ <c>Then</c> tolerates a null half, so a host need not branch.</summary>
    [Fact]
    public void Then_ToleratesANullHalf()
    {
        var ran = false;
        Hrot.Editor.AiComposition.AiDebugSessionPump.Then(null, _ => ran = true)(null!);
        Assert.True(ran);

        Assert.Null(Record.Exception(
            () => Hrot.Editor.AiComposition.AiDebugSessionPump.Then(null, null)(null!)));
    }
}
