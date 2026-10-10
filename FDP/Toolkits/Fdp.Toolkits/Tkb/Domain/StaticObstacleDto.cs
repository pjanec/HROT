using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a (D11, R-242/R-243) — a STATIC OBSTACLE type: a parked car, a sandbag wall, a concrete block, a crate.
    /// An entity of a type carrying this descriptor becomes TERRAIN on every node — a box of <see cref="Material"/> that stops
    /// rounds and sight by the wall rules, gives cover and is cut out of the navmesh (📄 docs/DESIGN_Peek_And_Fire.md §9).
    /// Projected by <c>ObstacleTkbTranslator</c>.
    /// </summary>
    [TkbDescriptor("Terrain.StaticObstacle")]
    public record StaticObstacleDto
    {
        /// <summary>Box length along the entity's heading (m).</summary>
        [EditUnit("m")]
        public float Length { get; init; }

        /// <summary>Box width across the heading (m).</summary>
        [EditUnit("m")]
        public float Width { get; init; }

        /// <summary>Box height above its base (m).</summary>
        [EditUnit("m")]
        public float Height { get; init; }

        /// <summary>A wall-library material name (<c>Terrain/Data/materials.json</c>): <c>car-body</c>, <c>sandbags</c>, <c>concrete</c> …</summary>
        public string Material { get; init; } = "concrete";
    }
}
