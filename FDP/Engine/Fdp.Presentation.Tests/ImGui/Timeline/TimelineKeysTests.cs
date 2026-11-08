using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Timeline;

/// <summary>
/// ⭐⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — the timeline's keys are ROWS of the keymap (focused-window scope, kind <c>Timeline</c>): every key and wheel it answers is declared, the panel reads them
/// through a <see cref="KeymapReader"/> (no ImGui frame needed — a hand-made device drives it), today's keys still do what they did, and a rebind moves each kind (Press · the wheel's Axis · the held pan button).
/// </summary>
public sealed class TimelineKeysTests
{
    private const long Tick = TimeSpan.TicksPerSecond / 60;
    private const string Shot = "combat.shot";

    private sealed class Cursor : ITimelineCursor
    {
        public long ExtentFrom => 0;
        public long ExtentTo => 600 * Tick;
        public long Now { get; set; } = 300 * Tick;
        public bool CanSeek => true;
        public bool CanPlay => true;
        public int Toggles;
        public List<long> Seeks { get; } = new();
        public void Seek(long ticks) { Seeks.Add(ticks); Now = ticks; }
        public void TogglePlay() => Toggles++;
    }

    private sealed class Track : ITimelineTrack
    {
        public string Id => "camera";
        public string Label => "Camera";
        public IReadOnlyList<TimelineTrackItem> Items { get; } = Array.Empty<TimelineTrackItem>();
        public void Select(string itemId) { }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<TimelineMenuItem>();
    }

