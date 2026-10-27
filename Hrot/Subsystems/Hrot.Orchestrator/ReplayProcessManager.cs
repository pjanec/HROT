using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Time.Domain;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// The replay's prepare AGGREGATORS: <see cref="ReplayConsensusAggregator"/> (a replay's prepare round) and its review variant, which make
/// the round's <see cref="ClusterOpCompletedEvent.ResultPayload"/> a typed <see cref="ReplayPrepareResult"/>.
/// <para>⛔ SUPERSEDED <c>2026-10-27</c> (CE-3329 row 9): this manager also paused the master clock at the replay's end — that moved into
/// <see cref="ReplayClockProcessManager"/>, the ONE owner of the replay clock (loop, sim-axis jumps, per-segment rate, the end).</para>
/// </summary>
public sealed class ReplayProcessManager
{
    private readonly FdpEventBus     _bus;
    private readonly ITimeController _timeController;

    /// <param name="bus">Shared event bus.</param>
    /// <param name="timeController">
    /// Active master time controller used to read the current simulation time.
    /// </param>
    public ReplayProcessManager(FdpEventBus bus, ITimeController timeController)
    {
        _bus            = bus            ?? throw new ArgumentNullException(nameof(bus));
        _timeController = timeController ?? throw new ArgumentNullException(nameof(timeController));
    }

    /// <summary>
    /// Returns the <see cref="ReplayConsensusAggregator"/> that must be registered with
    /// <see cref="ClusterMaster.RegisterAggregator"/> so that replay durations flow through
    /// the 2PC pipeline as a typed <see cref="ReplayPrepareResult"/>.
    /// </summary>
    public INodeResponseAggregator CreateAggregator() => new ReplayConsensusAggregator();

    /// <summary>⭐ CE-3329 P3 — a review's prepare round: the same aggregate, ending at the live point (F13).</summary>
    public INodeResponseAggregator CreateReviewAggregator() => new ReplayConsensusAggregator(NodeOpType.PrepareReview, endAtEarliestLast: true);

}
