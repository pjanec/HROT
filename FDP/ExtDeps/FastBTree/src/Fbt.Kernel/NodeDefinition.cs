using System.Runtime.CompilerServices;
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
        /// Raw payload storage. Bit 31 = IsResourceOwning flag. Bits 0-30 = payload index.
        /// - For Action/Condition: bits 0-30 index into MethodNames[]
        /// - For Wait: bits 0-30 index into FloatParams[] (duration)
        /// - For Decorator params: bits 0-30 index into IntParams[]
        /// - For Subtree: bits 0-30 index into SubtreeAssetIds[]
        /// </summary>
        public int RawPayloadIndex;     // 4 bytes

        // Total: 8 bytes

        /// <summary>
        /// Payload lookup index (bits 0-30 of RawPayloadIndex).
        /// Identical to the old PayloadIndex field for values that do not set bit 31.
        /// </summary>
        public readonly int PayloadIndex
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RawPayloadIndex & 0x7FFFFFFF;
        }

        /// <summary>
        /// True when bit 31 of RawPayloadIndex is set.
        /// Indicates this Action/Condition node owns standing ECS resources and has a
        /// registered deactivator delegate that must be called on branch exit.
        /// </summary>
        public readonly bool IsResourceOwning
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (RawPayloadIndex & unchecked((int)0x80000000)) != 0;
        }

        /// <summary>Sets bit 31 of RawPayloadIndex without disturbing bits 0-30.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResourceOwning() => RawPayloadIndex |= unchecked((int)0x80000000);
    }
}
