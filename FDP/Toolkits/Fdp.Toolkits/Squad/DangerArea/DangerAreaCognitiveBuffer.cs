using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// Inline array of 8 <see cref="DangerAreaDescriptor"/>s (8 * 72 = 576 bytes).
    /// Always write through <see cref="DangerAreaCognitiveBuffer.GetSpanRW"/> to
    /// avoid the InlineArray defensive-copy trap.
    /// </summary>
    [InlineArray(8)]
    public struct DangerAreaDescriptorArray
    {
#pragma warning disable CS0169
        private DangerAreaDescriptor _element;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Brain-side danger-area result cache written by <c>DangerAreaRefreshSystem</c>
    /// (squad danger-area pipeline, SS5.2).
    /// Total size: 4 (Count) + 4 (LastUpdateTick) + 4 (LastUpdateTimeSeconds) + 8*72 (Slots) + 8 (the edge memory, R-3039) = 596 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DangerAreaCognitiveBuffer)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct DangerAreaCognitiveBuffer
    {
        /// <summary>Number of valid descriptors in <see cref="Slots"/> (0..8).</summary>
        public int Count;

        /// <summary>
        /// ⭐ <c>CE-3072</c> B0 — the answer's stamp, the kind-neutral header every sensor family carries (the ranked
        /// family's is <c>EqsCognitiveBuffer.LastUpdateTick</c>): 0 = no answer yet. ⚠ Took the old 4-byte pad slot, so the
        /// layout is unchanged apart from the descriptor size.
        /// </summary>
        public uint LastUpdateTick;

        /// <summary>
        /// ⭐ <c>CE-3072</c> Q4 (behaviors lane, H6) — the simulation time in seconds the answer was applied, the second
        /// member of the kind-neutral header (the ranked family's is <c>EqsCognitiveBuffer.LastUpdateTimeSeconds</c>).
        /// ⭐ It is what the header trigger <c>BecomesStale</c> reads (<c>time - LastUpdateTimeSeconds</c>), which
        /// <c>SensorKindRegistry.HeaderTriggers</c> gives EVERY family. 0 = no answer yet (cleared on a route change).
        /// </summary>
        public float LastUpdateTimeSeconds;

        /// <summary>Cached danger-area descriptors from the last refresh.</summary>
        public DangerAreaDescriptorArray Slots;

        /// <summary>
        /// ⭐ <c>R-3039</c> S1 — the edge memory (was <c>DangerAreaSensorSystem._last</c>): the area ahead the last rating saw
        /// (<see cref="EdgeFeature"/>), whether it counted as threatened (the hysteresis), and whether there was a last rating at all. Here,
        /// on the sensor, so a checkpoint restored into a fresh process neither re-raises <c>AreaAhead</c> / <c>AreaThreatened</c> nor loses
        /// the <c>AreaCleared</c> it owes. ⚠ A route change clears the answer, never this (an edge across the change is still an edge).
        /// 📄 docs/DESIGN_System_State_Inventory.md §6 #10.
        /// </summary>
        public uint EdgeFeature;
        public byte EdgeThreatened;
        public byte EdgeRated;

        /// <summary>True once an answer arrived — ⭐ <c>CE-3072</c> B0: even an answer with NO areas (a clear route is an
        /// answer). It used to be <c>Count &gt; 0</c>, which read "no danger" as "not ready".</summary>
        public bool IsReady => LastUpdateTick != 0;

        /// <summary>Write-through span over Slots (defeats InlineArray defensive copy).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<DangerAreaDescriptor> GetSpanRW()
            => MemoryMarshal.CreateSpan(
                   ref Unsafe.As<DangerAreaDescriptorArray, DangerAreaDescriptor>(ref Slots), 8);

        /// <summary>Read-only span over Slots.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<DangerAreaDescriptor> GetSpanRO()
            => MemoryMarshal.CreateReadOnlySpan(
                   ref Unsafe.As<DangerAreaDescriptorArray, DangerAreaDescriptor>(ref Slots), 8);

        /// <summary>
        /// ⭐ <c>CE-2224</c> — an area that is still there keeps its handles: for every entry of <paramref name="fresh"/> whose
        /// <see cref="DangerAreaDescriptor.FeatureId"/> is also in <paramref name="previous"/>, the previous near / far handles are kept.
        /// The classifier samples the route from the unit's CURRENT position in fixed steps, so a refresh moves an unchanged area's
        /// handles by up to a step; a consumer that re-issues its move whenever the target changes (the blueprint
        /// <c>DangerCrossingBp</c>) then restarts its path every refresh and crawls. The id is the area's stable identity (its
        /// summary says so), so the handles are too. ⚠ A route change clears the answer (a new epoch), so a stale handle never
        /// survives a new route. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md (H7 ②).
        /// </summary>
        public static void KeepHandles(ReadOnlySpan<DangerAreaDescriptor> previous, Span<DangerAreaDescriptor> fresh)
        {
            for (int i = 0; i < fresh.Length; i++)
                for (int j = 0; j < previous.Length; j++)
                    if (previous[j].FeatureId == fresh[i].FeatureId && previous[j].Kind == fresh[i].Kind)
                    {
                        fresh[i].NearSideHandle = previous[j].NearSideHandle;
                        fresh[i].FarSideHandle = previous[j].FarSideHandle;
                        break;
                    }
        }
    }
}
