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
        /// <summary>⭐ <c>CE-2222</c> — its turn rate (°/s) and top-attack climb (°), from its <c>MissileDto</c>; 0 = the engine's.</summary>
        public float TurnRateDegPerSec, ClimbAngleDeg;

        // ⭐⭐ CE-10002 (air lane, AC-6, RV-1; docs/designs/air-attack/DESIGN_Air_Combat.md §3, §5b) — a MOTOR and a g limit (all 0 ⇒ the
        //   constant-speed round above, unchanged). SpeedMps stays the burn-out speed (it feeds the lead and the dive maths).
        /// <summary>The speed it flies at NOW (m/s).</summary>
        public float SpeedNow;
        /// <summary>Burn time left (s); its boost (m/s²) to <see cref="SpeedMps"/> while it burns.</summary>
        public float BurnLeftS, BoostMps2;
        /// <summary>The g it pulls at <see cref="SpeedMps"/> (falls with speed²); the speed lost per second coasting; below <see cref="MinSpeedMps"/> it is defeated.</summary>
        public float MaxG, DragPerS, MinSpeedMps;
        /// <summary>The steering's lag (s) and the direction it is steering now (it follows the lead through the lag).</summary>
        public float SteerLagS;
        public Vector3 SteerDir;
        /// <summary>The g it pulled last step (more drag while it turns).</summary>
        public float LoadG;

        /// <summary>SD-S4 (DESIGN_Simulated_Devices sec 3.13b D-86 / D-95) - the fire-control DEVICE this round is the launch of (set at launch by the seq echo,
        /// <c>FireControls.ClaimLaunch</c>); null for an AI shot. A <see cref="Fdp.Toolkit.Tkb.Domain.GuidanceKind.Command"/> round with a device flies the DEVICE's sight line
        /// (D-96), not the gunner's eye onto the target. Appended (R-4301).</summary>
        public Entity Device;
    }
}
