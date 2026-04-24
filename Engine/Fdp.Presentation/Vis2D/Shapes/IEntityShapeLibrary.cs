using Fdp.Core;

namespace Fdp.Toolkit.Vis2D.Shapes;

/// <summary>
/// Provides <see cref="EntityShapeProfile"/> instances by name or by DIS Entity
/// Type fallback.
///
/// <para>
/// The lookup order is:
/// <list type="number">
///   <item>Explicit shape name stored in the entity's <c>VisualData.MapShapeName</c>.</item>
///   <item>DIS Entity Type classification (Kind, Domain, Category).</item>
///   <item>An unconditional default shape.</item>
/// </list>
/// </para>
/// </summary>
public interface IEntityShapeLibrary
{
    /// <summary>
    /// Returns the best matching <see cref="EntityShapeProfile"/> for the given
    /// entity.  Never returns <c>null</c>; falls back to a default profile if
    /// neither <paramref name="shapeName"/> nor <paramref name="fallbackDisType"/>
    /// produce a match.
    /// </summary>
    /// <param name="shapeName">
    /// Optional explicit name from the entity's <c>VisualData.MapShapeName</c>.
    /// Pass <c>null</c> or an empty string to skip the named lookup.
    /// </param>
    /// <param name="fallbackDisType">
    /// DIS Entity Type used when <paramref name="shapeName"/> is absent or unknown.
    /// </param>
    EntityShapeProfile GetShape(string? shapeName, DISEntityType fallbackDisType);
}
