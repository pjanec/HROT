using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Replication.Edits;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Presentation.Edits;

/// <summary>
/// ⭐ Edit framework F7 (📄 docs/UX/DESIGN_Edit_Framework.md §2.5 / §2.5a, R-4127, R-4143) — the STICKY, NON-MODAL notification stack for a refused, lost or
/// conflicting edit, at the top-right of the main viewport (360 px wide, newest on top, at most <see cref="MaxShown"/> cards, then <i>+ N more · Dismiss all</i>).
/// ⭐ WHICH edits it shows is <see cref="EditNotifications"/>'s rule (read from the ONE <see cref="EditJournal"/>) — this class only draws it. Not the Blueprints
/// editor's <c>NotificationOverlay</c>: that one is a FADING, input-transparent toast; this one stays until the × and takes clicks (the design rejects the fade).
/// <para>Per frame: <see cref="EditJournal.Refresh(EntityRepository)"/> (nothing else in production calls it — without it an outcome never folds in and an unanswered edit
/// never times out to Lost), <see cref="EditNotifications.Collect"/> into a reused list, draw. 0 B in a steady frame; the card text (it allocates) is cached per request id
/// and status.</para>
/// <para>The card: a left bar (<see cref="StatusPalette.Error"/> for Refused / Lost, <see cref="StatusPalette.Pending"/> for Conflict), the glyph in its colour
/// (CircleXmark · Clock · Warning), the title (label + verdict), the reason, the buttons — <b>Show</b> (selects the entry's unit through the host's selection path), and on a
/// Conflict <b>Reload</b> / <b>Overwrite</b> through host callbacks (not drawn when the host wired none).</para>
/// </summary>
public sealed class EditNotificationOverlay
{
    /// <summary>Cards drawn at most; the rest are the <i>+ N more</i>.</summary>
    public const int MaxShown = 3;
    /// <summary>The mockup's sizes at UI scale 1 (the editor's base font, <see cref="Fdp.Presentation.Fonts.EditorFontService.BaseUiPx"/>). ⭐ CE-4343: every size the
    /// overlay DRAWS is measured from the font each frame (<see cref="Measure"/>) — these are only its scale-1 values; ⛔ never a fixed pixel height.</summary>
    public const float Width = 360f, CardHeight = 62f, Gap = 8f, RightMargin = 12f, TopMargin = 6f, FooterHeight = 18f, ButtonHeight = 20f;

    // ⭐ CE-4343 — this frame's layout, from the font (title row · reason row · button row, each a text line tall; nothing overlaps at any scale)
    private float _s = 1f, _w = Width, _cardH = CardHeight, _gap = Gap, _footerH = FooterHeight, _buttonH = ButtonHeight, _th = 13f;
    private float _titleY, _reasonY, _buttonY, _textX, _glyphX;

    /// <summary>This frame's card width / height / pitch (rails: the layout at a UI scale).</summary>
    public float CardWidthNow => _w;
    /// <summary>This frame's card height.</summary>
    public float CardHeightNow => _cardH;

    private void Measure()
    {
        _th = ImGui.GetTextLineHeight();
        _s = Math.Max(0.5f, ImGui.GetFontSize() / Fdp.Presentation.Fonts.EditorFontService.BaseUiPx);
        _w = Width * _s;
        _gap = Gap * _s;
        _buttonH = MathF.Max(ButtonHeight * _s, _th + 4f * _s);
        // the mockup's baselines (title 20, reason 37, buttons 41, card 62 at scale 1), each scaled — and never closer than a text line apart
        _titleY = 20f * _s - _th + 3f * _s;
        _reasonY = MathF.Max(37f * _s - _th + 3f * _s, _titleY + _th);
        _buttonY = MathF.Max(41f * _s, _reasonY + _th + 1f * _s);
        _cardH = MathF.Max(CardHeight * _s, _buttonY + _buttonH + 1f * _s);
        _footerH = MathF.Max(FooterHeight * _s, _th + 4f * _s);
        _glyphX = 14f * _s;
        _textX = MathF.Max(34f * _s, _glyphX + ImGui.CalcTextSize(IconsFontAwesome6.Clock).X + 4f * _s);   // the title starts after the glyph, never on it
    }

