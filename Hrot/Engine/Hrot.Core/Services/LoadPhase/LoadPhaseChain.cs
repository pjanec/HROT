using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐⭐ <b>THE ONE participant in a node's load step.</b> It claims <c>PrepareLive</c> /
/// <c>PrepareEdit</c> and the <c>PrepareState</c> that ends the load, runs every part its ROLES require
/// in order, and acknowledges the cluster <b>once</b>.
///
/// <para>🔴 <b>Why this type exists, measured <c>2026-09-18</c>.</b> <c>ClusterSlave</c> gives a step to
/// the FIRST handler whose <c>CanHandle</c> matches and then returns
/// (<c>ClusterSlave.cs:404-448</c>) — one step, one participant, one acknowledgement. Registering several
/// prerequisite loaders as handlers therefore makes them <b>compete</b>, and the losers vanish silently:
/// on CGF the terrain loader cancelled the scenario loader and the cluster loaded <b>zero entities</b>;
/// on SimHost the knowledge-base loader cancelled the terrain loader, so terrain had <b>never</b> loaded
/// there. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §2.2.</para>
///
/// <para>⛔ <b>The fix is NOT to reorder the list</b> — with one winner there is no order that lets three
/// prerequisites run. ⭐ Nor to make the dispatcher run every match: several handlers depend on the
/// exclusivity (<c>ReferenceLiveLoadHandler</c> claims a cold <c>PrepareLive</c> <i>only if</i> a scenario
/// handler did not, and running both would prepare the recording twice).</para>
///
/// <para>⭐⭐ <b>It is composed from <c>roles × providers</c></b>
/// (<see cref="FromRoles(NodeRole,IEnumerable{ILoadPartProvider},EntityRepository,IRecordReplayController,string)"/>),
/// never from a hand-written registration order, so <i>"this host forgot to register a step"</i> cannot be
/// expressed — a required part with no provider throws at startup instead of being silently absent.</para>
/// </summary>
public sealed class LoadPhaseChain : ITickableClusterStateHandler
{
    private readonly IReadOnlyList<ILoadPartProvider> _steps;
    private readonly EntityRepository? _world;
    private readonly IRecordReplayController? _recordingController;
    private readonly string? _storageDirectory;
    private readonly string _hostLabel;

    private TaskCompletionSource<object?>? _holdTcs;
    private Guid _pendingExerciseId;

    private LoadPhaseChain(
        IReadOnlyList<ILoadPartProvider> steps,
        EntityRepository? world,
        IRecordReplayController? recordingController,
        string? storageDirectory,
        string hostLabel)
    {
        _steps = steps;
        _world = world;
        _recordingController = recordingController;
        _storageDirectory = storageDirectory;
        _hostLabel = hostLabel;
    }

    /// <summary>⭐ The ordered parts this chain actually runs — for rails and diagnostics.</summary>
    public IReadOnlyList<LoadPart> Parts => _steps.Select(s => s.Part).ToArray();

    /// <summary>
    /// ⭐⭐⭐ Builds the chain for a host from its <paramref name="roles"/> and the providers it supplies.
    ///
    /// <para>⛔ <b>A required part with no provider THROWS, here, at startup.</b> ⚠ That is deliberate and
    /// it is the whole control: the defect class this replaces was a missing loader that produced an empty
    /// world and <c>ok:true</c>, with nothing in any log. A host that cannot satisfy its own role must fail
    /// where it is composed, not where a scenario is loaded.</para>
    ///
    /// <para>⭐ Extra providers are allowed and simply unused — §3.1's rule that a role never denies a
    /// capability means a host may compose more than its roles require.</para>
    /// </summary>
    /// <param name="roles">This node's roles. The WHAT comes from here — never from the caller.</param>
    /// <param name="providers">This host's implementations. The HOW comes from here.</param>
    /// <param name="world">
    /// 🔴 The node's live repository. <c>ClusterSlave</c> commits with <c>repo: null</c>, so this is what
    /// actually carries production.
    /// </param>
    /// <param name="recordingController">
    /// ⭐ Optional. When present the chain prepares the exercise recording once the load has resolved —
    /// the job <c>ReferenceLiveLoadHandler</c> did before the chain started claiming these operations.
    /// </param>
    /// <param name="storageDirectory">Where a recording is staged; required when a controller is given.</param>
    public static LoadPhaseChain FromRoles(
        NodeRole roles,
        IEnumerable<ILoadPartProvider> providers,
        EntityRepository? world = null,
        IRecordReplayController? recordingController = null,
        string? storageDirectory = null,
        string hostLabel = "node")
    {
        if (providers == null) throw new ArgumentNullException(nameof(providers));

        var available = providers.Where(p => p != null).ToList();
        var required  = RoleLoadRequirements.PartsFor(roles);
        var ordered   = new List<ILoadPartProvider>(required.Count);

        foreach (var part in required)
        {
            var provider = available.FirstOrDefault(p => p.Part == part);
            if (provider == null)
                throw new InvalidOperationException(
                    $"[LoadPhase] {hostLabel} has roles [{roles}], which require the '{part}' part of the "
                  + $"load phase, but no provider for it was supplied. Composed providers: "
                  + $"[{string.Join(", ", available.Select(p => p.Part))}]. "
                  + "A host may choose HOW it satisfies a part; it may not satisfy fewer parts than its "
                  + "roles require. See docs/DESIGN_Cluster_Load_Phase.md §4.1b.");

            ordered.Add(provider);
        }

        FdpLog<LoadPhaseChain>.Info(
            "[LoadPhase] {0} composed for roles [{1}]: {2}.",
            hostLabel, roles,
            ordered.Count == 0 ? "no parts required" : string.Join(" -> ", ordered.Select(p => p.Part)));

        return new LoadPhaseChain(ordered, world, recordingController, storageDirectory, hostLabel);
    }

