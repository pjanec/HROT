using Hrot.Blueprints.Core.Debug;
using Microsoft.Extensions.DependencyInjection;

namespace Hrot.Blueprints.Editor;

public static class BlueprintEditorServiceCollectionExtensions
{
    public static IServiceCollection AddBlueprintEditor(
        this IServiceCollection services,
        string assetRootDirectory)
    {
        services.AddSingleton<DirtyTracker>();
        services.AddSingleton<EditorSelectionStore>();
        services.AddSingleton<EditorState>();
        services.AddSingleton<IAssetCatalog>(_ => new FileSystemAssetCatalog(assetRootDirectory));
        services.AddSingleton<BlueprintEditorModule>(sp =>
            new BlueprintEditorModule(
                sp.GetRequiredService<IWindowRegistrar>(),
                sp.GetRequiredService<DirtyTracker>(),
                sp.GetRequiredService<EditorSelectionStore>(),
                sp.GetRequiredService<EditorState>(),
                sp.GetRequiredService<IAssetCatalog>(),
                sp.GetRequiredService<IOutputConsole>(),
                sp.GetService<IBlueprintDebugSession>()));
        return services;
    }
}
