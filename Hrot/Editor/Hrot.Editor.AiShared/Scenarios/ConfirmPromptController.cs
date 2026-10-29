using System;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐ <b>A one-slot deferred confirmation: the PRODUCER of a <see cref="RunModeConfirmation"/> for a destructive action, with no ImGui in it.</b>
/// 📄 <c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c> §3a *(<c>File/Live/New Exercise</c> — "with a confirmation dialog about finishing a running exercise")*.
///
/// <para>⭐⭐ <c>CE-1438</c> (R-3066 audit #5) — <b>it holds NO title / message / button list of its own any more.</b> The ONE confirmation shape is
/// <see cref="RunModeConfirmation"/> (AQ108 R3, D10), drawn by the ONE <see cref="ConfirmationModal"/>; this type only owns the SLOT and the
/// continuation. ⛔ A second shape for the same problem is what the seam law forbids.</para>
///
/// <para>⭐⭐ <b>ONE slot on purpose.</b> A second request while one is pending is DROPPED, not queued: two stacked confirmations for a cluster-wide reset
/// is worse than one, and a queue would let a forgotten prompt fire a destructive action long after the operator moved on.</para>
///
/// <para>⚠ This type carries no scenario knowledge at all — the continuation is opaque. That is what lets an interactive host prompt, a headless host
/// log-and-proceed *(ruling 53)*, and a rail assert both branches without a window.</para>
/// </summary>
public sealed class ConfirmPromptController
{
    private readonly string _popupId;

    /// <param name="popupId">The ImGui id of this producer's modal (unique per producer).</param>
    public ConfirmPromptController(string popupId = "scenario_confirm") => _popupId = popupId;

    /// <summary>The confirmation awaiting the operator (draw it with <see cref="ConfirmationModal.Draw"/>); <see langword="null"/> when none.</summary>
    public RunModeConfirmation? Pending { get; private set; }

    /// <summary>True while a confirmation is awaiting the operator.</summary>
    public bool IsPrompting => Pending != null;

    /// <summary>
    /// Queues a confirmation: <paramref name="confirmLabel"/> runs <paramref name="onConfirm"/>, Cancel does not. ⚠ Dropped when one is already pending.
    /// </summary>
    /// <returns><see langword="true"/> when the request was taken, <see langword="false"/> when dropped.</returns>
    public bool Request(string title, string message, string confirmLabel, Action onConfirm)
    {
        if (onConfirm is null) throw new ArgumentNullException(nameof(onConfirm));
        if (IsPrompting) return false;

        Pending = RunModeConfirmation.WithCancel(message ?? string.Empty, done => { if (ReferenceEquals(Pending, done)) Pending = null; },
                                                 title ?? string.Empty, _popupId,
                                                 new RunModeConfirmation.Button(confirmLabel ?? "OK", onConfirm));
        return true;
    }

    /// <summary>Presses the confirming button (the first). ⭐ The slot is cleared BEFORE the continuation runs, so an action that itself requests a confirmation is not dropped by its own prompt.</summary>
    public void ResolveConfirm() => Pending?.Resolve(Pending.Buttons[0]);

    /// <summary>Presses Cancel — the destructive action does NOT run.</summary>
    public void ResolveCancel() => Pending?.Cancel();
}
