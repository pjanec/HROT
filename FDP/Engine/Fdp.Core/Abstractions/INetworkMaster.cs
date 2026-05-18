namespace Fdp.Interfaces
{
    /// <summary>
    /// Contract for entity master descriptors.
    /// The master defines entity identity and type.
    /// </summary>
    public interface INetworkMaster
    {
        /// <summary>
        /// Globally unique entity identifier.
        /// </summary>
        long EntityId { get; }
        
        /// <summary>
        /// TKB type identifier for blueprint lookup.
        /// </summary>
        long TkbType { get; }
        
        // NOTE: OwnerId is NOT included - ownership is implicit from DDS writer
    }
}
