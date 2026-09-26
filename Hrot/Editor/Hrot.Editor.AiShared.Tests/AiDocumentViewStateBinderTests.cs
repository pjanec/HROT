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
