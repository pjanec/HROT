using System;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐ <c>CE-4097</c> part two (AQ108 D11) — the status-bar note while a run goes: <i>"Running — edits are not saved unless you save"</i> while a Preview or a Live
/// run is running. 🔒 User: <i>"edits must be allowed in live and preview, whether the user saves them is his responsibility, banner should be ok, rather in status bar —
/// non obtrusive"</i>. ⭐ One section on EVERY host (registered beside the time-control group on the editor and CGF), reading the facade's mode — no state of its own.
/// </summary>
public sealed class RunNoteStatusBarSection
{
    /// <summary>The status-bar section id (a duplicate registration replaces, never doubles).</summary>
    public const string SectionId = "run_note";

    /// <summary>Right of the map cursor (50), left of the message-log icon (90) and the time controls (100).</summary>
    public const int SortOrder = 60;

    /// <summary>The note, in the user's words (AQ108 §3).</summary>
    public const string Text = "Running — edits are not saved unless you save";

    private readonly ITimeTransportFacade _facade;

    /// <param name="facade">The host's ONE time facade.</param>
    public RunNoteStatusBarSection(ITimeTransportFacade facade) => _facade = facade ?? throw new ArgumentNullException(nameof(facade));

    /// <summary>Registers the section on <paramref name="bar"/> for <paramref name="perspective"/> (the perspective that shows the time controls).</summary>
    public static RunNoteStatusBarSection Register(StatusBarManager bar, ITimeTransportFacade facade, string perspective)
    {
        var section = new RunNoteStatusBarSection(facade);
        bar.RegisterSection(SectionId, SortOrder, section.Render, perspective);
        return section;
    }

    /// <summary>The note to show now, or <see langword="null"/> when no Preview or Live run is going (AAR and editing edit nothing a run could lose).</summary>
    public string? NoteNow() => _facade.Mode is TransportMode.Preview or TransportMode.Live ? Text : null;

    /// <summary>Called each frame by the <see cref="StatusBarManager"/>, inside an active ImGui frame and the status-bar window.</summary>
    public void Render()
    {
        if (NoteNow() is { } note) ImGui.TextDisabled(note);
    }
}
