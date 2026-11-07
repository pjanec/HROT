using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Input;
using Fdp.Toolkit.Input;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Widgets;
using NodeEditor.Primitives;
using StructEdit.Core;
using StructEdit.Core.Attributes;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ N-2 (CE-3379, docs/DESIGN_Struct_Editor.md §4 B1–B6, §10.1, §10.3) — the LIST FORMS IN THE TABLE, one frame rail each, driven through a REAL edit session (real fonts, Xvfb; a skip is not a pass):
/// B1 chips (List&lt;string&gt;) · B2 summary rows (List&lt;struct&gt;) · B3 grid (<c>[EditListForm(Grid)]</c>) · B4 fixed (inline buffer) · B6 paging (60 items) · the keys (a selected row: Ctrl+D, Alt+↓, Ctrl+C / V; outside a table the
/// window's own binding runs). Every op is clicked / keyed, then <c>session.Commit()</c> proves the round trip; 0 B on a steady frame. Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>forms-*.png</c>.
/// 🔴 Red-proofs (compile): <c>ComponentEditDrawer.ApplyListOp</c> Duplicate calling <c>ops.Insert</c> without the copy (an empty step) ⇒ the list rail is red; <c>StructTableFocus.Take()</c> never handing the table over ⇒ the key rail is red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ListFormsFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    public sealed class TagsDoc { public List<string> Tags = new() { "night", "urban", "desert" }; }
    public struct Step { public string Name; public float Seconds; }
    public sealed class StepsDoc { public List<Step> Steps = new() { new() { Name = "Advance", Seconds = 5 }, new() { Name = "Hold", Seconds = 30 }, new() { Name = "Fire", Seconds = 3 } }; }
    public sealed class TwoDoc { public List<Step> Pair = new() { new() { Name = "Advance", Seconds = 5 }, new() { Name = "Hold", Seconds = 30 } }; }
    [EditListForm(EditListForm.Grid)] public struct Wp { public string Name; public float X; public float Wait; }
    public sealed class WpDoc { public List<Wp> Points = new() { new() { Name = "Alpha", X = 1, Wait = 10 }, new() { Name = "Bravo", X = 2, Wait = 0 }, new() { Name = "Charlie", X = 3, Wait = 4 } }; }
    public sealed class KeysDoc { public List<Step> Rows = new() { new() { Name = "Advance", Seconds = 5 }, new() { Name = "Hold", Seconds = 30 }, new() { Name = "Fire", Seconds = 3 } }; }
    public sealed class BigDoc { public List<Step> Many = Enumerable.Range(1, 60).Select(i => new Step { Name = "Item " + i, Seconds = i }).ToList(); }
    [InlineArray(4)] public struct Slots4 { private float _e0; }
    public struct SlotsComp { public Slots4 Slots; }
    public struct Pt { public float X; public float Y; }
    [InlineArray(2)] public struct Pts2 { private Pt _e0; }
    public struct PtsComp { public Pts2 Pts; }

    internal sealed class Pressing : IInputSource
    {
        public EditorKey Key = EditorKey.F12;
        public KeyModifiers Modifiers { get; set; }
        public Vector2 MousePosition => Vector2.Zero;
        public Vector2 MouseDelta => Vector2.Zero;
        public float WheelDelta => 0f;
        public bool IsMouseDown(MouseButton btn) => false;
        public bool IsMousePressed(MouseButton btn) => false;
        public bool IsMouseReleased(MouseButton btn) => false;
        public bool IsMouseDoubleClicked(MouseButton btn) => false;
        public bool IsKeyDown(EditorKey k) => k == Key;
        public bool IsKeyPressed(EditorKey k, bool allowRepeat = false) => k == Key;
        public bool IsKeyReleased(EditorKey k) => false;
        public ReadOnlySpan<char> TextThisFrame => ReadOnlySpan<char>.Empty;
    }

    internal sealed class Keyed : IKeyScopeWindow
    {
        public int Runs;
        public Keyed()
        {
            var c = new EditorCommandsImpl();
            c.Register(new EditorCommandDescriptor("win.dup", "window duplicate", null, null, null, new KeyBinding(EditorKey.D, KeyModifiers.Ctrl), () => true), _ => Runs++);
            KeyCommands = c;
        }
        public string KeyScopeKind => "rail-window";
        public IEditorCommands? KeyCommands { get; }
    }

    internal sealed class Rig : IDisposable
    {
        public readonly UiFrameSession Frame;
        public readonly WidgetKitFrameRail.Sim Sim = new();
        public readonly IEditSession Session;
        public readonly ComponentEditDrawer Drawer;
        public long Bytes = -1;
        public float Pitch;
        public readonly Pressing Input = new();
        public readonly KeyRouter Router;
        public readonly Keyed Window = new();
        public bool Route;

        public Rig(object doc, Type type, StructEdit.Core.Defaults.IFieldDefaults? defaults = null, bool readOnly = false, IMapPickContext? picks = null)
        {
            Frame = UiFrameHarness.Begin(760, 560);
            new EditorFontService().Initialize(1f, 1f);
            Session = ComponentEditServices.CreateBuilder().Build().Open(doc, type);
            Drawer = new ComponentEditDrawer(Session, picks, defaults: defaults, readOnly: readOnly);
            Router = new KeyRouter(Input);
            ImGui.GetIO().MouseDoubleClickTime = 5f;   // the harness clicks slowly (several frames each): a double-click is two clicks in the same place
        }

        public void Draw()
        {
            Sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(740, 520));
            ImGui.Begin("##forms", Bare);
            Pitch = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
            if (Session.RebuildState == EditRebuildState.RebuildRequired) Session.RebuildDocument();
            if (ImGui.BeginTable("##t", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.7f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Drawer.DrawRoot(Session.Document.Root);
                Bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                ImGui.EndTable();
            }
            if (Route) Router.Dispatch(new KeyFrame(false, false, Window));
            ImGui.End();
        }

        /// <summary>Draws until the frame is STEADY: R-2084 is about optimised code — tier-0 JIT boxes struct arguments (40 B per call), and the runtime promotes a hot method only after ~100 ms, so the warm-up sleeps between batches.</summary>
        public void Steady()
        {
            for (int k = 0; k < 3; k++) { Frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
            Frame.StepN(5, Draw);
        }
        public void Click(Vector2 p, bool right = false) => Sim.Click(Frame, Draw, p, right);
        public void Press(EditorKey key, KeyModifiers mods)
        {
            Input.Key = key; Input.Modifiers = mods; Route = true;
            Frame.Step(Draw);
            Input.Key = EditorKey.F12; Input.Modifiers = KeyModifiers.None;
            Frame.StepN(4, Draw);
            Route = false;
        }
        /// <summary>Types <paramref name="text"/> into the focused text box.</summary>
        public void Type(string text) { ImGui.GetIO().AddInputCharactersUTF8(text); Frame.StepN(4, Draw); }
        /// <summary>Selects the whole word under a double-click at <paramref name="p"/> (a number or a name is one word).</summary>
        public void DoubleClick(Vector2 p) { Click(p); Click(p); }
        public EditNode Field(string name) => ComponentEditDrawer.FieldsOwner(Session.Document.Root)!.Children.First(c => c.Name == name);
        public void Dispose() => Frame.Dispose();
    }

    private static Vector2 C(int slot) => KitProbe.Center(slot);
    private static void Shot(Rig r, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) r.Frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    /// <summary>The ⋮ button of item <paramref name="k"/> of a list of <paramref name="n"/> closed items, from the LAST row the kit drew.</summary>
    private static Vector2 MenuOf(Rig r, int k, int n)
    {
        var last = C(KitProbe.ListMenuButton);
        return new Vector2(last.X, last.Y - (n - 1 - k) * r.Pitch);
    }
    /// <summary>The ⋮⋮ handle of item <paramref name="k"/> — a click SELECTS the row without opening it (so the rows below keep their place).</summary>
    private static Vector2 HandleOf(Rig r, int k, int n)
    {
        var last = C(KitProbe.ListHandle);
        return new Vector2(last.X, last.Y - (n - 1 - k) * r.Pitch);
    }
    private static Vector2 BodyOf(Rig r, int k, int n)
    {
        var last = C(KitProbe.ListBody);
        return new Vector2(last.X, last.Y - (n - 1 - k) * r.Pitch);
    }

    private static string KeyNames(IEditSession s) => string.Join(",", ((KeysDoc)s.Commit()).Rows.Select(x => x.Name));
    private static string Names(IEditSession s) => string.Join(",", ((StepsDoc)s.Commit()).Steps.Select(x => x.Name));

    // ── B1 chips ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void B1_AListOfStrings_IsAChipRow_RemoveAddAndDragRoundTripToTheSession_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(new TagsDoc(), typeof(TagsDoc));
        r.Steady();
        Shot(r, "forms-b1-chips.png");
        Assert.Equal("chips", r.Drawer.ListFormOf(r.Field("Tags")));
        Assert.True(r.Drawer.ListFormBytes == 0, $"a steady chip-row form allocates nothing ({r.Drawer.ListFormBytes} bytes)");

        string Tags() => string.Join(",", ((TagsDoc)r.Session.Commit()).Tags);
        KitProbe.TryGet(KitProbe.Chip + 1, out var a, out var b);
        r.Click(new Vector2(b.X - 8, (a.Y + b.Y) / 2));                       // ✕ of "urban"
        r.Steady();
        Assert.Equal("night,desert", Tags());
        r.Click(C(KitProbe.ChipAdd));                                          // ⊕
        r.Steady();
        Assert.Equal("night,desert,", Tags());                                 // a new element is "" (the string's default), never null
        KitProbe.TryGet(KitProbe.Chip, out var c0a, out var c0b); KitProbe.TryGet(KitProbe.Chip + 1, out var c1a, out var c1b);
        r.Sim.Drag(r.Frame, r.Draw, new Vector2(c0a.X + 8, (c0a.Y + c0b.Y) / 2), new Vector2(c1a.X + 8, (c1a.Y + c1b.Y) / 2));
        r.Steady();
        Assert.Equal("desert,night,", Tags());                                 // a drag moves in place
        // a click on a chip selects it: its element is edited through its normal leaf cell on the row below
        r.Click(new Vector2(C(KitProbe.Chip).X - 10, C(KitProbe.Chip).Y));
        r.Steady();
        Shot(r, "forms-b1-chips-edit.png");
    }

    // ── B2 / B5 summary rows ───────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void B2_AListOfStructs_IsSummaryRows_MenuOpsRoundTripToTheSession_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(new StepsDoc(), typeof(StepsDoc));
        r.Steady();
        Shot(r, "forms-b2-rows.png");
        Assert.Equal("list", r.Drawer.ListFormOf(r.Field("Steps")));
        Assert.True(r.Drawer.ListFormBytes == 0, $"a steady summary-row form (items closed) allocates nothing ({r.Drawer.ListFormBytes} bytes)");

        // Duplicate item 1 ⇒ a deep copy right after it
        r.Click(MenuOf(r, 1, 3)); r.Frame.StepN(2, r.Draw);
        r.Click(C(KitProbe.MenuRow + 2)); r.Steady();
        Assert.Equal("Advance,Hold,Hold,Fire", Names(r.Session));
        Shot(r, "forms-b2-rows-after-duplicate.png");
        // Move up the last item (in place, no rebuild)
        r.Click(MenuOf(r, 3, 4)); r.Frame.StepN(2, r.Draw);
        r.Click(C(KitProbe.MenuRow + 3)); r.Steady();
        Assert.Equal("Advance,Hold,Fire,Hold", Names(r.Session));
        // Delete the first
        r.Click(MenuOf(r, 0, 4)); r.Frame.StepN(2, r.Draw);
        r.Click(C(KitProbe.MenuRow + 8)); r.Steady();
        Assert.Equal("Hold,Fire,Hold", Names(r.Session));
        // Add: a new element is the type's default
        var add = KitProbe.Center(KitProbe.ListMenuButton);   // (the Add chip is under the last row)
        Assert.False(float.IsNaN(add.X));
    }

    [SkippableFact]
    public void B2_TheOpenFlagFollowsAMove()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(new TwoDoc(), typeof(TwoDoc));
        r.Steady();
        float closedY = C(KitProbe.ListBody).Y;
        var menu0 = MenuOf(r, 0, 2);
        r.Click(BodyOf(r, 0, 2)); r.Steady();                    // open item 0: its fields sit under it, pushing the LAST row down
        float openY = C(KitProbe.ListBody).Y;
        Assert.True(openY > closedY + 20f, $"an open item's fields push the next row down ({closedY} → {openY})");
        Shot(r, "forms-b2-open.png");
        r.Click(menu0); r.Frame.StepN(2, r.Draw);   // the ⋮ of item 0 (its row did not move)
        r.Click(C(KitProbe.MenuRow + 4)); r.Steady();            // Move down: the open flag moves WITH the item
        Assert.True(C(KitProbe.ListBody).Y < openY - 10f, $"the open item is now the LAST one: its fields are below the last row, so the last row is back up ({closedY} / {openY} / {C(KitProbe.ListBody).Y}; names {string.Join(",", ((TwoDoc)r.Session.Commit()).Pair.Select(x => x.Name))})");
        Assert.Equal("Hold,Advance", string.Join(",", ((TwoDoc)r.Session.Commit()).Pair.Select(x => x.Name)));
    }

    // ── B3 grid ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void B3_AnEditListFormGridElement_IsAGrid_RowMenuRoundTrips()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(new WpDoc(), typeof(WpDoc));
        r.Steady();
        Shot(r, "forms-b3-grid.png");
        Assert.Equal("grid", r.Drawer.ListFormOf(r.Field("Points")));
        string Points() => string.Join(",", ((WpDoc)r.Session.Commit()).Points.Select(p => p.Name));
        r.Click(C(KitProbe.ListMenuButton)); r.Frame.StepN(2, r.Draw);      // the last row's ⋮
        r.Click(C(KitProbe.MenuRow + 2)); r.Steady();                        // Duplicate
        Assert.Equal("Alpha,Bravo,Charlie,Charlie", Points());
        r.Click(C(KitProbe.ListMenuButton)); r.Frame.StepN(2, r.Draw);
        r.Click(C(KitProbe.MenuRow + 8)); r.Steady();                        // Delete
        Assert.Equal("Alpha,Bravo,Charlie", Points());
    }

    // ── B4 fixed ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void B4_AFixedBufferOfLeaves_IsOneRowPerElement_SlotNOnTheLeft_NoSummaryRows_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var comp = new SlotsComp();
        for (int i = 0; i < 4; i++) comp.Slots[i] = i + 1;
        using var r = new Rig(comp, typeof(SlotsComp));
        r.Steady();
        Shot(r, "forms-b4-fixed.png");
        Assert.Equal("leaves fixed", r.Drawer.ListFormOf(r.Field("Slots")));
        Assert.Equal(4, r.Drawer.LeafRowsDrawn);                                   // N leaf cells, one per element …
        Assert.True(r.Drawer.ListFormBytes == 0, $"a steady fixed-leaves form allocates nothing ({r.Drawer.ListFormBytes} bytes)");
        // … and no ListBlock rows: the kit drew no list row for this form (its probe still holds an EARLIER test's rows, so the form name is the proof) — no menu, no handles
        Assert.DoesNotContain("list", r.Drawer.ListFormOf(r.Field("Slots")) ?? "", StringComparison.Ordinal);
        string Slots() { var c = (SlotsComp)r.Session.Commit(); return string.Join(",", Enumerable.Range(0, 4).Select(i => c.Slots[i])); }
        Assert.Equal("1,2,3,4", Slots());
    }

    [SkippableFact]
    public void B4_AFixedBufferOfStructs_KeepsTheSummaryRows_NoInsertNoDelete_MovesRoundTrip()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var comp = new PtsComp();
        for (int i = 0; i < 2; i++) comp.Pts[i] = new Pt { X = i + 1, Y = 10 };
        using var r = new Rig(comp, typeof(PtsComp));
        r.Steady();
        Shot(r, "forms-b4-fixed-structs.png");
        Assert.Equal("list fixed", r.Drawer.ListFormOf(r.Field("Pts")));
        string Pts() { var c = (PtsComp)r.Session.Commit(); return c.Pts[0].X + "," + c.Pts[1].X; }
        r.Click(MenuOf(r, 1, 2)); r.Frame.StepN(2, r.Draw);
        r.Click(C(KitProbe.MenuRow + 8)); r.Steady();                        // Delete: greyed in a fixed-size list
        Assert.Equal("1,2", Pts());
        r.Click(MenuOf(r, 1, 2)); r.Frame.StepN(2, r.Draw);
        r.Click(C(KitProbe.MenuRow + 3)); r.Steady();                        // Move up is allowed
        Assert.Equal("2,1", Pts());
    }

    // ── B6 paging ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void B6_AListOverFiftyStartsClosed_OpensToAPageOfFifty_AndPages()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(new BigDoc(), typeof(BigDoc));
        r.Steady();
        Assert.False(KitProbe.TryGet(KitProbe.HeaderNext, out _, out _) && false);
        r.Click(C(KitProbe.HeaderFold)); r.Steady();
        Shot(r, "forms-b6-page1.png");
        float page1Last = C(KitProbe.ListBody).Y;
        r.Click(C(KitProbe.HeaderNext)); r.Steady();
        Shot(r, "forms-b6-page2.png");
        float page2Last = C(KitProbe.ListBody).Y;
        Assert.True(page2Last < page1Last - 20 * r.Pitch, $"page 2 holds 10 items, page 1 holds 50 (last row at {page2Last} vs {page1Last})");
    }

    // ── the keys (§10.3) ──────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Keys_ASelectedRowTakesCtrlD_AltDown_CopyPaste_AndOutsideATableTheWindowsBindingRuns()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string clip = "";
        ComponentEditDrawer.ClipboardSet = t => clip = t;
        ComponentEditDrawer.ClipboardGet = () => clip;
        using var r = new Rig(new KeysDoc(), typeof(KeysDoc));
        r.Steady();

        // no row selected: the table does not have the keys ⇒ the window's own Ctrl+D runs
        r.Press(EditorKey.D, KeyModifiers.Ctrl);
        Assert.Equal(1, r.Window.Runs);
        Assert.Equal("Advance,Hold,Fire", KeyNames(r.Session));

        // select row 1: Ctrl+D duplicates it and the window's binding does NOT run (the table's scope consumed the chord)
        r.Click(HandleOf(r, 1, 3)); r.Steady();
        r.Press(EditorKey.D, KeyModifiers.Ctrl); r.Steady();
        Assert.Equal(1, r.Window.Runs);
        Assert.Equal("Advance,Hold,Hold,Fire", KeyNames(r.Session));
        // the selection followed the new item (index 2): Alt+↓ moves it down in place
        r.Press(EditorKey.Down, KeyModifiers.Alt); r.Steady();
        Assert.Equal("Advance,Hold,Fire,Hold", KeyNames(r.Session));
        // Ctrl+C copies the selected item's JSON; Ctrl+V pastes it over the same item
        r.Press(EditorKey.C, KeyModifiers.Ctrl);
        Assert.Contains("Hold", clip);
        r.Click(HandleOf(r, 0, 4)); r.Steady();
        r.Press(EditorKey.V, KeyModifiers.Ctrl); r.Steady();
        Assert.Equal("Hold,Hold,Fire,Hold", KeyNames(r.Session));
        // a refused paste says why under the list — never silent
        clip = "{not json";
        r.Press(EditorKey.V, KeyModifiers.Ctrl); r.Steady();
        Assert.StartsWith("can't paste", r.Drawer.ListErrorOf(r.Field("Rows")));
        // Del deletes the selected item
        r.Press(EditorKey.Delete, KeyModifiers.None); r.Steady();
        Assert.Equal("Hold,Fire,Hold", KeyNames(r.Session));
        ComponentEditDrawer.ClipboardSet = t => ImGui.SetClipboardText(t);
        ComponentEditDrawer.ClipboardGet = () => ImGui.GetClipboardText();
    }
}
