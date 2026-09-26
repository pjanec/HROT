using System;
using System.Collections.Generic;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.UI.Action;
using NodeEditor.UI.Find;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Factory that builds all NodeEdit host objects for an HSM document and returns an
/// <see cref="AiCanvasContext"/> ready to be stored in <see cref="AiDocument.ViewState"/>.
///
/// <para>
/// Construction order per opened asset:
/// <list type="number">
///   <item>Cast <see cref="IEditableAsset"/> to <see cref="HsmAsset"/>.</item>
///   <item>Build <see cref="HsmGraphModel"/> (wraps the asset's states/transitions).</item>
///   <item>Build <see cref="HsmCommandSink"/> (asset reference).</item>
///   <item>Build <see cref="HsmEditorHostServices"/> with adapters from
///         <see cref="AiEditorAdapterBundle"/> + kind-specific catalog, type-system,
///         validator, and existing custom renderers.</item>
///   <item>Build <see cref="GraphView"/> (model + host services).</item>
///   <item>Return an <see cref="AiCanvasContext"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Container-node projection:</b> Composite and parallel <see cref="HsmAsset"/> states
/// project as <c>StateNode.IsContainer == true</c> (from the pre-existing
/// <see cref="HsmGraphModel"/> implementation), which NodeEdit treats as
/// <c>IContainerNodeModel</c> elements with children/regions automatically.
/// </para>
/// </summary>
public static class HsmDocumentFactory
{
    /// <summary>
    /// Builds the full host-service stack for the given HSM asset and returns a
    /// canvas context ready to be stored in <see cref="AiDocument.ViewState"/>.
    /// </summary>
    /// <param name="asset">The editable HSM asset (must be an <see cref="HsmAsset"/>).</param>
    /// <param name="bundle">Engine adapter bundle (pickers, icons, theme, input, clipboard, diagnostics).</param>
    /// <param name="debugSession">
    ///   Optional HSM debug session.  When non-null, the runtime-overlay and
    ///   breakpoint-gutter renderers bind to it so live execution state is shown.
    ///   When null (authoring mode), both renderers report <c>IsActive==false</c>
    ///   so there is no per-frame cost.
    /// </param>
    /// <param name="breakpointManager">
    ///   Optional shared <see cref="IDataBreakpointManager"/>. When non-null, the
    ///   breakpoint-gutter renderer also draws dots for breakpoints registered in the
    ///   universal-breakpoint stack.
    /// </param>
    /// <param name="extraRenderers">
    ///   Optional additional custom canvas renderers to append after the built-in HSM set.
    /// </param>
    /// <returns>A populated <see cref="AiCanvasContext"/> whose <see cref="AiCanvasContext.View"/>
    ///   is ready to render on the HSM canvas.</returns>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="asset"/> is not an <see cref="HsmAsset"/>.
    /// </exception>
    public static AiCanvasContext Build(
        IEditableAsset          asset,
        AiEditorAdapterBundle   bundle,
        IDebugSession?          debugSession      = null,
        IHsmDebugSession?       hsmDebugSession   = null,
        IDataBreakpointManager? breakpointManager = null,
        IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null,
        Func<Guid, bool>? isStatefulSubtree = null,
        Func<Guid, IReadOnlyCollection<int>>? sharedScopeKeys = null,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null)
    {
        if (asset  is null) throw new ArgumentNullException(nameof(asset));
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        if (asset is not HsmAsset hsmAsset)
            throw new ArgumentException(
                $"Expected {nameof(HsmAsset)} but got {asset.GetType().Name}.",
                nameof(asset));

        // ── 1. Graph model ────────────────────────────────────────────────────
        // ⭐⭐⭐ E5 / items 6-7 (2026-09-26) — THE CANVAS NOW ASKS THE SAME QUESTIONS THE DIAGNOSTICS
        //    WINDOW DOES. 🔴 This used to be `new HsmGraphModel(hsmAsset)` with NO resolvers, while
        //    the composition root handed both to HsmAssetValidator (EditorSubsystem:3383) — so
        //    rules 8 and 8b fired in the Diagnostics window and were INERT on the node badges.
        //    ⛔ That is exactly the split HsmGraphModel's own remarks warn about: "a resolver on
        //    only one of them would make a state light up in one surface and not the other."
        //
        // ⛔⛔ WHY THE TWO RESOLVERS ARE DELEGATES AND ITEM 7'S DEPENDENCY IS A CATALOGUE — the
        //    difference is real, not an inconsistency:
        //    ① rules 8/8b (§32.15): the predicate has to switch on BOTH BehaviorTreeAsset and
        //      HsmAsset, and this assembly (Hrot.Hsm.Editor) cannot see the former — so it COULD
        //      NOT compute it even if handed the catalogue. 🔒 The resolvers' own remarks say they
        //      live "at the only layer that sees both asset types", and that a second copy would
        //      let the two surfaces disagree. ⇒ pass the ANSWER, not the source.
        //    ② rule 10 / item 7 (§32.16): the hosting edge is read through ISubtreeHostingAsset,
        //      so the walk needs NO knowledge of either concrete type ⇒ the catalogue is passable
        //      and the adapter lives ONCE, inside SubtreeCycleDetector, instead of at every
        //      composition root. ⭐ The interface removed the constraint rather than working
        //      around it.
        // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.15 and §32.16.
        var graphModel = new HsmGraphModel(hsmAsset, isStatefulSubtree, sharedScopeKeys, catalog);

        // ── 2. Kind-specific host components ─────────────────────────────────
        var nodeCatalog  = new HsmNodeCatalog();
        var typeSystem   = new HsmTypeSystem();
        var validator    = new HsmLinkValidator(hsmAsset);
        var commandSink  = new HsmCommandSink(hsmAsset);

        // ── 3. Custom renderers (built-in HSM set + caller extras) ────────────
        var renderers = BuildRenderers(hsmAsset, hsmDebugSession, breakpointManager, extraRenderers, out var regionConflicts);

        // Feed region-conflict diagnostics to the renderer on every validation rebuild.
        graphModel.DiagnosticsRecomputed += regionConflicts.SetDiagnostics;
        regionConflicts.SetDiagnostics(graphModel.LastDiagnostics); // initial push

        // ── 4. Host services ──────────────────────────────────────────────────
        var hostServices = new HsmEditorHostServices(
            nodeCatalog:     nodeCatalog,
            typeSystem:      typeSystem,
            linkValidator:   validator,
            commandSink:     commandSink,
            pickers:         bundle.PickerRegistry,
            clipboard:       bundle.ClipboardInterface,
            icons:           bundle.IconProvider,
            diagnostics:     bundle.DiagnosticsSink,
            input:           bundle.InputSource,
            theme:           bundle.EditorTheme,
            debug:           debugSession,
            customRenderers: renderers);

        // Wire breakpoint manager into the host services (command-sink path via
        // HsmBreakpointContextMenuProvider; also makes BpGutterRenderer accessible).
        if (breakpointManager != null)
            hostServices.SetBreakpointManager(breakpointManager);

        // ── 5. GraphView ──────────────────────────────────────────────────────
        var view = new GraphView(
            graphModel,
            hostServices.CommandSink,
            hostServices.LinkValidator,
            hostServices.TypeSystem,
            hostServices.NodeCatalog,
            hostServices);

        // ── BCP-F: FindBar + IEditorCommands ─────────────────────────────────
        var commands = new EditorCommandsImpl();
        var findBar  = new FindBar(view, new FindEngine(graphModel, null));
        BuiltinCommandHandlers.RegisterAll(commands, view, findBar);

        // Store the HsmAsset in AssetRef so the composition root can wire
        // the selection→Inspector bridge (HsmSelectionBridgeHelper.BuildAfterDrawAction)
        // without a kind-specific dependency in AiShared.
        return new AiCanvasContext(view, "HSM")
        {
            AssetRef = hsmAsset,
            FindBar  = findBar,
            Commands = commands,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
        HsmAsset                hsmAsset,
        IHsmDebugSession?       hsmDebugSession,
        IDataBreakpointManager? breakpointManager,
        IReadOnlyList<ICustomCanvasRenderer>? extra,
        out HsmRegionConflictsRenderer regionConflictsRenderer)
    {
        // ── Registration order (per design-talk §9) ──────────────────────────
        // AfterWires pass:
        //   1. HsmTransitionLabelRenderer — event/guard/action text on transition links
        // AfterNodes pass (strictly ordered for correct z-index):
        //   2. HsmInitialArrowRenderer    — initial-child arrow markers
        //   3. HsmRegionConflictsRenderer — yellow warning lines for output-lane collisions
        //   4. HsmHistoryGlyphsRenderer   — circled H / H* / ⊙ final glyphs
        //   5. HsmBreakpointGutterRenderer— red dot for armed breakpoints
        //   6. HsmRuntimeOverlayRenderer  — teal glow on active-config (last: most ephemeral)

        var runtimeOverlay = new HsmRuntimeOverlayRenderer(hsmAsset);
        var gutterRenderer = new HsmBreakpointGutterRenderer(hsmAsset);
        regionConflictsRenderer = new HsmRegionConflictsRenderer(hsmAsset);

        // Wire HSM-specific debug session into overlay and gutter.
        if (hsmDebugSession != null)
        {
            runtimeOverlay.SetSession(hsmDebugSession);
            gutterRenderer.SetSession(hsmDebugSession);
        }
        if (breakpointManager != null)
            gutterRenderer.SetManager(breakpointManager);

        var list = new List<ICustomCanvasRenderer>
        {
            new HsmTransitionLabelRenderer(hsmAsset),  // AfterWires
            new HsmInitialArrowRenderer(hsmAsset),     // AfterNodes
            regionConflictsRenderer,                   // AfterNodes (above initial arrows)
            new HsmHistoryGlyphsRenderer(hsmAsset),    // AfterNodes (above conflicts)
            gutterRenderer,                            // AfterNodes (above glyphs)
            runtimeOverlay,                            // AfterNodes (last — most ephemeral)
        };

        if (extra != null)
            list.AddRange(extra);

        return list;
    }
}
