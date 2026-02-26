using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;

namespace Bagira.IG.Components;

/// <summary>
/// Managed ECS component storing the current vertex list of a user-editable
/// polyline overlay (route, area boundary, measurement track, etc.).
///
/// Written at entity spawn time; mutated by <see cref="Bagira.IG.Tools.EditTool"/>
/// when the operator commits a vertex-drag session.
///
/// This component is a <c>class</c> because it holds a <see cref="List{T}"/>
/// reference — registered via
/// <c>repo.RegisterManagedComponent&lt;EditablePolyline&gt;()</c>.
/// </summary>
[ComponentId(GlobalComponentIds.EditablePolyline)]
public sealed class EditablePolyline
{
    /// <summary>
    /// Ordered list of world-space XY vertices.  Index 0 is the first vertex;
    /// the last index is the terminal vertex.
    /// </summary>
    public List<Vector2> Points { get; set; } = new();

    /// <summary>
    /// Monotonically increasing version counter.  Incremented by
    /// <see cref="Bagira.IG.Tools.EditTool"/> each time a committed edit is
    /// applied, so subscribers can detect stale cached copies.
    /// </summary>
    public int Version { get; set; }
}
