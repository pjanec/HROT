using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat
{
    // ⭐⭐ CE-3117 (R-226) — what the map's combat layers DRAW, kept as recorded ECS state: the flight recorder keeps these
    //   singletons, so a replay SEEK restores exactly what the map showed at that moment (a ring, stamped with sim time).
    //   The text logs behind the routes (ShotLog, DetonationLog) stay the producers; CombatTraceSystem mirrors them in here.
    //   NoScenario: recorded and snapshotted, never saved into a scenario. No network translator: they exist only where the
    //   combat systems run. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.

    /// <summary>One terrain crossing of a traced shot: where, and whether the round got through.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShotTraceCrossing
    {
        public Vector3 At;
        public byte Passed;
    }

    /// <summary>The crossings of one traced shot.</summary>
    [InlineArray(ShotTraces.CrossingsPerShot)]
    public struct ShotTraceCrossings
    {
        private ShotTraceCrossing _element;
    }

    /// <summary>One traced shot: the muzzle, where it ended, how, and the first terrain crossings.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShotTrace
    {
        /// <summary>The <see cref="ShotRecord.Seq"/> this slot mirrors.</summary>
        public long Seq;
        /// <summary>Sim time it was mirrored (one frame after the shot at most).</summary>
        public double Time;
        public Vector3 Muzzle;
        /// <summary>Where it ended, or where it was aimed while it flies (<see cref="HasEnd"/> = 0).</summary>
        public Vector3 End;
        public ShotOutcome Outcome;
        public byte HasEnd;
        public byte CrossingCount;
        public ShotTraceCrossings Crossings;
    }

    /// <summary>The slots of <see cref="ShotTraces"/>.</summary>
    [InlineArray(ShotTraces.Capacity)]
    public struct ShotTraceSlots
    {
        private ShotTrace _element;
    }

    /// <summary>⭐ <c>CE-3117</c> — the last <see cref="Capacity"/> shots as <c>FireTraceGizmo</c> draws them (a world singleton).</summary>
    [ComponentId(GlobalComponentIds.ShotTraces)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ShotTraces
    {
        public const int Capacity = 64;
        public const int CrossingsPerShot = 4;

        /// <summary>Valid slots (≤ <see cref="Capacity"/>).</summary>
        public int Count;
        /// <summary>The slot the next shot goes into.</summary>
        public int Next;
        /// <summary>The next <see cref="ShotLog"/> sequence number to mirror.</summary>
        public long NextSeq;
        public ShotTraceSlots Slots;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<ShotTrace> SlotsRW() => MemoryMarshal.CreateSpan(ref Unsafe.As<ShotTraceSlots, ShotTrace>(ref Slots), Capacity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ReadOnlySpan<ShotTrace> SlotsRO() =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<ShotTraceSlots, ShotTrace>(ref Unsafe.AsRef(in Slots)), Capacity);
    }

    /// <summary>One fragment ray of a traced burst: the body point it aimed at, how much got through, and where the first thing in
    /// its way stood (when something did).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DetonationRay
    {
        public Vector3 To;
        /// <summary>The first obstacle on the ray; meaningful only when <see cref="HasStop"/> = 1.</summary>
        public Vector3 StopAt;
        /// <summary>The fragment transmission along the ray, 0..1 (1 = a clear line).</summary>
        public float Transmission;
        public byte HasStop;
    }

    /// <summary>One entity a traced burst reached: where it stood, its fragment exposure and the damage it took.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DetonationTarget
    {
        public Vector3 At;
        public float Exposure;
        public float Damage;
    }

    [InlineArray(DetonationTraces.RaysPerBurst)]
    public struct DetonationRays
    {
        private DetonationRay _element;
    }

    [InlineArray(DetonationTraces.TargetsPerBurst)]
    public struct DetonationTargets
    {
        private DetonationTarget _element;
    }

    /// <summary>One traced burst: where, when, its two radii, the entities it reached and every fragment ray.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DetonationTrace
    {
        public long Seq;
        public double Time;
        public Vector3 Burst;
        public float FragmentRadius;
        public float BlastInjuryRadius;
        public int TargetCount;
        public int RayCount;
        public DetonationTargets Targets;
        public DetonationRays Rays;

        public readonly ReadOnlySpan<DetonationTarget> TargetsRO() =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<DetonationTargets, DetonationTarget>(ref Unsafe.AsRef(in Targets)),
                Math.Min(TargetCount, DetonationTraces.TargetsPerBurst));

        public readonly ReadOnlySpan<DetonationRay> RaysRO() =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<DetonationRays, DetonationRay>(ref Unsafe.AsRef(in Rays)),
                Math.Min(RayCount, DetonationTraces.RaysPerBurst));
    }

    [InlineArray(DetonationTraces.Capacity)]
    public struct DetonationTraceSlots
    {
        private DetonationTrace _element;
    }

    /// <summary>⭐ <c>CE-3117</c> — the last <see cref="Capacity"/> bursts as <c>DetonationGizmo</c> draws them (a world singleton).</summary>
    [ComponentId(GlobalComponentIds.DetonationTraces)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DetonationTraces
    {
        public const int Capacity = 8;
        public const int TargetsPerBurst = 16;
        public const int RaysPerBurst = 48;

        public int Count;
        public int Next;
        /// <summary>The next <see cref="DetonationLog"/> sequence number to mirror.</summary>
        public long NextSeq;
        public DetonationTraceSlots Slots;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<DetonationTrace> SlotsRW() =>
            MemoryMarshal.CreateSpan(ref Unsafe.As<DetonationTraceSlots, DetonationTrace>(ref Slots), Capacity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ReadOnlySpan<DetonationTrace> SlotsRO() =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<DetonationTraceSlots, DetonationTrace>(ref Unsafe.AsRef(in Slots)), Capacity);
    }
}
