using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager that maintains the active episode set and publishes
/// <see cref="EpisodeStateChangedEvent"/> after every mutation.
/// Reacts to <see cref="ClusterOpCompletedEvent"/> carrying <see cref="EpisodeConsensusPayload"/>.
/// </summary>
public sealed class EpisodeProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly HashSet<Guid> _activeEpisodes = new();

    public EpisodeProcessManager(FdpEventBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <summary>
    /// Checks for <see cref="ClusterOpCompletedEvent"/> with <see cref="EpisodeConsensusPayload"/>
    /// and updates the internal episode set, then publishes <see cref="EpisodeStateChangedEvent"/>.
    /// Call once per frame after <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.StatusCode.IsError()) continue;
            if (ev.ResultPayload is not EpisodeConsensusPayload payload) continue;

            if (payload.IsStart)
                _activeEpisodes.Add(payload.EpisodeId);
            else
                _activeEpisodes.Remove(payload.EpisodeId);

            _bus.PublishManaged(new EpisodeStateChangedEvent
            {
                ActiveEpisodeIds = new HashSet<Guid>(_activeEpisodes),
            });
        }
    }
}
