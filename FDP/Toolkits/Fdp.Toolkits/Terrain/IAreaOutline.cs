using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-3216 S1 (📄 docs/designs/weather/DESIGN_Weather_And_Time.md §6.2) — an area entity's OUTLINE as toolkit code can read it: XY
    /// vertices RELATIVE to the entity's <c>SimTransform.Position</c> (X east, Y north). Implemented by the one area shape every map tool
    /// authors and every node replicates (<c>Hrot.IG.Components.EditablePolyline</c>, registered under
    /// <c>GlobalComponentIds.EditablePolyline</c>), so a toolkit reader (the weather field) needs no reference to the Hrot assembly that
    /// defines it — the same direction <see cref="ZoneFootprint"/> takes plain points for.
    /// </summary>
    public interface IAreaOutline
    {
        /// <summary>The vertices, relative to the entity's position, in order; a closed area needs at least three.</summary>
        IReadOnlyList<Vector2> Points { get; }
    }
}
