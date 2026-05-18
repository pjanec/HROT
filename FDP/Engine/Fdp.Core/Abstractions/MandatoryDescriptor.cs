namespace Fdp.Interfaces
{
    /// <summary>
    /// Defines a requirement for an entity type's construction.
    /// Hard requirements MUST be met; soft requirements have a timeout.
    /// </summary>
    public struct MandatoryDescriptor
    {
        /// <summary>
        /// Packed key: (DescriptorOrdinal << 32) | InstanceId
        /// </summary>
        public long PackedKey;
        
        /// <summary>
        /// If true, entity cannot be promoted without this descriptor.
        /// If false, promotion proceeds after SoftTimeoutFrames.
        /// </summary>
        public bool IsHard;
        
        /// <summary>
        /// For soft requirements: frames to wait before giving up.
        /// Ignored for hard requirements.
        /// </summary>
        public uint SoftTimeoutFrames;
        
        public override string ToString()
        {
            return $"{Fdp.Interfaces.PackedKey.ToString(PackedKey)} ({(IsHard ? "Hard" : $"Soft:{SoftTimeoutFrames}f")})";
        }
    }
}
