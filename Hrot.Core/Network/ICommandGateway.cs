namespace Hrot.Core.Network;

/// <summary>
/// Neutral interface for mission-control commands from ExCon/Editor to CGF.
/// Replaces the NED-specific INedCommandGateway from Hrot.Map.Common.
/// </summary>
public interface ICommandGateway : IDisposable
{
    /// <summary>Sends a create-entity request and returns the assigned entity ID.</summary>
    Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default);

    /// <summary>Sends an update-descriptor request.</summary>
    Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default);

    /// <summary>Sends a mission-control request (replace/jump/abort) to the CGF.</summary>
    Task SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default);
}
