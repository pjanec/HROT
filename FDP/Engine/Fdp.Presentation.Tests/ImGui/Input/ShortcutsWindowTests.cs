using System;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Input;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Input;
using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;
using ImGuiApi = ImGuiNET.ImGui;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.ImGui.Input;

/// <summary>
/// ⭐⭐ B-10 K6 (docs/UX/DESIGN_Input_And_Commands.md §6f) — the Keyboard shortcuts window is a VIEW of the ONE keymap: keys and the K7 gesture rows, by scope, searchable; the window manager's router and the
/// window read <see cref="Keymap.Process"/>; Ctrl+K opens it; its frame is measured (R-4138: aligned columns, the search box on top) and a steady frame allocates nothing.
/// </summary>
[Collection("ImGui Sequential")]
public sealed class ShortcutsWindowTests : IDisposable
{
    public ShortcutsWindowTests() => KeyboardFocus.MapHasKeyboard = true;
    public void Dispose() => KeyboardFocus.MapHasKeyboard = true;

    private static Keymap Sample()
    {
        var map = new Keymap();
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl), InputScope.FocusedWindow, "Graph canvas", ActionRef.Command("edit.undo")));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.S, KeyModifiers.Ctrl), InputScope.Global, null, ActionRef.Command("shell.save")));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.C, KeyModifiers.None), InputScope.Map, "unit", ActionRef.Global(7)));
        map.SetLabel(ActionRef.Command("edit.undo"), "Undo"); map.SetLabel(ActionRef.Command("shell.save"), "Save"); map.SetLabel(ActionRef.Global(7), "Center View");
        map.DeclareGesture(new(new MapGesture(GestureButton.Right, GestureKind.Click, GestureModifiers.Ctrl, GestureTarget.Hostile), InputScope.Hat, "Command", ActionRef.Command("rts.attack.force")));
        map.SetLabel(ActionRef.Command("rts.attack.force"), "RTS: attack whatever is there");
        return map;
    }

    /// <summary>🔴 Red-proof (compiles): the rows listing key rows only (gesture loop dropped) ⇒ the hat's gesture is missing ⇒ red.</summary>
    [Fact]
    public void TheRows_ListKeysAndGestures_ByScope_WithLabels_AndFilter()
    {
        var rows = new ShortcutRows(Sample());
        var all = rows.Rows("");
        Assert.Equal(new[] { "Everywhere", "In Graph canvas", "Map · unit", "Command hat" }, all.Select(r => r.Scope).ToArray());
        Assert.Equal(("Ctrl+S", "Save"), (all[0].Binding, all[0].Action));
        Assert.Equal(("Ctrl+Z", "Undo"), (all[1].Binding, all[1].Action));
        Assert.Equal(("C", "Center View"), (all[2].Binding, all[2].Action));
        Assert.True(all[3].IsGesture);
        Assert.Equal("Ctrl + Right click on a hostile", all[3].Binding);
        Assert.Equal("RTS: attack whatever is there", all[3].Action);

        Assert.Single(rows.Rows("undo"));                     // the action's label
        Assert.Single(rows.Rows("CTRL+S"));                   // the chord, any case
        Assert.Single(rows.Rows("command hat"));              // the scope
        Assert.Empty(rows.Rows("zzz"));
        Assert.Equal(4, rows.Rows("").Count);
    }

    /// <summary>
    /// ⭐ B-10 K4 (§6g) — the Keyboard shortcuts window shows a held or value binding with its KIND: <c>Shift (hold)</c>, <c>W (axis)</c>, <c>S (axis −1)</c>, <c>Wheel (axis)</c>, <c>Right button (hold)</c>;
    /// a Press stays the chord alone; the debug keys are their own group.
    /// 🔴 Red-proof (compiles): <c>BindingText</c> returning the chord for every kind ⇒ the kind is not shown ⇒ red.
    /// </summary>
    [Fact]
    public void K4_HoldAndAxisRows_ShowTheirKind()
    {
        var map = new Keymap();
        var a = ActionRef.Command("t.run"); var b = ActionRef.Command("t.fwd"); var c = ActionRef.Command("t.zoom"); var d = ActionRef.Command("t.cam"); var f = ActionRef.Command("t.dbg");
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.Unknown, KeyModifiers.Shift), InputScope.Hat, "Embodied", a, KeyTrigger.Hold));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.W, KeyModifiers.None), InputScope.Hat, "Embodied", b, KeyTrigger.Axis, 1f));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.S, KeyModifiers.None), InputScope.Hat, "Embodied", b, KeyTrigger.Axis, -1f));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.MouseRight, KeyModifiers.None), InputScope.Hat, "Embodied", c, KeyTrigger.Hold));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.MouseWheel, KeyModifiers.Ctrl), InputScope.Hat, "Embodied", d, KeyTrigger.Axis, 1f));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.F3, KeyModifiers.None), InputScope.Global, "debug", f));
        foreach (var x in new[] { a, b, c, d, f }) map.SetLabel(x, x.CommandId!);
        var rows = new ShortcutRows(map).Rows("");
        string Bind(string action) => rows.First(r => r.Action == action && r.Binding != "S (axis \u22121)").Binding;
        Assert.Equal("Shift (hold)", Bind("t.run"));
        Assert.Equal("W (axis)", Bind("t.fwd"));
        Assert.Contains(rows, r => r.Binding == "S (axis \u22121)");
        Assert.Equal("Right button (hold)", Bind("t.zoom"));
        Assert.Equal("Ctrl+Wheel (axis)", Bind("t.cam"));
        Assert.Equal("F3", Bind("t.dbg"));
        Assert.Equal("Debug keys", rows.First(r => r.Action == "t.dbg").Scope);
        Assert.Equal("Embodied hat", rows.First(r => r.Action == "t.run").Scope);
    }

    /// <summary>⛔ Hot path (R-2084) — a steady call returns the cached list: the same instance, 0 B.</summary>
    [Fact]
    public void ASteadyCall_AllocatesNothing_AndARebuildHappensOnlyWhenTheKeymapMoves()
    {
        var map = Sample();
        var rows = new ShortcutRows(map);
        var first = rows.Rows("");
        for (int i = 0; i < 3; i++) rows.Rows("");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Assert.Same(first, rows.Rows(""));
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.H, KeyModifiers.None), InputScope.Map, "unit", ActionRef.Global(9)));
        Assert.Equal(5, rows.Rows("").Count);                 // the keymap moved ⇒ rebuilt
    }

    /// <summary>
    /// ⭐⭐ The join and the real frame: the window manager's router IS <see cref="Keymap.Process"/>; the shell's commands (Ctrl+S …) are listed with their labels; Ctrl+K opens the window; the drawn window
    /// is MEASURED — the search box is above the first row, the three columns sit at one x in every row, the rows go down — and a steady frame of the open window allocates nothing.
    /// 🔴 Red-proof (compiles): the window manager building its router over <c>new Keymap()</c> ⇒ the first assertion is red; the table's columns drawn at different x per row ⇒ the measured rail is red.
    /// </summary>
    [Fact]
    public void OnARealFrame_CtrlKOpensTheWindow_ItListsTheOneKeymap_AndItsLayoutIsMeasured()
    {
        using var imgui = new ImGuiTestFixture();
        using var atlas = new IconAtlas(new IntPtr(1), 256f, 256f, 16f);
        var wm = new WM(atlas);
        Assert.Same(Keymap.Process, wm.Keys.Keymap);
        var win = wm.ShortcutsWindow;
        Assert.False(win.IsOpen);
        for (int i = 0; i < 4; i++)   // a few rows more than the shell's own, so "one x for every row" is a real claim
        {
            var id = ActionRef.Command("test.row" + i);
            Keymap.Process.Declare(new KeymapEntry(new KeyBinding(EditorKey.F12, KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Alt | (i % 2 == 0 ? KeyModifiers.None : KeyModifiers.Super)), InputScope.Modal, "test" + i, id));
            Keymap.Process.SetLabel(id, "Test row " + i);
        }
        var io = ImGuiApi.GetIO();

        void Frame() { imgui.NewFrame(); wm.Render(); imgui.Render(); }
        Frame(); Frame();
        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(ImGuiKey.K, true);
        Frame();
        io.AddKeyEvent(ImGuiKey.K, false);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Frame(); Frame();
        Assert.True(win.IsOpen, "Ctrl+K (a Global binding) opens it");

        var rows = win.Rows;
        Assert.Contains(rows, r => r.Scope == "Everywhere" && r.Binding == "Ctrl+K" && r.Action == "Keyboard shortcuts…");   // listed from the SAME keymap that fired it

        // measured layout
        Assert.True(win.RowCells.Count >= 5);
        Assert.True(win.SearchMax.Y <= win.RowCells[0].Y, "the search box sits above the first row");
        float x1 = win.RowCells[0].Chord, x2 = win.RowCells[0].Action;
        Assert.True(win.RowCells[0].Scope < x1 && x1 < x2, "columns run Where → Shortcut → Does");
        for (int i = 0; i < win.RowCells.Count; i++)
        {
            Assert.Equal(x1, win.RowCells[i].Chord);           // one x for every chord
            Assert.Equal(x2, win.RowCells[i].Action);          // one x for every action
            if (i > 0) Assert.True(win.RowCells[i].Y > win.RowCells[i - 1].Y, "rows go down");
        }

        // steady frame: nothing allocated by the window's model (the ImGui calls are stackalloc-marshalled)
        for (int i = 0; i < 3; i++) Frame();
        var model = win.Rows;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Assert.Same(model, win.Rows);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
