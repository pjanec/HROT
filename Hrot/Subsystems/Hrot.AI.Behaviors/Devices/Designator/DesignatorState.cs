using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;

namespace Hrot.AI.Behaviors.Devices.Designator
{
    /// <summary>The designator's procedure (D-113): <c>OFF →(POWER) STANDBY ⇄(LASE) LASING →(LaseSeconds) COOLING →(CoolSeconds) STANDBY</c>.</summary>
    public enum DesignatorPhase : byte { Off = 0, Standby = 1, Lasing = 2, Cooling = 3 }

    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-113 – D-115, D-57) — the LASER TARGET DESIGNATOR's state, a
    /// <c>[UnitMemory]</c> struct in the DEVICE entity's blackboard store: recorded, read by its HUD projector and the scenario's <c>field</c> cue
    /// (<c>memory: DesignatorState</c>). Written ONLY by the device's own logic (<see cref="DesignatorBehavior.Tick"/>) — the parallel rule.
    /// <para>⭐ The timers are SIM-TIME STAMPS (<see cref="LaseFrom"/>, <see cref="CoolFrom"/>), never counters: a device ticks every frame, paused or not, so a
    /// counted timer would depend on how many frames a sim instant had (S1e); and sim time stands in a pause, so the lase timer stands still by construction
    /// (D-115).</para>
    /// </summary>
    [UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public struct DesignatorState
    {
        /// <summary>Where the procedure is.</summary>
        public DesignatorPhase Phase;
        /// <summary>1 once the first tick took the type's data (the default code, the duty cycle).</summary>
        public byte Ready;
        /// <summary>⭐ D-115 — 1 while a LASE pressed in a pause waits for the first live frame (the HUD's <c>queued</c>).</summary>
        public byte Queued;
        /// <summary>⭐ D-115 / R-4300 — 1 while its own spot is a LIVE designation: LASING, the spot answers this lase, it is not a <c>Preview</c> and time
        /// advances. 0 on every paused frame — what a scenario judges "no designation in pause" by (the <c>LaserSpot</c> itself is <c>NoScenario</c>).</summary>
        public byte Designating;
        /// <summary>1 while a single RANGE pulse waits for its answer (the laser on, uncoded).</summary>
        public byte Ranging;
        /// <summary>1 once a range was measured with a return.</summary>
        public byte HasRange;
        /// <summary>1 when the last range was measured on a paused frame (a <c>Preview</c> spot).</summary>
        public byte RangePreview;
        /// <summary>The 4-digit PRF code (1111 … 1788 — <see cref="DesignatorCodes"/>); the beam carries it while LASING (D-114).</summary>
        public ushort Code;
        /// <summary>The last measured range (m) — from a RANGE pulse, or continuously from the spot while LASING.</summary>
        public float LastRange;
        /// <summary>Where the last range's laser met (world), and the body (this node's handle; null on terrain).</summary>
        public Vector3 LastPoint;
        /// <inheritdoc cref="LastPoint"/>
        public Entity LastHit;
        /// <summary>The laser request number (<c>LaserPointing.Seq</c>) — bumped per lase and per RANGE pulse; the spot echoes it.</summary>
        public uint Seq;
        /// <summary>The type's duty cycle (s), copied at the first tick (the HUD's bars need the whole of each).</summary>
        public float LaseSeconds, CoolSeconds;
        /// <summary>When (sim s) the running lase started / the cooling started.</summary>
        public double LaseFrom, CoolFrom;
        /// <summary>The press counters last seen (R7-11 — the one tracker every device keeps).</summary>
        public DevicePressTracker Presses;
        /// <summary>⭐ SD-S5b (D-119) — 1 once a target was received from a device that addressed it; the HUD draws its cue.</summary>
        public byte HasTarget;
        /// <summary>The last received target (copied from the device's inbox by its own logic).</summary>
        public Contracts.TargetLocation Target;
        /// <summary>Who sent it (its network id).</summary>
        public Fdp.Toolkit.Replication.EntityRef TargetFrom;
        /// <summary>The last applied message seq per sender (R3-10).</summary>
        public DeviceMessageTracker Messages;

        /// <summary>Seconds left of the running lase at <paramref name="now"/> (sim s), 0 when not lasing.</summary>
        public readonly double LaseLeft(double now) => Phase == DesignatorPhase.Lasing ? Math.Clamp(LaseSeconds - (now - LaseFrom), 0d, LaseSeconds) : 0d;

        /// <summary>Seconds left of the cooling at <paramref name="now"/> (sim s), 0 when not cooling.</summary>
        public readonly double CoolLeft(double now) => Phase == DesignatorPhase.Cooling ? Math.Clamp(CoolSeconds - (now - CoolFrom), 0d, CoolSeconds) : 0d;
    }

    /// <summary>
    /// The designator's press channels (<c>DeviceInput</c>) — the BUTTON indexes of TKB type 643's elements (D-113: POWER, LASE, CODE, RANGE; pinned by
    /// <c>DesignatorBehaviorTests.SDS5c_TheType…</c>); the numbers are the wire, the names are the type's data.
    /// </summary>
    public static class DesignatorChannels
    {
        public const int Count = 4;
        /// <summary>POWER — OFF ⇄ STANDBY (off from any phase: the laser goes off, a queued lase is dropped).</summary>
        public const int Power = 0;
        /// <summary>LASE — STANDBY: lase (in a pause: queued for the first live frame, again: un-queued); LASING: stop; COOLING / OFF: refused.</summary>
        public const int Lase = 1;
        /// <summary>CODE — steps the PRF code to the next valid one (STANDBY only).</summary>
        public const int Code = 2;
        /// <summary>RANGE — one ranging pulse (uncoded); while LASING the spot ranges continuously, so it adds nothing.</summary>
        public const int Range = 3;
    }

    /// <summary>
    /// ⭐ D-113 — the PRF CODE: four digits, each in its proven range — the first 1, the second 1–7, the third and fourth 1–8 (1111 … 1788, 448 codes). CODE
    /// steps to the NEXT valid code like an odometer (the last digit fastest; 1688 → 1711, 1788 → 1111 — it wraps).
    /// </summary>
    public static class DesignatorCodes
    {
        public const ushort First = 1111, Last = 1788;

        /// <summary>Is <paramref name="code"/> a valid PRF code?</summary>
        public static bool IsValid(int code)
        {
            int a = code / 1000, b = code / 100 % 10, c = code / 10 % 10, d = code % 10;
            return a == 1 && b >= 1 && b <= 7 && c >= 1 && c <= 8 && d >= 1 && d <= 8;
        }

        /// <summary>The valid code after <paramref name="code"/> (an invalid one steps to <see cref="First"/>).</summary>
        public static ushort Next(int code)
        {
            if (!IsValid(code)) return First;
            int b = code / 100 % 10, c = code / 10 % 10, d = code % 10;
            if (++d > 8) { d = 1; if (++c > 8) { c = 1; if (++b > 7) b = 1; } }
            return (ushort)(1000 + b * 100 + c * 10 + d);
        }
    }
}
