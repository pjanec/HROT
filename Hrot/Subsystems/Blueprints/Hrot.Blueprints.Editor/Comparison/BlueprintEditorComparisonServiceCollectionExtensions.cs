using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Comparison;
using AiCatalog = Hrot.Editor.AiShared.Catalog.IAssetCatalog;

namespace Hrot.Blueprints.Editor.Comparison;

/// <summary>
/// DI extension methods for the Blueprint comparison sanitizer.
/// </summary>
public static class BlueprintEditorComparisonServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BlueprintComparisonSanitizer"/> as a singleton and wires it into
    /// the shared <see cref="SanitizerRegistry"/> upon first resolution.
    /// Call this from the application's composition root after
    /// <c>AddSharedAiEditor()</c> has been called.
    /// </summary>
    public static IServiceCollection AddBlueprintEditorComparison(this IServiceCollection services)
    {
        services.AddSingleton<BlueprintComparisonSanitizer>(sp =>
        {
            var migrationAdapter = sp.GetRequiredService<IComparisonMigrationAdapter>();
            var metaSanitizer    = sp.GetRequiredService<IMetaEnvelopeSanitizer>();
            var catalog          = sp.GetRequiredService<AiCatalog>();
            var sanitizer        = new BlueprintComparisonSanitizer(migrationAdapter, metaSanitizer, catalog);
            sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
            return sanitizer;
        });

        return services;
    }
}
