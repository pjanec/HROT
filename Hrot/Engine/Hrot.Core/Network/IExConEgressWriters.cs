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

    /// <summary>
    /// Pushes a context actions update to the IG for the given map group.
    /// </summary>
    /// <param name="mapGroupId">Target map group (0 = broadcast).</param>
    /// <param name="forSelection">
    /// Entity IDs the menu applies to (empty or null for map-canvas right-click).
    /// </param>
    /// <param name="actionsJson">JSON-encoded context actions payload.</param>
    void PushContextActions(int mapGroupId, System.Collections.Generic.IReadOnlyList<int>? forSelection, string actionsJson);
}
