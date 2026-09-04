using System;
namespace Fdp.Core
{
    /// <summary>
    /// Global configuration constants for the Fast Data Plane engine.
    /// These are compile-time constants for maximum JIT optimization.
    /// </summary>
    public static class FdpConfig
    {
        /// <summary>
        /// Maximum number of entities supported by the engine.
        /// Default: 1,000,000 entities
        /// </summary>
        public const int MAX_ENTITIES = 1_000_000;
        
        /// <summary>
        /// Size of each memory chunk in bytes.
        /// MUST be 64KB (65536) for Windows VirtualAlloc alignment.
        /// </summary>
        public const int CHUNK_SIZE_BYTES = 64 * 1024; // 64KB

        /// <summary>
        /// Range of IDs reserved for system entities that should not be recorded.
        /// App can set minimal for the recorder to avoid recording them.
        /// Entities in this range (0 to SYSTEM_ID_RANGE) should be .
        /// </summary>
        // must be set to max capacity of a chunk - recorder writes whole chunks;
        //   to avoid overwriting the entity table of next chunk
        // chunk capacity depend on component size, using max byte size is safe
        // (smallest component size is 1 byte)
		public const int SYSTEM_ID_RANGE = CHUNK_SIZE_BYTES;
        
        /// <summary>
        /// Maximum number of component types supported.
        /// Limited by `BitMask512` capacity.
        /// </summary>
        public const int MAX_COMPONENT_TYPES = 512;
        
        /// <summary>
        /// Flight Recorder format version.
        /// Increment this whenever:
        /// - Any Tier 1 component struct layout changes
        /// - The .fdp file format structure is modified
        /// - Event serialization format changes
        /// Recordings are NOT backwards compatible - version must match exactly.
        ///
        /// v6 (3D Cognitive Spatial Awareness promotion, P3D-405): engine-wide break.
        /// SimTransform.Position.Z is now authoritative altitude (was force-zeroed); EqsResult grew
        /// PositionZ (24→32 B); TargetMemory gained PositionsZ; NavigationIntent/NavState/MoveToParams
        /// destinations and TrajectoryWaypoint.Position widened to Vector3. Pre-v6 recordings are
        /// rejected fast by PlaybackController/PlaybackSystem (version-mismatch), not mis-deserialized.
        /// </summary>
        public const uint FORMAT_VERSION = 6;
        
        /// <summary>
        /// Calculate chunk capacity for a given element size.
        /// This is a compile-time constant when T is known.
        /// </summary>
        public static int GetChunkCapacity<T>() where T : unmanaged
        {
            int elementSize = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            
            if (elementSize > CHUNK_SIZE_BYTES)
            {
                throw new System.InvalidOperationException(
                    $"Type {typeof(T).Name} ({elementSize} bytes) exceeds chunk size ({CHUNK_SIZE_BYTES} bytes). " +
                    $"Consider using multi-part descriptors or managed components.");
            }
            
            return CHUNK_SIZE_BYTES / elementSize;
        }
        
        /// <summary>
        /// Calculate the number of chunks needed for MAX_ENTITIES of type T.
        /// </summary>
        public static int GetRequiredChunks<T>() where T : unmanaged
        {
            int capacity = GetChunkCapacity<T>();
            return (MAX_ENTITIES + capacity - 1) / capacity; // Ceiling division
        }
        
        /// <summary>
        /// When <c>true</c>, all ECS component structs MUST carry an explicit
        /// <see cref="ComponentIdAttribute"/>.  Registration of any struct without the
        /// attribute throws <see cref="System.InvalidOperationException"/>.
        ///
        /// <para>
        /// Default: <c>false</c> — backward-compatible mode where structs without
        /// the attribute fall back to sequential auto-assignment (legacy behaviour).
        /// </para>
        ///
        /// <para>
        /// Set to <c>true</c> in production entry-points (SimHost, IG, ExCon <c>Program.cs</c>)
        /// before constructing any ECS world to guarantee deterministic IDs across the merged
        /// Runner process.  Leave <c>false</c> in test code during the transition period.
        /// </para>
        /// </summary>
        public static bool EnforceExplicitComponentIds { get; set; } = false;
        
