using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
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
        services.AddSingleton<AiTracerCoordinator>();

        // Windows
        services.AddSingleton<AssetBrowserWindow>();
        services.AddSingleton<InspectorWindow>();
        services.AddSingleton<RuntimeInspectorWindow>();
        services.AddSingleton<TraceTimelineWindow>();

        // Window registrar
        services.AddSingleton<IWindowRegistrar, SharedAiWindowRegistrar>();

        return services;
    }
}
