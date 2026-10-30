using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.ClusterRunner.Configuration;
using Hrot.Presentation.DebugApi;

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐⭐ <c>CE-3354</c> ⑥ (<c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.10b) — <b>the command line as a second SENDER of the same two messages</b>, never a second load path: once, when the process' master is up, it publishes
/// the cluster's map-in-load setting (<see cref="SetRunSettingsIntent"/>, when <c>--map-in-load</c> was given) and THEN the scenario load — the intent <see cref="ScenarioLoadSequence.LiveLoadIntent"/> /
/// <see cref="ScenarioLoadSequence.EditLoadIntent"/> builds for HTTP too — through a <see cref="ScenarioLoadSequence"/> (from <c>Idle</c> it goes out at once; from any other state the unload goes first and the load waits).
/// ⛔ No timer: the master already HOLDS a scenario load until its required nodes joined (CE-4087). The host pumps <see cref="Tick"/> every frame until <see cref="Done"/>.
/// </summary>
public sealed class StartupScenarioLoad
{
    /// <summary>What the command line asked for. <see cref="Scenario"/> null = only the setting.</summary>
    public sealed record Plan(string? Scenario, bool Live, bool Run, IReadOnlyList<string>? MapInLoadNodes)
    {
        /// <summary>The plan the validated <paramref name="config"/> describes, or null when it asks for nothing at startup. <paramref name="ownNodeNames"/> are this process' node names (what an empty <c>--map-in-load</c> means).</summary>
        public static Plan? From(HrotRunnerConfiguration config, IReadOnlyList<string> ownNodeNames)
        {
            bool load = config.HasStartupLoad;
            var map = config.MapInLoad?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
            if (config.MapInLoad != null && (map == null || map.Count == 0)) map = ownNodeNames.ToList();   // given with no name = this process' own nodes
            if (!load && map == null) return null;
            return new Plan(load ? config.ScenarioName.Trim() : null, load && config.StartupLoadsLive, load && config.Play, map);
        }
    }

    /// <summary>
    /// ⭐ The runner's one construction: the plan the validated <paramref name="config"/> asks for, bound to the subsystem that hosts the master (<see cref="IHostsClusterMaster"/>), or <see langword="null"/> when nothing is asked.
    /// <paramref name="nodeNameOf"/> names a subsystem's node (what an empty <c>--map-in-load</c> means for this process); the editor reports its own.
    /// </summary>
    public static StartupScenarioLoad? For(HrotRunnerConfiguration config, IEnumerable<Fdp.Toolkit.Runner.ISubsystem> subsystems, Fdp.Toolkit.Runner.ISubsystem? notANode, Func<string, string?> nodeNameOf)
    {
        var list = subsystems.ToList();
        var own = list.Where(s => !ReferenceEquals(s, notANode) && !s.Name.Equals("ReplayBrowser", StringComparison.OrdinalIgnoreCase))
                      .Select(s => s is Hrot.Editor.EditorSubsystem editor ? editor.NodeName : nodeNameOf(s.Name))
                      .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var plan = Plan.From(config, own);
        if (plan is null) return null;
        var host = list.OfType<IHostsClusterMaster>().FirstOrDefault()
                   ?? throw new InvalidOperationException("--scenario / --map-in-load need the cluster master, and no subsystem of this process hosts it.");
        return new StartupScenarioLoad(plan, () => host.ClusterMasterControl, () => host.ClusterStateNow);
    }

    private readonly Plan _plan;
    private readonly Func<IClusterMasterControl?> _master;
    private readonly Func<ClusterState?> _state;
    private ScenarioLoadSequence? _sequence;
    private bool _issued;

    /// <param name="master">The way to publish master intents; null while the host is not initialised.</param>
    /// <param name="state">The cluster state as the host sees it; null while unknown.</param>
    public StartupScenarioLoad(Plan plan, Func<IClusterMasterControl?> master, Func<ClusterState?> state)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>The plan has been sent: the setting went out and the load (if any) is no longer waiting for Idle.</summary>
    public bool Done => _issued && (_sequence is null || !_sequence.IsWaitingForIdle);

    /// <summary>The request id of the load intent, once sent (for a caller that follows its outcome); <see cref="Guid.Empty"/> before.</summary>
    public Guid LoadRequestId { get; private set; }

    /// <summary>Call every frame after startup; does nothing once <see cref="Done"/>.</summary>
    public void Tick()
    {
        if (Done) return;
        if (!_issued)
        {
            var master = _master();
            if (master is null || _state() is null) return;   // the host (and its master) is not up yet
            if (_plan.MapInLoadNodes is { } names)
                master.SetRunSettings(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), MapInLoadNodes = names.ToArray() });   // BEFORE the load: the load's per-node stamp reads it
            if (_plan.Scenario is { } scenario)
            {
                var intent = _plan.Live
                    ? ScenarioLoadSequence.LiveLoadIntent(scenario, startPaused: !_plan.Run)
                    : ScenarioLoadSequence.EditLoadIntent(scenario);
                LoadRequestId = intent.TransactionId;
                _sequence = new ScenarioLoadSequence(master.Transition, () => _state() ?? ClusterState.Idle);
                _sequence.Request(intent);
            }
            _issued = true;
            return;
        }
        _sequence?.Pump();
    }
}