    /// <inheritdoc/>
    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareLive ||
        operation == NodeOpType.PrepareEdit ||
        operation == NodeOpType.PrepareState;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐ Claims the two load operations outright, and <c>PrepareState</c> only for the two targets that
    /// END a load. ⛔ It deliberately does NOT claim <c>PrepareState(OperatingReplay|Idle)</c> — those
    /// belong to the replay handler, which is registered before it.
    /// </remarks>
    public bool CanHandle(ExecuteNodeOpIntent intent)
    {
        if (intent.Operation == NodeOpType.PrepareLive || intent.Operation == NodeOpType.PrepareEdit)
            return true;

        if (intent.Operation != NodeOpType.PrepareState) return false;

        var ctx = LoadPhaseContext.From(intent);
        return ctx.HasValue && (ctx.Value.IsOperatingTarget || ctx.Value.IsLoadingPhase);
    }

    /// <inheritdoc/>
    public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        var maybe = LoadPhaseContext.From(intent);
        if (maybe is not { } context) return null;

        // ── The transition that ENDS the load: hold the cluster in Loading* until every step reports
        //    resolved. ⭐ The hold is what keeps a node from going Operating with a half-built world.
        if (context.IsOperatingTarget)
        {
            _holdTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _holdTcs.Task.ConfigureAwait(false);

            // ⭐ The recording starts once the world is provably whole — the same point the former
            //   scenario handlers used, and the job ReferenceLiveLoadHandler used to do on hosts that had
            //   no scenario handler. Keeping it here means it happens on EVERY host, exactly once.
            if (_recordingController != null && _pendingExerciseId != Guid.Empty
             && context.TargetState == ClusterState.OperatingLive)
            {
                await _recordingController
                    .PrepareRecordingAsync(_pendingExerciseId, _storageDirectory ?? string.Empty)
                    .ConfigureAwait(false);
            }
            return null;
        }

        if (!context.IsLoadingPhase) return null;

        _pendingExerciseId = context.ExerciseId;

        // ── The content itself. ⭐ EVERY required step runs, in order, off the main thread.
        foreach (var step in _steps)
            await step.PrepareAsync(context, ct).ConfigureAwait(false);

        return null;
    }

    /// <inheritdoc/>
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        var maybe = LoadPhaseContext.From(intent);
        if (maybe is not { } context || !context.IsLoadingPhase) return;

        // 🔴 repo is null at both of ClusterSlave's dispatch sites; the injected world carries production.
        var world = repo ?? _world;
        foreach (var step in _steps)
            step.Commit(context, world);
    }

    /// <inheritdoc/>
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        var maybe = LoadPhaseContext.From(intent);
        if (maybe is { } context)
            foreach (var step in _steps)
                step.Abort(context);

        _holdTcs?.TrySetCanceled();
        _holdTcs = null;
        _pendingExerciseId = Guid.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ Main thread, every frame. The cluster leaves <c>Loading*</c> only when <b>every</b> step says it
    /// is resolved — ⛔ not when the last one's <c>Commit</c> returned. A step whose work drains over later
    /// frames (the scenario entities, through the genesis pipeline) keeps the whole chain waiting.
    /// </remarks>
    public void DrainDeferredAcks()
    {
        if (_holdTcs == null) return;

        foreach (var step in _steps)
            if (!step.IsResolved(_world)) return;

        _holdTcs.TrySetResult(null);
        _holdTcs = null;
    }
}
