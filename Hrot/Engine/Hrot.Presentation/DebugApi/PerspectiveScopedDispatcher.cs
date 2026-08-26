using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Controllers;
using Hrot.UI.Common.Facades;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>RESOLVES THE ACTIVE PERSPECTIVE TO THE SUBSYSTEM THAT OWNS IT, and issues reads/drives there.</b>
/// 📄 <c>Architect_Question_54</c> Q54-2 *(Option B + C)* · <c>DESIGN_Perspective_Unification.md</c> §1b.
///
/// <para>🔒 <b>User, `2026-08-24`:</b> <i>"MCP in 'mode all' should work from the context of the currently
/// selected perspective to be closest to how the user would control it."</i></para>
///
/// <para>⭐⭐⭐ <b>Issue where the user is, confirm where the truth is.</b> A step is issued through the ACTIVE
/// perspective's own drive facade — on a slave that is <c>StepTimeIntent</c> → its bus → DDS → the master,
/// exactly the path the operator's button takes. ⛔ But completion is read from the MASTER's
/// <see cref="MasterSyncController.IsAwaitingStepAcks"/>, because that is the only place that knows the tick
/// landed on every roster node. ⚠ Not a contradiction — two different questions.</para>
///
/// <para>⛔⛔ <b>PARTICIPATE ≠ OBSERVE</b> *(Q54)*. The roster that must ACK is <b>SimHost · IG · CGF</b> —
/// the nodes with an ECS kernel and a frame to execute. ⭐ ExCon has no kernel, so it can never be a barrier
/// participant; it does not need to be, because completion is OBSERVED on the master. ⛔ Adding a console to
/// the roster would stall the cluster forever waiting on an ACK it cannot produce.</para>
///
/// <para>⭐ <b>In <c>--mode all</c> every user-selectable perspective is a SLAVE context</b> — 📐 the
/// orchestrator has no perspective of its own *(both its windows are Global)*. ⇒ there is no
/// "direct on master" case among selectable perspectives today; the per-provider seam is kept so a future
/// master-owned perspective drops in without a special case.</para>
/// </summary>
public sealed class PerspectiveScopedDispatcher
{
    private readonly IReadOnlyList<ISubsystemDebugProvider> _providers;
    private readonly Func<string> _currentPerspective;
    private readonly Func<bool?>? _acksPending;

