namespace NodeEditor.UI.Picker;

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P3) — one footer button a request supplies INSTEAD of the single OK (<i>Open</i> / <i>Open and go live</i>;
/// <i>Place</i> / <i>Place many</i>). Clicking one confirms with <see cref="PickerResult.ActionId"/> = <see cref="Id"/>.
/// </summary>
/// <param name="Id">Stable id returned in <see cref="PickerResult.ActionId"/>.</param>
/// <param name="Label">Button text.</param>
/// <param name="IsDefault">The action Enter / double-click takes (the first default wins; with none flagged, the first action).</param>
/// <param name="DisabledReason">Given the entry the action would apply to (null = nothing chosen), the reason the action cannot run now —
/// shown as the disabled button's tooltip — or null when it can. Null delegate ⇒ always enabled.</param>
public sealed record PickerAction(
    string Id,
    string Label,
    bool IsDefault = false,
    Func<PickerEntry?, string?>? DisabledReason = null)
{
    /// <summary>
    /// The action a confirm takes: <paramref name="requestedId"/> when given (a button click), else the default one. Returns
    /// null when there is no such action or it is disabled for <paramref name="entry"/> — <paramref name="blockedReason"/> says why.
    /// With no <paramref name="actions"/> at all (the classic OK picker) it returns null and <paramref name="blocked"/> is false.
    /// </summary>
    public static PickerAction? Resolve(IReadOnlyList<PickerAction>? actions, string? requestedId, PickerEntry? entry,
                                        out bool blocked, out string? blockedReason)
    {
        blocked = false;
        blockedReason = null;
        if (actions is null || actions.Count == 0) return null;

        var action = requestedId is null
            ? actions.FirstOrDefault(a => a.IsDefault) ?? actions[0]
            : actions.FirstOrDefault(a => a.Id == requestedId);
        if (action is null) { blocked = true; blockedReason = $"unknown action '{requestedId}'"; return null; }

        blockedReason = action.DisabledReason?.Invoke(entry);
        if (blockedReason is not null) { blocked = true; return null; }
        return action;
    }
}
