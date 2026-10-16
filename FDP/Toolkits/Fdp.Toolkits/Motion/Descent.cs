using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>⭐ Airborne insertion (docs/designs/airborne/DESIGN_Airborne_Insertion.md §1) — how a body comes down.</summary>
    public enum DescentKind : byte
    {
        None = 0,
        /// <summary>Down a fast rope under a hovering helicopter (<see cref="RopeSlideModel"/>).</summary>
        FastRope = 1,
        /// <summary>A round canopy opened by a static line at the door (<see cref="ParachuteModel"/>).</summary>
        StaticLine = 2,
        /// <summary>Military free fall, then a steerable ram-air canopy.</summary>
        FreeFall = 3,
        /// <summary>Slice 2 — the rocket seat, then a round canopy.</summary>
        Ejection = 4,
    }

    /// <summary>⭐ Airborne insertion (§1 state diagram) — the one phase machine for every descent; the animation key (D-5).</summary>
    public enum DescentPhase : byte
    {
        Exit = 0, Grip = 1, Slide = 2, Brake = 3, FreeFalling = 4, Deploying = 5, Canopy = 6, Flare = 7, Landing = 8, Landed = 9,
        /// <summary>The anchor lost under him (the helicopter killed mid-rope): gravity, then the fall's damage (§5.3).</summary>
        Falling = 10,
    }

    /// <summary>⭐ Airborne insertion §4 — the canopy a jumper flies.</summary>
    public enum CanopyKind : byte { NoCanopy = 0, Round = 1, RamAir = 2 }

    /// <summary>⭐ Airborne insertion B-4 — how a passenger leaves a carrier (<c>DisembarkEntityCommand.Exit</c>).</summary>
    public enum ExitKind : byte
    {
        /// <summary>He steps out beside the hull (a landed carrier, a hop ≤ 3 m — CE-1084's drawn jump, D-8).</summary>
        StepOut = 0,
        /// <summary>He slides down the carrier's rope <c>Anchor</c>.</summary>
        RopeDown = 1,
        /// <summary>He jumps from the door (static line or free fall, by his parachute).</summary>
        JumpOut = 2,
    }

    /// <summary>
    /// ⭐⭐ Airborne insertion B-1 (D-1) — a body coming DOWN: a rope from a hovering helicopter, or a parachute. Present ⇒ its class is
    /// <see cref="LocomotionClass.Rope"/> / <see cref="LocomotionClass.Parachute"/> through <see cref="Locomotions.Of"/>, on every node (it
    /// replicates — the type's <see cref="Locomotion"/> does not); the descent model moves it (<see cref="AirMotionSystem"/>) and the ground
    /// mover skips it. Removed once he has <see cref="DescentPhase.Landed"/> for his landing time: a Pedestrian again. Muscle group.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Descent)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Descent
    {
        public DescentKind Kind;
        public DescentPhase Phase;
        public CanopyKind Canopy;
        /// <summary>The carrier's rope or door he left by (its <c>RopeAnchors</c> index; 0 = the first).</summary>
        public byte AnchorIndex;
        /// <summary>The sim time (s) the phase began.</summary>
        public double PhaseSince;
        /// <summary>The carrier he hangs from (a rope) or left (a jump) — this node's handle; the wire carries its network id.</summary>
        public Entity Anchor;
        /// <summary>Where he means to land (a jumper's landing point; zero = wherever he comes down).</summary>
        public Vector3 Target;

        /// <summary>The class this descent moves him as.</summary>
        public readonly LocomotionClass Class => Kind == DescentKind.FastRope ? LocomotionClass.Rope : LocomotionClass.Parachute;
    }
}

namespace Fdp.Toolkit.Motion
{
    // ⛔ CE-3216 W-5 (R-3012) — the `Wind` world singleton (D-6: one constant velocity per scenario) is RETIRED: the parachute and the
    //   release point read Fdp.Toolkit.Weather.WeatherField.WindAt (the regional blend and the profile at a height). Component id 393 is
    //   RESERVED (GlobalComponentIds.Wind) — never reuse. 📄 docs/designs/weather/DESIGN_Weather_And_Time.md W-5.
}
