namespace Fdp.Interfaces
{
    /// <summary>
    /// Marker interface for transient network event translators.
    /// Event translators do not manage persistent entity state and have no
    /// DescriptorOrdinal, TargetComponentIds, ApplyToEntity, or Dispose contract.
    /// </summary>
    public interface INetworkEventTranslator : INetworkTranslator
    {
    }
}
