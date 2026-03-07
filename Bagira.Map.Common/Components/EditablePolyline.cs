using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;

namespace Bagira.IG.Components;

/// <summary>
/// Managed ECS component storing the current vertex list of a user-editable
/// polyline overlay (route, area boundary, measurement track, etc.).
///
/// Written at entity spawn time; mutated by the IG edit tool when the operator
/// commits a vertex-drag session.
///
/// Defined in <c>Bagira.Map.Common</c> so that both the IG and SimHost/Runner
/// projects can reference it without introducing circular project dependencies.
/// Registered via <c>repo.RegisterManagedComponent&lt;EditablePolyline&gt;()</c>.
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
    /// Monotonically increasing version counter.  Incremented by the IG edit
    /// tool each time a committed edit is applied, so subscribers can detect
    /// stale cached copies.
    /// </summary>
    public int Version { get; set; }
}
