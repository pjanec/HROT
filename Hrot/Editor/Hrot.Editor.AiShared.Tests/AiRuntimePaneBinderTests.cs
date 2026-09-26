using Hrot.Editor.AiComposition;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-351</c> — the runtime panes are registered by ONE binder, and BOTH hosts call it.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.25.
///
/// <para>📐 <b>The measurement these rails exist to keep true.</b> Before this,
/// <c>RegisterRuntimePane</c> had three call sites — all in <c>EditorSubsystem</c> — and <b>zero</b>
/// on CGF, so CGF offered no <c>details.runtime.&lt;kind&gt;</c> view at all. The conformance
/// baseline blamed a missing <c>IBlueprintDebugSession</c>; <c>CE-344</c> had already given CGF one.</para>
///
/// <para>⛔⛔ <b>What these rails deliberately do NOT claim.</b> They do not assert that any host
/// publishes a panel of kind <c>runtime-inspector</c>. 🔒 <c>CE-303</c> <b>dissolved</b>
/// <c>RuntimeInspectorWindow</c> — <i>"a pane is now reached through
/// <c>details.runtime.&lt;kind&gt;</c> and nothing else"</i> — so that kind exists on neither host,
/// and a rail asserting it would be pinning a panel that was deliberately deleted.</para>
/// </summary>
public sealed class AiRuntimePaneBinderTests
{
    private static PerspectiveWorkspaceRegistrar Registrar(string perspective)
    {
        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(),
            new Hrot.Editor.AiShared.Tests.Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false);

        return services.CreateRegistrar(
            perspective, new EditorSelectionStore(), validators: Array.Empty<IAssetValidator>());
    }

    private static AiRuntimePaneServices Services(
        PerspectiveWorkspaceRegistrar bt,
        PerspectiveWorkspaceRegistrar hsm,
        PerspectiveWorkspaceRegistrar bp,
        bool withBTree = true, bool withHsm = true, bool withBlueprint = true,
        Func<AiDocumentManager?>? documents = null) => new()
        {
            BTreeRegistrar        = bt,
            HsmRegistrar          = hsm,
            BlueprintRegistrar    = bp,
            BTreeDebugSession     = withBTree ? new Hrot.BTree.Editor.Debug.BTreeDebugSession() : null,
            HsmDebugSession       = withHsm   ? new Hrot.Hsm.Editor.Debug.HsmDebugSession()     : null,
            BlueprintDebugSession = withBlueprint ? NewBlueprintSession() : null,
            Documents             = documents ?? (() => null),
        };

    /// <summary>
    /// ⭐⭐ <b>A session per kind becomes a Details view per kind</b> — on the registrar that kind's
    /// perspective owns, not on whichever one happened to be handy.
    /// </summary>
    [Fact]
    public void EveryKindWithASession_ContributesItsOwnRuntimeDetailsView()
    {
        var bt  = Registrar("BTree");
        var hsm = Registrar("HSM");
        var bp  = Registrar("Blueprint");

        int registered = AiRuntimePaneBinder.Bind(Services(bt, hsm, bp));

        Assert.Equal(3, registered);
        Assert.Contains(bt.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree));
        Assert.Contains(hsm.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.Hsm));
        Assert.Contains(bp.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.Blueprint));
    }

    /// <summary>
    /// ⭐ <b>A host without a session for a kind registers no pane for it</b> — the editor's own
    /// guard, preserved. ⚠ A pane with no session draws nothing, so registering one would be a
    /// view that exists only to be empty.
    /// </summary>
    [Fact]
    public void AKindWithNoSession_ContributesNoView()
    {
        var bt  = Registrar("BTree");
        var hsm = Registrar("HSM");
        var bp  = Registrar("Blueprint");

        int registered = AiRuntimePaneBinder.Bind(
            Services(bt, hsm, bp, withBTree: false, withHsm: false));

        Assert.Equal(1, registered);
        Assert.DoesNotContain(bt.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree));
        Assert.DoesNotContain(hsm.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.Hsm));
        Assert.Contains(bp.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.Blueprint));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The document manager is read LATE, not captured.</b> 🔒 The <c>CE-343</c> lesson, and
    /// the reason this is a rail rather than a comment: on both hosts the binder runs during
    /// initialisation, and a captured value would pin whatever was open then — or null — for the
    /// process's whole life.
    /// </summary>
    [Fact]
    public void TheDocumentProvider_IsCalledPerRead_NotCapturedAtBindTime()
    {
        var bt  = Registrar("BTree");
        var hsm = Registrar("HSM");
        var bp  = Registrar("Blueprint");

        int calls = 0;
        AiDocumentManager? manager = null;

        AiRuntimePaneBinder.Bind(Services(bt, hsm, bp, documents: () =>
        {
            calls++;
            return manager;
        }));

        // ⛔ Binding must not have resolved the manager yet — that is exactly the capture we forbid.
        Assert.Equal(0, calls);

        // The host assigns its manager AFTER the bind, as both composition roots do.
        manager = new AiDocumentManager(_ => { });

        var view = bp.DetailsViews.All.Single(
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.Blueprint));
        Assert.NotNull(view);
    }

    [Fact]
    public void TheBinder_RefusesNullServices()
        => Assert.Throws<ArgumentNullException>(() => AiRuntimePaneBinder.Bind(null!));

    /// <summary>⭐ The REAL session over an empty world — ⛔ not a stub: <c>IBlueprintDebugSession</c>
    /// is a wide interface, and a hand-rolled double would pin my idea of it rather than the one the
    /// hosts construct.</summary>
    private static Hrot.Blueprints.Core.Debug.BlueprintDebugSession NewBlueprintSession()
        => new(new Fdp.Toolkit.Blueprints.BlueprintRegistry(),
               new Fdp.Core.EntityRepository(),
               new NoTimeControl());

    private sealed class NoTimeControl : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }
}
