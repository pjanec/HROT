using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Formation
{
    /// <summary>
    /// Formation target (transient scratchpad component).
    /// Written by FormationTargetSystem, read by CarKinematicsSystem.
    /// Not persisted between frames.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FormationTarget)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct FormationTarget
    {
        public Vector2 TargetPosition;   // Desired world position
        public Vector2 TargetHeading;    // Desired forward vector
        public float TargetSpeed;        // Desired speed (includes catch-up factor)
        public byte IsValid;             // 1 = valid target, 0 = ignore
    }
}
