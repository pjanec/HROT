using System.Runtime.InteropServices;

namespace Fbt
{
    /// <summary>
    /// Single node in the behavior tree bytecode.
    /// Size: 8 bytes (tightly packed).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NodeDefinition
    {
        /// <summary>Type of this node.</summary>
        public NodeType Type;           // 1 byte
        
        /// <summary>Number of immediate children.</summary>
        public byte ChildCount;         // 1 byte
        
        /// <summary>
        /// Distance to next sibling (in node indices).
        /// Used for skipping entire subtrees.
        /// NextSiblingIndex = CurrentIndex + SubtreeOffset
        /// </summary>
        public ushort SubtreeOffset;    // 2 bytes
        
        /// <summary>
        /// Index into lookup tables:
        /// - For Action/Condition: index into MethodNames[]
        /// - For Wait: index into FloatParams[] (duration)
        /// - For Decorator params: index into IntParams[]
        /// - For Subtree: index into SubtreeAssetIds[]
        /// </summary>
        public int PayloadIndex;        // 4 bytes
        
        // Total: 8 bytes
    }
}
