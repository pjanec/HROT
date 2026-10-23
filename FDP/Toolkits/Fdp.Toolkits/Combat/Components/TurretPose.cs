using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐ WL-1 (docs/DESIGN_Weapon_Layers.md W-9, M23, R-255) — a turret's pose, on its PART child (<c>PartMetadata</c>, part id
    /// <see cref="TurretParts.FirstPartId"/> + the turret index): the traverse (radians, counter-clockwise from the hull's forward)
    /// and the gun elevation (radians, up from the turret plane). Written by the weapon MUSCLE (W-1), or by ambient fire (§6 A-4);
    /// read by every viewer. Not saved — a turret is a derived part.
    /// </summary>
    [ComponentId(GlobalComponentIds.TurretPose)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct TurretPose
    {
        public float Azimuth;
        public float Elevation;
    }
}
