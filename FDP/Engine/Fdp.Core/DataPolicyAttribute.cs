using System;

namespace Fdp.Core
{
    /// <summary>
    /// Controls how component data is handled by the FDP engine pipeline.
    /// Separate flags for concurrency safety (snapshots) vs persistence (recording/saving).
    /// </summary>
    [Flags]
    public enum DataPolicy
    {
        /// <summary>
        /// Default behavior:
        /// - Structs/Records: Snapshot + Record + Save  
        /// - Mutable Classes: ERROR (must specify policy)
        /// </summary>
        Default = 0,
        
        // ━━━ ModuleHost (Concurrency Safety) ━━━
        
        /// <summary>
        /// Exclude from background snapshots (GDB/SoD).
        /// Accessing this component in background modules returns null/default.
        /// Safe for mutable classes.
        /// </summary>
        NoPreview = 1 << 0,
        
        /// <summary>
        /// Include in background snapshots via Deep Clone.
        /// Safe for mutable classes but slower than reference copy.
        /// Use when background modules need to read mutable state.
        /// </summary>
        SnapshotViaClone = 1 << 1,
        
        // ━━━ Persistence (Disk/Network) ━━━
        
        /// <summary>
        /// Exclude from Flight Recorder and Binary Checkpoints. Use for debug-only data
        /// or metrics that should not pollute binary state snapshots.
        /// </summary>
        NoReplay = 1 << 2,
        
        /// <summary>
        /// Exclude from Scenario JSON serialization. Use for runtime execution state
        /// (e.g., BTree pointers, active weapon channels) that should be preserved in
        /// binary checkpoints but omitted from declarative authoring templates.
        /// </summary>
        NoScenario = 1 << 3,
        
        // ━━━ Federation (Replay Browser / Review merge) ━━━

        /// <summary>
        /// ⭐ G-7 / CE-4354 (docs/UX/DESIGN_Review_Perspective.md §6.4 I9, Q14) — a component that belongs to ONE NODE'S process and must
        /// never be copied into a merged view of several nodes' recordings: a spatial grid or network map keyed by that node's entity
        /// indices, a per-node cache, a terrain / traffic service, a network hand-shake record. Orthogonal to the other flags (it says
        /// nothing about snapshots, recording or scenarios); the binary merge (<c>BinaryMerger</c>) skips every type that carries it.
        /// ⛔ A flag, not a hand-kept list: a new node-local type declares it where it is defined.
        /// </summary>
        NodeLocal = 1 << 4,

        // ━━━ Convenience Presets ━━━
        
        /// <summary>
        /// Completely transient: excluded from snapshots, recording, and saving.
        /// Replaces [TransientComponent] attribute.
        /// Use for: UI caches, temporary buffers, debug metrics.
        /// </summary>
        Transient = NoPreview | NoReplay | NoScenario
    }
    
    /// <summary>
    /// Attribute to specify component data policy.
    /// </summary>
    /// <example>
    /// <code>
    /// // Mutable class: Record but don't share with background threads
    /// [DataPolicy(DataPolicy.NoPreview)]
    /// public class CombatHistory { /* mutable state */ }
    /// 
    /// // Completely transient
    /// [DataPolicy(DataPolicy.Transient)]
    /// public class UIRenderCache { /* temp data */ }
    /// 
    /// // Shareable via cloning (safe but slow)
    /// [DataPolicy(DataPolicy.SnapshotViaClone)]
    /// public class AIBlackboard { /* mutable AI state */ }
    /// </code>
    /// </example>
    // ⭐ D7 — Interface too: a service singleton is keyed by its interface (ITkbDatabase, INavmeshProvider …), so that is where
    //   its policy is declared.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class DataPolicyAttribute : Attribute
    {
        public DataPolicy Policy { get; }
        
        public DataPolicyAttribute(DataPolicy policy)
        {
            Policy = policy;
        }

        /// <summary>The policy <paramref name="type"/> declares (<see cref="DataPolicy.Default"/> without the attribute).</summary>
        public static DataPolicy Of(Type type)
            => ((DataPolicyAttribute?)GetCustomAttribute(type, typeof(DataPolicyAttribute), inherit: false))?.Policy ?? DataPolicy.Default;

        /// <summary>⭐ G-7 — <paramref name="type"/> declares <see cref="DataPolicy.NodeLocal"/>.</summary>
        public static bool IsNodeLocal(Type type) => Of(type).HasFlag(DataPolicy.NodeLocal);
    }
}
