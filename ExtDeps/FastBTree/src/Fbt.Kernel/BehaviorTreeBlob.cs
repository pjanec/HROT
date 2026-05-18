using System;

namespace Fbt
{
    /// <summary>
    /// Compiled behavior tree asset (immutable, shared across entities).
    /// </summary>
    [Serializable]
    public class BehaviorTreeBlob
    {
        // ===== Metadata =====
        
        /// <summary>Name of this tree (e.g., "OrcCombat").</summary>
        /// <summary>Name of this tree (e.g., "OrcCombat").</summary>
        public string TreeName = string.Empty;
        
        /// <summary>Version number for compatibility checking.</summary>
        public int Version = 1;
        
        /// <summary>
        /// Hash of node structure (types + hierarchy).
        /// Used for hot reload detection.
        /// </summary>
        public int StructureHash;
        
        /// <summary>
        /// Hash of parameters (floats, ints).
        /// Used for soft reload (parameter-only changes).
        /// </summary>
        public int ParamHash;
        
        // ===== Core Data =====
        
        /// <summary>
        /// The bytecode: flat array of nodes (depth-first order).
        /// </summary>
        public NodeDefinition[] Nodes = Array.Empty<NodeDefinition>();
        
        // ===== Lookup Tables =====
        
        /// <summary>
        /// Method names for Action/Condition nodes.
        /// PayloadIndex in NodeDefinition indexes into this.
        /// Example: ["Attack", "Patrol", "HasTarget"]
        /// </summary>
        public string[] MethodNames = Array.Empty<string>();
        
        /// <summary>
        /// Float parameters (e.g., Wait durations, ranges).
        /// Example: [2.0f, 5.0f, 10.0f]
        /// </summary>
        public float[] FloatParams = Array.Empty<float>();
        
        /// <summary>
        /// Integer parameters (e.g., repeat counts, thresholds).
        /// Example: [3, 10, 100]
        /// </summary>
        public int[] IntParams = Array.Empty<int>();
        
        /// <summary>
        /// Asset IDs for subtree references (if using runtime linking).
        /// Example: ["Patrol", "CombatTactics"]
        /// </summary>
        public string[] SubtreeAssetIds = Array.Empty<string>();
        
        // ===== Compiled Delegates (Optional) =====
        
        /// <summary>
        /// JIT-compiled delegate (if using JIT mode).
        /// Null in interpreter mode.
        /// </summary>
        [NonSerialized]
        public object? CompiledDelegate; // Typed as object to avoid generic in blob

        /// <summary>
        /// Per-node debug metadata (managed, not serialized). Null for blobs compiled from JSON.
        /// When non-null, length equals Nodes.Length.
        /// </summary>
        [NonSerialized]
        public NodeDebugMetadata[]? DebugMetadata;
    }
}
