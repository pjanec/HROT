using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
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
    private readonly MasterSyncController? _master;

    /// <param name="providers">One per contributing subsystem. ⚠ An empty list is legal — the manifest then honestly reports nothing routable.</param>
    /// <param name="currentPerspective">
    /// ⭐ Reads the LIVE perspective *(the window manager's)*, never a cached copy — 📌 <c>R-126</c>'s shape:
    /// read the source, do not latch it.
    /// </param>
    /// <param name="master">
    /// ⭐⭐ The cluster master's controller, for the ack-gate. ⛔ <see langword="null"/> when this host runs no
    /// master *(then a step cannot be confirmed cluster-wide, and <see cref="IsAwaitingStepAcks"/> says so by
    /// answering <see langword="false"/> — ⚠ documented rather than silently pretending)*.
    /// </param>
    public PerspectiveScopedDispatcher(
        IEnumerable<ISubsystemDebugProvider> providers,
        Func<string> currentPerspective,
        MasterSyncController? master)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers)))
                     .Where(p => p != null).ToList();
        _currentPerspective = currentPerspective ?? throw new ArgumentNullException(nameof(currentPerspective));
        _master = master;
    }

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
    public bool IsAwaitingStepAcks => _master?.IsAwaitingStepAcks ?? false;

    /// <summary>⭐ True when a master is present, so the manifest can say whether a step is confirmable here.</summary>
    public bool HasMaster => _master is not null;

    // ── the read/drive surface, per active perspective ────────────────────────

    /// <summary>⭐ The active perspective's world, or <see langword="null"/> ⇒ <c>NOT_SUPPORTED_HERE</c>.</summary>
    public EntityRepository? World => Active()?.World;

    /// <summary>⭐ The active perspective's entity map, or <see langword="null"/>.</summary>
    public NetworkEntityMap? EntityMap => Active()?.EntityMap;

    /// <summary>⭐ The active perspective's drive facade, or <see langword="null"/>.</summary>
    public ITimeTransportFacade? Drive => Active()?.Drive;

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
