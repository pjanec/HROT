namespace Hrot.Core.Network;

/// <summary>
/// Aggregate of neutral write operations for ExCon-originated entity lifecycle commands.
/// Replaces individually injected IDdsWriter&lt;NedWireType&gt; fields in ExConLogic.
/// </summary>
public interface IExConEgressWriters : IDisposable
{
    /// <summary>Publishes a map interaction configuration update.</summary>
    void WriteMapConfig(MapConfigDto config);

    /// <summary>Publishes a delete-entity command.</summary>
    void WriteDeleteEntity(int entityId);

    /// <summary>Publishes a create-entity command.</summary>
    void WriteCreateEntity(CreateEntityCommand cmd);

    /// <summary>Publishes a generic map command request.</summary>
    void WriteMapCommand(MapCommandDto cmd);
}
