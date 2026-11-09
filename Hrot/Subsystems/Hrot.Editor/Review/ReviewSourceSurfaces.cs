using System;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Fdp.Presentation.Panels.ReplayBrowser;
using ImGuiNET;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D9, mockup <c>review-perspective-visual.svg</c>) — <b>the status bar's SOURCE segment</b>, its own section at the bar's LEFT end
/// (the end <c>StationSeatStatusBarSection</c> leaves free for it, CE-3419): Merged ⇒ <i>"Merged · 4 participants (…) — step / scrub"</i> in teal; a part ⇒ <i>"Part: Node 2"</i>; an ordinary AAR ⇒
/// nothing. 🔒 User: <i>"We need some indication that we are actually running in merged federate view, I guess. Maybe the status bar is a good place."</i> A click opens the Source menu (<see cref="ReviewSourceSwitch"/>, mockup ③).
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
        var glyph = NodeEditor.Core.Widgets.Icons.Get(_source.FaceIconKey);   // ⭐ mockup ③ — the same glyph the toolbar's face carries, before the text
        ImGui.TextColored(glyph.Color, glyph.Glyph); ImGui.SameLine(0f, 6f);
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

/// <summary>
/// ⭐ G-7 S5 follow-up (CE-4354, mockup ③ of <c>docs/UX/better-merge-visual.svg</c>) — <b>the Source menu</b>, shared by the status bar's popup and the toolbar's combo: header <i>Source of the world</i> · <i>Merged — every part</i>
/// (sitemap, teal) · one row per participant <i>"&lt;name&gt;'s part"</i> with its ROLE icon in its colour (the <c>node/*</c> keys of <c>Icons</c>) · a separator · the <i>Local entities shown from</i> checkboxes (the Federation panel's
/// ONE <see cref="FederationPanel.DrawLocalEntities"/>) · a separator · <i>Offsets…</i> (expert) which asks the host to open the Federation window. ⛔ There is NO Single-Node / Merged radio: the rows ARE that choice (ONE control per choice).
/// Draws every frame while open ⇒ 0 B steady (labels cached in <see cref="ReviewSource"/>).
/// </summary>
public static class ReviewSourceSwitch
{
    public const string Header = "Source of the world";
    public const string MergedRow = "Merged — every part";
    public const string OffsetsRow = "Offsets…  (expert — a manual slider per node, default 0)";

    /// <summary>A rail's measurement (set <see cref="CaptureLayout"/> first): what was drawn, in order, and each row's icon / text x and y as last drawn.</summary>
    public static bool CaptureLayout;
    public static readonly System.Collections.Generic.List<string> DrawnTexts = new();
    public static readonly System.Collections.Generic.List<(string Label, float IconX, float TextX, float Y)> Rows = new();

    public static void Draw(ReviewSource source)
    {
        if (CaptureLayout) { DrawnTexts.Clear(); Rows.Clear(); }
        ImGui.TextDisabled(Header); Note(Header);
        if (IconRow("##src_merged", MergedRow, NodeEditor.Core.Widgets.Icons.Get("review/merged"), source.Kind == ReviewSourceKind.Merged)) source.SelectMerged();
        var parts = source.Participants;
        for (int i = 0; i < parts.Count; i++)
        {
            int node = parts[i].Node;
            ImGui.PushID(node);
            if (IconRow("##src_part", source.PartLabel(node), NodeEditor.Core.Widgets.Icons.Get(source.RoleKeyOf(node)), source.Kind == ReviewSourceKind.Part && source.PartNode == node)) source.SelectPart(node);
            ImGui.PopID();
        }
        if (source.SwitchPanel is { } panel)
        {
            ImGui.Separator();
            Note(FederationPanel.LocalsHeader);
            panel.DrawLocalEntities();   // ONE component: the Replay Browser's Federation window draws the same checkboxes
        }
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        if (ImGui.Selectable(OffsetsRow)) source.RequestOffsets();
        ImGui.PopStyleColor();
        Note(OffsetsRow);
    }

    private static void Note(string text) { if (CaptureLayout) DrawnTexts.Add(text); }

    /// <summary>A selectable row whose coloured icon sits in a fixed cell before the text (one x for every row).</summary>
    private static bool IconRow(string id, string label, NodeEditor.Core.Widgets.IconGlyph icon, bool selected)
    {
        var at = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Selectable(id, selected);
        float cell = ImGui.GetFontSize() * 1.6f, pad = 4f;
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(new Vector2(at.X + pad, at.Y), ImGui.ColorConvertFloat4ToU32(icon.Color), icon.Glyph);
        dl.AddText(new Vector2(at.X + pad + cell, at.Y), ImGui.GetColorU32(ImGuiCol.Text), label);
        if (CaptureLayout) { Rows.Add((label, at.X + pad, at.X + pad + cell, at.Y)); DrawnTexts.Add(label); }
        return clicked;
    }
}

