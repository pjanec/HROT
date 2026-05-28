using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Hsm.Editor.Comparison;

/// <summary>
/// DI extension methods for the HSM comparison sanitizer.
/// </summary>
public static class HsmEditorComparisonServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HsmComparisonSanitizer"/> as a singleton and wires it into
    /// the shared <see cref="SanitizerRegistry"/> upon first resolution.
    /// Call this from the application's composition root after
    /// <c>AddSharedAiEditor()</c> has been called.
    /// </summary>
    public static IServiceCollection AddHsmEditorComparison(this IServiceCollection services)
    {
        services.AddSingleton<HsmComparisonSanitizer>(sp =>
        {
            var catalog   = sp.GetRequiredService<IAssetCatalog>();
            var sanitizer = new HsmComparisonSanitizer(catalog);
            sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
            return sanitizer;
        });

        return services;
    }
}
