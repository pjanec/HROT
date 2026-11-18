using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-4657</c> (TD-3, TD-4; 📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §3e, §4) — what an entity's heat source is doing. An ENGINE (TKB
    /// <c>ThermalSignatureDto.HasPowerPlant</c>) is Off · Idle · Running · Dead; a BODY (a person, an animal) is Standing · Moving. ⛔ The kind and the load /
    /// exertion step are NOT stored — every reader derives them from the type and the speed it already has.
    /// </summary>
    public enum HeatSourceState : byte
    {
        /// <summary>Never written (an entity without a decision yet) — the calculator treats it as cold.</summary>
        None = 0,
        /// <summary>An engine switched off (parked and empty, or a silent watch).</summary>
        Off = 1,
        /// <summary>An engine running with the vehicle stopped.</summary>
        Idle = 2,
        /// <summary>An engine driving (moving, or ordered to move).</summary>
        Running = 3,
        /// <summary>A destroyed vehicle, or its engine part dead.</summary>
        Dead = 4,
        /// <summary>A body standing still.</summary>
        Standing = 5,
        /// <summary>A body walking / running (the step comes from the speed).</summary>
        Moving = 6,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4657</c> (TD-3, TD-4, §4) — THE heat-source driver: only what cannot be derived (§3e) — the DECISION (<see cref="State"/>) and its TIMES. Written by
    /// the node that moves the entity (<c>HeatSourceSystem</c>, CE-4658) on a change only, added with its final value (D-31); replicated as <c>dtHeatSource</c>
    /// (owner → every node, the image-generator node included), recorded. ⭐ <see cref="DataPolicy.Default"/>: the authored start <see cref="State"/> is SAVED (a
    /// convoy already running); the times are reset at load by the writer. Read by <see cref="ThermalDriversOps.Signature"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.HeatSource)]
    [DataPolicy(DataPolicy.Default)]
    public struct HeatSource
    {
        public HeatSourceState State;
        /// <summary>When <see cref="State"/> last changed — how long the engine has idled / run / been off (a late joiner knows it).</summary>
        public SimInstant Since;
        /// <summary>When it last started or stopped MOVING (TD-4) — how long it has moved or stood.</summary>
        public SimInstant MotionSince;

        /// <summary>An engine's state (Off · Idle · Running · Dead).</summary>
        public readonly bool IsEngine => State is HeatSourceState.Off or HeatSourceState.Idle or HeatSourceState.Running or HeatSourceState.Dead;

        public static HeatSource Of(HeatSourceState state, SimInstant since, SimInstant motionSince)
            => new() { State = state, Since = since, MotionSince = motionSince };
    }

    /// <summary>One fire spot (TD-6, §3c): in the HOLDER's coordinates, going out by itself at <see cref="Since"/> + <see cref="Seconds"/>.</summary>
    public readonly struct FireSpot
    {
        public readonly float X, Y, Z, Radius;
        public readonly SimInstant Since;
        public readonly float Seconds;
        /// <summary>0..1 — how hard it burns.</summary>
        public readonly float Intensity;

        public FireSpot(float x, float y, float z, float radius, SimInstant since, float seconds, float intensity)
        {
            X = x; Y = y; Z = z; Radius = radius; Since = since; Seconds = seconds; Intensity = intensity;
        }

        /// <summary>Burning at <paramref name="now"/>: lit and not yet gone out.</summary>
        public bool IsBurning(double now) => Seconds > 0f && now >= Since.Seconds && now < Since.Seconds + Seconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4657</c> (TD-6, TD-16; §3c, §4) — the fire on an entity as a few SPOTS (a car: one; a ship's sections, a building's rooms: several), each going out by
    /// itself. ONE component on three holders — a vehicle / wagon, a building's damage-record entity, a terrain cell's record entity. ADDED at the first ignition (rare,
    /// with its final value — D-31) by the Health owner; spreading = the owner ADDS a spot. Replicated as <c>dtFireState</c>, recorded (unmanaged: bytes), ⛔ not saved
    /// (a scenario starts healthy or with a cold wreck). Full (<see cref="Capacity"/>) ⇒ the owner opens a second holder (the building precedent).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FireState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct FireState
    {
        /// <summary>Spots per holder (8 × 32 B + the count ≈ 260 B).</summary>
        public const int Capacity = 8;

        /// <summary>Spots in use.</summary>
        public byte Count;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        public fixed float Radius[Capacity];
        /// <summary>Seconds of sim time (a fixed buffer holds no <see cref="SimInstant"/>; <see cref="Get"/> types it).</summary>
        public fixed double Since[Capacity];
        public fixed float Seconds[Capacity];
        public fixed float Intensity[Capacity];

        /// <summary>Spot <paramref name="i"/> (0 ≤ i &lt; <see cref="Capacity"/>).</summary>
        public readonly FireSpot Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new FireSpot(X[i], Y[i], Z[i], Radius[i], (SimInstant)Since[i], Seconds[i], Intensity[i]);
        }

        /// <summary>Overwrites slot <paramref name="i"/> (does not change <see cref="Count"/>).</summary>
        public void Set(int i, in FireSpot s)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            X[i] = s.X; Y[i] = s.Y; Z[i] = s.Z; Radius[i] = s.Radius; Since[i] = s.Since.Seconds; Seconds[i] = s.Seconds; Intensity[i] = s.Intensity;
        }

        /// <summary>Adds <paramref name="spot"/>, reusing a slot whose spot has gone out by <paramref name="now"/>; false when every slot burns (open a second holder).</summary>
        public bool TryAdd(in FireSpot spot, double now)
        {
            for (int i = 0; i < Count; i++)
                if (!Get(i).IsBurning(now)) { Set(i, in spot); return true; }
            if (Count >= Capacity) return false;
            Set(Count, in spot);
            Count++;
            return true;
        }

        /// <summary>True when any spot burns at <paramref name="now"/>.</summary>
        public readonly bool IsBurning(double now)
        {
            for (int i = 0; i < Count; i++)
                if (Get(i).IsBurning(now)) return true;
            return false;
        }

        /// <summary>The strongest burning spot's intensity at <paramref name="now"/> (0 when out).</summary>
        public readonly float Intensity01(double now)
        {
            float best = 0f;
            for (int i = 0; i < Count; i++)
            {
                var s = Get(i);
                if (s.IsBurning(now) && s.Intensity > best) best = s.Intensity;
            }
            return best;
        }
    }
}
