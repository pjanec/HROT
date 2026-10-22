using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐⭐ One kind of content a node makes resident during the cluster's <c>Loading*</c> phase.
///
/// <para>⛔ <b>The three are NOT derived the same way, and conflating them is a measured defect.</b>
/// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.1:</para>
/// <list type="bullet">
///   <item><description>⭐⭐⭐ <see cref="KnowledgeBase"/> is required by <b>being an ECS node at all</b> —
///   never by the role. Every ECS node composes the full genesis pipeline with no opt-out
///   (<c>Q65-A′</c>), so a node that can be asked to create an entity must be able to resolve that
///   entity's template. 🔴 Measured <c>2026-09-18</c>: only SimHost loaded a named knowledge base, so CGF
///   and IG would have ignored a scenario's <c>TkbName</c> entirely.</description></item>
///   <item><description>⭐ <see cref="Terrain"/> is <b>role-derived</b> — <c>MuscleGround</c> and
///   <c>NavigationSolver</c> are the only roles with a measured consumer of the road graph. 🔒 User,
///   <c>2026-09-18</c>: <i>"load nothing where nothing reads it"</i>.</description></item>
///   <item><description>⭐ <see cref="ScenarioEntities"/> is <b>role-derived</b> — <c>Brain</c> only,
///   because only <c>Brain</c> also edits and saves the scenario.</description></item>
/// </list>
///
/// <para>⭐⭐ <b>Where a future part belongs</b>, so it is placed rather than guessed: ask which question
/// it answers. <i>"Can this node be asked to create an entity?"</i> ⇒ unconditional.
/// <i>"Does this node read X?"</i> ⇒ role-derived.</para>
/// </summary>
public enum LoadPart
{
    /// <summary>The TKB — entity templates. ⭐ Unconditional on every ECS node.</summary>
    KnowledgeBase = 0,

    /// <summary>The named terrain definition and its road graph. ⭐ Role-derived.</summary>
    Terrain = 1,

    /// <summary>The scenario's authored entities. ⭐ Role-derived — <c>Brain</c> only.</summary>
    ScenarioEntities = 2,
}

/// <summary>
/// ⭐⭐ Everything a load step needs to know about the round it is running in — read off the ONE message
/// the orchestrator fans out, never off a file.
///
/// <para>📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.2. ⚠ <see cref="TerrainName"/> may be <see langword="null"/>
/// when the sender omitted it (an older peer) or when the scenario genuinely names none — the step distinguishes the
/// two by falling back to its staged header, and treats "no name anywhere" as legal and silent.</para>
///
/// <para>⛔ <c>R-4018</c> — no TKB name: the knowledge-base step reads the name its file sync left
/// (<c>StagedTkb</c>).</para>
/// </summary>
public readonly record struct LoadPhaseContext(
    Guid TransactionId,
    ClusterState TargetState,
    string? ScenarioId,
    string? TerrainName,
    Guid ExerciseId,
    bool IsNewScenario,
    bool IsWorldBoundary = false,
    bool IsCatchUp = false,
    double CheckpointingSeconds = 0)   // ⭐ CE-3299 D17 (R-3045) — the session's recording mode (0 = FULL)
{
    /// <summary>⭐ Builds the context from a node-op intent, or <c>null</c> when it carries no load payload.</summary>
    public static LoadPhaseContext? From(ExecuteNodeOpIntent intent)
        => intent.DomainPayload is EditLoadHandlerPayload p
            ? new LoadPhaseContext(
                intent.TransactionId, p.TargetState, p.ScenarioId,
                p.TerrainName, p.ExerciseId, p.IsNewScenario, p.IsWorldBoundary, p.IsCatchUp, p.CheckpointingSeconds)
            : null;

    /// <summary>⭐ True while the cluster is BUILDING a world — the phase in which content loads.</summary>
    public bool IsLoadingPhase
        => TargetState == ClusterState.LoadingLive || TargetState == ClusterState.LoadingEdit;

    /// <summary>⭐ True for the transition that ENDS the load — the phase that must be held open.</summary>
    public bool IsOperatingTarget
        => TargetState == ClusterState.OperatingLive || TargetState == ClusterState.OperatingEdit;
}