/// <summary>
/// ⭐ G-7 S5 follow-up (mockup ③) — <b>the Source FACE</b>: a frame with the coloured glyph, <i>"Merged · N participants"</i> (or the part's name) and ▾; a click opens the popup <see cref="ReviewSourceSwitch"/> draws.
/// The toolbar combo draws it as a framed button; the status bar prefixes the same glyph to its text.
/// </summary>
public static class ReviewSourceFace
{
    /// <summary>Draws the face (<paramref name="width"/> wide) and, on a click, opens <paramref name="popupId"/> under it. Returns the frame's rectangle.</summary>
    public static (Vector2 Min, Vector2 Max) Draw(ReviewSource source, string label, string popupId, float width)
    {
        var at = ImGui.GetCursorScreenPos();
        float h = ImGui.GetFrameHeight();
        var size = new Vector2(width, h);
        bool clicked = ImGui.InvisibleButton(popupId + "_face", size);
        bool hovered = ImGui.IsItemHovered();
        var min = at; var max = at + size;
        var dl = ImGui.GetWindowDrawList();
        var icon = NodeEditor.Core.Widgets.Icons.Get(source.FaceIconKey);
        dl.AddRectFilled(min, max, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 3f);
        if (source.Kind == ReviewSourceKind.Merged) dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(ReviewSourceStatusSection.Teal with { W = 0.8f }), 3f);
        float ty = at.Y + (h - ImGui.GetFontSize()) * 0.5f;
        dl.AddText(new Vector2(at.X + 7f, ty), ImGui.ColorConvertFloat4ToU32(icon.Color), icon.Glyph);
        dl.AddText(new Vector2(at.X + 7f + ImGui.GetFontSize() * 1.6f, ty), ImGui.GetColorU32(ImGuiCol.Text), label);
        dl.AddText(new Vector2(max.X - ImGui.GetFontSize() - 2f, ty), ImGui.GetColorU32(ImGuiCol.Text), "\uf078");   // FA chevron-down (▾ is not in every face)
        if (clicked) ImGui.OpenPopup(popupId);
        return (min, max);
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
    public const float Width = 220f;

    private readonly ReviewSource _source;

    public ReviewSourceToolbarCombo(ReviewSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>What the combo's face says now: <c>"Merged · 4 participants"</c> / <c>"CGF-1's part"</c> (mockup ③); null while no group is open (nothing is drawn).</summary>
    public string? Label => _source.IsOpen ? _source.FaceLabel : null;

    /// <summary>Whether the last <see cref="Draw"/> drew the combo (a rail's observation).</summary>
    public bool Drawn { get; private set; }

    /// <summary>Whether the menu was drawn on the last frame, and the face's screen rectangle (a rail's measurement).</summary>
    internal bool PopupDrawn { get; private set; }
    internal (Vector2 Min, Vector2 Max) FaceRect;

    public const string PopupId = "##review_source_combo_menu";

    public void Draw()
    {
        Drawn = false; PopupDrawn = false;
        if (Label is not { } label) return;
        Drawn = true;
        FaceRect = ReviewSourceFace.Draw(_source, label, PopupId, Width);
        if (ImGui.IsPopupOpen(PopupId)) ImGui.SetNextWindowPos(new Vector2(FaceRect.Min.X, FaceRect.Max.Y));
        if (!ImGui.BeginPopup(PopupId)) return;
        PopupDrawn = true;
        ReviewSourceSwitch.Draw(_source);
        ImGui.EndPopup();
    }

    public void RegisterInto(MainToolbarManager toolbar)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        toolbar.RegisterEntry(EntryId, SortOrder, MainToolbarManager.DefaultEntryHeight, Draw);
    }
}

/// <summary>
/// ⭐ G-7 S5 follow-up (mockup ③) — <b>the editor's Federation window</b>: the Replay Browser's own <c>FederationWindow</c> class over the open group's <see cref="FederationPanel"/> (the per-node offset sliders), registered in the Review
/// perspective and opened by the Source menu's <i>Offsets…</i> line (<see cref="ReviewSource.OffsetsRequested"/>). The panel is created per group (<see cref="ReviewSource.Load"/>), so the window is re-registered under the same id each time —
/// as the Replay Browser does (<c>RegisterFederationWindow</c>). ⛔ No radio, no checkboxes here: the Source menu holds both (<see cref="FederationPanel.ShowModeRadio"/>, <see cref="FederationPanel.ShowLocals"/>) — ONE control per choice.
/// </summary>
public sealed class ReviewFederationWindowHost
{
    public const string WindowId = "editor_review_federation";
    public const string Title = "Federation (offsets)";

    private readonly ReviewSource _source;
    private readonly Action<ManagedWindow> _register;
    private readonly string _perspective;
    private readonly Vector4 _colour;
    private FederationPanel? _panel;

    public ReviewFederationWindowHost(ReviewSource source, Action<ManagedWindow> register, string perspective, Vector4 colour)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _perspective = perspective; _colour = colour;
        source.Changed += Sync;
        source.OffsetsRequested += Open;
        Sync();
    }

    /// <summary>The window over the CURRENT group's panel; null while no group is open.</summary>
    public Fdp.Presentation.Windows.ReplayBrowser.FederationWindow? Window { get; private set; }

    private void Sync()
    {
        var panel = _source.SwitchPanel;
        if (panel is null || ReferenceEquals(panel, _panel)) return;
        _panel = panel;
        panel.ShowModeRadio = false; panel.ShowLocals = false;
        Window = new Fdp.Presentation.Windows.ReplayBrowser.FederationWindow(WindowId, Title, _perspective, panel, _colour) { IsOpen = false };
        _register(Window);
    }

    /// <summary>Opens the window (the Source menu's <i>Offsets…</i>).</summary>
    public void Open() { Sync(); if (Window is not null) Window.IsOpen = true; }
}
