using System.Numerics;
using Fdp.Presentation.Input;
using Fdp.Toolkit.Input;
using ImGuiNET;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐⭐ B-10 K6 (📄 docs/UX/DESIGN_Input_And_Commands.md §6f) — <b>Edit ▸ Keyboard shortcuts…</b> (Ctrl+K): every binding of the ONE keymap — keys and the K7 mouse gestures — grouped by scope, with a search box.
/// Read-only; it owns no list (<see cref="ShortcutRows"/> reads <see cref="Keymap.Process"/>). House style: the chord in the accent colour, the scope shown once per group, three aligned columns.
/// </summary>
public sealed class KeyboardShortcutsWindow : ManagedWindow
{
    /// <summary>The window's stable id.</summary>
    public const string WindowId = "shell_keyboard_shortcuts";
    /// <summary>The command that opens it.</summary>
    public const string CommandId = "shell.keyboard-shortcuts";

    private readonly ShortcutRows _rows;
    private readonly byte[] _search = new byte[96];
    private string _filter = "";
    private string _filterSource = "";

    /// <summary>Layout, for the rail: where the search box and each column were drawn this frame (screen coordinates).</summary>
    public Vector2 SearchMin, SearchMax;
    /// <summary>The x of each column's cell, per drawn row (grown, never shrunk).</summary>
    public readonly System.Collections.Generic.List<(float Scope, float Chord, float Action, float Y)> RowCells = new();

    /// <summary>Builds the window over <paramref name="keymap"/>.</summary>
    public KeyboardShortcutsWindow(Keymap keymap) : base(WindowId, "Keyboard shortcuts", "any", WindowScope.Global)
    {
        _rows = new ShortcutRows(keymap);
        IsOpen = false;
        ShowInMenu = false;
    }

    /// <summary>Opens (and raises) the window.</summary>
    public void Show() => IsOpen = true;

    /// <summary>The rows the current filter shows (the model, for rails).</summary>
    public System.Collections.Generic.IReadOnlyList<ShortcutRow> Rows => _rows.Rows(_filter);

    /// <summary>Sets the search text (rails; the user types it).</summary>
    public void SetFilter(string text) { _filter = text ?? ""; }

    protected override void BeforeBegin() => ImGui.SetNextWindowSize(new Vector2(640, 420), ImGuiCond.FirstUseEver);

    protected override void DrawClientArea()
    {
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##shortcut_search", _search, (uint)_search.Length))
            _filter = ReadSearch();
        SearchMin = ImGui.GetItemRectMin();
        SearchMax = ImGui.GetItemRectMax();
        if (_search[0] == 0 && !ImGui.IsItemActive())   // the hint, drawn over the empty box
            ImGui.GetWindowDrawList().AddText(SearchMin + new Vector2(ImGui.GetStyle().FramePadding.X, ImGui.GetStyle().FramePadding.Y),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), "Search shortcuts…");
        ImGui.Spacing();

        var rows = _rows.Rows(_filter);
        RowCells.Clear();
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerH;
        if (!ImGui.BeginTable("##shortcuts", 3, flags, new Vector2(0, -1f))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("Shortcut", ImGuiTableColumnFlags.WidthFixed, 270f);
        ImGui.TableSetupColumn("Does", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        var accent = new Vector4(0.36f, 0.62f, 0.95f, 1f);
        string? lastScope = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            float x0 = ImGui.GetCursorScreenPos().X, y = ImGui.GetCursorScreenPos().Y;
            if (r.Scope != lastScope)
            {
                ImGui.TextDisabled(r.Scope);
                lastScope = r.Scope;
            }
            ImGui.TableSetColumnIndex(1);
            float x1 = ImGui.GetCursorScreenPos().X;
            ImGui.TextColored(accent, r.Binding);
            ImGui.TableSetColumnIndex(2);
            float x2 = ImGui.GetCursorScreenPos().X;
            ImGui.TextUnformatted(r.Action);
            RowCells.Add((x0, x1, x2, y));
        }
        ImGui.EndTable();
    }

    private string ReadSearch()
    {
        int n = System.Array.IndexOf(_search, (byte)0);
        if (n < 0) n = _search.Length;
        _filterSource = System.Text.Encoding.UTF8.GetString(_search, 0, n);
        return _filterSource;
    }
}
