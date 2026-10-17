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
        /// <summary>⭐ ui handoff P4 (DESIGN_Weapon_Layers W-13e) — the mount the weapon brain is firing / about to fire: the unit's mount
        /// index (0 = its primary, 1… its own mounts, 32 + k a weapon system it controls). Kept when the weapon is lowered; sent.</summary>
        public byte ActiveMount;
        /// <summary>⭐ <c>CE-3234</c> (civilians B-12, AW-14; docs/DESIGN_Civilians_And_Crowd_Control.md §7.14 HI-1) — what is in his hand:
        /// Weapon / Throwable = the <see cref="ActiveMount"/> held up (never a copy of it), Phone / Binoculars = a pocket item. Written only
        /// through <c>HeldItems.Set</c>; kept when the weapon is lowered; sent (APPENDED).</summary>
        public HeldItemKind HeldItem;
    }

    /// <summary>⭐ <c>CE-3234</c> (B-12, AW-14) — what a person holds in his hand.</summary>
    public enum HeldItemKind : byte
    {
        None = 0,
        /// <summary>The weapon on <see cref="WeaponPosture.ActiveMount"/>.</summary>
        Weapon = 1,
        /// <summary>A thing to throw — the hands mount (a stone, a grenade) on <see cref="WeaponPosture.ActiveMount"/>.</summary>
        Throwable = 2,
        /// <summary>A phone (a pocket item): a cue to an observer, and a remote charge's trigger.</summary>
        Phone = 3,
        /// <summary>Binoculars (a pocket item): he sees further and narrower while they are up.</summary>
        Binoculars = 4,
    }
}
