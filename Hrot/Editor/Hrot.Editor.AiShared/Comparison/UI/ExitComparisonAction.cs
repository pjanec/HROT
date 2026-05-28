namespace Hrot.Editor.AiShared.Comparison.UI;

/// <summary>
/// Service that clears the active comparison session for an asset.
/// After calling <see cref="Exit"/>, the annotation renderer's
/// <see cref="Rendering.ComparisonAnnotationRenderer.IsActive"/> returns false
/// and all panels show their "no session" state.
/// See design section 6.8.
/// </summary>
public sealed class ExitComparisonAction
{
    private readonly ComparisonSessionRegistry _registry;

    public ExitComparisonAction(ComparisonSessionRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>
    /// Clears the comparison session for <paramref name="assetId"/>.
    /// No-op when no session is active for the given asset.
    /// </summary>
    public void Exit(Guid assetId) => _registry.ClearSession(assetId);
}
