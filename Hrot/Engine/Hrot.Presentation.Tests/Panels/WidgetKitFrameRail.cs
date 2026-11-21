using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ K5 (docs/DESIGN_Struct_Editor.md §8, mockup struct-editor-visual.svg panels E / F) — ONE frame rail per widget of the kit, the widget ALONE (real fonts, Xvfb; a skip is not a pass): <c>Chip</c> · <c>ChipRow</c> ·
/// <c>ListBlock</c> · <c>GridBlock</c> · <c>RowMarks</c> · <c>EntityField</c>. Each draws its mockup states, reads the layout off the item rectangles (and, for popups, off <see cref="KitProbe"/>), clicks / drags with the harness's
/// synthetic mouse and asserts the <c>op</c> that comes back, and asserts <b>0 B</b> on a steady frame (R-2084). Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>kit-chip.png</c>, <c>kit-chiprow.png</c>,
/// <c>kit-listblock.png</c>, <c>kit-listblock-menu.png</c>, <c>kit-gridblock.png</c>, <c>kit-rowmarks.png</c>, <c>kit-entityfield.png</c>, <c>kit-entityfield-popup.png</c>.
/// 🔴 Red-proofs (compile): <c>ChipRow.Place</c> always answering <c>x + spacing + w</c> with <c>SameLine</c> (never wrapping) ⇒ the wrap test is red; <c>OpenSet.Move</c> not moving the flag (<c>_f[to] = false</c>) ⇒ the ListBlock
/// open-flag test is red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class WidgetKitFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    // ── harness helpers ────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed class Sim
    {
        public Vector2 Pos = new(-100, -100);
        public bool Left, Right;
        public void Feed()
        {
            var io = ImGui.GetIO();
            if (Pos.X >= 0) Raylib.SetMousePosition((int)Pos.X, (int)Pos.Y);   // rlImGui re-reads the REAL pointer every frame, so warp it too (a drag must hold its position across frames)
            io.AddMousePosEvent(Pos.X, Pos.Y);
            io.AddMouseButtonEvent(0, Left);
            io.AddMouseButtonEvent(1, Right);
        }
        public void Click(UiFrameSession f, Action draw, Vector2 p, bool right = false)
        {
            Pos = p; Left = Right = false; f.StepN(2, draw);
            if (right) Right = true; else Left = true;
            f.Step(draw);
            Left = Right = false; f.StepN(3, draw);   // the release is applied one frame late; the third frame shows its effect
        }
        public void Drag(UiFrameSession f, Action draw, Vector2 from, Vector2 to)
        {
            Pos = from; Left = false; f.StepN(2, draw);
            Left = true; f.StepN(2, draw);
            Pos = (from + to) * 0.5f; f.StepN(2, draw);
            Pos = to; f.StepN(3, draw);
            Left = false; f.StepN(3, draw);
        }
    }

    private static void Shot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    private static int Count(Image shot, Vector2 min, Vector2 max, Func<Color, bool> what)
    {
        int n = 0;
        for (int x = (int)min.X; x <= (int)max.X; x++)
            for (int y = (int)min.Y; y <= (int)max.Y; y++)
                if (what(Raylib.GetImageColor(shot, x, y))) n++;
        return n;
    }

    private static void Window(string name, float w, float h)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(w, h));
        ImGui.Begin(name, Bare);
    }

    private static UiFrameSession Start(int w, int h)
    {
        var frame = UiFrameHarness.Begin(w, h);
        new EditorFontService().Initialize(1f, 1f);
        return frame;
    }

    private static Vector2 Mid(Vector2 a, Vector2 b) => (a + b) * 0.5f;

    // ── Chip ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Chip_ThreeEnds_OneHeight_ReportsWhichPartWasHit_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(760, 140);
        var sim = new Sim();
        var rect = new (Vector2 Min, Vector2 Max)[5];
        var latch = new ChipHit[5];
        bool on = true; long bytes = -1;
        var entity = Icons.Get("thing/entity");

        void Latch(int i, ChipHit h) { if (h != ChipHit.None) latch[i] = h; rect[i] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); }
        void Draw()
        {
            sim.Feed();
            Window("##chip", 740, 100);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Latch(0, Chip.Draw("##a", "Bravo 2-3", entity, 300f, ChipEnd.Dropdown));
            ImGui.SameLine();
            Latch(1, Chip.Draw("##b", "night", null, 300f, ChipEnd.Removable));
            ImGui.SameLine();
            var t = Chip.Draw("##c", "Hostile", null, 300f, ChipEnd.Toggle, on: on); if (t == ChipHit.Body) on = !on; Latch(2, t);
            ImGui.SameLine();
            Latch(3, Chip.Draw("##d", "Friend", null, 300f, ChipEnd.Toggle, on: false));
            ImGui.SameLine();
            Latch(4, Chip.Draw("##e", "gone · #42", new IconGlyph("\uf071", IconPalette.Error), 300f, ChipEnd.Dropdown, tint: IconPalette.Error));
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        frame.StepN(8, Draw);
        Shot(frame, "kit-chip.png");
        Assert.True(bytes == 0, $"a steady frame of every chip allocates nothing ({bytes} bytes)");

        float h = rect[0].Max.Y - rect[0].Min.Y;
        for (int i = 0; i < 5; i++)
        {
            Assert.True(Math.Abs((rect[i].Max.Y - rect[i].Min.Y) - h) < 0.5f, $"chip {i}: one height");
            Assert.True(Math.Abs(rect[i].Min.Y - rect[0].Min.Y) < 0.5f, $"chip {i}: on one line");
        }
        for (int i = 1; i < 5; i++) Assert.True(rect[i].Min.X > rect[i - 1].Max.X, $"chip {i} is right of chip {i - 1}");

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            // the toggle: ON is tinted blue, OFF is not; the tinted (gone) chip is framed in Error
            var onPx = Raylib.GetImageColor(shot, (int)rect[2].Min.X + 4, (int)Mid(rect[2].Min, rect[2].Max).Y);
            var offPx = Raylib.GetImageColor(shot, (int)rect[3].Min.X + 4, (int)Mid(rect[3].Min, rect[3].Max).Y);
            Assert.True(onPx.B - offPx.B >= 20 && onPx.G - offPx.G >= 15, $"toggle ON is blue-tinted, brighter than OFF (on {onPx.R},{onPx.G},{onPx.B} · off {offPx.R},{offPx.G},{offPx.B})");
            Assert.True(Count(shot, rect[4].Min, rect[4].Max, c => Near(c, IconPalette.Error, 40)) > Count(shot, rect[0].Min, rect[0].Max, c => Near(c, IconPalette.Error, 40)) + 20, "gone: red frame and text; a plain chip has none");
        }
        finally { Raylib.UnloadImage(shot); }

        // clicks: a dropdown answers Body; a removable chip answers Remove on its ✕ and Body elsewhere; a toggle answers Body and flips
        sim.Click(frame, Draw, new Vector2(rect[0].Min.X + 12, Mid(rect[0].Min, rect[0].Max).Y));
        Assert.Equal(ChipHit.Body, latch[0]);
        sim.Click(frame, Draw, new Vector2(rect[1].Min.X + 8, Mid(rect[1].Min, rect[1].Max).Y));
        Assert.Equal(ChipHit.Body, latch[1]);
        latch[1] = ChipHit.None;
        sim.Click(frame, Draw, new Vector2(rect[1].Max.X - 8, Mid(rect[1].Min, rect[1].Max).Y));
        Assert.Equal(ChipHit.Remove, latch[1]);
        sim.Click(frame, Draw, Mid(rect[2].Min, rect[2].Max));
        Assert.True(latch[2] == ChipHit.Body && !on, "a toggle chip answers Body and the host flips it");
    }

    // ── ChipRow ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] Tags = { "night", "urban", "desert", "forest", "arctic", "coastal" };

    [SkippableFact]
    public void ChipRow_WrapsWithinTheWidth_OneOpPerFrame_DragReorders_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(420, 200);
        var sim = new Sim();
        var labels = new List<string>(Tags);
        ChipRowOp op = default; Vector2 start = default; float avail = 0, h = 0, spacing = 0; long bytes = -1;
        var widths = new float[Tags.Length + 1];

        void Draw()
        {
            sim.Feed();
            Window("##chiprow", 240, 160);
            start = ImGui.GetCursorScreenPos(); avail = ImGui.GetContentRegionAvail().X; h = ImGui.GetFrameHeight(); spacing = ImGui.GetStyle().ItemSpacing.X;
            for (int i = 0; i < Tags.Length; i++) widths[i] = Chip.WidthOf(Tags[i], null, avail, ChipEnd.Removable);
            widths[Tags.Length] = h;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var o = ChipRow.Draw("tags", labels, canRemove: true, canAdd: true);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (o.Kind != ChipRowOpKind.None) op = o;
            ImGui.End();
        }
        frame.StepN(8, Draw);
        Shot(frame, "kit-chiprow.png");
        Assert.True(bytes == 0, $"a steady chip row allocates nothing ({bytes} bytes)");

        // where each chip must be if the row wraps at the available width (the same maths, derived independently of the widget's cursor): predicted centres
        var centre = new Vector2[Tags.Length + 1];
        float x = 0, y = 0; int lines = 1;
        for (int i = 0; i <= Tags.Length; i++)
        {
            if (i > 0) { if (x + spacing + widths[i] <= avail) x += spacing; else { x = 0; y += h + ImGui.GetStyle().ItemSpacing.Y; lines++; } }
            centre[i] = new Vector2(start.X + x + widths[i] * 0.5f, start.Y + y + h * 0.5f);
            x += widths[i];
        }
        Assert.True(lines >= 2, $"six tags and a ⊕ do not fit one line of {avail}px — the rail's premise ({lines} line(s))");
        Assert.True(centre[Tags.Length].X + h * 0.5f <= start.X + avail + 0.5f, "the ⊕ stays inside the width");

        // ① wrapping: clicking each PREDICTED centre hits exactly that chip (a widget that did not wrap would put chips elsewhere)
        for (int i = 0; i < Tags.Length; i++)
        {
            op = default;
            sim.Click(frame, Draw, centre[i] - new Vector2(widths[i] * 0.25f, 0));
            Assert.True(op.Kind == ChipRowOpKind.Click && op.Index == i, $"chip {i} is where the wrap puts it (op {op.Kind} {op.Index})");
        }
        // ② ✕ removes, ⊕ adds
        op = default;
        sim.Click(frame, Draw, centre[1] + new Vector2(widths[1] * 0.5f - 8f, 0));
        Assert.True(op.Kind == ChipRowOpKind.Remove && op.Index == 1, $"✕ of chip 1 removes it (op {op.Kind} {op.Index})");
        op = default;
        sim.Click(frame, Draw, centre[Tags.Length]);
        Assert.True(op.Kind == ChipRowOpKind.Add, "⊕ adds");
        // ③ drag chip 0 onto chip 2 ⇒ Move(0 → 2)
        op = default;
        sim.Drag(frame, Draw, centre[0] - new Vector2(widths[0] * 0.25f, 0), centre[2] - new Vector2(widths[2] * 0.25f, 0));
        Assert.True(op.Kind == ChipRowOpKind.Move && op.Index == 0 && op.Target == 2, $"dragging chip 0 onto chip 2 moves it (op {op.Kind} {op.Index}→{op.Target})");
    }

    // ── ListBlock ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] Steps = { "Advance  to @PhaseLine · 5 s", "Hold  30 s · until contact", "Fire  at Bravo 2-3 · 3 bursts" };
    private static readonly IconGlyph[] StepKinds = { Icons.Get("tab/orders"), Icons.Get("section/time"), Icons.Get("form/pick") };

    private static ListOptions StepOptions()
    {
        var o = new ListOptions { Title = null, AddLabel = "Add step", Kinds = new[] { new ListKind(StepKinds[0], "Move"), new ListKind(StepKinds[1], "Wait"), new ListKind(StepKinds[2], "Fire") } };
        o.Shortcut(ListCmd.Duplicate, "Ctrl+D").Shortcut(ListCmd.MoveUp, "Alt+↑").Shortcut(ListCmd.MoveDown, "Alt+↓").Shortcut(ListCmd.Copy, "Ctrl+C").Shortcut(ListCmd.Paste, "Ctrl+V").Shortcut(ListCmd.Delete, "Del");
        return o;
    }

    [SkippableFact]
    public void ListBlock_OneSummaryRowPerItem_MenuOpsAndOpenFlagsTravel_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(760, 420);
        var sim = new Sim();
        var opts = StepOptions();
        var handle = new (Vector2 Min, Vector2 Max)[3]; var body = new (Vector2 Min, Vector2 Max)[3]; var menuBtn = new (Vector2 Min, Vector2 Max)[3];
        ListOp op = default; long bytes = -1; bool openHere = false; bool menuOpenOn1 = false; bool open1 = false, open2 = false;
        Vector2 addMin = default, addMax = default;

        void Draw()
        {
            sim.Feed();
            Window("##list", 740, 380);
            open1 = ListBlock.IsOpen("steps", 1); open2 = ListBlock.IsOpen("steps", 2);
            long before = GC.GetAllocatedBytesForCurrentThread();
            ListBlock.Begin("steps", Steps, opts);
            for (int v = 0; v < ListBlock.VisibleCount; v++)
            {
                int i = ListBlock.VisibleIndex(v);
                openHere = ListBlock.ItemBegin(i, StepKinds[i]);
                KitProbe.TryGet(KitProbe.ListHandle, out handle[i].Min, out handle[i].Max);
                KitProbe.TryGet(KitProbe.ListBody, out body[i].Min, out body[i].Max);
                KitProbe.TryGet(KitProbe.ListMenuButton, out menuBtn[i].Min, out menuBtn[i].Max);
                if (i == 1) menuOpenOn1 = ImGui.IsPopupOpen("##menu");
                if (openHere) { ImGui.TextUnformatted("Seconds"); ImGui.SameLine(120f); ImGui.TextDisabled("30"); }
                ListBlock.ItemEnd();
            }
            var o = ListBlock.End();
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            addMin = ImGui.GetItemRectMin(); addMax = ImGui.GetItemRectMax();
            if (o.Kind != ListOpKind.None) op = o;
            ImGui.End();
        }
        frame.StepN(3, Draw);
        // the mockup's state: item 2 (index 1) open
        sim.Click(frame, Draw, Mid(body[1].Min, body[1].Max));
        Assert.True(open1, "a click on the summary row opens the item");
        frame.StepN(6, Draw);
        Shot(frame, "kit-listblock.png");
        Assert.True(bytes == 0, $"a steady list frame (one item open) allocates nothing ({bytes} bytes)");

        // ① layout: ONE handle x, ONE body x, ONE row height (the frame height); rows follow one another
        float h = ImGui.GetFrameHeight();
        for (int i = 0; i < 3; i++)
        {
            Assert.True(Math.Abs((body[i].Max.Y - body[i].Min.Y) - (body[0].Max.Y - body[0].Min.Y)) < 0.5f, $"item {i}: one summary-row height");
            Assert.True(Math.Abs(handle[i].Min.X - handle[0].Min.X) < 0.5f && Math.Abs(body[i].Min.X - body[0].Min.X) < 0.5f && Math.Abs(menuBtn[i].Min.X - menuBtn[0].Min.X) < 0.5f, $"item {i}: handle, body and ⋮ at the same x");
            Assert.True(menuBtn[i].Min.X >= body[i].Max.X - 0.5f && body[i].Min.X >= handle[i].Max.X - 0.5f, $"item {i}: ⋮⋮ · summary · ⋮ in that order");
        }
        Assert.True(body[1].Min.Y > body[0].Max.Y && body[2].Min.Y > body[1].Max.Y + 10f, "the open item's fields sit between its row and the next row");

        // ② the ⋮ menu: nine rows in order, Move up greyed on the first item, the caller's shortcut labels drawn
        sim.Click(frame, Draw, Mid(menuBtn[0].Min, menuBtn[0].Max));
        frame.StepN(3, Draw);
        Shot(frame, "kit-listblock-menu.png");
        var rowY = new float[9];
        for (int m = 0; m < 9; m++)
        {
            Assert.True(KitProbe.TryGet(KitProbe.MenuRow + m, out var a, out var b), $"menu row {m} was drawn");
            rowY[m] = a.Y;
            if (m > 0) Assert.True(a.Y > rowY[m - 1], $"menu row {m} is below row {m - 1}");
        }
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            KitProbe.TryGet(KitProbe.MenuRow + 2, out var dmin, out var dmax);
            Assert.True(Count(shot, new Vector2(dmax.X - 70, dmin.Y), dmax, c => c.R > 90 && c.G > 90) >= 8, "Duplicate shows the caller's shortcut label (Ctrl+D) at its right edge");
        }
        finally { Raylib.UnloadImage(shot); }
        op = default;
        sim.Click(frame, Draw, KitProbe.Center(KitProbe.MenuRow + 3));   // Move up on the FIRST item: greyed
        Assert.Equal(ListOpKind.None, op.Kind);
        frame.StepN(3, Draw);
        sim.Click(frame, Draw, new Vector2(700, 380));                    // click away closes whatever is open

        // ③ Duplicate on item 1; and the open flag of item 1 travels with it on Move down (1 → 2)
        op = default;
        sim.Click(frame, Draw, Mid(menuBtn[1].Min, menuBtn[1].Max));
        frame.StepN(2, Draw);
        sim.Click(frame, Draw, KitProbe.Center(KitProbe.MenuRow + 2));
        Assert.True(op.Kind == ListOpKind.Duplicate && op.Index == 1, $"Duplicate answers its item ({op.Kind} {op.Index})");
        frame.StepN(3, Draw);
        Assert.True(open1, "Duplicate inserts a CLOSED item after item 1 — item 1 itself stays open");
        op = default;
        sim.Click(frame, Draw, Mid(menuBtn[1].Min, menuBtn[1].Max));
        frame.StepN(2, Draw);
        sim.Click(frame, Draw, KitProbe.Center(KitProbe.MenuRow + 4));
        Assert.True(op.Kind == ListOpKind.MoveDown && op.Index == 1 && op.Target == 2, $"Move down answers (1 → 2) ({op.Kind} {op.Index}→{op.Target})");
        frame.StepN(3, Draw);
        Assert.True(open2 && !open1, $"the open flag moved with its item (item 2 open: {open2}, item 1 open: {open1})");

        // ④ right-click on the summary row opens the same menu
        sim.Click(frame, Draw, new Vector2(100, 10));   // away
        frame.StepN(2, Draw);
        sim.Click(frame, Draw, Mid(body[1].Min, body[1].Max), right: true);
        Assert.True(menuOpenOn1, "a right-click on the summary row opens the item menu");
        sim.Click(frame, Draw, new Vector2(700, 380));
        frame.StepN(2, Draw);

        // ⑤ Add ▾ with three kinds opens the kinds popup (B5); picking Wait answers Add with kind 1
        op = default;
        sim.Click(frame, Draw, Mid(addMin, addMax));
        frame.StepN(2, Draw);
        Assert.True(KitProbe.TryGet(KitProbe.KindRow + 1, out _, out _), "the kinds popup lists the kinds");
        sim.Click(frame, Draw, KitProbe.Center(KitProbe.KindRow + 1));
        Assert.True(op.Kind == ListOpKind.Add && op.Target == 1, $"Add ▾ → Wait answers Add(kind 1) ({op.Kind} {op.Target})");

        // ⑥ drag the handle of item 0 onto item 2 ⇒ Move(0 → 2)
        op = default;
        frame.StepN(3, Draw);
        sim.Drag(frame, Draw, Mid(handle[0].Min, handle[0].Max), Mid(body[2].Min, body[2].Max));
        Assert.True(op.Kind == ListOpKind.Move && op.Index == 0 && op.Target == 2, $"dragging ⋮⋮ of item 0 onto item 2 moves it ({op.Kind} {op.Index}→{op.Target})");
    }

    [SkippableFact]
    public void ListBlock_FixedSize_GreysTheCountChangingCommands_AndLongListsStartClosedFilterAndPage()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(760, 420);
        var sim = new Sim();

        // B4: four slots — no Add, no insert / delete
        var slots = new[] { "Rifle", "Grenade", "—", "—" };
        var fixedOpts = new ListOptions { FixedSize = true };
        ListOp op = default; var bodies = new (Vector2 Min, Vector2 Max)[4]; var menuBtn = new (Vector2 Min, Vector2 Max)[4];
        void DrawFixed()
        {
            sim.Feed();
            Window("##fixed", 740, 380);
            ListBlock.Begin("slots", slots, fixedOpts);
            for (int v = 0; v < ListBlock.VisibleCount; v++)
            {
                int i = ListBlock.VisibleIndex(v);
                ListBlock.ItemBegin(i);
                KitProbe.TryGet(KitProbe.ListBody, out bodies[i].Min, out bodies[i].Max);
                KitProbe.TryGet(KitProbe.ListMenuButton, out menuBtn[i].Min, out menuBtn[i].Max);
                ListBlock.ItemEnd();
            }
            var o = ListBlock.End();
            if (o.Kind != ListOpKind.None) op = o;
            ImGui.End();
        }
        frame.StepN(4, DrawFixed);
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            // the footer "4 / 4 — fixed size" sits under the last row and there is no Add chip (no frame-coloured block)
            var under = new Vector2(bodies[3].Min.X, bodies[3].Max.Y + 2);
            Assert.True(Count(shot, under, under + new Vector2(200, 22), c => c.R > 100 && c.G > 100 && c.B > 100) >= 10, "the footer text is drawn under the last row");
        }
        finally { Raylib.UnloadImage(shot); }
        sim.Click(frame, DrawFixed, Mid(menuBtn[0].Min, menuBtn[0].Max));
        frame.StepN(2, DrawFixed);
        sim.Click(frame, DrawFixed, KitProbe.Center(KitProbe.MenuRow + 8));   // Delete: greyed in a fixed-size list
        Assert.Equal(ListOpKind.None, op.Kind);
        sim.Click(frame, DrawFixed, new Vector2(700, 380));
        sim.Click(frame, DrawFixed, Mid(menuBtn[0].Min, menuBtn[0].Max));
        frame.StepN(2, DrawFixed);
        sim.Click(frame, DrawFixed, KitProbe.Center(KitProbe.MenuRow + 4));   // Move down stays allowed
        Assert.True(op.Kind == ListOpKind.MoveDown && op.Index == 0, $"slots can still be moved ({op.Kind})");

        // B6: 120 items — closed at first, opens to a page of 50, the filter narrows by the summary, ‹ › page
        var many = new string[120];
        for (int i = 0; i < many.Length; i++) many[i] = "Spawn " + (i + 1);
        var longOpts = new ListOptions { Title = "Spawn points" };
        int visible = -1, first = -1, last = -1;
        void DrawLong()
        {
            sim.Feed();
            Window("##long", 740, 380);
            ListBlock.Begin("spawn", many, longOpts);
            visible = ListBlock.VisibleCount; first = visible > 0 ? ListBlock.VisibleIndex(0) : -1; last = visible > 0 ? ListBlock.VisibleIndex(visible - 1) : -1;
            for (int v = 0; v < ListBlock.VisibleCount; v++) { ListBlock.ItemBegin(ListBlock.VisibleIndex(v)); ListBlock.ItemEnd(); }
            ListBlock.End();
            ImGui.End();
        }
        frame.StepN(4, DrawLong);
        Assert.Equal(0, visible);                                                   // starts closed
        sim.Click(frame, DrawLong, KitProbe.Center(KitProbe.HeaderFold));
        frame.StepN(3, DrawLong);
        Assert.True(visible == 50 && first == 0 && last == 49, $"opened: page 1 of 50 ({visible}, {first}…{last})");
        sim.Click(frame, DrawLong, KitProbe.Center(KitProbe.HeaderNext));
        frame.StepN(2, DrawLong);
        Assert.True(visible == 50 && first == 50 && last == 99, $"› : page 2 ({first}…{last})");
        sim.Click(frame, DrawLong, KitProbe.Center(KitProbe.HeaderNext));
        frame.StepN(2, DrawLong);
        Assert.True(visible == 20 && first == 100 && last == 119, $"› : the last page holds the remaining 20 ({visible}, {first}…{last})");
        sim.Click(frame, DrawLong, KitProbe.Center(KitProbe.HeaderNext));
        frame.StepN(2, DrawLong);
        Assert.True(first == 100, "› on the last page stays");
        // the filter: "Spawn 11" matches Spawn 11 and Spawn 110…119 ⇒ 11 items, the first is index 10; it matches the SUMMARY
        sim.Click(frame, DrawLong, KitProbe.Center(KitProbe.HeaderFilter));
        frame.StepN(2, DrawLong);
        ImGui.GetIO().AddInputCharactersUTF8("Spawn 11");
        frame.StepN(4, DrawLong);
        Assert.True(visible == 11 && first == 10 && last == 118, $"filter 'Spawn 11' ⇒ 11 items ({visible}, {first}…{last})");
    }

    // ── GridBlock ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] GridHeaders = { "Name", "Position", "Wait" };

    [SkippableFact]
    public void GridBlock_ColumnsAlign_RowMenuIsTheListsMenu_DragMoves_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(760, 340);
        var sim = new Sim();
        var opts = new ListOptions { AddLabel = "Add waypoint" };
        var vals = new float[9];
        var cell = new (Vector2 Min, Vector2 Max)[3, 3]; var handle = new Vector2[3]; var menu = new Vector2[3];
        ListOp op = default; long bytes = -1; Vector2 addMin = default, addMax = default;

        void Draw()
        {
            sim.Feed();
            Window("##grid", 740, 300);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (GridBlock.Begin("wp", GridHeaders, 3, opts))
            {
                for (int r = 0; r < 3; r++)
                {
                    GridBlock.Row(r);
                    KitProbe.TryGet(KitProbe.ListMenuButton, out var a, out var b); menu[r] = Mid(a, b);
                    KitProbe.TryGet(KitProbe.ListHandle, out var ha, out var hb); handle[r] = Mid(ha, hb);
                    for (int c = 0; c < 3; c++)
                    {
                        GridBlock.Cell(c);
                        ImGui.DragFloat("##v", ref vals[r * 3 + c]);
                        cell[r, c] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                    }
                }
            }
            var o = GridBlock.End();
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            addMin = ImGui.GetItemRectMin(); addMax = ImGui.GetItemRectMax();
            if (o.Kind != ListOpKind.None) op = o;
            ImGui.End();
        }
        frame.StepN(8, Draw);
        Shot(frame, "kit-gridblock.png");
        Assert.True(bytes == 0, $"a steady grid frame allocates nothing ({bytes} bytes)");

        for (int c = 0; c < 3; c++)
            for (int r = 1; r < 3; r++)
            {
                Assert.True(Math.Abs(cell[r, c].Min.X - cell[0, c].Min.X) < 0.5f && Math.Abs((cell[r, c].Max.X - cell[r, c].Min.X) - (cell[0, c].Max.X - cell[0, c].Min.X)) < 0.5f, $"column {c}: row {r} has the same x and width as row 0");
            }
        for (int c = 1; c < 3; c++) Assert.True(cell[0, c].Min.X > cell[0, c - 1].Max.X, $"column {c} is right of column {c - 1}");
        float pitch = cell[1, 0].Min.Y - cell[0, 0].Min.Y;
        Assert.True(pitch > 0 && Math.Abs((cell[2, 0].Min.Y - cell[1, 0].Min.Y) - pitch) < 0.5f, "rows have one pitch");

        // the row menu IS the list's: Delete on row 1
        sim.Click(frame, Draw, menu[1]);
        frame.StepN(2, Draw);
        Assert.True(KitProbe.TryGet(KitProbe.MenuRow + 8, out _, out _), "the ⋮ menu has the list's nine rows");
        op = default;
        sim.Click(frame, Draw, KitProbe.Center(KitProbe.MenuRow + 8));
        Assert.True(op.Kind == ListOpKind.Delete && op.Index == 1, $"Delete on row 1 ({op.Kind} {op.Index})");
        // Add (one kind ⇒ direct)
        op = default;
        sim.Click(frame, Draw, Mid(addMin, addMax));
        Assert.True(op.Kind == ListOpKind.Add && op.Index == 3, $"Add appends ({op.Kind} {op.Index})");
        // drag row 0's handle onto row 2's handle
        op = default;
        sim.Drag(frame, Draw, handle[0], handle[2]);
        Assert.True(op.Kind == ListOpKind.Move && op.Index == 0 && op.Target == 2, $"dragging row 0 onto row 2 ({op.Kind} {op.Index}→{op.Target})");
    }

    // ── RowMarks ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] MarkLabels = { "Name", "Heading", "Count", "Max range" };

    [SkippableFact]
    public void RowMarks_ChangedBarAndReset_ErrorLineUnderItsRow_SetBox_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(760, 300);
        var sim = new Sim();
        var ctl = new (Vector2 Min, Vector2 Max)[4]; Vector2 errMin = default, errMax = default, resetMid = default, setMid = default;
        float v = 3f; bool isSet = false, reset = false; long bytes = -1;

        void Draw()
        {
            sim.Feed();
            Window("##marks", 740, 260);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (FormGrid.Begin("rail.marks", MarkLabels))
            {
                for (int r = 0; r < 4; r++)
                {
                    FormGrid.Row(MarkLabels[r]);
                    RowMarks.ChangedBar(changed: r == 1);
                    if (r == 3) { RowMarks.SetBox("##set", ref isSet); setMid = Mid(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); ImGui.SameLine(); }
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 40f);
                    ImGui.DragFloat("##v", ref v);
                    ctl[r] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                    if (r == 1) { if (RowMarks.ResetButton("##reset")) reset = true; resetMid = Mid(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); }
                    if (r == 2) { RowMarks.ErrorLine("'3x' is not a whole number"); errMin = ImGui.GetItemRectMin(); errMax = ImGui.GetItemRectMax(); }
                }
                FormGrid.End();
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        frame.StepN(8, Draw);
        Shot(frame, "kit-rowmarks.png");
        Assert.True(bytes == 0, $"a steady frame of marked rows allocates nothing ({bytes} bytes)");

        // the bar slot is always reserved: rows 0..2 start their control at one x (row 3 has the set box in front of it)
        Assert.True(Math.Abs(ctl[1].Min.X - ctl[0].Min.X) < 0.5f && Math.Abs(ctl[2].Min.X - ctl[0].Min.X) < 0.5f, "a changed row's control starts where an unchanged row's does");
        Assert.True(errMin.Y >= ctl[2].Max.Y - 0.5f, "the error line is UNDER its row's control");
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            int bar0 = Count(shot, new Vector2(ctl[0].Min.X - 7, ctl[0].Min.Y), new Vector2(ctl[0].Min.X - 1, ctl[0].Max.Y), c => Near(c, IconPalette.Amber, 20));
            int bar1 = Count(shot, new Vector2(ctl[1].Min.X - 7, ctl[1].Min.Y), new Vector2(ctl[1].Min.X - 1, ctl[1].Max.Y), c => Near(c, IconPalette.Amber, 20));
            Assert.True(bar1 >= 3 * (int)(ctl[1].Max.Y - ctl[1].Min.Y) - 6 && bar0 == 0, $"an amber bar of the control's height at the changed row only ({bar1} px, plain row {bar0})");
            Assert.True(Count(shot, errMin, new Vector2(errMin.X + 260, errMax.Y), c => Near(c, IconPalette.Error, 30)) >= 20, "the error line is red (IconPalette.Error)");
            Assert.True(Count(shot, new Vector2(resetMid.X - 8, resetMid.Y - 8), new Vector2(resetMid.X + 8, resetMid.Y + 8), c => Near(c, IconPalette.Amber, 40)) >= 6, "↺ is amber");
        }
        finally { Raylib.UnloadImage(shot); }

        sim.Click(frame, Draw, resetMid);
        Assert.True(reset, "a click on ↺ answers true");
        bool wasSet = isSet;
        sim.Click(frame, Draw, setMid);
        Assert.True(isSet != wasSet, "the set box toggles");
    }

    // ── EntityField ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly EntityCandidate[] Candidates =
    {
        new("Alpha 1-1", IconPalette.Blue, Icons.Get("thing/entity"), "Friend", "90 m", 11),
        new("Bravo 2-3", IconPalette.Red, Icons.Get("thing/entity"), "Hostile", "210 m", 23),
        new("Bravo 2", IconPalette.Red, Icons.Get("orbat/command"), "Hostile", "340 m", 2),
        new("Brava supply", IconPalette.Blue, Icons.Get("tab/crew"), "Friend", "1.2 km", 77),
    };

    /// <summary>
    /// ⭐ CE-4349 — the pick dropdown at UI scale 1.5 with a LONG name, opened from a chip near the window's right edge: every column comes from the measured
    /// text — the name starts after the glyph (never under it: the user saw "fantry (HROT)…"), the side column starts after the widest name (never over it),
    /// the popup is wide enough for its rows and stays on the screen. Renders <c>ce4349-pick-dropdown.png</c>. 🔴 Mutation: the old fixed x + 28 / width × 0.5 ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE4349_ThePickDropdown_LaysOutItsColumnsFromTheText_AtUiScale1_5()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1000, 520);
        new EditorFontService().Initialize(1f, 1.5f);
        var sim = new Sim();
        var rows = new EntityCandidate[]
        {
            new("Infantry (HROT) Rifle Squad 1-1", IconPalette.Blue, Icons.Get("thing/entity"), "Friend", "90 m", 11),
            new("Bravo 2-3", IconPalette.Red, Icons.Get("orbat/command"), "Hostile", "1.2 km", 23),
        };
        Vector2 chipMin = default, chipMax = default;
        void Draw()
        {
            sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(560, 10)); ImGui.SetNextWindowSize(new Vector2(420, 300));
            ImGui.Begin("##ent-scale", Bare);
            EntityField.Draw("weapon-at", "Pick on map...", null, 380f, rows, version: 1);
            chipMin = ImGui.GetItemRectMin(); chipMax = ImGui.GetItemRectMax();
            ImGui.End();
        }
        frame.StepN(4, Draw);
        sim.Click(frame, Draw, Mid(chipMin, chipMax));
        frame.StepN(3, Draw);
        Shot(frame, "ce4349-pick-dropdown.png");
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is not { Length: > 0 })
            frame.Screenshot(System.IO.Path.Combine(System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-frames")).FullName, "ce4349-pick-dropdown.png"));

        var c = EntityField.LastColumns;
        float longW = ImGui.CalcTextSize(rows[0].Label).X;
        Assert.True(c.LabelX >= c.GlyphW + 6f, $"the name starts after the glyph ({c.LabelX:0} vs glyph {c.GlyphW:0})");
        Assert.True(c.LabelW >= longW - 0.5f, $"the name column holds the longest name ({c.LabelW:0} vs {longW:0})");
        Assert.True(c.SideX >= c.LabelX + c.LabelW, "the side column starts after the widest name");
        Assert.True(c.PopupW >= c.SideX + ImGui.CalcTextSize("Hostile").X + ImGui.CalcTextSize("1.2 km").X, $"the popup is wide enough for its rows ({c.PopupW:0})");
        Assert.True(KitProbe.TryGet(KitProbe.CandidateRow, out var r0min, out var r0max), "the first row is listed");
        Assert.True(r0min.X >= 0f && r0max.X <= 1000f, $"the popup stays on the screen ({r0min.X:0}…{r0max.X:0})");
    }

    [SkippableFact]
    public void EntityField_ChipAndPicker_FilteredByTypeAhead_OpsForEveryEntry_GoneIsRed_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = Start(760, 420);
        var sim = new Sim();
        EntityFieldOp op = default; long bytes = -1; Vector2 chipMin = default, chipMax = default, goneMin = default, goneMax = default;

        void Draw()
        {
            sim.Feed();
            Window("##ent", 740, 380);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var o = EntityField.Draw("target", "Alpha 1-1", Icons.Get("thing/entity"), 300f, Candidates, version: 1);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            chipMin = ImGui.GetItemRectMin(); chipMax = ImGui.GetItemRectMax();
            if (o.Kind != EntityFieldOpKind.None) op = o;
            ImGui.Dummy(new Vector2(1, 6));
            var g = EntityField.Draw("leader", "", null, 300f, Candidates, version: 1, goneText: "gone · #42");
            goneMin = ImGui.GetItemRectMin(); goneMax = ImGui.GetItemRectMax();
            if (g.Kind != EntityFieldOpKind.None) op = g;
            ImGui.End();
        }
        frame.StepN(8, Draw);
        Shot(frame, "kit-entityfield.png");
        Assert.True(bytes == 0, $"a steady closed entity field allocates nothing ({bytes} bytes)");
        Assert.True(Math.Abs((chipMax.Y - chipMin.Y) - (goneMax.Y - goneMin.Y)) < 0.5f, "the gone chip has the chip's height");
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            Assert.True(Count(shot, goneMin, goneMax, c => Near(c, IconPalette.Error, 40)) > Count(shot, chipMin, chipMax, c => Near(c, IconPalette.Error, 40)) + 20, "a gone reference draws its chip RED");
        }
        finally { Raylib.UnloadImage(shot); }

        // open the picker: every candidate is a row (glyph · name · side · distance), then the three entries
        sim.Click(frame, Draw, Mid(chipMin, chipMax));
        frame.StepN(3, Draw);
        Shot(frame, "kit-entityfield-popup.png");
        for (int r = 0; r < Candidates.Length; r++) Assert.True(KitProbe.TryGet(KitProbe.CandidateRow + r, out _, out _), $"candidate row {r} is listed");
        Assert.True(KitProbe.TryGet(KitProbe.Entry, out var mapMin, out _) && mapMin.Y > KitProbe.Center(KitProbe.CandidateRow + 3).Y, "the entries come after the rows");
        frame.StepN(3, Draw);
        Assert.True(bytes == 0, $"a steady frame with the picker OPEN allocates nothing ({bytes} bytes)");

        // type-ahead: "bra" hides Alpha 1-1 ⇒ the first row now answers Bravo 2-3
        ImGui.GetIO().AddInputCharactersUTF8("bra");
        frame.StepN(4, Draw);
        Shot(frame, "kit-entityfield-filtered.png");
        op = default;
        sim.Click(frame, Draw, KitProbe.Center(KitProbe.CandidateRow));
        Assert.True(op.Kind == EntityFieldOpKind.Pick && op.Id == 23, $"after typing 'bra' the first row is Bravo 2-3 (op {op.Kind} id {op.Id})");

        // the entries
        foreach (var (slot, kind) in new[] { (0, EntityFieldOpKind.PickOnMap), (1, EntityFieldOpKind.ByKey), (2, EntityFieldOpKind.Clear) })
        {
            op = default;
            sim.Click(frame, Draw, Mid(chipMin, chipMax));
            frame.StepN(3, Draw);
            sim.Click(frame, Draw, KitProbe.Center(KitProbe.Entry + slot));
            Assert.True(op.Kind == kind, $"entry {slot} answers {kind} (got {op.Kind})");
        }
    }
}
