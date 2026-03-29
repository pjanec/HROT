using System.Collections.Generic;
using System.Text.Json;
using Bagira.BDC.SSTD.Orchestration;

namespace Bagira.Orchestrator;

// ── Step abstractions ──────────────────────────────────────────────────────────

/// <summary>A single entry in a planned transition trajectory.</summary>
public abstract class ISysOpStep { }

/// <summary>
/// Instructs all cluster nodes to transition to <see cref="TargetState"/> as part of a 2PC round.
/// </summary>
public sealed class TransitionStep : ISysOpStep
{
    public DSMState TargetState { get; }
    public TransitionStep(DSMState target) => TargetState = target;
}

/// <summary>
/// An out-of-band operation appended after the final <see cref="TransitionStep"/>,
/// for example a replay-seek to a specific wall-clock position.
/// </summary>
public sealed class OperationStep : ISysOpStep
{
    public SysOpType Operation { get; }
    public string    PayloadJson { get; }
    public OperationStep(SysOpType operation, string payloadJson)
    {
        Operation   = operation;
        PayloadJson = payloadJson;
    }
}

// ── Planner ────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves <see cref="SysOpRequest"/> targets into an ordered <see cref="Queue{T}"/> of
/// <see cref="ISysOpStep"/> entries via Breadth-First Search over the DSM directed graph.
/// Pure application-layer class — no DDS dependency.
/// </summary>
/// <remarks>
/// <b>Adjacency definition</b> follows CGF-1-DESIGN §4.1.  Failure-recovery edges
/// (e.g. <c>LoadingEdit → Standby</c>) are excluded from the planning graph; they are
/// automatic rollback paths triggered by node-side errors, not plannable transitions.
/// <c>DSMState.Degraded</c> is a system-imposed state with no outgoing planning edges
/// and is therefore unreachable/invalid as a planning target.
/// </remarks>
public sealed class TransitionPlanner
{
    // ── DSM adjacency (forward planning edges only; failure/rollback edges excluded) ──
    // NOTE: RunningEdit → LoadingLive is intentionally absent even though the design
    // adjacency list includes it.  The example trajectories (RunningEdit → RunningLive =
    // 4 steps) are normative and require routing through UnloadingEdit → Standby.
    // Logical rationale: you cannot start live-load from an active Edit session without
    // first unloading.  The design's adjacency entry is considered a documentation error.
    private static readonly IReadOnlyDictionary<DSMState, DSMState[]> Adjacency =
        new Dictionary<DSMState, DSMState[]>
        {
            [DSMState.Standby]         = new[] { DSMState.LoadingEdit, DSMState.LoadingLive, DSMState.LoadingReplay },
            [DSMState.LoadingEdit]     = new[] { DSMState.RunningEdit },
            [DSMState.RunningEdit]     = new[] { DSMState.LoadingDryRun, DSMState.UnloadingEdit },
            [DSMState.LoadingDryRun]   = new[] { DSMState.RunningDryRun },
            [DSMState.RunningDryRun]   = new[] { DSMState.UnloadingDryRun },
            [DSMState.UnloadingDryRun] = new[] { DSMState.RunningEdit },
            [DSMState.UnloadingEdit]   = new[] { DSMState.Standby },
            [DSMState.LoadingLive]     = new[] { DSMState.RunningLive },
            [DSMState.RunningLive]     = new[] { DSMState.UnloadingLive },
            [DSMState.UnloadingLive]   = new[] { DSMState.Standby },
            [DSMState.LoadingReplay]   = new[] { DSMState.RunningReplay },
            [DSMState.RunningReplay]   = new[] { DSMState.UnloadingReplay, DSMState.LoadingLive },
            [DSMState.UnloadingReplay] = new[] { DSMState.Standby },
            // DSMState.Degraded has no planning outgoing edges (system-imposed state).
        };

    /// <summary>
    /// Computes the shortest path from <paramref name="current"/> to <paramref name="target"/>
    /// using BFS over the DSM graph.
    /// </summary>
    /// <returns>
    /// The list of states to visit (excluding <paramref name="current"/>), in traversal order.
    /// An empty list when <paramref name="current"/> == <paramref name="target"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no path exists (e.g. <paramref name="current"/> or <paramref name="target"/>
    /// is <c>Degraded</c>, or the graph is otherwise disconnected for the given pair).
    /// The message includes both state names.
    /// </exception>
    public IReadOnlyList<DSMState> CalculateShortestPath(DSMState current, DSMState target)
    {
        if (current == target) return Array.Empty<DSMState>();

        // BFS
        var visited = new HashSet<DSMState> { current };
        var queue   = new Queue<(DSMState state, List<DSMState> path)>();
        queue.Enqueue((current, new List<DSMState>()));

        while (queue.Count > 0)
        {
            var (state, path) = queue.Dequeue();

            if (!Adjacency.TryGetValue(state, out var neighbors)) continue;

            foreach (var next in neighbors)
            {
                if (!visited.Add(next)) continue;

                var newPath = new List<DSMState>(path) { next };
                if (next == target) return newPath;

                queue.Enqueue((next, newPath));
            }
        }

        throw new InvalidOperationException(
            $"[TransitionPlanner] No valid DSM path from {current} to {target}. " +
            $"The transition '{current}' → '{target}' is not reachable in the planning graph.");
    }

