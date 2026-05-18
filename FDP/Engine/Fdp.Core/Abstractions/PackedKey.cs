namespace Fdp.Interfaces
{
    /// <summary>
    /// Utilities for packing descriptor type and instance ID into a single long.
    /// Layout: [High 32 bits: Ordinal] [Low 32 bits: InstanceId]
    /// </summary>
    public static class PackedKey
    {
        public static long Create(int ordinal, int instanceId)
        {
            return ((long)ordinal << 32) | (uint)instanceId;
        }
        
        public static int GetOrdinal(long packedKey)
        {
            return (int)(packedKey >> 32);
        }
        
        public static int GetInstanceId(long packedKey)
        {
            return (int)(packedKey & 0xFFFFFFFF);
        }
        
        public static string ToString(long packedKey)
        {
            return $"(Ord:{GetOrdinal(packedKey)}, Inst:{GetInstanceId(packedKey)})";
        }
    }
}
