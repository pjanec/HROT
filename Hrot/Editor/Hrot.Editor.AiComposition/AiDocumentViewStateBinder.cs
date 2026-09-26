using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Selection;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐⭐ <b>Everything the three per-kind document factories need, gathered ONCE.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.18 (<c>CE-340</c>).
///
/// <para>⭐⭐ <b>The properties a host genuinely differs on are the OPTIONAL ones</b> — the three debug
/// sessions *(the editor has them; CGF constructs none)* and <see cref="OnDocumentOpened"/>. ⛔ Every
/// other member is the same policy pointed at the host's own instance.</para>
///
/// <para>🔒 <b>This record is the control.</b> A new factory argument is added HERE, and both hosts get
/// it — which is precisely what did not happen when <c>CE-338</c> wired rules 8/8b into one host and
/// left the other silent.</para>
/// </summary>
public sealed record AiDocumentHostServices
{
    /// <summary>The atlas-backed adapter bundle; each host builds its own.</summary>
    public required AiEditorAdapterBundle Adapters { get; init; }

    /// <summary>The document manager — also how the BTree arm's "Open Blueprint" navigates.</summary>
    public required AiDocumentManager DocumentManager { get; init; }

    public EditorSelectionStore?    BTreeSelectionStore { get; init; }
    public IAssetCatalog?           Catalog             { get; init; }
    public IActionSchemaExporter?   ActionSchema        { get; init; }
    public IDataBreakpointManager?  BreakpointManager   { get; init; }

    /// <summary>⭐ Drives the comparison annotation renderer for EVERY kind (`CE-071`).</summary>
    public ComparisonSessionRegistry? ComparisonSessions { get; init; }

    // ── Blueprint-arm services ──────────────────────────────────────────────
    public Hrot.Blueprints.Editor.NodeDrawers.EditService?        BlueprintEditService { get; init; }
    public Hrot.Blueprints.Editor.NodeDrawers.NodeKindRegistry?   BlueprintPalette     { get; init; }
    public Hrot.Blueprints.Editor.BlueprintPeerSource?            BlueprintPeerCatalog { get; init; }
    public Hrot.Blueprints.Editor.ActionCatalog.IBehaviorActionCatalog? BehaviorActions { get; init; }
    public Hrot.Blueprints.Core.Compiler.Catalogs.IChannelCommandCatalog? ChannelCommands { get; init; }

    // ── The per-host DIFFERENCES, and they are few ──────────────────────────

    /// <summary>⚠ <c>null</c> on a host that constructs no BTree debug session — CGF says so explicitly
    /// rather than defaulting silently.</summary>
    public Hrot.BTree.Editor.Debug.BTreeDebugSession? BTreeDebugSession { get; init; }
    public Hrot.Hsm.Editor.Debug.HsmDebugSession?     HsmDebugSession   { get; init; }
    public Hrot.Blueprints.Core.Debug.BlueprintDebugSession? BlueprintDebugSession { get; init; }

