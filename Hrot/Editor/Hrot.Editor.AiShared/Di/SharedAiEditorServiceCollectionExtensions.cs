using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Di;

/// <summary>
/// DI extension method for registering all shared AI editor services and windows.
/// Call from the subsystem's composition root (or from tests).
/// </summary>
public static class SharedAiEditorServiceCollectionExtensions
{
    public static IServiceCollection AddSharedAiEditor(
        this IServiceCollection services,
        Action<IEditableAsset>? onAssetActivated = null)
    {
        // Core services
        services.AddSingleton<EditorSelectionStore>();
        services.AddSingleton<IAssetCatalog, AssetCatalog>();
        services.AddSingleton<IDebugSessionRegistry, DebugSessionRegistry>();
        services.AddSingleton<LiveSessionRegistry>();
        services.AddSingleton<ILiveSessionProvider>(sp => sp.GetRequiredService<LiveSessionRegistry>());
        services.AddSingleton<AiTracerCoordinator>();

        // Comparison sanitization registry (populated at startup by each subsystem host)
        services.AddSingleton<SanitizerRegistry>();
        services.AddSingleton<ComparisonExportBuilder>();
        services.AddSingleton<BlackboardComparisonSanitizer>(sp =>
        {
            var sanitizer = new BlackboardComparisonSanitizer();
            sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
            return sanitizer;
        });
        // Default no-op adapters — overridable by registering production implementations before AddSharedAiEditor.
        services.TryAddSingleton<IComparisonMigrationAdapter, NoOpComparisonMigrationAdapter>();
        services.TryAddSingleton<IMetaEnvelopeSanitizer, NoOpMetaEnvelopeSanitizer>();

        // Comparison session registry (singleton, holds active comparison state per asset).
        services.AddSingleton<ComparisonSessionRegistry>();

        // Stale badge watcher (marks comparison sessions stale when the asset is saved).
        services.AddSingleton<StaleBadgeWatcher>();

        // Refactor services
        services.AddSingleton<IReferenceCatalog, ReferenceCatalog>();
        services.AddSingleton<AtomicMultiFileWriter>();
        services.AddSingleton<IRefactorService, RefactorService>();

        // Default no-op IIconProvider — hosts override with TryAddSingleton before calling this.
        services.TryAddSingleton<IIconProvider, NoOpIconProvider>();

        // Windows
        services.AddSingleton<AssetBrowserDockedWindow>(sp =>
        {
            return new AssetBrowserDockedWindow(
                sp.GetRequiredService<IAssetCatalog>(),
                sp.GetRequiredService<IIconProvider>(),
                new AssetBrowserPanelOptions { Kinds = AssetKindFilter.All, ShowAllTab = false },
                onAssetActivated ?? (_ => { }));
        });
        // ⛔ S5 (2026-08-22): InspectorWindow is RETIRED — all six of its arms became Details views
        //    or asset-row menu items (BP-399 §7.6 ⑤). Nothing to register.
        services.AddSingleton<RuntimeInspectorWindow>();
        services.AddSingleton<TraceTimelineWindow>();
        // ⭐⭐⭐ A6 (2026-08-23) — THE PERSPECTIVE IS PASSED, and this registration is WHY the rule was
        //    worth having. 📄 DESIGN_Perspective_Unification.md §1c "the LATENT generator".
        // 🔴 `AddSingleton<FindResultsWindow>()` resolved the ctor with every argument defaulted, so it
        //    was a SECOND site that silently invented a perspective — §1c said no production caller
        //    omitted it, and this container is the one that did. ⚠ Only harmless because
        //    AddSharedAiEditor has no production caller today (measured: tests only) — ⛔ i.e. luck.
        // ⭐ "Authoring" is passed EXPLICITLY to preserve the exact prior behaviour and to match its
        //    siblings above (RuntimeInspectorWindow/TraceTimelineWindow default to the same name).
        //    ⚠ Note "Authoring" is NOT a live perspective (§1): no production registration claims it.
        //    ⇒ a host adopting this container must pass its own perspective, and now it CANNOT forget.
        services.AddSingleton<FindResultsWindow>(_ => new FindResultsWindow("Authoring"));
        services.AddSingleton<ComparisonSummaryPanel>();
        services.AddSingleton<ComparisonSidebar>();
        services.AddSingleton<BlackboardAuthoringWindow>(sp =>
            new BlackboardAuthoringWindow(
                sp.GetRequiredService<EditorSelectionStore>(),
                sp.GetRequiredService<IRefactorService>(),
                sp.GetRequiredService<SanitizerRegistry>(),
                sp.GetRequiredService<ComparisonExportBuilder>(),
                sp.GetRequiredService<ComparisonSessionRegistry>(),
                // ⭐⭐ DEBT-AIB-009 (Batch 69) — the second production constructor, and it was missing
                //    the exporter too. ⚠ GetService, not GetRequiredService: a host that registers no
                //    exporter is legitimate (the parameter is optional by design), and demanding one
                //    would turn a missing OPTIONAL dependency into a startup crash.
                actionSchemaExporter: sp.GetService<IActionSchemaExporter>()));
        services.AddSingleton<DiagnosticsWindow>(sp =>
            new DiagnosticsWindow(
                sp.GetRequiredService<IAssetCatalog>(),
                sp.GetServices<IAssetValidator>().ToList()));

        // ⛔⛔⛔ NO WINDOW REGISTRAR HERE — `CE-070` DELETED `SharedAiWindowRegistrar`.
        // 📄 `docs/DESIGN_Subsystem_Composition_Unification.md` §5b.5.
        // ⭐⭐ The live registration path is `PerspectiveWorkspaceRegistrar`, which BOTH hosts construct
        //    three times each (per perspective) — `CgfSubsystem:298-300`/`:1550`, `EditorSubsystem:366-367`.
        // 📐 The deleted class was a FLAT, host-level `IWindowRegistrar` over 7 window INSTANCES, and it
        //    had zero constructions in the repository: its entire in-degree was the DI rail that asserted
        //    it resolved. ⚠⚠ That rail is what made it look adopted for months.
        // ⛔ And it could never have worked as written: the windows it registered declare
        //    `WindowScope.PerspectiveBound`, so a flat host-level registrar is the WRONG SHAPE for them —
        //    `AI_Editor_Shared_Infrastructure.md:1865` designed a descriptor-based, perspective-aware
        //    registrar, and the built class was a flat partial of it.
        // ⭐ Absent and explained beats present and broken (ruling 49).

        return services;
    }

    /// <summary>
    /// No-op <see cref="IIconProvider"/> used as the DI default.
    /// Hosts override this by registering a real implementation via
    /// <c>TryAddSingleton&lt;IIconProvider&gt;</c> before calling
    /// <see cref="AddSharedAiEditor"/>.
    /// </summary>
    private sealed class NoOpIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = default;
            return false;
        }
    }
}
