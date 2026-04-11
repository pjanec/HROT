using System;

namespace Fdp.Kernel
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
        NoSnapshot = 1 << 0,
        
        /// <summary>
        /// Include in background snapshots via Deep Clone.
        /// Safe for mutable classes but slower than reference copy.
        /// Use when background modules need to read mutable state.
        /// </summary>
        SnapshotViaClone = 1 << 1,
        
        // ━━━ Persistence (Disk/Network) ━━━
        
        /// <summary>
        /// Exclude from Flight Recorder (.fdp replay files).
        /// Use for debug-only data that shouldn't be in recordings.
        /// </summary>
        NoRecord = 1 << 2,
        
        /// <summary>
        /// Exclude from Save Game / Checkpoints.
        /// Use for runtime-only data that doesn't persist across sessions.
        /// </summary>
        NoSave = 1 << 3,
        
        // ━━━ Convenience Presets ━━━
        
        /// <summary>
        /// Completely transient: excluded from snapshots, recording, and saving.
        /// Replaces [TransientComponent] attribute.
        /// Use for: UI caches, temporary buffers, debug metrics.
        /// </summary>
        Transient = NoSnapshot | NoRecord | NoSave
    }
    
    /// <summary>
    /// Attribute to specify component data policy.
    /// </summary>
    /// <example>
    /// <code>
    /// // Mutable class: Record but don't share with background threads
    /// [DataPolicy(DataPolicy.NoSnapshot)]
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
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class DataPolicyAttribute : Attribute
    {
        public DataPolicy Policy { get; }
        
        public DataPolicyAttribute(DataPolicy policy)
        {
            Policy = policy;
        }
    }
}
