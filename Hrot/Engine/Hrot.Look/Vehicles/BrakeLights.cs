using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Look.Vehicles
{
    /// <summary>
    /// ⭐ <c>CE-4670</c> (VA-2 / VA-3; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6b) — a renderer's brake-light tracker: brake lights are
    /// DERIVED from deceleration, never stored or sent (they change at driving rate). The wire carries velocity, not acceleration, so a stateless reader cannot see
    /// a deceleration from one sample: each renderer (our 3D map, a gateway) owns ONE of these and passes it to <see cref="VehicleLook.Lights"/>. It samples the
    /// speed the node sees (<see cref="SimVelocity"/>, else the replica's <see cref="NetworkVelocity"/>) over ≥ <see cref="Window"/> s and lights the stop lights
    /// at ≥ <see cref="OnDecel"/> m/s² of deceleration, held <see cref="Hold"/> s so a renderer sampling every frame sees no flicker. A time that goes BACK (a
    /// replay seek) restarts the sample. 0 B per call in the steady state (one array, grown by entity index, never shrunk).
    /// </summary>
    public sealed class BrakeLights
    {
        /// <summary>Deceleration (m/s²) that lights the stop lights — firmer than coasting (a tank rolls down ~0.3 m/s² on the flat).</summary>
        public const float OnDecel = 0.8f;
        /// <summary>The shortest span (s) a deceleration is measured over.</summary>
        public const double Window = 0.25;
        /// <summary>How long (s) the lights stay on after the last braking sample.</summary>
        public const double Hold = 0.5;
        /// <summary>Below this speed (m/s) a vehicle is stopped: nothing to brake.</summary>
        public const float Stopped = 0.3f;

        private struct Sample
        {
            public int Generation;   // the entity's generation + 1 (0 = never sampled)
            public double At;
            public float Speed;
            public double LitUntil;
        }

        private Sample[] _samples = Array.Empty<Sample>();

        /// <summary>Is <paramref name="vehicle"/> braking at <paramref name="now"/>, as this renderer has seen its speed?</summary>
        public bool IsBraking(EntityRepository world, Entity vehicle, double now)
        {
            if (vehicle.Index < 0) return false;
            if (vehicle.Index >= _samples.Length) Array.Resize(ref _samples, Math.Max(vehicle.Index + 1, _samples.Length * 2));
            ref var s = ref _samples[vehicle.Index];
            float speed = SpeedOf(world, vehicle);
            if (s.Generation != vehicle.Generation + 1 || now < s.At)
            {
                s = new Sample { Generation = vehicle.Generation + 1, At = now, Speed = speed, LitUntil = double.NegativeInfinity };
                return false;
            }
            double dt = now - s.At;
            if (dt >= Window)
            {
                if ((s.Speed - speed) / dt >= OnDecel && speed > Stopped) s.LitUntil = now + Hold;
                s.At = now;
                s.Speed = speed;
            }
            return now < s.LitUntil;
        }

        /// <summary>The speed this node sees: the simulated velocity where it has one (the owner, a replica carrying it), else the received one.</summary>
        private static float SpeedOf(EntityRepository world, Entity vehicle)
        {
            if (world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(vehicle))
                return world.GetComponentRO<SimVelocity>(vehicle).Linear.Length();
            if (world.IsComponentTypeRegistered<NetworkVelocity>() && world.HasComponent<NetworkVelocity>(vehicle))
                return world.GetComponentRO<NetworkVelocity>(vehicle).Value.Length();
            return 0f;
        }
    }
}
