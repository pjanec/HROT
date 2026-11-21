using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐⭐ <c>CE-3173</c> (M-1, M-2, M-3; <c>docs/DESIGN_Entity_Control.md</c> §6, §8b) — an OPERATOR's motion axes for a body in
    /// <c>KinematicsMode.Manual</c>: written on the brain by the possessing behaviour, read by the muscle's
    /// <c>CarKinematicsSystem</c> (<c>CarKinem.Controllers.ManualMotion</c>). ⛔ Not a destination — the body moves as the axes say.
    /// A PERSON uses <see cref="Forward"/>, <see cref="Lateral"/> and <see cref="Yaw"/>; a VEHICLE <see cref="Forward"/> (throttle)
    /// and <see cref="Steer"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ManualDrive)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ManualDrive
    {
        /// <summary>Body-frame forward (+) / back (−) ∈ [−1, 1]; a vehicle's throttle (− brakes, then reverses).</summary>
        public float Forward;

        /// <summary>A person's strafe ∈ [−1, 1], + = left (the body's Y). A vehicle ignores it.</summary>
        public float Lateral;

        /// <summary>A vehicle's steer ∈ [−1, 1] of its <c>MaxSteerAngle</c>, + = left. A person ignores it.</summary>
        public float Steer;

        /// <summary>A person's facing: the ABSOLUTE world yaw (radians, X-forward, Z-up) his body turns toward — latency-tolerant,
        /// unlike a yaw rate (M-2).</summary>
        public float Yaw;

        /// <summary>⭐ CE-2168 (Motion Domains MD-D) — a flyer's climb (+) / descent (−) ∈ [−1, 1] of its climb rate. The ground ignores it.</summary>
        public float Vertical;

        /// <summary>A person's pace: 0 = run (full speed), 1 = walk (half).</summary>
        public byte Gait;

        /// <summary>The control frame this came from (newest wins).</summary>
        public uint Seq;
    }
}
