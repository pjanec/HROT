using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>One heard estimate as the map draws it: where it seemed to come from, how uncertain, what it sounded like, when.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HeardTrace
    {
        public double Time;
        public Vector3 At;
        public float Radius;
        /// <summary><c>Signatures.SoundKind</c>.</summary>
        public byte Kind;
        /// <summary><c>Tkb.Domain.SoundSourceClass</c> (coarse, never an identity — R-207).</summary>
        public byte SourceClass;
    }

    [InlineArray(HeardTraces.Capacity)]
    public struct HeardTraceSlots
    {
        private HeardTrace _element;
    }

    /// <summary>
    /// ⭐ <c>CE-3117</c> (R-226) — a listener's last heard estimates, as <c>HearingGizmo</c> draws them. Written where the acoustic
    /// sensor answers (<c>SensorMemoryStage.Flush</c>, the node that solves perception), recorded so a replay seek restores them.
    /// NoScenario; not replicated. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// </summary>
    [ComponentId(GlobalComponentIds.HeardTraces)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct HeardTraces
    {
        public const int Capacity = 4;

        public int Count;
        public int Next;
        public HeardTraceSlots Slots;

        public void Add(in HeardTrace t)
        {
            Span<HeardTrace> s = MemoryMarshal.CreateSpan(ref Unsafe.As<HeardTraceSlots, HeardTrace>(ref Slots), Capacity);
            s[Next] = t;
            Next = (Next + 1) % Capacity;
            Count = Math.Min(Count + 1, Capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ReadOnlySpan<HeardTrace> SlotsRO() =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<HeardTraceSlots, HeardTrace>(ref Unsafe.AsRef(in Slots)), Capacity);
    }
}