/// <summary>
/// ⭐⭐⭐ One step of the load phase. ⛔ <b>NOT an <c>IClusterStateHandler</c></b>, and that is the point:
/// a cluster handler CLAIMS an operation, and <c>ClusterSlave</c> gives the operation to the FIRST
/// claimant and returns (<c>ClusterSlave.cs:404-448</c>) — so two prerequisite loaders registered as
/// handlers silently cancel each other.
///
/// <para>🔴 <b>Measured <c>2026-09-18</c>, and this interface exists because of it:</b> on CGF the terrain
/// handler shadowed the scenario handler and the cluster loaded <b>zero entities</b>; on SimHost the TKB
/// handler shadowed the terrain handler, so terrain had <b>never</b> loaded there. Neither logged
/// anything. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §2.2.</para>
///
/// <para>⭐ A step therefore never claims anything: <see cref="LoadPhaseChain"/> claims once, runs every
/// required step in order, and acknowledges once.</para>
/// </summary>
public interface ILoadPartProvider
{
    /// <summary>Which part this provider satisfies. ⭐ The chain matches it against the node's requirements.</summary>
    LoadPart Part { get; }

    /// <summary>
    /// ⭐ The I/O half. ⛔ <b>Must not touch ECS</b>: the cluster may still abort the round, and a mutation here would
    /// not roll back. ⚠ Runs on a WORKER only when <see cref="PreparesOffFrameThread"/> is true; otherwise inline on
    /// the frame thread.
    /// </summary>
    Task PrepareAsync(LoadPhaseContext context, CancellationToken ct);

    /// <summary>
    /// ⭐⭐ P4 (Q91 §4-B2, R-4001) — <c>true</c> when <see cref="PrepareAsync"/> builds ONLY a private object nobody else
    /// can see yet (and writes every shared thing in <see cref="Commit"/>), so it may run on a worker while the frame
    /// thread keeps ticking — and the heartbeat keeps beating. ⛔ Default <c>false</c>: a step that touches anything
    /// shared (an id allocator, a live database) prepares inline on the frame thread. 🔒 User: <i>"load preparation can
    /// be run in background thread only if it is safe not touching global data."</i>
    /// </summary>
    bool PreparesOffFrameThread => false;

    /// <summary>
    /// ⭐⭐ <c>CE-2242</c> — frame thread, once per load, BEFORE any step prepares (after the world-boundary clear). For the
    /// little a step must have in place while its own prepare still runs, because other nodes commit and replicate in
    /// during that window (the terrain's geo origin). ⚠ Unlike <see cref="PrepareAsync"/> it may write the world; an
    /// abort does not undo it, so a step writes here only what is harmless to keep. Default: nothing.
    /// </summary>
    void BeforePrepare(LoadPhaseContext context, EntityRepository? world) { }

    /// <summary>
    /// ⭐ The ECS half — main thread, and the only place ECS is written.
    ///
    /// <para>🔴 <c>ClusterSlave</c> commits with <c>repo: null</c> at both of its dispatch sites, so
    /// <paramref name="world"/> is the chain's own injected world, never the slave's parameter. A step
    /// that published only through a parameter would publish NOTHING on every host — a trap each of the
    /// three former handlers had to rediscover separately.</para>
    /// </summary>
    void Commit(LoadPhaseContext context, EntityRepository? world);

    /// <summary>⭐ Frees whatever <see cref="PrepareAsync"/> built and never published.</summary>
    void Abort(LoadPhaseContext context);

    /// <summary>
    /// ⭐⭐⭐ <b>The readiness contribution — asked every frame while the cluster is held in
    /// <c>Loading*</c>.</b> A step that finishes inside <see cref="Commit"/> returns <c>true</c>; a step
    /// whose work drains over later frames (the scenario entities, through the genesis pipeline) returns
    /// <c>false</c> until it is provably done.
    ///
    /// <para>⛔ <b>There is exactly ONE implementation of the scenario predicate</b>, which is the whole
    /// reason this is a method on the step and not a copy in each host's handler. 🔴 Measured: of the
    /// three former handlers, one was missing the cross-reference-intent conditions entirely and another
    /// was missing the terrain-residency call — each had lost a line the others kept.</para>
    /// </summary>
    bool IsResolved(EntityRepository? world) => true;
}
