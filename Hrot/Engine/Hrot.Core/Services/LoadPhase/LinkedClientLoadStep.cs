using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐ <c>CE-11026</c> (R-11009, <c>DESIGN_Linked_Client_Director.md</c> §2d-1, §2e) — <b>the load waits for the node's linked client</b> to present the world the load committed. The shape of
/// <see cref="MapBuildLoadStep"/>: no work of its own, it asks and then POLLS (<see cref="ILoadPartProvider.IsResolved"/>).
/// <list type="bullet">
///   <item>at the commit (the chain runs it after the terrain step's) it calls <see cref="ILinkedClient.BeginWorld"/> with the load's transaction id — the client then moves to a NEW world, so no poll can read the OLD one as ready;</item>
///   <item>resolved when <see cref="ILinkedClient.IsWorldReady"/> says true; no client asked (none offered at the commit) ⇒ resolved at once, and nobody is polled;</item>
///   <item>a non-null failure ends the wait and FAILS the load with that reason through the chain's one failure path; while waiting the node reports the client's own step on its heartbeat.</item>
/// </list>
/// ⚠ It never asks <c>IsWorldReady</c> before its <see cref="Commit"/> (the race the two calls exist to close). It must come AFTER <see cref="LoadPart.Terrain"/> and <see cref="LoadPart.Map3D"/>: the client loads the world those commit.
/// </summary>
public sealed class LinkedClientLoadStep : ILoadPartProvider
{
    private readonly LinkedClients _clients;
    private ILinkedClient? _asked;     // the client THIS load asked (set at its commit)
    private Guid _loadId;
    private string? _failure, _step;
    private Stopwatch? _waitStarted;

    public LinkedClientLoadStep(LinkedClients clients) => _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    public LoadPart Part => LoadPart.LinkedClient;

    public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct) => Task.CompletedTask;   // nothing to read: the client loads from the world this load commits

    /// <summary>The load's commit (after the terrain step's): the linked client is told a new world begins.</summary>
    public void Commit(LoadPhaseContext context, EntityRepository? world)
    {
        _failure = null;
        _step = null;
        var client = _clients.Current;
        if (client == null)
        {
            _asked = null;
            // ⭐ CE-11026 (the Unreal lane's live run, 2026-11-21): say so, so a log shows WHY a load did not wait for a client
            FdpLog<LinkedClientLoadStep>.Info("[LoadPhase] no linked client is offered on this node: this load does not wait for one.");
            return;
        }
        _loadId = context.TransactionId;
        _asked = client;
        _waitStarted = Stopwatch.StartNew();
        FdpLog<LinkedClientLoadStep>.Info("[LoadPhase] this load ({0}) waits for the linked client '{1}' to present its world.", _loadId, client.Name);
        client.BeginWorld(_loadId);
    }

    public void Abort(LoadPhaseContext context)
    {
        _asked = null;
        _waitStarted = null;
        _failure = null;
        _step = null;
    }

    /// <summary>Asks the client this load asked (never before the commit); keeps its answer. The chain reads <see cref="Failure"/> before <see cref="IsResolved"/> in a pass, so both ask — the call is cheap and idempotent.</summary>
    private bool Refresh(ILinkedClient client) => client.IsWorldReady(_loadId, out _failure, out _step);

    public bool IsResolved(EntityRepository? world)
    {
        var client = _asked;
        if (client == null) return true;   // no client was asked at this load's commit: nothing to wait for
        bool ready = Refresh(client);
        bool done = ready || _failure != null;   // a failure ends the wait (the chain then fails the load with it)
        if (done && _waitStarted is { } waited)
        {
            _waitStarted = null;
            FdpLog<LinkedClientLoadStep>.Info("[LoadPhase] the linked client's world is {0} after {1:F1} s.", _failure is { } f ? "FAILED (" + f + ")" : "ready", waited.Elapsed.TotalSeconds);
        }
        return done;
    }

    public string? Failure
    {
        get
        {
            var client = _asked;
            if (client == null) return null;
            Refresh(client);
            return _failure;
        }
    }

    public string? ProgressStep => _asked != null && !string.IsNullOrEmpty(_step) ? _step : null;
}
