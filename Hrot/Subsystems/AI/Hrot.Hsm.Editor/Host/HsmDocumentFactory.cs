using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;

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
    /// <param name="debugSession">Optional debug session (null while Phase 3 is not wired).</param>
    /// <param name="extraRenderers">
    ///   Optional additional custom canvas renderers to append after the built-in HSM set.
    /// </param>
    /// <returns>A populated <see cref="AiCanvasContext"/> whose <see cref="AiCanvasContext.View"/>
    ///   is ready to render on the HSM canvas.</returns>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="asset"/> is not an <see cref="HsmAsset"/>.
    /// </exception>
    public static AiCanvasContext Build(
        IEditableAsset        asset,
        AiEditorAdapterBundle bundle,
        IDebugSession?        debugSession   = null,
        IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null)
    {
        if (asset  is null) throw new ArgumentNullException(nameof(asset));
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        if (asset is not HsmAsset hsmAsset)
            throw new ArgumentException(
                $"Expected {nameof(HsmAsset)} but got {asset.GetType().Name}.",
                nameof(asset));

        // ── 1. Graph model ────────────────────────────────────────────────────
        var graphModel = new HsmGraphModel(hsmAsset);

        // ── 2. Kind-specific host components ─────────────────────────────────
        var nodeCatalog  = new HsmNodeCatalog();
        var typeSystem   = new HsmTypeSystem();
        var validator    = new HsmLinkValidator(hsmAsset);
        var commandSink  = new HsmCommandSink(hsmAsset);

        // ── 3. Custom renderers (built-in HSM set + caller extras) ────────────
        var renderers = BuildRenderers(hsmAsset, extraRenderers);

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

        // ── 5. GraphView ──────────────────────────────────────────────────────
        var view = new GraphView(
            graphModel,
            hostServices.CommandSink,
            hostServices.LinkValidator,
            hostServices.TypeSystem,
            hostServices.NodeCatalog,
            hostServices);

        return new AiCanvasContext(view, "HSM");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
        HsmAsset asset,
        IReadOnlyList<ICustomCanvasRenderer>? extra)
    {
        // Include the standard HSM custom renderers.
        var list = new List<ICustomCanvasRenderer>
        {
            new HsmTransitionLabelRenderer(asset),
            new HsmInitialArrowRenderer(asset),
            new HsmHistoryGlyphsRenderer(asset),
            new HsmRegionConflictsRenderer(asset),
        };

        if (extra != null)
            list.AddRange(extra);

        return list;
    }
}
