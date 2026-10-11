using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13a) — how a unit holds its weapon.</summary>
    public enum WeaponPostureKind : byte
    {
        /// <summary>Carried low across the body: no engagement for a while.</summary>
        AtEase = 0,
        /// <summary>Raised but not aimed: the target is hidden, a reload or a throw is running, or the engagement just ended.</summary>
        Ready = 1,
        /// <summary>Shouldered and pointed at <see cref="WeaponPosture.Target"/> (or <see cref="WeaponPosture.Point"/>).</summary>
        Aimed = 2,
    }

    /// <summary>
    /// ⭐⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13, R-272: <i>"the aiming/at ease needs to be weapon state controlled from the weapon
    /// brain"</i>) — the weapon brain's posture of a unit's weapon. Written ONLY through <see cref="WeaponPostureOf"/> (the executors'
    /// status points and the dispatcher's idle branch), on the unit, in the Brain ownership group; replicated as <c>dtWeaponPosture</c> to
    /// every node; read by the 3-D figure. Never saved with a scenario.
    /// </summary>
    [ComponentId(GlobalComponentIds.WeaponPosture)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct WeaponPosture
    {
        public WeaponPostureKind Kind;
        /// <summary>What it is aimed at; <see cref="Entity.Null"/> = <see cref="Point"/> (or nothing when not aimed).</summary>
        public Entity Target;
        /// <summary>The aim point when there is no target entity (world metres).</summary>
        public Vector3 Point;
        /// <summary>Seconds with no weapon action running — the Brain's own clock for lowering the weapon; never sent.</summary>
        public float IdleSeconds;
    }
}