    /// <summary>
    /// ⭐⭐ <b>The TAIL, and the one place the two hosts genuinely behaved differently.</b>
    /// The editor marks the document dirty <b>and</b> queues the asset into its regeneration
    /// scheduler; CGF marks dirty only *(it regenerates nothing — the reload pipeline recompiles from
    /// the in-memory asset)*. ⇒ ⛔ this is a real host difference, not an oversight, so it is a
    /// parameter rather than something the binder decides.
    /// ⚠ Called AFTER the view state is built, once per newly-opened document.
    /// </summary>
    public Action<AiDocument>? OnDocumentOpened { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-340</c> — ONE subscription that turns an opened AI document into its canvas.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.18.
///
/// <para>🔒 <b>Raised by the user, <c>2026-09-26</c>:</b> <i>"you mention editor path is wired and then
/// you go cgf, that seems like editor is running different code, not unified, which is
/// undesired."</i> 📐 Measured: <c>EditorSubsystem</c> and <c>CgfSubsystem</c> each carried a
/// structurally identical <c>DocumentOpened</c> handler — same guard, same three <c>AssetKind</c>
/// cases, same three factories, same <c>extraRenderers</c> expression — differing only in the VALUES
/// each host supplied.</para>
///
/// <para>⭐⭐ <b>Why the duplication existed, because it was NOT carelessness.</b>
/// <c>DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md</c> §11 ① names the deliverable as <i>"the same
/// three factories the editor wires, minus the debug sessions CGF has none of"</i>, and that document's
/// STATUS carries <c>known-conflict: CONSUMES Hrot.Editor.AiShared; must NOT modify it (freeze owner =
/// variable-model lane)</c>. ⇒ 🔒 <b>a FREEZE made the shared home off-limits, so copying was the only
/// LEGAL move at the time.</b> ⭐ That freeze was <b>LIFTED <c>2026-08-25</c></b> (<c>R-128</c>), and
/// nothing had revisited the copies since.</para>
///
/// <para>⛔⛔ <b>What the duplication COST, measured rather than asserted:</b> <c>CE-338</c> found CGF
/// silently missing HSM validation rules 8/8b, and <c>CE-341</c> found both hosts carrying
/// byte-identical copies of the resolvers that fixed it. ⇒ ⭐ <b>the failure mode is always the same —
/// a capability lands on one host and not the other, and nothing says so.</b> This type makes that
/// structurally impossible for document composition: there is one argument list.</para>
/// </summary>
public static class AiDocumentViewStateBinder
{
    /// <summary>
    /// ⭐ Subscribe <paramref name="services"/>'s document manager so that opening a BTree / HSM /
    /// Blueprint asset populates its <c>ViewState</c> through the matching factory.
    ///
    /// <para>⚠ <b>Kinds with no canvas are a deliberate no-op</b> — Scenario, Blackboard and Utility
    /// are not document-backed, and a host must not have to know that.</para>
    /// </summary>
    public static void Bind(AiDocumentHostServices services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var manager = services.DocumentManager;

        manager.DocumentOpened += doc =>
        {
            // ⭐ Re-opening an existing document must not rebuild its canvas.
            if (doc.ViewState != null) return;

            // ⭐ CE-071 — the comparison annotation renderer joins EVERY kind's built-in set, and
            //   it is per-document because it keys on the asset id.
            var extraRenderers = Hrot.Editor.AiShared.Comparison.Rendering.ComparisonCanvasRenderers
                .For(services.ComparisonSessions, doc.Asset.AssetId);

            switch (doc.Kind)
            {
                case AssetKind.BTree:
                    doc.ViewState = Hrot.BTree.Editor.Host.BTreeDocumentFactory.Build(
                        doc.Asset, services.Adapters, services.BTreeSelectionStore,
                        btreeDebugSession: services.BTreeDebugSession,
                        breakpointManager: services.BreakpointManager,
                        actionSchema:      services.ActionSchema,
                        assetCatalog:      services.Catalog,
                        // ⭐ "Open Blueprint" on a composed AiPrimitive node — through the same
                        //   manager, which also switches perspective.
                        openBlueprint:     a => manager.Open(a),
                        extraRenderers:    extraRenderers);
                    break;

                case AssetKind.Hsm:
                    doc.ViewState = Hrot.Hsm.Editor.Host.HsmDocumentFactory.Build(
                        doc.Asset, services.Adapters,
                        hsmDebugSession:   services.HsmDebugSession,
                        breakpointManager: services.BreakpointManager,
                        extraRenderers:    extraRenderers,
                        // ⭐⭐ §32.17 — ONE argument: the validator derives rules 8/8b AND the
                        //    item-7 hosting-cycle walk from this single catalogue.
                        catalog:           services.Catalog);
                    break;

                case AssetKind.Blueprint:
                    doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
                        doc.Asset, services.Adapters,
                        services.BlueprintEditService,
                        services.BlueprintPalette,
                        channelCommands:  services.ChannelCommands,
                        peerAssetCatalog: services.BlueprintPeerCatalog,
                        behaviorActions:  services.BehaviorActions,
                        debugSession:     services.BlueprintDebugSession,
                        extraRenderers:   extraRenderers);
                    break;

                default:
                    // ⛔ Scenario / Blackboard / Utility are not document-backed kinds.
                    break;
            }

            // ⚠ The host tail — see AiDocumentHostServices.OnDocumentOpened for why this is a
            //   parameter and not a decision made here.
            services.OnDocumentOpened?.Invoke(doc);
        };
    }
}
