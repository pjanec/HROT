using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐ CE-4240 — the instructor's End button + its confirmation, ONE instance per window (the Rules toolbar and the Mission summary both draw it). Disabled with the reason when there is none
/// (<see cref="InstructorEnd.Offer"/>); a click asks <i>End the exercise now?</i> in a modal; <see cref="Fire"/> writes <see cref="RuleRequest.Fire"/> through <see cref="RuleCommands.Control"/> (origin: instructor).
/// </summary>
public sealed class InstructorEndControl
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<string?> _blocked;
    private int _seq;
    private bool _open;

    public InstructorEndControl(Func<EntityRepository?> world, Func<string?> blocked) { _world = world; _blocked = blocked; }

    /// <summary>The last answer (what was written, or why not).</summary>
    public string? Message { get; private set; }
    /// <summary>True while the confirmation is up.</summary>
    public bool Confirming => _open;
    public InstructorEnd.Offer Offer => InstructorEnd.Of(_world(), _blocked());

    /// <summary>Asks for the confirmation (the button's click). False when the button is disabled.</summary>
    public bool Ask() { if (!Offer.Available) return false; _open = true; return true; }
    public void Cancel() => _open = false;

    /// <summary>Fires the trigger now (the modal's <i>End</i>). The written route is the answer in <see cref="Message"/>.</summary>
    public bool Fire()
    {
        _open = false;
        var offer = Offer;
        var world = _world();
        if (world == null || !offer.Available) { Message = offer.DisabledReason; return false; }
        var route = RuleCommands.Control(world, offer.Trigger, RuleRequest.Fire, RuleOrigin.Instructor, ref _seq, out _);
        bool ok = route != Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused;
        Message = ok ? (route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct ? "End: done here" : "End: requested of the owner") : "End: nothing was written - this node has no way to ask the owner";
        return ok;
    }

    /// <summary>Draws the button (always) and, when asked, the modal. <paramref name="id"/> keeps two windows' popups apart.</summary>
    private string? _labelsOf; private string _button = "", _modal = "", _endNow = "", _cancel = "";

    public void Draw(string id)
    {
        if (!string.Equals(_labelsOf, id, StringComparison.Ordinal))   // ⭐ CE-1465 — the labels are built once per id, not per frame (0 B steady)
        { _labelsOf = id; _button = $"End exercise##{id}"; _modal = $"End the exercise now?##{id}"; _endNow = $"End now##{id}"; _cancel = $"Cancel##{id}"; }
        var offer = Offer;
        if (!offer.Available) ImGui.BeginDisabled();
        if (ImGui.Button(_button)) { Ask(); ImGui.OpenPopup(_modal); }
        if (!offer.Available) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(offer.DisabledReason); }
        bool open = _open;
        if (ImGui.BeginPopupModal(_modal, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("The exercise ends as if its own end had fired: every objective is closed, the result is written.");
            if (ImGui.Button(_endNow)) { Fire(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button(_cancel)) { Cancel(); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!open) _open = false;
    }
}
