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
        services.AddSingleton<BlueprintEditorModule>();
        return services;
    }
}