    /// <summary>What a recorded rectangle is (the rails read them).</summary>
    public enum ItemKind : byte { Card, Close, Show, Reload, Overwrite, DismissAll }

    /// <summary>One drawn rectangle, in screen pixels: the card (and its buttons) it belongs to.</summary>
    public readonly record struct Item(ItemKind Kind, int Card, Vector2 Min, Vector2 Max);

    private readonly Action<int>? _show;
    private readonly Action<EditJournalEntry>? _reload, _overwrite;
    private readonly Func<DateTime> _utcNow;
    private readonly List<EditJournalEntry> _shown = new(MaxShown + 1);
    private readonly Dictionary<Guid, (EditStatus Status, string Title)> _titles = new();
    private readonly Item[] _items = new Item[MaxShown * 5 + 2];
    private int _itemCount;

    /// <param name="show">Selects the unit with this NETWORK id (the host's existing selection path); null ⇒ no Show button.</param>
    /// <param name="reload">A Conflict's Reload (the mission plan's reload); with <paramref name="overwrite"/> both or neither is drawn.</param>
    /// <param name="overwrite">A Conflict's Overwrite (the mission plan's force-commit).</param>
    /// <param name="history">⭐ F7 — the Message Log's Edits tab this overlay feeds after each fold (default <see cref="EditHistoryMessageLogSource.Shared"/>, the one every host registers).</param>
    public EditNotificationOverlay(Action<int>? show = null, Action<EditJournalEntry>? reload = null, Action<EditJournalEntry>? overwrite = null, Func<DateTime>? utcNow = null,
        EditHistoryMessageLogSource? history = null)
    {
        _show = show; _reload = reload; _overwrite = overwrite;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _history = history ?? EditHistoryMessageLogSource.Shared;
    }

    private readonly EditHistoryMessageLogSource _history;

    /// <summary>Reload / Overwrite are drawn on a Conflict only when the host wired BOTH.</summary>
    public bool ConflictButtonsWired => _reload != null && _overwrite != null;

    /// <summary>The rectangles drawn last frame (cards, ×, buttons, Dismiss all).</summary>
    public ReadOnlySpan<Item> Items => _items.AsSpan(0, _itemCount);

    /// <summary>The cards drawn last frame.</summary>
    public int ShownCount { get; private set; }

    /// <summary>All notifications there are (drawn + the <i>+ N more</i>).</summary>
    public int TotalCount { get; private set; }

    private static Vector4 Bg => new(0.094f, 0.094f, 0.110f, 1f);        // #18181C
    private static Vector4 Border => new(0.227f, 0.227f, 0.267f, 1f);    // #3A3A44
    private static Vector4 Dim => new(0.50f, 0.50f, 0.50f, 1f);          // #808080 (the ×)
    private static Vector4 Sub => new(0.784f, 0.784f, 0.816f, 1f);       // #C8C8D0 (the reason)
    private static Vector4 Button => new(0.137f, 0.255f, 0.373f, 1f);    // #23415F
    private static Vector4 ButtonHot => new(0.20f, 0.41f, 0.68f, 1f);    // #3369AD

