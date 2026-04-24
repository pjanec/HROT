using System.Collections.Generic;

namespace Fdp.Toolkit.Vis2D.Shapes;

/// <summary>
/// A named collection of <see cref="PolylineDefinition"/> elements that together
/// describe the 2-D map silhouette of an entity class.
///
/// <para>
/// Profiles are typically pre-built at application startup by
/// <see cref="DefaultEntityShapeLibrary"/> (or a custom <see cref="IEntityShapeLibrary"/>
/// implementation) and looked up cheaply at render time.
/// </para>
/// </summary>
public sealed class EntityShapeProfile
{
    /// <summary>Logical name used as the dictionary key inside the library.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Ordered list of polyline elements that form the shape.</summary>
    public IReadOnlyList<PolylineDefinition> Elements { get; init; }
        = System.Array.Empty<PolylineDefinition>();
}
