using System;
using System.Numerics;

namespace Hrot.Core.Network;

/// <summary>
/// Protocol-neutral sender for SimHost visualization mission-control commands.
/// Created by <see cref="INetworkFactory"/>; allows <c>SimHostVisualization</c> to
/// dispatch behavior-based navigation missions without referencing NED wire types.
/// </summary>
public interface ISimHostMissionSender : IDisposable
{
    /// <summary>
    /// Sends a "navigate to point" behavior mission for the specified entity.
    /// The underlying implementation constructs the appropriate NED wire message.
    /// </summary>
    /// <param name="entityNetworkId">Network entity ID (from <c>NetworkIdentity</c>).</param>
    /// <param name="destination">2-D Cartesian destination in local simulation space.</param>
    /// <param name="speed">Target movement speed in m/s.</param>
    /// <param name="arrivalRadius">Radius in metres at which the entity considers itself arrived.</param>
    void SendNavigateToPoint(long entityNetworkId, Vector2 destination, float speed, float arrivalRadius);
}
