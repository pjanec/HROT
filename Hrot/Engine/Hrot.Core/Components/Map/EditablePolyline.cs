using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// Managed ECS component storing the current vertex list of a user-editable
/// polyline overlay (route, area boundary, measurement track, etc.).
///
/// Written at entity spawn time; mutated by the IG edit tool when the operator
/// commits a vertex-drag session.
///
/// Defined in <c>Hrot.Map.Common</c> so that both the IG and SimHost/Runner
/// projects can reference it without introducing circular project dependencies.
/// Registered via <c>repo.RegisterManagedComponent&lt;EditablePolyline&gt;()</c>.
/// </summary>
[DataPolicy(DataPolicy.SnapshotViaClone)]
[ComponentId(GlobalComponentIds.EditablePolyline)]
public sealed class EditablePolyline
{
    /// <summary>
    /// Ordered list of XY vertices <b>RELATIVE to the entity's <c>SimTransform.Position</c></b>
    /// (X=East, Y=North).  Index 0 is the first vertex; the last index is the terminal vertex.
    /// <para>
    /// ⚠ These are OFFSETS, not world coordinates — a consumer must add the origin
    /// (<c>origin + Points[i]</c>) to get world space, as <c>MapOverlayGizmo</c> and
    /// <c>MapVisualOverlayEgressTranslator</c> do. A previous version of this comment said
    /// "world-space XY vertices", which was false for shipped data and produced a gizmo that
    /// drew the same area twice, ~820 m apart.
    /// </para>
    /// <para>
    /// ⭐ Relative is the one convention, with no exceptions: moving the entity is a single
    /// <c>SimTransform</c> write and leaves these bytes untouched. Consequently any staleness
    /// key over this shape must hash the TRANSFORM as well as the points.
    /// 📄 <c>docs/DESIGN_Terrain_Zones_And_Assets.md</c> §2.2.
    /// </para>
    /// </summary>
    public List<Vector2> Points { get; set; } = new();

    /// <summary>
    /// Version counter intended to be incremented on each committed edit so subscribers could
    /// detect stale cached copies.
    /// <para>
    /// 🔴 <b>DO NOT RELY ON THIS — it does not work today.</b> Measured: nothing anywhere
    /// increments it, and the vertex edit tool commits a drag by constructing a fresh
    /// <c>EditablePolyline</c>, so a committed edit RESETS it to default instead of advancing it.
    /// It also could not detect a MOVE in any case, because <see cref="Points"/> are relative and
    /// a move leaves them byte-identical.
    /// </para>
    /// <para>
    /// ⭐ A staleness key must therefore hash the resolved footprint —
    /// <c>hash(SimTransform ⊕ Points)</c> — which no writer has to cooperate with.
    /// 📄 <c>docs/DESIGN_Terrain_Zones_And_Assets.md</c> §9.7 ③c.
    /// </para>
    /// </summary>
    public int Version { get; set; }
}
