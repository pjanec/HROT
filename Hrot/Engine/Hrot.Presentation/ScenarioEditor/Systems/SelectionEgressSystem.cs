using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-6</c> — tells remote observers what this host's map selection
/// BECAME, from the ANNOUNCEMENT rather than from one of its causes.</b>
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.6 (the carve-out) and §2.7.17 (this slice's as-built).
///
/// <para>🔒 <b>The design's own words:</b> <i>"IG → observers: the IG publishes when <b>its own map</b>
/// selection changes"</i>. ⭐ Under the request/notify protocol *"its own map selection changed"* IS
/// <see cref="SelectionChangedNotification"/> — so this is the faithful reading, not a new mechanism.</para>
///
/// <para>🔴 <b>What it replaces, and the gap is the point.</b> 📐 Measured <c>2026-09-20</c>: the outbound
/// <c>SelectionChangedEvent</c> was published from inside IG's <b>map-click handler</b>. ⇒ a selection
/// changed by ANY other cause — the entity inspector, the orbat, a context-menu <i>Select</i>, a remote
/// <c>CMD_SET_SELECTION</c> — <b>never reached ExCon at all</b>, so its "Selection &amp; Mission" panel
/// silently showed a selection the IG no longer had. ⭐ Same defect shape as the one <c>S-3</c> fixed on
/// the INBOUND side, and for the same reason: a consequence hung off one cause instead of the
/// announcement.</para>
///
/// <para>⭐⭐⭐ <b>AND THIS IS WHERE ECHO SUPPRESSION BELONGS.</b> 🔒 §2.6: <i>"Echo suppression belongs at
/// the EGRESS translator (do not re-publish outward what ingress produced), never by muting the internal
/// notification."</i></para>
///
/// <para>⛔⛔ <b>Why the old suppression had to move, stated precisely.</b> It worked by NOT PUBLISHING
/// the notification for a remote command — <c>ParseCommandAndSetSelection</c>'s own doc comment said so.
/// ⭐ That was viable only while nothing internal depended on the notification. <c>S-3</c> made it the
/// thing every panel and the inspector context follow ⇒ suppressing it would leave <b>every local
/// surface stale</b> after a remote selection. 📌 The comment claiming that suppression survived until
/// this slice and was FALSE by then: <c>S-2</c> had already routed the remote command through the
/// request system, which announces for every cause.</para>
///
/// <para>⚠ <b>The discriminator is the request's <c>Reason</c></b>, carried through to the notification.
/// ⛔ Not a flag, not a latch, not a "suppress next" counter — all three are state that can desynchronise.
/// ⭐ The reason travels WITH the thing it describes.</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class SelectionEgressSystem : IEcsModuleSystem
{
    /// <summary>
    /// ⭐⭐ The prefix that marks a selection change as having ARRIVED from a remote peer.
    /// ⛔ Anything published with it is not sent back out, which is the whole echo suppression.
    /// </summary>
    public const string RemoteOriginPrefix = "Remote.";

    private readonly Action<IReadOnlyList<int>> _publish;

    /// <param name="publish">
    /// ⚠ A DELEGATE, and that is <c>R-134</c>, not style: no DDS or network type may cross into the
    /// FDP-internal path, so this system speaks <c>int</c> network ids and the host owns the transport.
    /// ⭐ It is also what lets this class live beside the other selection systems rather than in the
    /// network assembly.
    /// </param>
    public SelectionEgressSystem(Action<IReadOnlyList<int>> publish)
        => _publish = publish ?? throw new ArgumentNullException(nameof(publish));

    /// <summary>
    /// ⭐ <c>true</c> when a selection change arrived from a remote peer and must not be echoed back.
    /// ⚠ Exposed so a rail can assert the predicate directly rather than inferring it from a publish
    /// that did not happen — ⛔ an absence is the weakest possible assertion.
    /// </summary>
    public static bool IsRemoteOrigin(string? reason)
        => reason != null && reason.StartsWith(RemoteOriginPrefix, StringComparison.Ordinal);

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        foreach (var note in world.Bus.ReadManaged<SelectionChangedNotification>())
        {
            if (note == null) continue;

            // ⭐⭐⭐ ECHO SUPPRESSION, and it is one line because the reason rides with the change.
            if (IsRemoteOrigin(note.Reason)) continue;

            // ⚠ Resolve to NETWORK ids: a peer cannot do anything with an ECS handle, and §2.7.3 rule 7
            //   puts the translation AT THE BOUNDARY — which is here.
            // ⛔ An entity with no usable network id is SKIPPED rather than sent as 0: id 0 is the
            //   "unreplicated" sentinel (§6.8's "no id, no pick target"), and a peer resolving it would
            //   select whatever happens to answer.
            var ids = new List<int>(note.Selected.Count);
            for (int i = 0; i < note.Selected.Count; i++)
            {
                long netId = NetworkIdResolver.RuntimeNetworkIdOf(world, note.Selected[i]);
                if (netId > 0) ids.Add((int)netId);
            }

            // ⭐ An EMPTY list is a real message — "the selection was cleared" — and must go out.
            //   ⛔ Skipping it would leave every observer painting a selection that no longer exists,
            //     which is the same "accepted and silently discarded" shape the Clear branch of
            //     SelectionRequestSystem was bitten by.
            _publish(ids);
        }
    }
}
