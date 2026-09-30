using System;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using RuntimeEqsTemplates = Fdp.Toolkit.Spatial.Eqs.EqsTemplateRegistry;

namespace Hrot.Editor.AiComposition;

/// <summary>What building the Blueprint node drawers needs — the same on every host.</summary>
public sealed record AiBlueprintNodeAuthoringServices
{
    /// <summary>The Blueprint edit service the drawers mutate through.</summary>
    public IEditService? EditService { get; init; }

    /// <summary>
    /// ⚠ The SAME predicate compiler the host's data-breakpoint manager uses (it carries the
    /// behaviour and blueprint registries — without the latter, blueprint conditional predicates
    /// compile to constant-false, BP-29).
    /// </summary>
    public IPredicateCompiler? PredicateCompiler { get; init; }

    /// <summary>The CallPeerBlueprint picker's source.</summary>
    public IBlueprintPeerProvider? PeerProvider { get; init; }
}

/// <summary>The drawer registry plus the EQS template list it was built with.</summary>
public sealed record AiBlueprintNodeAuthoring(BlueprintNodeDrawerRegistry Drawers, EqsTemplateRegistry EqsTemplates);

/// <summary>
/// ⭐⭐⭐ <b>The Brain-side EQS authoring startup — and the Blueprint node drawers it lives in — written
/// ONCE, called by the editor AND CGF.</b> 📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.8.
///
/// <para>🔒 <b>User, <c>2026-09-30</c>:</b> <i>"the EQS brain part must be a shared code including the
/// startup code for editor and CGF alike."</i> 📐 Measured before this class existed: the editor filled
/// the <c>SpawnEqsSensor</c> template picker inline (<c>EditorSubsystem</c>), built the drawer registry
/// and installed the Details node view; CGF did <b>none</b> of the three — a blueprint opened on CGF had
/// no node Details at all, so no EQS template could be picked there. ⛔ No CGF slice design records that
/// as intended ⇒ a GAP (the <c>CE-347</c> reading), closed the <c>CE-340</c>/<c>CE-343</c> way: one
/// binder in this shared home, the editor's inline code deleted in the same change.</para>
///
/// <para>⭐ Two steps because the editor builds the drawers during AI-debug setup and installs Details
/// during window registration; both hosts call both.</para>
/// </summary>
public static class AiBlueprintNodeAuthoringBinder
{
    /// <summary>
    /// Every <c>[EqsTemplate]</c> the RUNTIME can answer, as the editor-side picker list — the same
    /// discovery <see cref="RuntimeEqsTemplates.InstallDefault"/> puts in the world.
    /// </summary>
    public static EqsTemplateRegistry CreateEqsTemplates()
    {
        var picker = new EqsTemplateRegistry();
        foreach (var t in RuntimeEqsTemplates.Discover(RuntimeEqsTemplates.CandidateAssemblies()).Entries)
            picker.Register(new EqsTemplateEntry
            {
                AssetId     = t.AssetId,
                DisplayName = t.Name.Substring(t.Name.LastIndexOf('.') + 1),
            });
        return picker;
    }

    /// <summary>Builds the Blueprint node drawers (including the EQS template picker).</summary>
    public static AiBlueprintNodeAuthoring CreateDrawers(AiBlueprintNodeAuthoringServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var edit      = services.EditService       ?? throw new ArgumentException("EditService is required.", nameof(services));
        var predicate = services.PredicateCompiler ?? throw new ArgumentException("PredicateCompiler is required.", nameof(services));

        var eqsTemplates = CreateEqsTemplates();
        var drawers = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            BuiltInChannelCommandCatalog.Instance, BuiltInEngineEventCatalog.Instance, edit, predicate, eqsTemplates,
            peerProvider: services.PeerProvider);
        return new AiBlueprintNodeAuthoring(drawers, eqsTemplates);
    }

    /// <summary>
    /// Installs the Blueprint Details node view (and Properties form) for the drawers, reading the
    /// active Blueprint from the document manager every frame (R-126's pull).
    /// </summary>
    public static void InstallDetails(
        PerspectiveWorkspaceRegistrar registrar,
        Fdp.Presentation.WindowManager.WindowManager windowManager,
        AiDocumentManager documentManager,
        AiBlueprintNodeAuthoring authoring,
        IRefactorService refactorService)
    {
        ArgumentNullException.ThrowIfNull(documentManager);
        ArgumentNullException.ThrowIfNull(authoring);
        BlueprintDetailsContribution.InstallInto(
            registrar:       registrar,
            windowManager:   windowManager,
            asset:           () => ActiveBlueprint(documentManager),
            drawerRegistry:  authoring.Drawers,
            refactorService: refactorService);
    }

    /// <summary>The Blueprint asset of the active document, or null when the active one is not a Blueprint.</summary>
    public static BlueprintAsset? ActiveBlueprint(AiDocumentManager documentManager)
    {
        var active = documentManager.Active;
        if (active?.Kind != AssetKind.Blueprint) return null;
        return (active.ViewState as AiCanvasContext)?.AssetRef as BlueprintAsset;
    }
}
