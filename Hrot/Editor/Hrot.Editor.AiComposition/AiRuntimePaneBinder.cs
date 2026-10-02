using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐ What registering the three runtime panes needs: a registrar and a session per kind, plus the
/// document manager the Blueprint pane reads its active asset from.
/// </summary>
public sealed record AiRuntimePaneServices
{
    public required PerspectiveWorkspaceRegistrar BTreeRegistrar     { get; init; }
    public required PerspectiveWorkspaceRegistrar HsmRegistrar       { get; init; }
    public required PerspectiveWorkspaceRegistrar BlueprintRegistrar { get; init; }

    /// <summary>⚠ Nullable per kind: a host without that session registers no pane for it, which is
    /// the editor's own guard and the only honest answer — a pane with no session draws nothing.</summary>
    public required Hrot.BTree.Editor.Debug.BTreeDebugSession?           BTreeDebugSession     { get; init; }
    public required Hrot.Hsm.Editor.Debug.HsmDebugSession?               HsmDebugSession       { get; init; }
    public required Hrot.Blueprints.Core.Debug.IBlueprintDebugSession?   BlueprintDebugSession { get; init; }

    /// <summary>
    /// ⚠ A PROVIDER, not a value — the `CE-343` lesson. Both hosts assign their document manager
    /// during initialisation, and the Blueprint pane's asset id must follow the ACTIVE DOCUMENT at
    /// draw time, not whatever was open when this was wired.
    /// </summary>
    public required Func<AiDocumentManager?> Documents { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-351</c> — THE RUNTIME PANES, REGISTERED THE SAME WAY ON BOTH HOSTS.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.25.
///
/// <para>📐 Measured <c>2026-09-26</c>: <c>RegisterRuntimePane</c> had <b>three</b> call sites, all in
/// <c>EditorSubsystem</c> (<c>:4112</c>, <c>:4118</c>, <c>:4135</c>), and <b>zero</b> on CGF — so CGF
/// offered <b>no</b> <c>details.runtime.&lt;kind&gt;</c> view at all. ⚠ The reason recorded in the
/// conformance baseline (<i>"requires an <c>IBlueprintDebugSession</c> and CGF constructs none"</i>)
/// stopped being true at <c>CE-344</c>, and <c>CE-345</c>/<c>CE-349</c> added the other two sessions.</para>
///
/// <para>⛔⛔ <b>What this does NOT do, stated because the task it came from claimed otherwise.</b>
/// It does <b>not</b> make either host publish a panel of kind <c>runtime-inspector</c>. 🔒
/// <c>CE-303</c> (<c>2026-09-21</c>) <b>dissolved</b> <c>RuntimeInspectorWindow</c> — <i>"a pane is now
/// reached through <c>details.runtime.&lt;kind&gt;</c> and nothing else"</i> — so that panel kind exists
/// on <b>neither</b> host. ⇒ the conformance rail still listing it is STALE, not a CGF gap, and no
/// amount of wiring here turns it green.</para>
/// </summary>
public static class AiRuntimePaneBinder
{
    /// <summary>
    /// ⭐ Registers one runtime pane per kind that has a session. Returns how many were registered,
    /// so a rail can assert the host wired what it had rather than trusting the call happened.
    /// </summary>
    public static int Bind(AiRuntimePaneServices services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        int registered = 0;

        if (services.BTreeDebugSession is not null)
        {
            var pane = new Hrot.BTree.Editor.Inspector.BTreeRuntimeInspectorPane();
            pane.SetSession(services.BTreeDebugSession);
            services.BTreeRegistrar.RegisterRuntimePane(pane);
            registered++;
        }

        if (services.HsmDebugSession is not null)
        {
            var pane = new Hrot.Hsm.Editor.Inspector.HsmRuntimeInspectorPane();
            pane.SetSession(services.HsmDebugSession);
            services.HsmRegistrar.RegisterRuntimePane(pane);
            registered++;
        }

        if (services.BlueprintDebugSession is not null)
        {
            var pane = new Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane();
            pane.SetSession(services.BlueprintDebugSession);
            // ⭐⭐ CE-303 — the ENTITY is not resolved here: it arrives with the DetailsContext, LIVE
            //    when docked and FROZEN when pinned. ⚠ Only the ASSET id stays a resolver, because it
            //    follows the active DOCUMENT rather than the selection.
            pane.SetResolvers(activeAssetIdResolver: () => ActiveBlueprintAssetId(services.Documents));
            services.BlueprintRegistrar.RegisterRuntimePane(pane);
            registered++;
        }

        return registered;
    }

    /// <summary>
    /// ⭐ The active document's Blueprint asset id, or null when the active document is not a
    /// Blueprint. ⚠ Was written out longhand in the editor; both hosts now read it from here.
    /// </summary>
    private static Guid? ActiveBlueprintAssetId(Func<AiDocumentManager?> documents)
    {
        var ctx = documents()?.Active?.ViewState as AiCanvasContext;
        return (ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset)?.AssetId;
    }
}