    /// <summary>Folds the journal, collects, draws. Call once per frame, last, from the host's draw (after the windows, so the stack is on top).</summary>
    public void Draw(EntityRepository? world)
    {
        _itemCount = 0; ShownCount = 0; TotalCount = 0;
        if (world == null || ImGui.GetCurrentContext() == IntPtr.Zero) return;
        var journal = EditJournal.Of(world);
        _journal = journal;
        journal.Refresh(EditOutcomeLog.Of(world), _utcNow());                 // ⭐ the one caller: outcomes fold in, an unanswered edit times out to Lost
        _history.Sync(journal);                                                // ⭐ F7 — the history (Message Log ▸ Edits) gets every final answer
        TotalCount = EditNotifications.Collect(journal, _shown, MaxShown);
        ShownCount = _shown.Count;
        if (ShownCount == 0) return;

        var vp = ImGui.GetMainViewport();
        Measure();
        int more = TotalCount - ShownCount;
        float stackH = ShownCount * _cardH + (ShownCount - 1) * _gap;
        bool footer = TotalCount > 1;                                      // one card needs no "Dismiss all"
        float height = stackH + (footer ? _footerH : 0f);
        var origin = new Vector2(vp.WorkPos.X + vp.WorkSize.X - _w - RightMargin * _s, vp.WorkPos.Y + TopMargin * _s);

        ImGui.SetNextWindowPos(origin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(_w, height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        // non-modal by construction: no title bar, never takes focus when it appears, never navigable, covers only its own rectangle
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        bool open = ImGui.Begin("##edit-notifications", flags);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        if (open)
        {
            BringToFront();
            var dl = ImGui.GetWindowDrawList();
            for (int i = 0; i < ShownCount; i++)
                DrawCard(dl, i, _shown[i], origin + new Vector2(0f, i * (_cardH + _gap)));
            if (footer) DrawFooter(dl, more, origin + new Vector2(0f, stackH + 2f * _s));
        }
        ImGui.End();
        if (_titles.Count > 512) _titles.Clear();                             // bounded: the journal holds 256
    }

    [System.Runtime.InteropServices.DllImport("cimgui", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern IntPtr igGetCurrentWindow();
    [System.Runtime.InteropServices.DllImport("cimgui", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void igBringWindowToDisplayFront(IntPtr window);
    private static bool _frontBroken;

    /// <summary>
    /// ⭐ Keeps the stack ABOVE the other windows without taking focus: a plain window drops behind the next one the user clicks (ImGui orders windows by focus), and then its
    /// × would be unreachable. <c>SetNextWindowFocus</c> would bring it up too, but steals the keyboard from whatever the user is typing in — so the display order alone is raised
    /// (ImGui's own <c>BringWindowToDisplayFront</c>, which cimgui exports). If the native symbol is missing the stack still works, only without the guarantee.
    /// </summary>
    private static void BringToFront()
    {
        if (_frontBroken) return;
        try { igBringWindowToDisplayFront(igGetCurrentWindow()); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { _frontBroken = true; }
    }

    private string TitleOf(EditJournalEntry e)
    {
        if (_titles.TryGetValue(e.RequestId, out var c) && c.Status == e.Status) return c.Title;
        string t = EditNotifications.Title(e);                                 // allocates — once per request id + status
        _titles[e.RequestId] = (e.Status, t);
        return t;
    }

    private void Record(ItemKind kind, int card, Vector2 min, Vector2 max)
    {
        if (_itemCount < _items.Length) _items[_itemCount++] = new Item(kind, card, min, max);
    }

    private void DrawCard(ImDrawListPtr dl, int index, EditJournalEntry e, Vector2 p)
    {
        var max = p + new Vector2(_w, _cardH);
        bool conflict = e.Status == EditStatus.Conflict;
        var accent = conflict ? StatusPalette.Pending : StatusPalette.Error;
        dl.AddRectFilled(p, max, ImGui.GetColorU32(Bg), 3f);
        dl.AddRect(p, max, ImGui.GetColorU32(Border), 3f);
        dl.AddRectFilled(p, new Vector2(p.X + 4f * _s, max.Y), ImGui.GetColorU32(accent));
        Record(ItemKind.Card, index, p, max);

        // the glyph, in its colour: CircleXmark (refused) · Clock (lost) · Warning (conflict)
        string glyph = e.Status switch { EditStatus.Lost => IconsFontAwesome6.Clock, EditStatus.Conflict => Icons.Get("form/warning").Glyph, _ => Icons.Get("form/error").Glyph };
        dl.AddText(new Vector2(p.X + _glyphX, p.Y + _titleY), ImGui.GetColorU32(accent), glyph);
        float xBox = _th + 6f * _s;                                                        // the ×'s square, from the font
        float textRight = max.X - xBox - 8f * _s;                                          // title and reason stop before the ×
        var clipMin = new Vector2(p.X + _textX, p.Y);
        dl.PushClipRect(clipMin, new Vector2(textRight, max.Y), true);
        dl.AddText(new Vector2(p.X + _textX, p.Y + _titleY), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), TitleOf(e));
        dl.AddText(new Vector2(p.X + _textX, p.Y + _reasonY), ImGui.GetColorU32(Sub), e.Reason);
        dl.PopClipRect();

        // × — dismiss (kept in the journal: the history)
        var xMin = new Vector2(max.X - xBox - 4f * _s, p.Y + _titleY - 2f * _s);
        ImGui.SetCursorScreenPos(xMin);
        ImGui.PushID(index);
        if (ImGui.InvisibleButton("x", new Vector2(xBox, xBox))) EditNotifications.Dismiss(e);
        bool xHot = ImGui.IsItemHovered();
        Record(ItemKind.Close, index, xMin, xMin + new Vector2(xBox, xBox));
        float xFont = ImGui.GetFontSize() * 16f / Fdp.Presentation.Fonts.EditorFontService.BaseUiPx;   // the mockup's 16 at scale 1
        var xSize = ImGui.CalcTextSize("×") * (xFont / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), xFont, xMin + (new Vector2(xBox, xBox) - xSize) * 0.5f, ImGui.GetColorU32(xHot ? new Vector4(1f, 1f, 1f, 1f) : Dim), "×");

        // the buttons: Show (a unit to select) · on a Conflict Reload / Overwrite (when the host wired them) — on their own row, under the reason
        float bx = p.X + _textX, by = p.Y + _buttonY;
        if (conflict)
        {
            if (ConflictButtonsWired)
            {
                if (DrawButton("Reload", ItemKind.Reload, index, ref bx, by)) _reload!(e);
                if (DrawButton("Overwrite", ItemKind.Overwrite, index, ref bx, by)) _overwrite!(e);
            }
        }
        else if (_show != null && e.NetworkId > 0)
        {
            if (DrawButton("Show", ItemKind.Show, index, ref bx, by)) _show(e.NetworkId);
        }
        ImGui.PopID();
    }

    private bool DrawButton(string label, ItemKind kind, int card, ref float x, float y)
    {
        float w = 14f * _s + ImGui.CalcTextSize(label).X;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.PushStyleColor(ImGuiCol.Button, Button);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ButtonHot);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ButtonHot);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, 0f));
        bool hit = ImGui.Button(label, new Vector2(w, _buttonH));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        Record(kind, card, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        x += w + 6f * _s;
        return hit;
    }

    private void DrawFooter(ImDrawListPtr dl, int more, Vector2 p)
    {
        // "+ N more  ·  Dismiss all", right-aligned under the stack
        string all = "Dismiss all";
        float aw = ImGui.CalcTextSize(all).X;
        var aMin = new Vector2(p.X + _w - 10f * _s - aw, p.Y);
        ImGui.SetCursorScreenPos(aMin);
        ImGui.PushID("all");
        bool hit = ImGui.InvisibleButton("##all", new Vector2(aw, _footerH - 2f * _s));
        bool hot = ImGui.IsItemHovered();
        ImGui.PopID();
        Record(ItemKind.DismissAll, -1, aMin, aMin + new Vector2(aw, _footerH - 2f * _s));
        uint col = ImGui.GetColorU32(hot ? new Vector4(1f, 1f, 1f, 1f) : Sub);
        dl.AddText(aMin, col, all);
        if (more > 0)
        {
            string m = MoreText(more);
            dl.AddText(new Vector2(aMin.X - ImGui.CalcTextSize(m).X, p.Y), ImGui.GetColorU32(Sub), m);
        }
        if (hit) EditNotifications.DismissAll(EditJournalOfLast);
    }

    private EditJournal EditJournalOfLast => _journal!;
    private EditJournal? _journal;
    private string? _moreText; private int _moreFor = -1;
    private string MoreText(int more)
    {
        if (_moreFor != more) { _moreText = "+ " + more + " more  ·  "; _moreFor = more; }
        return _moreText!;
    }
}
