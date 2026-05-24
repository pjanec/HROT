using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Muscle-side ECS singleton: contiguous native ring-buffer holding packed EQS results.
    /// The solver writes ranked candidates here and emits an <see cref="EqsResultEvent"/> with
    /// a handle into this array; the DDS egress translator reads the handle in the same frame.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EqsResultPool)]
    public struct EqsResultPool
    {
        /// <summary>Maximum number of concurrently in-flight result sets.</summary>
        public const int MaxConcurrentInFlightResults = 1024;
        /// <summary>Maximum Top-K candidates stored per result set.</summary>
        public const int MaxTopK = 16;
        /// <summary>Total pool capacity (MaxConcurrentInFlightResults * MaxTopK).</summary>
        public const int PoolCapacity = MaxConcurrentInFlightResults * MaxTopK;

        /// <summary>Ring-buffer write cursor. Wraps on overflow.</summary>
        public int NextFreeIndex;
        /// <summary>Pre-allocated contiguous block of result entries.</summary>
        public NativeArray<EqsResult> Results;

        /// <summary>
        /// Writes <paramref name="results"/> contiguously into the pool ring buffer, wrapping
        /// to index 0 if there is insufficient space at the current cursor position.
        /// Returns the base index where the batch was written (the result handle).
        /// </summary>
        public int WriteAndWrap(ReadOnlySpan<EqsResult> results)
        {
            int count = results.Length > MaxTopK ? MaxTopK : results.Length;
            int handle = NextFreeIndex;

            // Wrap if the batch would exceed the pool boundary.
            if (handle + count > PoolCapacity)
                handle = 0;

            for (int i = 0; i < count; i++)
                Results[handle + i] = results[i];

            int next = handle + count;
            // Advance cursor; reset to 0 when exactly at capacity so the next write starts fresh.
            NextFreeIndex = next >= PoolCapacity ? 0 : next;
            return handle;
        }
    }

    /// <summary>
    /// Strictly unmanaged ECS event published by the EQS solver when a result set is ready.
    /// Consumed in the same frame by <c>EqsResultEventEgressTranslator</c> to build the DDS payload.
    /// </summary>
    [EventId(2050)]
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsResultEvent
    {
        /// <summary>Network ID of the entity that owns the originating <see cref="EqsSensor"/>.</summary>
        public long SensorNetworkId;
        /// <summary>
        /// Epoch echoed from the sensor at solve time. The Brain discards events whose epoch
        /// does not match the current sensor epoch (staleness guard).
        /// </summary>
        public uint Epoch;
        /// <summary>Simulation tick at which the solver completed this evaluation.</summary>
        public uint RefreshTick;
        /// <summary>Base index into <see cref="EqsResultPool.Results"/> where the batch starts.</summary>
        public int ResultHandle;
        /// <summary>Number of valid candidates stored at <see cref="ResultHandle"/>.</summary>
        public int EntryCount;
    }
}
