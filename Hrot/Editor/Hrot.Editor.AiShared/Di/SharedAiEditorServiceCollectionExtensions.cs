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
using Fdp.Toolkit.Runner;
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
        services.AddSingleton<InspectorWindow>();
        services.AddSingleton<RuntimeInspectorWindow>();
        services.AddSingleton<TraceTimelineWindow>();
        services.AddSingleton<FindResultsWindow>();
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

        // Window registrar
        services.AddSingleton<IWindowRegistrar, SharedAiWindowRegistrar>();

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
