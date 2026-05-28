using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.BTree.Editor.Comparison;

/// <summary>
/// DI extension methods for the BTree comparison sanitizer.
/// </summary>
public static class BTreeEditorComparisonServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BTreeComparisonSanitizer"/> as a singleton and wires it into
    /// the shared <see cref="SanitizerRegistry"/> upon first resolution.
    /// Call this from the application's composition root after
    /// <c>AddSharedAiEditor()</c> has been called.
    /// </summary>
    public static IServiceCollection AddBTreeEditorComparison(this IServiceCollection services)
    {
        // Register via factory so that the sanitizer is wired into SanitizerRegistry
        // the first time it is resolved from DI.
        services.AddSingleton<BTreeComparisonSanitizer>(sp =>
        {
            var catalog   = sp.GetRequiredService<IAssetCatalog>();
            var sanitizer = new BTreeComparisonSanitizer(catalog);
            sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
            return sanitizer;
        });

        return services;
    }
}
