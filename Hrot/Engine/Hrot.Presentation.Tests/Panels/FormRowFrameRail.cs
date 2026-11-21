using System;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Forms;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ U-11 / D0 (docs/UX/DESIGN_Details_Forms.md §6.2–§6.6, mockup details-forms-visual.svg) — every FORM ROW STATE as a frame rail (real fonts, Xvfb; a skip is not a pass), read off the rendered pixels and the item rectangles:
/// (①) every control starts at ONE x and has ONE width whether or not its row carries a mark, and every mark sits at ONE x — the slot never shifts the layout; (②) each mark is the glyph's colour from the ONE palette
/// (refused = Error, pending = Pending, locked = Muted, authored≠type = Info); invalid input has the Error frame and the Error reason under the field; a refused row is tinted; only a refused / reset mark is clickable; 0 B on a steady frame.
/// 🔴 Red-proof (compiles): <c>FormRow.IconKeyOf(Refused)</c> answering <c>"form/pending"</c> ⇒ the refused mark is amber, not Error ⇒ red.
/// Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>form-rows.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class FormRowFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

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

    [Fact]
    public void TheOneMark_IsTheStrongest_RefusedThenPendingThenLockThenReset()
    {
        Assert.Equal(FormMark.Refused, FormRow.Strongest(new FormRowState(Refused: true, Pending: true, Locked: true, DiffersFromType: true)));
        Assert.Equal(FormMark.Pending, FormRow.Strongest(new FormRowState(Pending: true, Locked: true, DiffersFromType: true)));
        Assert.Equal(FormMark.Locked, FormRow.Strongest(new FormRowState(Locked: true, DiffersFromType: true)));
        Assert.Equal(FormMark.DiffersFromType, FormRow.Strongest(new FormRowState(DiffersFromType: true)));
        Assert.Equal(FormMark.None, FormRow.Strongest(new FormRowState(Invalid: true)));   // invalid input has its own frame, not a mark
        foreach (var m in new[] { FormMark.Refused, FormMark.Pending, FormMark.Locked, FormMark.DiffersFromType })
            Assert.True(Icons.TryGet(FormRow.IconKeyOf(m), out _), $"{m}: a glyph in the icon map");
        Assert.Equal(StatusPalette.Error, Icons.Get(FormRow.IconKeyOf(FormMark.Refused)).Color);
        Assert.Equal(StatusPalette.Pending, Icons.Get(FormRow.IconKeyOf(FormMark.Pending)).Color);
        Assert.Equal(StatusPalette.Muted, Icons.Get(FormRow.IconKeyOf(FormMark.Locked)).Color);
        Assert.Equal(StatusPalette.Info, Icons.Get(FormRow.IconKeyOf(FormMark.DiffersFromType)).Color);
    }

    [SkippableFact]
    public void EveryRowState_KeepsTheLayout_ShowsItsMarkInItsColour_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(760, 380);
        new EditorFontService().Initialize(1f, 1f);

        string[] labels = { "Name", "Skill", "Weight", "Speed", "Seat", "Temperament" };
        var states = new[]
        {
            new FormRowState(),
            new FormRowState(Invalid: true, InvalidReason: "must be 0..1"),
            new FormRowState(Refused: true, RefusedReason: "Refused by node 3"),
            new FormRowState(Pending: true, PendingTip: "Waiting for node 3 (Brain)…"),
            new FormRowState(Locked: true, LockedReason: "Your seat (Observer) cannot change Seat"),
            new FormRowState(DiffersFromType: true, TypeTip: "Type 0.50 — reset"),
        };
        var control = new (Vector2 Min, Vector2 Max)[labels.Length];
        var mark = new (Vector2 Min, Vector2 Max)[labels.Length];
        float v = 0.4f; long bytes = -1; FormMark lastClick = FormMark.None; int clickRow = -1; Vector2 mouse = new(-100, -100); bool press = false;

        void Draw()
        {
            ImGui.GetIO().AddMousePosEvent(mouse.X, mouse.Y);
            ImGui.GetIO().AddMouseButtonEvent(0, press);
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(720, 340));
            ImGui.Begin("##formrows", Bare);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (FormGrid.Begin("rail.formrow", labels, statusSlot: true))
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    FormRow.Begin(labels[i], labels[i], in states[i]);
                    ImGui.SliderFloat("##v", ref v, 0f, 1f);
                    control[i] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                    var click = FormRow.End(in states[i]);
                    if (click != FormMark.None) { lastClick = click; clickRow = i; }
                    mark[i] = states[i] is { Refused: false, Pending: false, Locked: false, DiffersFromType: false } ? default : (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                }
                FormGrid.End();
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int i = 0; i < 8; i++) frame.Step(Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "form-rows.png"));
        Assert.True(bytes == 0, $"a steady form frame with every state allocates nothing ({bytes} bytes)");

        // ① layout: ONE control x and ONE width on every row (a mark, a reason line, a lock change none of it); ONE x for every mark
        for (int i = 1; i < labels.Length; i++)
        {
            Assert.True(Math.Abs(control[i].Min.X - control[0].Min.X) < 0.5f, $"row {i}: the control starts at the same x as row 0 ({control[i].Min.X} vs {control[0].Min.X})");
            Assert.True(Math.Abs((control[i].Max.X - control[i].Min.X) - (control[0].Max.X - control[0].Min.X)) < 0.5f, $"row {i}: the control has the same width");
        }
        Assert.True(Math.Abs(mark[2].Min.X - mark[3].Min.X) < 0.5f && Math.Abs(mark[3].Min.X - mark[4].Min.X) < 0.5f && Math.Abs(mark[4].Min.X - mark[5].Min.X) < 0.5f, "every mark sits at ONE x (the slot)");
        Assert.True(mark[2].Min.X >= control[2].Max.X, "the slot is right of the control");

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            // ② the marks, in their colours
            Assert.True(Count(shot, mark[2].Min, mark[2].Max, c => Near(c, StatusPalette.Error, 24)) >= 6, "refused: a cross in Error");
            Assert.True(Count(shot, mark[3].Min, mark[3].Max, c => Near(c, StatusPalette.Pending, 24)) >= 6, "pending: an hourglass in Pending");
            Assert.True(Count(shot, mark[4].Min, mark[4].Max, c => Near(c, StatusPalette.Muted, 16)) >= 6, "locked: a lock in Muted");
            Assert.True(Count(shot, mark[5].Min, mark[5].Max, c => Near(c, StatusPalette.Info, 24)) >= 6, "authored ≠ type: a reset arrow in Info");
            // invalid input: the Error frame round the control and the reason under it
            var frameAt = Raylib.GetImageColor(shot, (int)control[1].Min.X, (int)((control[1].Min.Y + control[1].Max.Y) / 2));
            Assert.True(Near(frameAt, StatusPalette.Error, 30), $"invalid: the control's frame is Error (got {frameAt.R},{frameAt.G},{frameAt.B})");
            Assert.True(Count(shot, new Vector2(control[1].Min.X, control[1].Max.Y + 1), new Vector2(control[1].Min.X + 160, control[1].Max.Y + 22), c => Near(c, StatusPalette.Error, 24)) >= 8, "invalid: the reason is Error text under the field");
            Assert.True(Count(shot, new Vector2(control[0].Max.X + 2, control[0].Min.Y), new Vector2(control[0].Max.X + 40, control[0].Max.Y), c => Near(c, StatusPalette.Error, 24)) == 0, "a plain row has no Error ink");
            // refused row: tinted background (Error at 18 % over the window), a plain row is not
            var tint = Raylib.GetImageColor(shot, (int)control[2].Min.X - 3, (int)((control[2].Min.Y + control[2].Max.Y) / 2));
            var plain = Raylib.GetImageColor(shot, (int)control[0].Min.X - 3, (int)((control[0].Min.Y + control[0].Max.Y) / 2));
            Assert.True(tint.R - tint.G > 15 && plain.R - plain.G < 6, $"the refused row is tinted red (got {tint.R},{tint.G},{tint.B}); a plain row is not ({plain.R},{plain.G},{plain.B})");
        }
        finally { Raylib.UnloadImage(shot); }

        // clicks: only a refused or reset mark answers
        void ClickAt(int row)
        {
            mouse = (mark[row].Min + mark[row].Max) / 2f; press = false;
            frame.StepN(2, Draw); press = true; frame.Step(Draw); press = false; frame.StepN(2, Draw);
        }
        lastClick = FormMark.None; clickRow = -1; ClickAt(3);
        Assert.Equal(FormMark.None, lastClick);                                                  // pending: not clickable
        ClickAt(4);
        Assert.Equal(FormMark.None, lastClick);                                                  // locked: not clickable
        ClickAt(2);
        Assert.True(lastClick == FormMark.Refused && clickRow == 2, "a click on the refused mark clears it");
        lastClick = FormMark.None; ClickAt(5);
        Assert.True(lastClick == FormMark.DiffersFromType && clickRow == 5, "a click on the reset mark resets");
    }
}
