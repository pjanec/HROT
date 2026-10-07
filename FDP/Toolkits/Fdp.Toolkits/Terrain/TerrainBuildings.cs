using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What an opening in a wall panel is. 📄 docs/DESIGN_Building_Interiors.md §3a.</summary>
    public enum TerrainOpeningKind : byte { Door = 0, Window = 1, Gap = 2 }

    /// <summary>
    /// A door's state — the terrain's INITIAL value (template → instance override → scenario, §3a/§3b). The replicated
    /// runtime state (<c>DoorState</c>, Stage 5) uses the same values.
    /// </summary>
    public enum TerrainDoorState : byte { Open = 0, Closed = 1, Locked = 2, Destroyed = 3 }

    /// <summary>An opening on a wall panel, in WORLD units: <see cref="At"/> metres along the panel from <c>A</c>, sill and
    /// head as absolute Z.</summary>
    public sealed class TerrainOpening
    {
        public TerrainOpeningKind Kind { get; init; }
        public float At { get; init; }
        public float Width { get; init; }
        public float SillZ { get; init; }
        public float HeadZ { get; init; }
        /// <summary>The door's terrain-object key (§3b K2, <c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>); null = a plain opening.</summary>
        public string? DoorKey { get; init; }
    }

    /// <summary>
    /// ⭐ THE ONE NEW PRIMITIVE (§2 B2): a wall segment <see cref="A"/>→<see cref="B"/> with thickness, a Z span and
    /// openings, made of a <see cref="Material"/>. ⭐ At parse it is also EXPANDED into thin <see cref="TerrainPrism"/>s
    /// (the solid pieces between and around its openings), so every existing consumer — <c>SurfaceZ</c>,
    /// <c>SegmentBlocked</c>, the navmesh, cover — sees doorways and windows as gaps with no change of its own.
    /// </summary>
    public sealed class TerrainWallPanel
    {
        public Vector2 A { get; init; }
        public Vector2 B { get; init; }
        public float Thickness { get; init; }
        public float BaseZ { get; init; }
        public float TopZ { get; init; }
        public TerrainMaterial Material { get; init; } = null!;
        public IReadOnlyList<TerrainOpening> Openings { get; init; } = Array.Empty<TerrainOpening>();
        /// <summary>The building it belongs to (index into <see cref="TerrainWorld.Buildings"/>), −1 for a free wall/fence.</summary>
        public int Building { get; init; } = -1;
        /// <summary>The storey within that building (0 = ground storey), −1 for a free wall/fence.</summary>
        public int Storey { get; init; } = -1;
        public string? Label { get; init; }
        public float Length => Vector2.Distance(A, B);
    }

    /// <summary>A door the terrain defines (its static definition); one door ENTITY per key arrives with Stage 5.</summary>
    public sealed class TerrainDoorDef
    {
        /// <summary>§3b K2 — <c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>, a string, never a network id.</summary>
        public string Key { get; init; } = "";
        public int Panel { get; init; }
        public int Opening { get; init; }
        public TerrainDoorState Initial { get; init; }
        public Vector2 Center { get; init; }
        public float SillZ { get; init; }
    }

    /// <summary>
    /// ⭐ A placed building at runtime (§3a): the template's name, where it stands and its storey floors — kept so room /
    /// portal queries (sound v2, room entry) can work per building; its walls/floors/stairs live in the world's flat
    /// lists like every other primitive.
    /// </summary>
    public sealed class TerrainBuilding
    {
        public string Label { get; init; } = "";
        /// <summary>The template's name, or null for an inline building.</summary>
        public string? Template { get; init; }
        public Vector2 Position { get; init; }
        public float RotationDegrees { get; init; }
        public float BaseZ { get; init; }
        /// <summary>Floor Z of each storey (index 0 = the ground storey), plus the roof Z as the last entry.</summary>
        public IReadOnlyList<float> StoreyZ { get; init; } = Array.Empty<float>();
        /// <summary>World footprint (counter-clockwise).</summary>
        public Vector2[] Footprint { get; init; } = Array.Empty<Vector2>();
        public bool Solid { get; init; }
        public int Storeys => Math.Max(0, StoreyZ.Count - 1);
    }
}
