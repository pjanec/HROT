using System;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c, ruling 9) — THE ONE mapping from an operator's axes to a body's
    /// <see cref="ManualDrive"/>, shared by the two ways a man flies a drone by hand: possessed (<c>OperatorControlBehavior</c>, the station's frame) and
    /// through a ground station (<c>RemotePilotBehavior</c>, the station device's sticks). <see cref="Heading"/> is the one "turn by a rate" rule: the heading
    /// it is told to turn to is its own led by <see cref="TurnLead"/> to the side the yaw axis holds (held, it turns at its own rate; released, it holds) —
    /// extracted from the station's <c>FpsCameraDriver.FlyerHeading</c> (CE-1351), which now calls it.
    /// </summary>
    public static class ManualFlight
    {
        /// <summary>⭐ CE-1351 — how far ahead of a flyer's nose a full yaw deflection puts the heading it turns to (rad).</summary>
        public const float TurnLead = 0.8f;

        /// <summary>The drive from the axes: body-frame <paramref name="forward"/> / <paramref name="lateral"/> (+ left), the ABSOLUTE heading <paramref name="yaw"/>,
        /// <paramref name="vertical"/> (+ climb), a vehicle's <paramref name="steer"/>, a person's <paramref name="gait"/>, the frame number <paramref name="seq"/>.</summary>
        public static ManualDrive FromAxes(float forward, float lateral, float yaw, float vertical, float steer = 0f, byte gait = 0, uint seq = 0)
            => new() { Forward = forward, Lateral = lateral, Steer = steer, Yaw = yaw, Vertical = vertical, Gait = gait, Seq = seq };

        /// <summary>The heading a flyer is told to turn to: its own <paramref name="heading"/> led <see cref="TurnLead"/> toward <paramref name="side"/> (+1 = left, counter-clockwise).</summary>
        public static float Heading(float heading, float side) => heading + Math.Clamp(side, -1f, 1f) * TurnLead;
    }
}
