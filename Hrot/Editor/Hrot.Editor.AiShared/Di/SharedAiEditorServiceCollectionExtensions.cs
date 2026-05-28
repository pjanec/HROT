using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

namespace Hrot.Editor.AiShared.Di;

/// <summary>
/// DI extension method for registering all shared AI editor services and windows.
/// Call from the subsystem's composition root (or from tests).
/// </summary>
public static class SharedAiEditorServiceCollectionExtensions
{
    public static IServiceCollection AddSharedAiEditor(this IServiceCollection services)
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

        // Windows
        services.AddSingleton<AssetBrowserWindow>();
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
                sp.GetRequiredService<ComparisonSessionRegistry>()));
        services.AddSingleton<DiagnosticsWindow>(sp =>
            new DiagnosticsWindow(
                sp.GetRequiredService<IAssetCatalog>(),
                sp.GetServices<IAssetValidator>().ToList()));

        // Window registrar
        services.AddSingleton<IWindowRegistrar, SharedAiWindowRegistrar>();

        return services;
    }
}