    /// <summary>
    /// Plans a full trajectory for a <see cref="SysOpRequest"/>, returning an ordered queue
    /// of steps to execute as part of the next distributed transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Payload encoding (normative — prefer JSON object form in new code):</b>
    /// <see cref="SysOpRequest.PayloadJson"/> must be one of:
    /// <list type="bullet">
    ///   <item>A plain integer string (e.g. <c>"30"</c>): target DSM state numeric value.
    ///         Supported for backward compatibility; new code should use the JSON object form.</item>
    ///   <item>A JSON object with at least <c>TargetState</c> (int) and optionally
    ///         <c>TargetWallTicks</c> (long) — the <b>preferred</b> encoding:
    ///         <code>{ "TargetState": 30, "TargetWallTicks": 999000 }</code></item>
    /// </list>
    /// An empty, whitespace-only, non-parseable JSON string, or a JSON object that
    /// lacks <c>TargetState</c> is a caller error and will cause an
    /// <see cref="InvalidOperationException"/> to be thrown.
    /// </para>
    /// <para>
    /// When the resolved target is <see cref="DSMState.RunningReplay"/> AND
    /// <c>TargetWallTicks</c> is present in the JSON payload, an
    /// <see cref="OperationStep"/>(<see cref="SysOpType.ReplaySeek"/>) is appended after
    /// the final <see cref="TransitionStep"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="request"/> has an empty, whitespace-only, non-parseable,
    /// or structurally incomplete <see cref="SysOpRequest.PayloadJson"/> (missing
    /// <c>TargetState</c>).  Also propagated from <see cref="CalculateShortestPath"/> when
    /// the requested path is unreachable.  Always thrown before any DDS command is issued.
    /// </exception>
    public Queue<ISysOpStep> PlanTrajectory(DSMState current, SysOpRequest request)
    {
        DSMState targetState  = DSMState.Standby;
        string?  seekTicksRaw = null;

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            throw new InvalidOperationException(
                "[TransitionPlanner] TransitionState payload is empty or whitespace — " +
                "a valid target DSM state is required (integer or JSON object with TargetState).");
        }

        if (int.TryParse(request.PayloadJson, out var rawInt))
        {
            targetState = (DSMState)rawInt;
        }
        else
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(request.PayloadJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"[TransitionPlanner] TransitionState payload is not valid JSON: {ex.Message}", ex);
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("TargetState", out var tsProp))
                    throw new InvalidOperationException(
                        "[TransitionPlanner] TransitionState JSON payload does not contain " +
                        "required 'TargetState' property.");

                targetState = (DSMState)tsProp.GetInt32();

                if (doc.RootElement.TryGetProperty("TargetWallTicks", out var seekProp))
                    seekTicksRaw = seekProp.GetRawText();
            }
        }

        var path  = CalculateShortestPath(current, targetState);
        var queue = new Queue<ISysOpStep>();

        // CGF1-S0307: If the payload carries a ScenarioId, prepend a PrefetchScenario
        // step so the StorageGateway copies scenario files to all nodes before the first
        // DSM transition executes.
        string? scenarioId = null;
        if (!string.IsNullOrWhiteSpace(request.PayloadJson) && !int.TryParse(request.PayloadJson, out _))
        {
            try
            {
                using var scenarioDoc = JsonDocument.Parse(request.PayloadJson);
                if (scenarioDoc.RootElement.TryGetProperty("ScenarioId", out var sidProp))
                    scenarioId = sidProp.GetString();
            }
            catch (JsonException) { /* ignore malformed JSON — handled above */ }
        }

        if (!string.IsNullOrWhiteSpace(scenarioId))
            queue.Enqueue(new OperationStep(SysOpType.PrefetchScenario, scenarioId!));

        foreach (var state in path)
            queue.Enqueue(new TransitionStep(state));

        // Append a ReplaySeek operation when targeting RunningReplay with a seek hint.
        if (targetState == DSMState.RunningReplay && seekTicksRaw != null)
            queue.Enqueue(new OperationStep(SysOpType.ReplaySeek, seekTicksRaw));

        return queue;
    }
}
