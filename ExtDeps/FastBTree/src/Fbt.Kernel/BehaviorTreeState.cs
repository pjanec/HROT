using System.Runtime.InteropServices;

namespace Fbt
{
    /// <summary>
    /// Per-entity behavior tree runtime state.
    /// Size: Exactly 64 bytes (single cache line).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct BehaviorTreeState
    {
        // ===== HEADER (8 bytes) =====
        
        /// <summary>
        /// Index of currently running node (0 = not running/root start).
        /// Using ushort allows 65,535 nodes max (sufficient).
        /// </summary>
        [FieldOffset(0)]
        public ushort RunningNodeIndex;
        
        /// <summary>
        /// Current depth in the execution stack (for subtrees).
        /// </summary>
        [FieldOffset(2)]
        public ushort StackPointer;
        
        /// <summary>
        /// Tree generation/version for async safety.
        /// Incremented on abort or reset.
        /// </summary>
        [FieldOffset(4)]
        public uint TreeVersion;
        
        // ===== EXECUTION STACK (16 bytes) =====
        
        /// <summary>
        /// Stack of node indices (for subtree calls).
        /// Each level stores the running node index at that depth.
        /// Max depth: 8 levels (sufficient for most trees).
        /// </summary>
        [FieldOffset(8)]
        public fixed ushort NodeIndexStack[8];
        
        // ===== LOCAL REGISTERS (16 bytes) =====
        
        /// <summary>
        /// General-purpose storage for node-local state.
        /// [0-3]: Available for nodes to store counters, flags, etc.
        /// Commonly: [0] = loop counter, [1] = auxiliary state
        /// </summary>
        [FieldOffset(24)]
        public fixed int LocalRegisters[4];
        
        // ===== ASYNC HANDLES (24 bytes) =====
        
        /// <summary>
        /// Storage for async operation handles.
        /// Each handle is a packed ulong: (TreeVersion << 32) | RequestID
        /// [0-2]: Three concurrent async operations max
        /// </summary>
        [FieldOffset(40)]
        public fixed ulong AsyncHandles[3];
        
        /// <summary>
        /// Convenience accessor for the first async handle, often used for Wait node timer storage.
        /// </summary>
        public ulong AsyncData
        {
            get => AsyncHandles[0];
            set => AsyncHandles[0] = value;
        }
        
        // Total: 64 bytes exactly
        
        // ===== Helper Properties =====
        
        /// <summary>Get/set current running node at current stack depth.</summary>
        public ushort CurrentRunningNode
        {
            get => NodeIndexStack[StackPointer];
            set => NodeIndexStack[StackPointer] = value;
        }
        
        /// <summary>Reset state to initial values.</summary>
        public void Reset()
        {
            RunningNodeIndex = 0;
            StackPointer = 0;
            TreeVersion++;
            
            // Clear stacks and registers
            for (int i = 0; i < 8; i++)
                NodeIndexStack[i] = 0;
            for (int i = 0; i < 4; i++)
                LocalRegisters[i] = 0;
            for (int i = 0; i < 3; i++)
                AsyncHandles[i] = 0;
        }
        
        /// <summary>Push node index onto stack (entering subtree).</summary>
        public void PushNode(ushort nodeIndex)
        {
            if (StackPointer < 7) // Max depth check
            {
                StackPointer++;
                NodeIndexStack[StackPointer] = nodeIndex;
            }
            // else: Stack overflow - handle gracefully or log error
        }
        
        /// <summary>Pop from stack (exiting subtree).</summary>
        public void PopNode()
        {
            if (StackPointer > 0)
            {
                NodeIndexStack[StackPointer] = 0; // Clear for safety
                StackPointer--;
            }
        }
    }
}
