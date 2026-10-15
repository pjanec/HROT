using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>⭐ M4 — the steering phase of a guided round.</summary>
    public enum GuidedPhase : byte { Cruise = 0, Climb = 1, Dive = 2, Lost = 3 }

    /// <summary>
    /// ⭐⭐ M4 (D-D; <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3) — a GUIDED round, beside its
    /// <see cref="BallisticProjectile"/>: what steers it, at whom, and how long it may fly. Added by <c>FireProcessingSystem</c> when the
    /// fired munition has a <c>MissileDto</c>; read by <c>GuidedRoundSystem</c> (steering) and <c>BallisticsSystem</c> (the lifetime).
    /// Muscle-local, never replicated (the round's position is what the viewers draw).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GuidedRound)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct GuidedRound
    {
        /// <summary>The simulation rate the tick lifetimes assume (<c>CombatConstants.BulletLifetimeTicks</c>: 120 ≈ 2 s).</summary>
        public const float TicksPerSecond = 60f;

        public Fdp.Toolkit.Tkb.Domain.GuidanceKind Guidance;
        public GuidedPhase Phase;
        /// <summary>1 = climb, cruise and dive onto the roof (fire-and-forget, beyond the top-attack range).</summary>
        public byte TopAttack;
        public Entity Target;
        /// <summary>The unit that fired it — a command round flies the gunner's sight line.</summary>
        public Entity Gunner;
        public float SpeedMps;
        /// <summary>Ticks it may fly (the bullet lifetime does not apply).</summary>
        public uint LifetimeTicks;
        /// <summary>Top attack: the cruise height (world Z).</summary>
        public float CruiseZ;
        public float DiveAngleDeg;
        /// <summary>Where the seeker last saw the target (fire-and-forget) — it steers there when it loses sight.</summary>
        public Vector3 LastSeen;
        /// <summary>⭐ <c>CE-3198</c> — 1 = the overfly top attack (DESIGN_Missiles_And_Ammo §3b): steer over the roof, fire down on the pass.</summary>
        public byte Overfly;
        /// <summary>⭐ <c>CE-3198</c> — the height above the roof it passes at.</summary>
        public float OverflyHeightM;
        /// <summary>⭐ <c>CE-2187</c> (§3c) — cos(half the seeker cone); 0 = it sees all round. The seeker sees a point when the angle
        /// between the flight and the line to it is inside the cone.</summary>
        public float SeekerCos;
        /// <summary>⭐ <c>CE-2187</c> (§3c, AD2) — the proximity fuze's reach (m); 0 = none.</summary>
        public float ProximityM;
        /// <summary>⭐ <c>CE-2188</c> (§3c, AD3) — the chance its seeker ignores a decoy salvo of its kind (flare for IR, chaff for radar).</summary>
        public float Rejection;
    }
}