    /// <param name="providers">One per contributing subsystem. ⚠ An empty list is legal — the manifest then honestly reports nothing routable.</param>
    /// <param name="currentPerspective">
    /// ⭐ Reads the LIVE perspective *(the window manager's)*, never a cached copy — 📌 <c>R-126</c>'s shape:
    /// read the source, do not latch it.
    /// </param>
    /// <param name="acksPending">
    /// ⭐⭐ <b>The ack-gate's live read: <see langword="null"/> ⇒ no master on this host, otherwise the master's
    /// own "still awaiting step ACKs?".</b> 📄 <c>HN-028</c>. ⭐ A <see cref="Func{T}"/> of
    /// <see cref="Nullable{Boolean}"/>, not a controller and not a latched value, for three measured reasons:
    /// the master is created after the composition root and destroyed before it *(a captured reference lies —
    /// 📌 exactly deviation ③ of the conformance batch)*; <see langword="null"/> makes ABSENCE assertable
    /// *(charter `D3`/`D4`)*; and it withholds <c>Step</c>/<c>SetTimeScale</c>, which belong to the
    /// perspective-scoped drive facade *(Q54-2)*, not to the gate.
    /// ⛔ Pass <see langword="null"/> when this host has no orchestrator at all.
    /// </param>
    public PerspectiveScopedDispatcher(
        IEnumerable<ISubsystemDebugProvider> providers,
        Func<string> currentPerspective,
        Func<bool?>? acksPending)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers)))
                     .Where(p => p != null).ToList();
        _currentPerspective = currentPerspective ?? throw new ArgumentNullException(nameof(currentPerspective));
        _acksPending = acksPending;
    }

    /// <summary>
    /// ⭐⭐ <b><c>MD-002</c> — EVERY provider, not just the active one.</b>
    /// <para>⛔⛔ Deliberately not <see cref="Active"/>: architecture diagnostics are the one read where
    /// "the perspective the user is looking at" is the WRONG scope. 📐 A <c>--mode all</c> node runs
    /// SimHost, IG, CGF and the orchestrator side by side, each with its own <c>ModuleHostKernel</c> —
    /// and an operator asking *"what is this NODE running?"* means all of them. ⚠ Every other read here
    /// is perspective-scoped precisely because it answers a different question.</para>
    /// </summary>
    public IReadOnlyList<ISubsystemDebugProvider> AllProviders => _providers;

    /// <summary>⭐ The perspectives this dispatcher can route to, for the manifest and for diagnostics.</summary>
    public IReadOnlyList<string> RoutablePerspectives =>
        _providers.Select(p => p.Perspective).OrderBy(p => p, StringComparer.Ordinal).ToList();

    /// <summary>⭐ The live perspective name, as the window manager reports it.</summary>
    public string CurrentPerspective => _currentPerspective();

    /// <summary>
    /// ⭐⭐ The provider owning the active perspective, or <see langword="null"/> when nothing claims it.
    /// <para>⚠ <see langword="null"/> is a real answer: in <c>--mode all</c> the startup perspective may be
    /// one whose subsystem contributes no surface. ⛔ The caller reports <c>NOT_SUPPORTED_HERE</c>; it does
    /// not silently fall back to "the first provider", which would answer for the wrong node.</para>
    /// </summary>
    public ISubsystemDebugProvider? Active()
        => Resolve(_currentPerspective());

    /// <summary>⭐ The provider for a NAMED perspective — <c>Q54-2</c>'s optional <c>?perspective=</c> override.</summary>
    public ISubsystemDebugProvider? Resolve(string? perspective)
        => perspective is null
            ? null
            : _providers.FirstOrDefault(p => string.Equals(p.Perspective, perspective, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ⭐⭐⭐ <b>The ack-gate's truth, read on the MASTER.</b> ⛔ <see langword="false"/> when this host has no
    /// master: nothing here can be confirmed cluster-wide, and answering <see langword="true"/> forever would
    /// hang every step.
    /// </summary>
    public bool IsAwaitingStepAcks => _acksPending?.Invoke() ?? false;

    /// <summary>
    /// ⭐ True when a master is present, so the manifest can say whether a step is confirmable here.
    /// ⚠ Evaluated LIVE — the master exists only between the orchestrator's <c>Initialize</c> and
    /// <c>Shutdown</c>, so this is a question with a different answer at different times, not a constant.
    /// </summary>
    public bool HasMaster => _acksPending?.Invoke() is not null;

    // ── the read/drive surface, per active perspective ────────────────────────

    /// <summary>⭐ The active perspective's world, or <see langword="null"/> ⇒ <c>NOT_SUPPORTED_HERE</c>.</summary>
    public EntityRepository? World => Active()?.World;

    /// <summary>⭐ The active perspective's entity map, or <see langword="null"/>.</summary>
    public NetworkEntityMap? EntityMap => Active()?.EntityMap;

    /// <summary>⭐ The active perspective's drive facade, or <see langword="null"/>.</summary>
    public ITimeTransportFacade? Drive => Active()?.Drive;

    /// <summary>
    /// ⭐⭐ The active perspective's cluster-transition publisher, or <see langword="null"/> ⇒
    /// <c>NOT_SUPPORTED_HERE(scenario.load)</c>. 📄 <c>MCP_Integration.md</c> § Group U.
    /// <para>⭐ Perspective-scoped for the same reason a step is: the request travels the path the operator's
    /// own button takes on that node — its bus, then DDS to the master.</para>
    /// </summary>
    public Action<Fdp.Toolkit.Orchestration.TransitionStateIntent>? RequestTransition
        => Active()?.RequestTransition;

    /// <summary>
    /// ⭐⭐ <b>The cluster's state, from whichever node tracks it</b> — the readiness gate for
    /// <c>scenario/load/*</c>. <see langword="null"/> when no node here does.
    ///
    /// <para>⚠⚠ <b>This is the ONE member that deliberately falls back past the active perspective</b>, and the
    /// reason is not convenience: ⭐ <b>there is one cluster and one cluster state.</b> A node's
    /// <c>ClusterUiCache</c> is a CACHE OF A GLOBAL FACT, not that node's own data — so reading it from
    /// whichever node keeps one answers the same question. ⛔ Contrast <see cref="World"/>: falling back there
    /// would answer about the WRONG NODE, which is exactly what Q54-2 forbids. 📌 Measured: in
    /// <c>--mode all</c> only ExCon builds a cache, so without this fallback a load from the <c>Scenario</c> or
    /// <c>SimHost</c> perspective could never observe its own completion.</para>
    /// </summary>
    public Fdp.Toolkit.Orchestration.ClusterState? ClusterStateAnyNode
        => Active()?.ClusterState
           ?? _providers.Select(p => p.ClusterState).FirstOrDefault(s => s is not null);

    /// <summary>
    /// ⭐⭐ <b><c>MD-006</c> — trigger the cluster dump from whichever node can publish it.</b>
    /// <para>⚠ Prefers the ACTIVE perspective *(the request then travels the path that operator's own button
    /// takes)*, ⭐ but falls back to any provider with a bus — because the DUMP is cluster-wide by
    /// construction, so it does not matter which node asks. ⛔ Contrast <see cref="Drive"/>, where the
    /// asking node IS the answer.</para>
    /// </summary>
    public Action<ExecuteDiagnosticDumpIntent>? RequestDiagnosticDumpAnyNode
        => Active()?.RequestDiagnosticDump
           ?? _providers.Select(p => p.RequestDiagnosticDump).FirstOrDefault(a => a is not null);

    /// <summary>⭐ <c>MD-007</c> — the last dump's outcome from whichever node caches it; same one-cluster-one-fact rationale as <see cref="ClusterStateAnyNode"/>.</summary>
    public DiagnosticDumpStatus? DumpStatusAnyNode
        => Active()?.DumpStatus
           ?? _providers.Select(p => p.DumpStatus).FirstOrDefault(s => s is not null);

    /// <summary>⭐ Scenario inventory from whichever node caches it — same rationale as <see cref="ClusterStateAnyNode"/>.</summary>
    public IReadOnlyList<string>? AvailableScenariosAnyNode
        => Active()?.AvailableScenarios
           ?? _providers.Select(p => p.AvailableScenarios).FirstOrDefault(s => s is not null);

    /// <summary>
    /// ⭐⭐ <b>The measured availability matrix — <c>(perspective × capability) → present</c>.</b>
    /// 📄 Q54 § Manifest scope. ⛔ Every cell comes from a provider's own wired-dependency check; nothing
    /// here is authored.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> Matrix()
        => _providers.ToDictionary(
            p => p.Perspective,
            p => p.DescribeCapabilities(),
            StringComparer.Ordinal);
}
