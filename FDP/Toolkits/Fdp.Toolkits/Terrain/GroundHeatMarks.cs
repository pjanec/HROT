using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4664</c> (thermal drivers TD-14) — what left a ground heat mark.</summary>
    public enum HeatMarkKind : byte
    {
        /// <summary>No mark (an empty slot). ⚠ Member names are unique across this file's enums: the DDS code generator puts them in one IDL scope.</summary>
        NoMark = 0,
        /// <summary>A vehicle stood here with its engine on.</summary>
        VehicleStand = 1,
        /// <summary>Ground a fire burned (later).</summary>
        BurnedArea = 2,
        /// <summary>Where a body lay (later).</summary>
        BodyImprint = 3,
    }

    /// <summary>⭐ <c>CE-4664</c> — which end of a mark carries the engine and exhaust: its hottest part.</summary>
    public enum HeatMarkEnd : byte
    {
        NoHotEnd = 0,
        Front = 1,
        Rear = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-4664</c> (thermal drivers TD-3 / TD-14) — the size of a heat source, from its TKB (<c>ThermalSignatureDto.PowerClass</c>, backend CE-4657): how much heat its
    /// engine puts into the ground, and how long a mark it leaves stays warm (<see cref="GroundHeatMarkRules.ColdAfterSeconds"/>).
    /// </summary>
    public enum ThermalPowerClass : byte
    {
        NoPowerClass = 0,
        Car = 1,
        Truck = 2,
        Tank = 3,
        Aircraft = 4,
    }

    /// <summary>
    /// ⭐ <c>CE-4664</c> (📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §4b, TD-14) — one warm stand a vehicle left: its footprint (<see cref="At"/> the centre,
    /// <see cref="Heading"/> radians, <see cref="Length"/> × <see cref="Width"/> m), its <see cref="HotEnd"/>, when it stood (<see cref="StandSince"/>, <see cref="LeftAt"/>),
    /// how long its engine ran there and its <see cref="PowerClass"/>. A DRIVER, never a temperature (TD-1): the image generator and
    /// <c>ThermalDriversOps.MarkWarmth</c> compute the warmth.
    /// </summary>
    public readonly record struct HeatMark(
        HeatMarkKind Kind, Vector2 At, float Heading, float Length, float Width, HeatMarkEnd HotEnd,
        SimInstant StandSince, SimInstant LeftAt, float EngineOnSeconds, ThermalPowerClass PowerClass)
    {
        /// <summary>How long the stand lasted (s).</summary>
        public double StandSeconds => LeftAt.Seconds - StandSince.Seconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4664</c> (TD-14, §4b) — the heat marks ONE moving node's vehicles left in ONE ground cell (<see cref="GroundDamage.CellSize"/> m — the crater record's
    /// shape and cell): a runtime entity that node owns, recorded by bytes (unmanaged), replicated to every node (<c>dtGroundHeatMarks</c>, a late joiner included). A cold mark
    /// is dropped by the next write to its cell. ⛔ Never drawn in the normal views; ⛔ <c>NoScenario</c> — a mark cools within hours and is never saved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GroundHeatMarks)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct GroundHeatMarks : ICellRecord<HeatMark>
    {
        int ICellRecord<HeatMark>.CellX { get => CellX; set => CellX = value; }
        int ICellRecord<HeatMark>.CellY { get => CellY; set => CellY = value; }
        readonly int ICellRecord<HeatMark>.ItemCount => Count;
        readonly bool ICellRecord<HeatMark>.IsFull => Count >= Capacity;
        readonly HeatMark ICellRecord<HeatMark>.Item(int i) => Get(i);
        bool ICellRecord<HeatMark>.TryAdd(in HeatMark item, out bool full) => GroundHeatMarkRules.Add(ref this, in item, out full);

        /// <summary>How many marks one entity holds (16 × 43 B + the cell ≈ 700 B — under the 1024 B command-buffer limit).</summary>
        public const int Capacity = 16;

        public int CellX;
        public int CellY;
        /// <summary>Marks in use.</summary>
        public byte Count;

        public fixed byte Kind[Capacity];
        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Heading[Capacity];
        public fixed float Length[Capacity];
        public fixed float Width[Capacity];
        public fixed byte HotEnd[Capacity];
        /// <summary>Seconds of sim time (a fixed buffer holds no <see cref="SimInstant"/>; <see cref="Get"/> types it).</summary>
        public fixed double StandSince[Capacity];
        public fixed double LeftAt[Capacity];
        public fixed float EngineOnSeconds[Capacity];
        public fixed byte PowerClass[Capacity];

        /// <summary>Mark <paramref name="i"/> (0 ≤ i &lt; <see cref="Count"/>).</summary>
        public readonly HeatMark Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new HeatMark((HeatMarkKind)Kind[i], new Vector2(X[i], Y[i]), Heading[i], Length[i], Width[i], (HeatMarkEnd)HotEnd[i],
                                (SimInstant)StandSince[i], (SimInstant)LeftAt[i], EngineOnSeconds[i], (ThermalPowerClass)PowerClass[i]);
        }

        /// <summary>Overwrites slot <paramref name="i"/> (does not change <see cref="Count"/>).</summary>
        public void Set(int i, in HeatMark m)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            Kind[i] = (byte)m.Kind; X[i] = m.At.X; Y[i] = m.At.Y; Heading[i] = m.Heading; Length[i] = m.Length; Width[i] = m.Width; HotEnd[i] = (byte)m.HotEnd;
            StandSince[i] = m.StandSince.Seconds; LeftAt[i] = m.LeftAt.Seconds; EngineOnSeconds[i] = m.EngineOnSeconds; PowerClass[i] = (byte)m.PowerClass;
        }
    }

    /// <summary>⭐ <c>CE-4664</c> — "a vehicle left a warm stand": published by <see cref="GroundHeatMarkRules.LeaveStand"/> on the moving node, read by
    /// <see cref="GroundHeatMarkSystem"/> the frame after. LOCAL (the record replicates, the event does not).</summary>
    [EventId(TerrainEventIds.GroundHeatMark)]
    [StructLayout(LayoutKind.Sequential)]
    public struct GroundHeatMarkEvent
    {
        public byte Kind;
        public float X, Y, Heading, Length, Width;
        public byte HotEnd;
        public SimInstant StandSince, LeftAt;
        public float EngineOnSeconds;
        public byte PowerClass;

        public readonly HeatMark Mark => new((HeatMarkKind)Kind, new Vector2(X, Y), Heading, Length, Width, (HeatMarkEnd)HotEnd, StandSince, LeftAt,
                                             EngineOnSeconds, (ThermalPowerClass)PowerClass);

        public static GroundHeatMarkEvent Of(in HeatMark m) => new()
        {
            Kind = (byte)m.Kind, X = m.At.X, Y = m.At.Y, Heading = m.Heading, Length = m.Length, Width = m.Width, HotEnd = (byte)m.HotEnd,
            StandSince = m.StandSince, LeftAt = m.LeftAt, EngineOnSeconds = m.EngineOnSeconds, PowerClass = (byte)m.PowerClass,
        };
    }

    /// <summary>⭐ <c>CE-4664</c> (TD-14) — the rules of a ground heat mark: the writer entry point, its cell, when it is cold, adding one.</summary>
    public static class GroundHeatMarkRules
    {
        /// <summary>⭐ The TKB type of a heat-marks entity (bare: the marks travel on <see cref="GroundHeatMarks"/> / <c>dtGroundHeatMarks</c>; 7209 free on every lane, <c>2026-11-18</c>).</summary>
        public const long TkbType = 7209;

        /// <summary>A stand shorter than this leaves no mark (§4b: "a stand of ≥ ~60 s").</summary>
        public const double MinStandSeconds = 60.0;

        /// <summary>
        /// ⭐⭐ THE WRITER ENTRY POINT — <c>HeatSourceSystem</c> (backend CE-4658) calls it on the node that moves the vehicle when the vehicle LEAVES a stand:
        /// a stand of ≥ <see cref="MinStandSeconds"/> with the engine on (<see cref="HeatMark.EngineOnSeconds"/> &gt; 0) becomes one mark in its cell's record
        /// (<see cref="GroundHeatMarkSystem"/> writes it the frame after). False — nothing published — for a short or cold stand, or a world without the event.
        /// One event per stand: a user action rate, never per frame.
        /// </summary>
        public static bool LeaveStand(EntityRepository repo, in HeatMark mark)
        {
            if (!Leaves(in mark) || !repo.Bus.IsRegistered<GroundHeatMarkEvent>()) return false;
            repo.Bus.Publish(GroundHeatMarkEvent.Of(in mark));
            return true;
        }

        /// <summary>True when <paramref name="mark"/> is worth a mark: a long enough stand with the engine on.</summary>
        public static bool Leaves(in HeatMark mark)
            => mark.Kind != HeatMarkKind.NoMark && mark.EngineOnSeconds > 0f && mark.StandSeconds >= MinStandSeconds && mark.Length > 0f && mark.Width > 0f;

        /// <summary>The cell of a mark (the crater record's cells).</summary>
        public static (int X, int Y) CellOf(in HeatMark m) => GroundDamages.CellOf(m.At);

        /// <summary>
        /// How long after it is left a mark of <paramref name="power"/> is COLD (§4b: "about two hours of sim time, by PowerClass") — starter values: a car 1 h,
        /// a truck 1.5 h, a tank or an aircraft 2 h. <see cref="Fdp.Toolkit.Perception.Signatures.ThermalDriversOps.MarkWarmth"/> reaches ~0 there.
        /// </summary>
        public static double ColdAfterSeconds(ThermalPowerClass power) => power switch
        {
            ThermalPowerClass.Car => 3600.0,
            ThermalPowerClass.Truck => 5400.0,
            _ => 7200.0,
        };

        /// <summary>True when <paramref name="m"/> is cold at <paramref name="now"/>.</summary>
        public static bool IsCold(in HeatMark m, double now) => now - m.LeftAt.Seconds >= ColdAfterSeconds(m.PowerClass);

        /// <summary>
        /// Adds <paramref name="m"/> to <paramref name="d"/>, first dropping every mark already COLD when <paramref name="m"/> was left (§4b: "dropped by the next write to that
        /// cell" — the new mark's <see cref="HeatMark.LeftAt"/> is the writer's now, so no clock is needed). False when it is already there or <paramref name="d"/> is still full
        /// (<paramref name="full"/> = true: the caller opens another entity).
        /// </summary>
        public static bool Add(ref GroundHeatMarks d, in HeatMark m, out bool full)
        {
            full = false;
            for (int i = 0; i < d.Count; i++) if (d.Get(i) == m) return false;
            double now = m.LeftAt.Seconds;
            int kept = 0;
            for (int i = 0; i < d.Count; i++)
            {
                var old = d.Get(i);
                if (IsCold(in old, now)) continue;
                if (kept != i) d.Set(kept, in old);
                kept++;
            }
            for (int i = kept; i < d.Count; i++) d.Set(i, default);
            d.Count = (byte)kept;
            if (d.Count >= GroundHeatMarks.Capacity) { full = true; return false; }
            d.Set(d.Count, in m);
            d.Count++;
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4664</c> (TD-14) — turns <see cref="GroundHeatMarkEvent"/>s into <see cref="GroundHeatMarks"/> entities: THE cell-record writer
    /// (<see cref="CellRecordSystem{TRecord, TItem}"/>, CE-5000) with the leave-stand event and a mark's cell. Runs where the vehicles move (SimHost's
    /// <c>DamageAssessmentModule</c>, beside the craters). ⛔ Never in a replay; writes only entities this node owns.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class GroundHeatMarkSystem : CellRecordSystem<GroundHeatMarks, HeatMark>
    {
        /// <summary>The wire ordinal of <c>EDescriptorType.dtGroundHeatMarks</c> (NED). ⚠ A rail pins the two equal.</summary>
        public const long DescriptorOrdinal = 242;
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => GroundHeatMarkRules.TkbType;
        protected override (int X, int Y) CellOf(in HeatMark item) => GroundHeatMarkRules.CellOf(in item);

        protected override void ReadItems(EntityRepository repo, List<HeatMark> into)
        {
            if (!repo.Bus.IsRegistered<GroundHeatMarkEvent>()) return;
            var events = repo.ReadEvents<GroundHeatMarkEvent>();
            for (int i = 0; i < events.Length; i++) { var m = events[i].Mark; if (GroundHeatMarkRules.Leaves(in m)) into.Add(m); }
        }

        public static bool OwnsMarks(EntityRepository repo, Entity e) => OwnsRecord(repo, e, OwnershipKey);
    }
}