    private static TimelineIndex Index()
    {
        var events = new[] { new TimelineEvent(100, Shot, 1, 0, 0, 0, 0), new TimelineEvent(250, Shot, 1, 0, 0, 0, 0), new TimelineEvent(400, Shot, 1, 0, 0, 0, 0) };
        var lanes = events.GroupBy(e => e.Lane).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Frame).ToArray());
        return new TimelineIndex(lanes, Enumerable.Range(0, 600).Select(f => f * Tick).ToArray(), 600, 600);
    }

    private static (TimelinePanel Panel, Keymap Map, ManualChordInput Hand, Cursor Cursor) Rig()
    {
        var map = new Keymap();
        var hand = new ManualChordInput();
        var panel = new TimelinePanel();
        panel.UseKeys(map, hand);
        panel.View.OnExtent(0, 600 * Tick);
        return (panel, map, hand, new Cursor());
    }

    private static void Keys(TimelinePanel p, Cursor c) => p.Keys(Index(), new[] { Shot }, 600f, c.Now, c);

    private static void Frame(ManualChordInput hand, EditorKey key, KeyModifiers mods = KeyModifiers.None) { hand.NextFrame(); hand.Modifiers = mods; hand.Press(key); }

    /// <summary>Every key and wheel the panel answers is declared in the focused-window scope, labelled, without a conflict, in all three kinds.</summary>
    [Fact]
    public void TheTimelinesKeysAreDeclaredRows_LabelledAndWithoutAConflict()
    {
        var (_, map, _, _) = Rig();
        Assert.Empty(map.Validate());
        var rows = Enumerable.Range(0, map.Count).Select(i => map[i]).ToList();
        Assert.All(rows, r => Assert.Equal((InputScope.FocusedWindow, TimelineKeys.Kind), (r.Scope, r.WindowKind)));
        Assert.All(rows, r => Assert.NotEqual(r.Action.ToString(), map.LabelOf(r.Action)));
        Assert.Contains(rows, r => r.Trigger == KeyTrigger.Press && r.Chord == new KeyBinding(EditorKey.Space, KeyModifiers.None) && r.Action.Equals(TimelineKeys.Play));
        Assert.Contains(rows, r => r.Trigger == KeyTrigger.Hold && r.Action.Equals(TimelineKeys.PanDrag) && r.Chord.Key == EditorKey.MouseMiddle);
        Assert.Contains(rows, r => r.Trigger == KeyTrigger.Axis && r.Action.Equals(TimelineKeys.ZoomWheelAnywhere) && r.Chord.Modifiers == KeyModifiers.Ctrl);
        Assert.Equal(2, rows.Count(r => r.Action.Equals(TimelineKeys.Redo)));           // Ctrl+Y and Ctrl+Shift+Z
        Assert.Equal(2, rows.Count(r => r.Action.Equals(TimelineKeys.ZoomIn)));         // = and the keypad +
    }

    /// <summary>
    /// Today's keys through the keymap: the arrows step a frame, Shift+arrows the previous / next EVENT (never also a frame), Home / End the ends, Space plays, = zooms in.
    /// 🔴 Red-proof (compiles): <c>Keys</c> reading <c>ImGui.IsKeyPressed(ImGuiKey.Space)</c> ⇒ the hand's Space is not seen ⇒ red; the Shift row's shadowing removed ⇒ Shift+→ also steps a frame ⇒ two seeks ⇒ red.
    /// </summary>
    [Fact]
    public void TodaysKeysStillWork_ThroughTheKeymap()
    {
        var (panel, _, hand, cursor) = Rig();
        Frame(hand, EditorKey.Right); Keys(panel, cursor);
        Assert.Equal(new[] { 301 * Tick }, cursor.Seeks);                                      // one frame on
        cursor.Seeks.Clear();
        Frame(hand, EditorKey.Right, KeyModifiers.Shift); Keys(panel, cursor);
        Assert.Equal(new[] { 400 * Tick }, cursor.Seeks);                                      // the next event, and only that
        cursor.Seeks.Clear();
        Frame(hand, EditorKey.Left); Keys(panel, cursor);
        Assert.Equal(new[] { 399 * Tick }, cursor.Seeks);
        cursor.Seeks.Clear();
        Frame(hand, EditorKey.Home); Keys(panel, cursor);
        Frame(hand, EditorKey.End); Keys(panel, cursor);
        Assert.Equal(new[] { 0L, 599 * Tick }, cursor.Seeks);
        Frame(hand, EditorKey.Space); Keys(panel, cursor);
        Assert.Equal(1, cursor.Toggles);
        panel.View.Fit();
        long span = panel.Window.Span;
        Frame(hand, EditorKey.Equals); Keys(panel, cursor);
        Assert.True(panel.Window.Span < span);                                                 // zoomed in
        Frame(hand, EditorKey.Minus); Keys(panel, cursor);
        Assert.True(panel.Window.Span > 0);
    }

    /// <summary>A rebind moves a Press: Space no longer plays, P does.</summary>
    [Fact]
    public void ARebindOfAPress_MovesIt()
    {
        var (panel, map, hand, cursor) = Rig();
        Assert.Equal(1, map.Unbind(InputScope.FocusedWindow, TimelineKeys.Kind, TimelineKeys.Play));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.P, KeyModifiers.None), InputScope.FocusedWindow, TimelineKeys.Kind, TimelineKeys.Play));
        Frame(hand, EditorKey.Space); Keys(panel, cursor);
        Assert.Equal(0, cursor.Toggles);
        Frame(hand, EditorKey.P); Keys(panel, cursor);
        Assert.Equal(1, cursor.Toggles);
    }

    /// <summary>Undo / redo of a track edit: Ctrl+Z undoes, Ctrl+Y and Ctrl+Shift+Z redo (never also undo), Alt blocks, no tracks blocks.</summary>
    [Fact]
    public void UndoAndRedo_AreRows_ShiftOwnsTheRedo_AltAndNoTracksBlock()
    {
        var (panel, _, hand, cursor) = Rig();
        int undone = 0, redone = 0;
        panel.Undo = () => undone++;
        panel.Redo = () => redone++;
        Frame(hand, EditorKey.Z, KeyModifiers.Ctrl); Keys(panel, cursor);
        Assert.Equal((0, 0), (undone, redone));                                               // no tracks shown: nothing to undo
        panel.TracksNow = new ITimelineTrack[] { new Track() };
        Frame(hand, EditorKey.Z, KeyModifiers.Ctrl); Keys(panel, cursor);
        Assert.Equal((1, 0), (undone, redone));
        Frame(hand, EditorKey.Z, KeyModifiers.Ctrl | KeyModifiers.Shift); Keys(panel, cursor);
        Assert.Equal((1, 1), (undone, redone));
        Frame(hand, EditorKey.Y, KeyModifiers.Ctrl); Keys(panel, cursor);
        Assert.Equal((1, 2), (undone, redone));
        Frame(hand, EditorKey.Z, KeyModifiers.Ctrl | KeyModifiers.Alt); Keys(panel, cursor);
        Assert.Equal((1, 2), (undone, redone));
    }

    /// <summary>
    /// The mouse: the plain wheel zooms (over the ruler), Ctrl+wheel zooms anywhere — the two never add up — and Shift+wheel is the PAN's, never also a zoom; the horizontal wheel pans; the pan button held
    /// and moved pans once it has moved 2 px, and a rebind moves the button.
    /// 🔴 Red-proof (compiles): <c>WheelZoom</c> reading only the plain row ⇒ Ctrl+wheel does not zoom ⇒ red; <c>PanFromInput</c> not checking <c>Held</c> ⇒ the moved mouse pans with no button ⇒ red.
    /// </summary>
    [Fact]
    public void TheMouse_IsAxisAndHoldRows_WheelZoomPanAndTheHeldButton()
    {
        var (panel, map, hand, _) = Rig();
        panel.View.Zoom(300f, 600f, 0.25);                                                     // zoomed in, so a pan has room
        hand.Wheel = 2f;
        Assert.Equal(2f, panel.WheelZoom());
        hand.Modifiers = KeyModifiers.Ctrl;
        Assert.Equal(2f, panel.WheelZoom());                                                   // Ctrl+wheel: its own row, the plain one stands aside — not 4
        hand.Modifiers = KeyModifiers.Shift;
        Assert.Equal(0f, panel.WheelZoom());                                                   // Shift+wheel is the pan's

        var before = panel.Window;
        panel.PanFromInput(600f);
        Assert.NotEqual(before, panel.Window);                                                 // Shift+wheel panned
        hand.Modifiers = KeyModifiers.Ctrl | KeyModifiers.Shift;
        before = panel.Window;
        panel.PanFromInput(600f);
        Assert.Equal(before, panel.Window);                                                    // with Ctrl it is a zoom, never a pan
        hand.NextFrame(); hand.Modifiers = KeyModifiers.None;
        hand.WheelH = 3f;
        panel.PanFromInput(600f);
        Assert.NotEqual(before, panel.Window);                                                 // the horizontal wheel pans

        hand.NextFrame();
        before = panel.Window;
        hand.DeltaX = 30f;
        panel.PanFromInput(600f);
        Assert.Equal(before, panel.Window);                                                    // moved, no button: nothing
        hand.Hold(EditorKey.MouseMiddle); hand.DeltaX = 1f;
        panel.PanFromInput(600f);
        Assert.Equal(before, panel.Window);                                                    // under 2 px: not yet a drag
        hand.DeltaX = 30f;
        panel.PanFromInput(600f);
        Assert.NotEqual(before, panel.Window);                                                 // dragging pans

        // a rebind moves the held button: the middle button no longer drags, the right one does
        Assert.Equal(1, map.Unbind(InputScope.FocusedWindow, TimelineKeys.Kind, TimelineKeys.PanDrag));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.MouseRight, KeyModifiers.None), InputScope.FocusedWindow, TimelineKeys.Kind, TimelineKeys.PanDrag, KeyTrigger.Hold));
        before = panel.Window;
        panel.PanFromInput(600f);
        Assert.Equal(before, panel.Window);                                                    // the middle button is held, the row is the right one
        hand.Release(EditorKey.MouseMiddle).Hold(EditorKey.MouseRight);
        panel.PanFromInput(600f);
        panel.PanFromInput(600f);
        Assert.NotEqual(before, panel.Window);
    }
}