        /// <summary>
        /// When true, FdpEventBus will throw an InvalidOperationException if an event is
        /// published before its stream has been explicitly registered.
        /// Set to true in production entry-points to guarantee all events are known to the schema.
        /// </summary>
        public static bool EnforceExplicitEventRegistration { get; set; } = false;

        /// <summary>
        /// When <c>true</c>, the module host RETHROWS any exception a module or system raises during a
        /// frame instead of catching and reporting it. The node dies at the first fault, with the
        /// original stack.
        /// </summary>
        /// <remarks>
        /// <para><b>Why this exists.</b> The kernel catches per-module exceptions so one faulty module
        /// cannot take down a distributed simulation — correct for production, and exactly wrong while
        /// debugging: a system that throws on its first frame keeps "running" forever, every system
        /// after it in the same phase group is skipped, and the node still answers healthy. Measured:
        /// <c>StatelessGizmoSystem</c> threw on every frame of every editor run and nothing failed
        /// (<c>CE-188</c>).</para>
        ///
        /// <para><b>Default <c>false</c>, and settable without a rebuild:</b> the environment variable
        /// <c>FDP_FAIL_FAST=1</c> turns it on at process start. Set the property directly in a test or a
        /// debug entry point.</para>
        ///
        /// <para>⚠ <b>This is not a substitute for the fault being visible.</b> Fail-fast only helps when
        /// you are watching. The kernel therefore also records every fault against the module's circuit
        /// breaker and reports repeats as a COUNT rather than a line each — a fault printed ten thousand
        /// times is hidden just as effectively as one that was never printed.</para>
        /// </remarks>
        public static bool FailFastOnModuleException { get; set; } =
            Environment.GetEnvironmentVariable("FDP_FAIL_FAST") is "1" or "true" or "TRUE";

        /// <summary>
        /// Global switch to control CPU usage for parallel operations.
        /// -1 = Use all cores (Environment.ProcessorCount)
        ///  1 = Single threaded
        ///  N = Specific number of threads
        /// </summary>
        public static int MaxDegreeOfParallelism { get; set; } = -1;
        
        /// <summary>
        /// Helper to get ParallelOptions configured with MaxDegreeOfParallelism.
        /// </summary>
        internal static System.Threading.Tasks.ParallelOptions ParallelOptions => 
            new System.Threading.Tasks.ParallelOptions 
            { 
                MaxDegreeOfParallelism = MaxDegreeOfParallelism 
            };
    }
    
    /// <summary>
    /// Hints for the parallel partitioner regarding the workload "heaviness".
    /// Used to balance overhead vs. granularity for optimal CPU utilization.
    /// </summary>
    public enum ParallelHint
    {
        /// <summary>
        /// (Default) Simple math operations (e.g., Position += Velocity). 
        /// Large batches (1024+), low overhead.
        /// Best for systems with minimal per-entity computation.
        /// </summary>
        Light, 

        /// <summary>
        /// Moderate logic (e.g., Collision checks, simple state machines).
        /// Medium batches (256-512).
        /// Balances overhead with load distribution.
        /// </summary>
        Medium,

        /// <summary>
        /// Expensive logic (e.g., Pathfinding, raycasting, complex AI).
        /// Small batches (32-64) to maximize load balancing.
        /// Ensures all cores stay busy even with variable workloads.
        /// </summary>
        Heavy,

        /// <summary>
        /// Extremely expensive operations (e.g., File I/O, heavy procedural generation).
        /// Tiny batches (1-16) to ensure every core contributes.
        /// Maximum granularity for maximum parallelism.
        /// </summary>
        VeryHeavy
    }
}
