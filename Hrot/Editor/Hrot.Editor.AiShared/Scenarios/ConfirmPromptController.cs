using System;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐ <b>A one-slot deferred confirmation: the DECISION logic for a destructive action, with no ImGui in
/// it.</b> 📄 <c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c> §3a *(<c>File/Live/New Exercise</c> —
/// "with a confirmation dialog about finishing a running exercise")*.
///
/// <para>⭐ <b>Mirrors <c>AppExitPromptController</c> deliberately</b> — the editor's app-exit modal
/// already splits *"what the buttons mean"* (a headless controller) from *"how it is drawn"* (an
/// <c>ImGui</c> method in the composition root). ⛔ Inventing a second shape for the same problem is what
/// the seam law exists to prevent, so this reuses the pattern rather than a modal framework.</para>
///
/// <para>⭐⭐ <b>ONE slot on purpose.</b> A second request while one is pending is DROPPED, not queued:
/// two stacked confirmations for a cluster-wide reset is worse than one, and a queue would let a
/// forgotten prompt fire a destructive action long after the operator moved on.</para>
///
/// <para>⚠ This type carries no scenario knowledge at all — the continuation is opaque. That is what lets
/// an interactive host prompt, a headless host log-and-proceed *(ruling 53)*, and a rail assert both
/// branches without a window.</para>
/// </summary>
public sealed class ConfirmPromptController
{
    private Action? _onConfirm;

    /// <summary>True while a confirmation is awaiting the operator.</summary>
    public bool IsPrompting => _onConfirm != null;

    /// <summary>Title for the pending prompt; empty when nothing is pending.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Body text for the pending prompt; empty when nothing is pending.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Label for the confirming button; empty when nothing is pending.</summary>
    public string ConfirmLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Queues a confirmation. ⚠ Dropped when one is already pending — see the type remarks.
    /// </summary>
    /// <returns><see langword="true"/> when the request was taken, <see langword="false"/> when dropped.</returns>
    public bool Request(string title, string message, string confirmLabel, Action onConfirm)
    {
        if (onConfirm is null) throw new ArgumentNullException(nameof(onConfirm));
        if (IsPrompting) return false;

        Title        = title        ?? string.Empty;
        Message      = message      ?? string.Empty;
        ConfirmLabel = confirmLabel ?? "OK";
        _onConfirm   = onConfirm;
        return true;
    }

    /// <summary>
    /// Runs the pending continuation and clears the slot. ⭐ The slot is cleared BEFORE the continuation
    /// runs, so an action that itself requests a confirmation cannot be dropped by its own prompt.
    /// </summary>
    public void ResolveConfirm()
    {
        var run = _onConfirm;
        Clear();
        run?.Invoke();
    }

    /// <summary>Discards the pending continuation — the destructive action does NOT run.</summary>
    public void ResolveCancel() => Clear();

    private void Clear()
    {
        _onConfirm   = null;
        Title        = string.Empty;
        Message      = string.Empty;
        ConfirmLabel = string.Empty;
    }
}
