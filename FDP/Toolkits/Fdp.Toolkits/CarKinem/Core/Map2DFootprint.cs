using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Core
{
    /// <summary>
    /// Shape category for the 2D map gizmo (BATCH-S2-G2). Independent of 3D — drives which
    /// symbolic profile the 2D renderer draws. Derived from the physics CollisionShapeKind (and/or DIS
    /// type when available).
    /// </summary>
    public enum GizmoShapeCategory : byte
    {
        Unknown      = 0,
        Humanoid     = 1,
        GroundVehicle = 2,
        FixedWing    = 3,
        RotaryWing   = 4,
    }

    /// <summary>
    /// Neutral 2D map footprint (BATCH-S2-G2): real-world length/width in METERS plus a shape
    /// category. Written by the TKB translator from the entity's shape dims; read by the 2D gizmo renderer.
    /// Carries NO 3D/physics types — pure data, so the generic renderer stays 3D-agnostic.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Map2DFootprint)]
    public struct Map2DFootprint
    {
        /// <summary>Real-world length in meters, along the entity's forward (X) extent.</summary>
        public float LengthM;

        /// <summary>Real-world width in meters, lateral (Y) extent.</summary>
        public float WidthM;

        /// <summary>Symbolic profile selector for the 2D renderer.</summary>
        public GizmoShapeCategory Shape;
    }
}
