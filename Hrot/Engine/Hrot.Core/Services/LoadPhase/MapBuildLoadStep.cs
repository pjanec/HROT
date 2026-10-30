using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐⭐ <c>CE-4078</c> / <c>CE-1446</c> (R-1046, <c>DESIGN_Cluster_Load_Phase.md</c> §11.9) — <b>the load waits for the node's 3-D map</b>, for the node whose PROFILE says so (the Player). A step with no
/// work of its own: the map builds on a worker (ui's <c>Map3DBuild</c>), and this step only POLLS its <see cref="IMapBuildStatus"/> — the chain's one shape (<see cref="ILoadPartProvider.IsResolved"/>).
/// <list type="bullet">
///   <item>resolved when the status says <c>IsBuilt</c>; a <see langword="null"/> status (a headless node, no window, nothing to build) counts as resolved;</item>
///   <item>a non-null <c>Failure</c> FAILS the load with that reason — through the chain's one failure path (the hold faults ⇒ the slave's <c>FailPrepare</c> ⇒ Failed phase, round to Idle);</item>
///   <item>while waiting, the node reports the status' own <c>Step</c> (<i>"3-D map: terrain"</i>) on its heartbeat, not just "Map3D".</item>
/// </list>
/// ⚠ <c>IsBuilt</c> is true while no terrain is committed, so the step must come AFTER <see cref="LoadPart.Terrain"/> — <c>LoadPhaseChain.FromRoles</c> appends profile parts after the role parts.
/// </summary>
public sealed class MapBuildLoadStep : ILoadPartProvider
{
    private readonly Func<IMapBuildStatus?> _status;
    private readonly bool _alwaysRequired;
    private volatile bool _required;   // ⭐ CE-3354 ③ — THIS load waits for the map (set by its prepare)

    /// <param name="alwaysRequired">⭐ CE-3354 ③ — the node's PROFILE waits for its map in every load (the Player); otherwise a load waits only when its payload says <see cref="LoadPhaseContext.MapInLoad"/>
    /// (the master's map-in-load set names this node). A load that does not wait resolves at once and never starts a build.</param>
    public MapBuildLoadStep(Func<IMapBuildStatus?> status, bool alwaysRequired = false)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _alwaysRequired = alwaysRequired;
        _required = alwaysRequired;
    }

    public LoadPart Part => LoadPart.Map3D;

    public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)   // nothing to read: the map builds from the terrain this load commits
    {
        _required = _alwaysRequired || context.MapInLoad;
        return Task.CompletedTask;
    }

    /// <summary>⭐ CE-3354 ③ — at the commit (the chain runs it after the terrain step's), a load that waits for the map ASKS for it: the build starts for the terrain just committed.</summary>
    public void Commit(LoadPhaseContext context, EntityRepository? world)
    {
        if (_required) _status()?.Request3D();
    }

    public void Abort(LoadPhaseContext context) => _required = false;

    public bool IsResolved(EntityRepository? world)
    {
        if (!_required) return true;   // ⭐ CE-3354 ③ — this load does not wait for the map
        var s = _status();
        return s == null || s.Failure != null || s.IsBuilt;   // a failure ends the wait (the chain then fails the load with it)
    }

    public string? Failure => _required ? _status()?.Failure : null;

    public string? ProgressStep
    {
        get
        {
            var s = _required ? _status() : null;
            return s == null || s.IsBuilt || string.IsNullOrEmpty(s.Step) ? null : s.Step;
        }
    }
}
