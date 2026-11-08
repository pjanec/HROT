using System.Numerics;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.WindowManager;
using Hrot.Presentation.Edits;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐⭐ U5 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §3 U5, Q3; B-14 rest, Z-2) — <b>Edit ▸ History…</b>: the editor's ONE <see cref="EditHistory"/> as a list of its
/// steps, oldest first, the state before the first step on top (<i>Scenario as opened</i>). The CURRENT position carries a blue dot and the selection bar;
/// the steps undone (a redo is waiting) are dimmed; a click on a row goes to the state AFTER that step by repeated Undo / Redo (<see cref="EditHistory.GoTo"/>).
/// <para>A row that cannot be reached is greyed with the reason — a step on the way is no longer available (a world step whose snapshot the ring evicted, a take
/// that was closed). Outside Edit mode every row is greyed (undo works in Edit mode only) and the header says so.</para>
/// <para>⭐ Hot path: the row texts are rebuilt only when the history (<see cref="EditHistory.Version"/>) or the mode changed; a steady frame draws kept strings, 0 B.</para>
/// </summary>
public sealed class EditHistoryWindow : ManagedWindow
{
    public const string Kind = "edit-history";

    /// <summary>The label of the row for the state before the first step.</summary>
    public const string OriginalLabel = "Scenario as opened";
    /// <summary>The header outside Edit mode.</summary>
    public const string OnlyInEditHeader = "Undo works in Edit mode only.";
    /// <summary>The header in Edit mode.</summary>
    public const string ClickHeader = "Click a step to go back (or forward) to the state after it.";

    private readonly record struct Row(string Display, bool Reachable, bool Current, bool Undone);

    private readonly EditHistory _history;
    private readonly Func<bool> _inEdit;
    private readonly List<Row> _rows = new();
    private int _builtVersion = -1;
    private bool _builtInEdit;
    private int _builtUnavailable = -1;
    private Action<string>? _focus;

    public EditHistoryWindow(EditHistory history, Func<bool> inEdit, string id, string perspective, Vector4 titleBarColor)
        : base(id, "History", perspective, WindowScope.PerspectiveBound)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _inEdit = inEdit ?? throw new ArgumentNullException(nameof(inEdit));
        TitleBarColor = titleBarColor; IsOpen = false;
    }

    /// <inheritdoc/>
    public override void OnRegistered(WindowManager manager) => _focus = manager.FocusWindow;

    /// <summary>Opens (and focuses) the window — Edit ▸ History….</summary>
    public void Show() { IsOpen = true; _focus?.Invoke(Id); }

    /// <summary>The rows as drawn: row <c>p</c> is the state after <c>p</c> steps (rails).</summary>
    public IReadOnlyList<string> Rows { get { Rebuild(); var l = new List<string>(_rows.Count); foreach (var r in _rows) l.Add(r.Display); return l; } }

    /// <summary>Row <paramref name="position"/> can be clicked (in Edit mode, every step on the way available).</summary>
    public bool IsReachable(int position) { Rebuild(); return (uint)position < (uint)_rows.Count && _inEdit() && _rows[position].Reachable; }

    /// <summary>Row <paramref name="position"/> is the current one.</summary>
    public bool IsCurrent(int position) { Rebuild(); return (uint)position < (uint)_rows.Count && _rows[position].Current; }

    /// <summary>A click on row <paramref name="position"/>: go to the state after that many steps. False (nothing moved) outside Edit mode or when a step on the way is unavailable.</summary>
    public bool Click(int position) => _inEdit() && _history.GoTo(position);

    private void Rebuild()
    {
        bool edit = _inEdit();
        // a step can become unavailable WITHOUT the history changing (the ring evicts an old slot for a newer capture): count them, a cheap loop, no allocation
        int unavailable = 0;
        for (int i = 0; i < _history.Count; i++) if (!_history.StepAvailable(i)) unavailable++;
        if (_builtVersion == _history.Version && _builtInEdit == edit && _builtUnavailable == unavailable && _rows.Count == _history.Count + 1) return;
        _builtVersion = _history.Version; _builtInEdit = edit; _builtUnavailable = unavailable;
        _rows.Clear();
        int at = _history.Position;
        for (int p = 0; p <= _history.Count; p++)
        {
            string label = p == 0 ? OriginalLabel : _history.StepLabel(p - 1);
            bool reach = _history.CanGoTo(p);
            string display = label;
            if (!reach && p != at)
            {
                // the first step on the way that cannot run, named — the reason the row is greyed
                int blocked = -1; bool undo = p < at;
                if (undo) { for (int i = at - 1; i >= p; i--) if (!_history.StepAvailable(i)) { blocked = i; break; } }
                else { for (int i = at; i < p; i++) if (!_history.StepAvailable(i)) { blocked = i; break; } }
                if (blocked >= 0) display = $"{label}   -   '{_history.StepLabel(blocked)}' can no longer be {(undo ? "undone" : "redone")}";
            }
            _rows.Add(new Row(display, reach, p == at, p > at));
        }
    }

    protected override void DrawClientArea() => DrawInto();

    private readonly List<(Vector2 Min, Vector2 Max)> _rects = new();
    /// <summary>The selectable bar of each row as last drawn (screen coordinates) — a frame rail asserts the alignment (R-4138: layout is measured).</summary>
    internal IReadOnlyList<(Vector2 Min, Vector2 Max)> LastRowRects => _rects;

    private static readonly Vector4 Dim = new(1f, 1f, 1f, 0.55f);

    /// <summary>The window's content, into the current ImGui window (a frame rail draws it without the window manager).</summary>
    public void DrawInto()
    {
        Rebuild();
        bool edit = _inEdit();
        ImGui.TextDisabled(edit ? ClickHeader : OnlyInEditHeader);
        float dot = ImGui.CalcTextSize(IconsFontAwesome6.Circle).X;
        float gap = ImGui.GetStyle().ItemSpacing.X;
        _rects.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool enabled = edit && row.Reachable;
            ImGui.PushID(i);
            bool clicked = ImGui.Selectable("##row", row.Current, enabled ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled);
            var min = ImGui.GetItemRectMin();
            _rects.Add((min, ImGui.GetItemRectMax()));
            // the text goes over the bar with the draw list (no cursor games: SetCursorPos would extend the window's bounds without an item)
            var dl = ImGui.GetWindowDrawList();
            if (row.Current) dl.AddText(min, ImGui.GetColorU32(IconPalette.Blue), IconsFontAwesome6.Circle);   // the position: a blue dot (the colour of its meaning)
            var at = new Vector2(min.X + dot + gap, min.Y);
            if (!enabled) dl.AddText(at, ImGui.GetColorU32(ImGuiCol.TextDisabled), row.Display);
            else if (row.Undone) dl.AddText(at, ImGui.GetColorU32(Dim), row.Display);
            else dl.AddText(at, ImGui.GetColorU32(ImGuiCol.Text), row.Display);
            ImGui.PopID();
            if (clicked) Click(i);
        }
    }
}
