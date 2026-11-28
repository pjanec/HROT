using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Motion
{
    /// <summary>⭐ SD-S2c (§3.13c D-104 / D-107) — what a remotely piloted drone is doing (its own autopilot phase), echoed to the station in <see cref="DroneLink"/>.</summary>
    public enum PilotPhase : byte
    {
        /// <summary>On the ground (never flown, or landed).</summary>
        Grounded = 0,
        /// <summary>Climbing to its hover height after TAKEOFF.</summary>
        TakeOff = 1,
        /// <summary>The sticks fly it (sticks centred: it holds its hover).</summary>
        Manual = 2,
        /// <summary>Coming down where it is (LAND).</summary>
        Land = 3,
        /// <summary>Flying back to its launch point (RTH), then it lands.</summary>
        ReturnHome = 4,
        /// <summary>⭐ D-107 — the link is lost: it hovers <c>FailsafeSeconds</c>.</summary>
        FailsafeHover = 5,
        /// <summary>⭐ D-107 — the failsafe's return home (it continues after the link comes back until the station sends a NEW command).</summary>
        FailsafeReturn = 6,
        /// <summary>⭐ D-107 — the failsafe's landing at home.</summary>
        FailsafeLand = 7,
        /// <summary>The battery is empty: down where it is (the existing rule).</summary>
        BatteryDown = 8,
    }

    /// <summary>
    /// ⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-106) — a remotely piloted drone's LINK to its ground station,
    /// written by the DRONE's own brain on ITSELF each tick (on change): <see cref="Quality"/> 1 inside its type's range (<c>RemoteLinkDto.RangeM</c>) from its
    /// station device's world pose, falling to 0 over the last 20 %; <see cref="Jammed"/> inside an enemy jammer's bubble; <see cref="Lost"/> = quality 0, or
    /// jammed, or no station, or the station off. ⭐ AS BUILT it also carries the TELEMETRY the station's mode needs — the pilot's <see cref="Phase"/> and the
    /// command it last acted on (<see cref="AckSeq"/>) — the proven GCS downlink (the station shows LANDED / RTH / FAILSAFE from what the airframe does, not
    /// from what it was told). Unmanaged, recorded; not authored.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DroneLink)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DroneLink
    {
        /// <summary>0 … 1.</summary>
        public float Quality;
        public byte Jammed;
        public byte Lost;
        public PilotPhase Phase;
        /// <summary>The station's <c>RemoteControl.Seq</c> the pilot last acted on.</summary>
        public uint AckSeq;
    }

    /// <summary>⭐ SD-S2c (D-106) — the ONE link rule, and the one writer of <see cref="DroneLink"/>.</summary>
    public static class RemoteLinks
    {
        /// <summary>The share of the range over which the quality falls from 1 to 0.</summary>
        public const float FalloffShare = 0.2f;

        /// <summary>The quality at <paramref name="distance"/> from the station for a link of <paramref name="rangeM"/>: 1 inside 80 %, falling linearly to 0 at the range.</summary>
        public static float Quality(float distance, float rangeM)
        {
            if (rangeM <= 0f) return 0f;
            float full = rangeM * (1f - FalloffShare);
            if (distance <= full) return 1f;
            return Math.Clamp((rangeM - distance) / (rangeM * FalloffShare), 0f, 1f);
        }

        /// <summary>The link of a drone at <paramref name="at"/> to a station at <paramref name="station"/> (null: none, or off).</summary>
        public static DroneLink Of(EntityRepository w, Entity drone, Vector3 at, Vector3? station, float rangeM)
        {
            var link = new DroneLink();
            link.Quality = station is { } s ? Quality(Vector3.Distance(at, s), rangeM) : 0f;
            link.Jammed = DroneJamming.InBubble(w, drone, at) ? (byte)1 : (byte)0;
            link.Lost = link.Quality <= 0f || link.Jammed != 0 ? (byte)1 : (byte)0;
            return link;
        }

        /// <summary>The drone's link, or a lost one when it has none.</summary>
        public static DroneLink Read(EntityRepository w, Entity drone)
            => !drone.IsNull && w.IsAlive(drone) && w.IsComponentTypeRegistered<DroneLink>() && w.HasComponent<DroneLink>(drone)
                ? w.GetComponentRO<DroneLink>(drone) : new DroneLink { Lost = 1 };

        /// <summary>Writes the drone's own link on change (added WITH its value — the parallel rule).</summary>
        public static void Write(EntityRepository w, Entity self, in DroneLink next)
        {
            if (!w.IsComponentTypeRegistered<DroneLink>()) return;
            if (w.HasComponent<DroneLink>(self))
            {
                ref readonly var now = ref w.GetComponentRO<DroneLink>(self);
                if (now.Quality == next.Quality && now.Jammed == next.Jammed && now.Lost == next.Lost && now.Phase == next.Phase && now.AckSeq == next.AckSeq) return;
                w.SetComponent(self, next);
            }
            else w.AddComponent(self, next);
        }
    }
}
