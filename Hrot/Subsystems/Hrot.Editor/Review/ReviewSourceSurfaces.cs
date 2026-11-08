using System;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Fdp.Presentation.Panels.ReplayBrowser;
using ImGuiNET;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D9, mockup <c>review-perspective-visual.svg</c>) — <b>the status bar's SOURCE segment</b>, its own section at the bar's LEFT end
/// (the end <c>StationSeatStatusBarSection</c> leaves free for it, CE-3419): Merged ⇒ <i>"Merged · 4 participants (…) — step / scrub"</i> in teal; a part ⇒ <i>"Part: Node 2"</i>; an ordinary AAR ⇒
/// nothing. 🔒 User: <i>"We need some indication that we are actually running in merged federate view, I guess. Maybe the status bar is a good place."</i> A click opens the Source switch — a
/// popup over the Replay Browser's own <see cref="FederationPanel"/> (mode, offsets), headed by the participants' parts.
/// </summary>
public sealed class ReviewSourceStatusSection
{
    public const string SectionId = "review_source";
    /// <summary>The bar's left end: before the map cursor (50) and everything after it.</summary>
    public const int SortOrder = 0;
    public const string PopupId = "##review_source_switch";
    public const string Tooltip = "Where the world comes from. Click for the Source switch (Merged or one participant's part).";

    /// <summary>The teal of Merged (the mockup's).</summary>
    public static readonly Vector4 Teal = new(0.25f, 0.78f, 0.74f, 1f);

    private readonly ReviewSource _source;
    private bool _openRequested;

    public ReviewSourceStatusSection(ReviewSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Registers the section on <paramref name="bar"/> for EVERY perspective (the source is the replay's, not a layout's).</summary>
    public static ReviewSourceStatusSection Register(StatusBarManager bar, ReviewSource source)
    {
        var section = new ReviewSourceStatusSection(source);
        bar.RegisterSection(SectionId, SortOrder, section.Render);
        return section;
    }

    /// <summary>The text now (null ⇒ nothing is drawn).</summary>
    public string? Text => _source.StatusText();

    /// <summary>Whether the last click asked for the switch (a rail reads it; <see cref="Render"/> consumes it).</summary>
    public bool OpenRequested => _openRequested;
    public void Open() => _openRequested = true;

    /// <summary>Whether the switch popup was drawn on the last frame.</summary>
    internal bool PopupDrawn { get; private set; }

    /// <summary>The screen rectangle of the text as last drawn (a rail's measurement).</summary>
    internal (Vector2 Min, Vector2 Max) TextRect;

    /// <summary>Called each frame by the <see cref="StatusBarManager"/>, inside an active ImGui frame and the status-bar window.</summary>
    public void Render()
    {
        if (Text is not { } text) return;
        if (_source.Kind == ReviewSourceKind.Merged) ImGui.TextColored(Teal, text); else ImGui.TextUnformatted(text);
        TextRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (_source.IsOpen && ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Tooltip);
        }
        if (_source.IsOpen && ImGui.IsItemClicked(ImGuiMouseButton.Left)) _openRequested = true;
        if (_openRequested) { ImGui.OpenPopup(PopupId); _openRequested = false; }
        PopupDrawn = false;
        if (!ImGui.BeginPopup(PopupId)) return;
        PopupDrawn = true;
        ReviewSourceSwitch.Draw(_source);
        ImGui.EndPopup();
    }
}

/// <summary>⭐ The Source switch's body, shared by the status bar's popup and any host that wants it: the participants (Merged · each part) over the Replay Browser's <see cref="FederationPanel"/>.</summary>
public static class ReviewSourceSwitch
{
    public static void Draw(ReviewSource source)
    {
        ImGui.TextDisabled("Source");
        if (ImGui.Selectable("Merged — every participant's world as one", source.Kind == ReviewSourceKind.Merged)) source.SelectMerged();
        foreach (var (node, _) in source.Participants)
            if (ImGui.Selectable("Part: " + source.NameOf(node), source.Kind == ReviewSourceKind.Part && source.PartNode == node)) source.SelectPart(node);
        if (source.SwitchPanel is { } panel)
        {
            ImGui.Separator();
            panel.DrawContent();   // the RB's own switch — mode, node offsets, the local-entities provider
        }
    }
}

/// <summary>
/// ⭐ R-5 / V6 (D9) — <b>the toolbar's compact Source combo, only while a participant GROUP is open</b>: its own main-toolbar entry beside the AAR transport segment (ui's time control untouched).
/// </summary>
public sealed class ReviewSourceToolbarCombo
{
    public const string EntryId = "ReviewSource";
    /// <summary>Right after the AAR transport segment (12).</summary>
    public const int SortOrder = 13;
    public const float Width = 150f;

    private readonly ReviewSource _source;
    private string? _label;
    private ReviewSourceKind _labelKind = ReviewSourceKind.None;
    private int _labelNode = int.MinValue;

    public ReviewSourceToolbarCombo(ReviewSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>What the combo's face says now; null while no group is open (nothing is drawn).</summary>
    public string? Label
    {
        get
        {
            if (!_source.IsOpen) return null;
            if (_label is null || _labelKind != _source.Kind || _labelNode != _source.PartNode)
            {
                _labelKind = _source.Kind; _labelNode = _source.PartNode;
                _label = _source.Kind == ReviewSourceKind.Part ? "Part: " + _source.NameOf(_source.PartNode) : "Merged";
            }
            return _label;
        }
    }

    /// <summary>Whether the last <see cref="Draw"/> drew the combo (a rail's observation).</summary>
    public bool Drawn { get; private set; }

    public void Draw()
    {
        Drawn = false;
        if (Label is not { } label) return;
        Drawn = true;
        ImGui.SetNextItemWidth(Width);
        if (!ImGui.BeginCombo("##review_source_combo", label)) return;
        if (ImGui.Selectable("Merged", _source.Kind == ReviewSourceKind.Merged)) _source.SelectMerged();
        foreach (var (node, _) in _source.Participants)
            if (ImGui.Selectable("Part: " + _source.NameOf(node), _source.Kind == ReviewSourceKind.Part && _source.PartNode == node)) _source.SelectPart(node);
        ImGui.EndCombo();
    }

    public void RegisterInto(MainToolbarManager toolbar)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        toolbar.RegisterEntry(EntryId, SortOrder, MainToolbarManager.DefaultEntryHeight, Draw);
    }
}
