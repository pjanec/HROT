using System;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using StructEdit.Core.Attributes;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ N-3 / N-4 (CE-3380 / CE-3381, docs/DESIGN_Struct_Editor.md §3 Parse, §6 S6–S9, §10.1) — the LEAF CELLS through a real session (real fonts, Xvfb; a skip is not a pass): a typed value parses (a refusal keeps the typed text and the
/// old value and draws its line), <c>long</c> parses as <c>long</c>, <c>[Flags]</c> are toggle chips, a nullable has its set box; a field at its default is grey, a changed one amber with ↺ (which writes the default back), a read-only table refuses
/// input and still shows on the map. Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>cells-*.png</c>.
/// 🔴 Red-proofs (compile): <c>DrawTextCell</c> writing the old text back on a refusal ⇒ the typed-text assertion is red; <c>IsChangedFromDefault</c> always false ⇒ the amber / ↺ rail is red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class LeafCellsFrameRail
{
    [Flags] public enum Fl { None = 0, A = 1, B = 2, C = 4 }
    public sealed class IntDoc { public int Count = 3; }
    public sealed class LongDoc { public long Big = 5; }
    public sealed class FlagsDoc { public Fl Mode = Fl.A; }
    public sealed class NullDoc { public int? Opt; }
    public sealed class DefDoc { public int Level = 3; }
    public sealed class LockDoc { [EditReadOnly] public int Locked = 7; }
    public sealed class RefDoc { public EntityRef Target = new EntityRef(5); }

    private static Vector2 C(int slot) => KitProbe.Center(slot);
    private static void Shot(ListFormsFrameRail.Rig r, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) r.Frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    /// <summary>Brightest channel value among the pixels of <paramref name="rect"/> that are not background (the text's ink).</summary>
    private static int Ink(Image shot, Vector2 min, Vector2 max)
    {
        int best = 0;
        for (int x = (int)min.X + 3; x < (int)max.X - 3; x++)
            for (int y = (int)min.Y + 3; y < (int)max.Y - 3; y++)
            {
                var c = Raylib.GetImageColor(shot, x, y);
                best = Math.Max(best, Math.Max(c.R, Math.Max(c.G, c.B)));
            }
        return best;
    }

    private static Vector2 RightEnd(int slot) { KitProbe.TryGet(slot, out var a, out var b); return new Vector2(b.X - 6, (a.Y + b.Y) / 2); }

    // ── N-3 ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void N3_ARefusedTextKeepsTheTypedText_AndTheOldValue_AndShowsItsLine_UntilItParses()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new ListFormsFrameRail.Rig(new IntDoc(), typeof(IntDoc));
        r.Steady();
        var node = r.Field("Count");
        Assert.True(r.Drawer.CellErrorOf(node) == null && r.Drawer.CellTextOf(node) == "3");

        r.Click(RightEnd(KitProbe.TextCell));              // focus the box, caret at its end
        r.Type("x");                                       // "3x"
        Shot(r, "cells-n3-refused.png");
        Assert.Equal("'3x' is not a whole number — the value stays 3", r.Drawer.CellErrorOf(node));   // the line under the row says why
        Assert.Equal("3x", r.Drawer.CellTextOf(node));                           // the typed text is still in the box
        Assert.Equal(3, ((IntDoc)r.Session.Commit()).Count);                     // the old value stands

        KitProbe.TryGet(KitProbe.TextCell, out var ca, out var cb);
        r.DoubleClick(new Vector2(ca.X + 8, (ca.Y + cb.Y) / 2));   // double-click the word: it is selected
        r.Type("12");
        Assert.Null(r.Drawer.CellErrorOf(node));
        Assert.Equal(12, ((IntDoc)r.Session.Commit()).Count);
    }

    [SkippableFact]
    public void N3_ALongParsesAsLong()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new ListFormsFrameRail.Rig(new LongDoc(), typeof(LongDoc));
        r.Steady();
        r.Click(RightEnd(KitProbe.TextCell));
        r.Type("0000000000");                              // "50000000000" — more than an int holds
        Assert.Null(r.Drawer.CellErrorOf(r.Field("Big")));
        Assert.Equal(50000000000L, ((LongDoc)r.Session.Commit()).Big);
    }

    [SkippableFact]
    public void N3_FlagsAreToggleChips_WrittenAsTheirJoinedNames_AndANullableHasItsSetBox()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using (var r = new ListFormsFrameRail.Rig(new FlagsDoc(), typeof(FlagsDoc)))
        {
            r.Steady();
            Shot(r, "cells-n3-flags.png");
            r.Click(C(KitProbe.FlagChip + 1));            // B on
            Assert.Equal(Fl.A | Fl.B, ((FlagsDoc)r.Session.Commit()).Mode);
            r.Click(C(KitProbe.FlagChip));                // A off
            Assert.Equal(Fl.B, ((FlagsDoc)r.Session.Commit()).Mode);
        }
        using (var r = new ListFormsFrameRail.Rig(new NullDoc(), typeof(NullDoc)))
        {
            r.Steady();
            Shot(r, "cells-n3-nullable.png");
            Assert.Null(((NullDoc)r.Session.Commit()).Opt);
            r.Click(C(KitProbe.SetBox));                  // set ⇒ the type's default
            Assert.Equal(0, ((NullDoc)r.Session.Commit()).Opt);
            r.Click(C(KitProbe.SetBox));                  // unset ⇒ null
            Assert.Null(((NullDoc)r.Session.Commit()).Opt);
        }
    }

    [SkippableFact]
    public void N3_SteadyCells_AllocateNothing_ParseFormatAndTheDefaultMarkAreCached()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new ListFormsFrameRail.Rig(new DefDoc(), typeof(DefDoc), defaults: TypeDefaults.For(typeof(DefDoc)));
        r.Steady();
        Assert.True(r.Drawer.LeafRowBytes == 0, $"a steady number row with default marks allocates nothing ({r.Drawer.LeafRowBytes} bytes)");
        r.Click(RightEnd(KitProbe.TextCell)); r.Type("5");                          // changed: bar + ↺ drawn
        r.Steady();
        Assert.True(r.Drawer.LeafRowBytes == 0, $"a steady changed row (bar + ↺) allocates nothing ({r.Drawer.LeafRowBytes} bytes)");
    }

    // ── N-4 ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void N4_AValueAtItsDefaultIsGrey_AChangedOneAmberWithAnUndoMark_AndTheMarkWritesTheDefaultBack()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new ListFormsFrameRail.Rig(new DefDoc(), typeof(DefDoc), defaults: TypeDefaults.For(typeof(DefDoc)));
        r.Steady();
        var node = r.Field("Level");
        Assert.False(r.Drawer.IsChanged(node));
        KitProbe.TryGet(KitProbe.TextCell, out var a, out var b);
        int greyInk;
        using (var shot = Snapshot(r)) greyInk = Ink(shot.Image, a, b);
        Shot(r, "cells-n4-default.png");

        r.Click(RightEnd(KitProbe.TextCell)); r.Type("5");                       // "35"
        r.Steady();
        Assert.True(r.Drawer.IsChanged(node), "35 differs from the default 3");
        KitProbe.TryGet(KitProbe.TextCell, out a, out b);
        Shot(r, "cells-n4-changed.png");
        using (var shot = Snapshot(r))
        {
            int inkChanged = Ink(shot.Image, a, b);
            Assert.True(inkChanged > greyInk + 25, $"a changed value's text is brighter than the grey default ({inkChanged} vs {greyInk})");
            int bar = 0;
            for (int x = (int)a.X - 10; x < (int)a.X; x++) for (int y = (int)a.Y; y < (int)b.Y; y++)
            {
                var c = Raylib.GetImageColor(shot.Image, x, y);
                if (Math.Abs(c.R - IconPalette.Amber.X * 255) < 30 && Math.Abs(c.G - IconPalette.Amber.Y * 255) < 30 && Math.Abs(c.B - IconPalette.Amber.Z * 255) < 30) bar++;
            }
            Assert.True(bar >= 10, $"the amber bar stands left of the changed cell ({bar} px)");
        }
        Assert.True(KitProbe.TryGet(KitProbe.ResetMark, out _, out _));
        r.Click(C(KitProbe.ResetMark));                                          // ↺
        r.Steady();
        Assert.Equal(3, ((DefDoc)r.Session.Commit()).Level);
        Assert.False(r.Drawer.IsChanged(node));
        Assert.Equal("3", r.Drawer.CellTextOf(node));
    }

    private sealed class ShotHolder : IDisposable { public Image Image; public void Dispose() => Raylib.UnloadImage(Image); }
    private static ShotHolder Snapshot(ListFormsFrameRail.Rig r) => new() { Image = Raylib.LoadImageFromScreen() };

    private sealed class Spy : IMapPickContext
    {
        public int Shown;
        public bool IsPickPendingFor(string jsonPath) => false;
        public void RequestEntityPick(string jsonPath, string[]? filterPresets) { }
        public void RequestLocationPick(string jsonPath) { }
        public bool TryConsumeEntityPick(string jsonPath, out EntityRef picked) { picked = default; return false; }
        public bool TryConsumeLocationPick(string jsonPath, out PointLink location) { location = default; return false; }
        public bool CanShowOnMap => true;
        public void ShowOnMap(EntityRef entity) => Shown++;
    }

    [SkippableFact]
    public void N4_AReadOnlyTableRefusesTypedInput_AndStillShowsOnTheMap()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        // a node's own [EditReadOnly]
        using (var r = new ListFormsFrameRail.Rig(new LockDoc(), typeof(LockDoc)))
        {
            r.Steady();
            r.Click(RightEnd(KitProbe.TextCell)); r.Type("1");
            Assert.Equal(7, ((LockDoc)r.Session.Commit()).Locked);
            Assert.Null(r.Drawer.CellErrorOf(r.Field("Locked")));
        }
        // the whole table read-only (a host in Live / Replay): input refused, ⌖ live
        var spy = new Spy();
        using (var r = new ListFormsFrameRail.Rig(new IntDoc(), typeof(IntDoc), readOnly: true))
        {
            r.Steady();
            r.Click(RightEnd(KitProbe.TextCell)); r.Type("1");
            Assert.Equal(3, ((IntDoc)r.Session.Commit()).Count);
        }
        using (var r = new ListFormsFrameRail.Rig(new RefDoc(), typeof(RefDoc), readOnly: true, picks: spy))
        {
            r.Steady();
            Assert.True(KitProbe.TryGet(KitProbe.ShowOnMap, out _, out _), "the ⌖ is drawn on a read-only row");
            r.Click(C(KitProbe.ShowOnMap));
            Assert.True(spy.Shown >= 1, "show-on-map fires although the table is read-only");
        }
    }
}
