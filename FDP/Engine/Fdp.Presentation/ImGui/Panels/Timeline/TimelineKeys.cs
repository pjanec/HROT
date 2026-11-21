using Fdp.Toolkit.Input;
using NodeEditor.Primitives;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>
/// ⭐⭐ B-10 K4 (📄 docs/UX/DESIGN_Input_And_Commands.md §6g) — the timeline's input as ROWS of the keymap, in the FOCUSED-WINDOW scope, window kind <see cref="Kind"/> (the panel reads them
/// while it is focused — "panel focused only, never stolen from the map", DESIGN_Director_Timeline_Editor §10b). ⛔ SUPERSEDES the <c>ImGui.IsKeyPressed(…)</c> / <c>io.MouseWheel</c> reads of
/// <c>TimelinePanel.Keys</c> and <c>PanAnywhere</c>.
/// <para>Press rows are one-shot (the zoom, the arrows and the undo repeat while held, as ImGui's key repeat always gave them); the wheel, the horizontal wheel and the middle-drag movement are Axis rows
/// with the modifier of their meaning (the plain wheel zooms over the ruler, <b>Ctrl</b>+wheel zooms anywhere, <b>Shift</b>+wheel pans); the middle button is a Hold row.</para>
/// </summary>
public static class TimelineKeys
{
    /// <summary>The window kind of the timeline's rows (the Keyboard shortcuts window says <i>In Timeline</i>).</summary>
    public const string Kind = "Timeline";

    private static ActionRef Id(string id) => ActionRef.Command(id);

    // ── Press, one-shot ─────────────────────────────────────────────────────────────────────
    /// <summary>The selected track item's own K / J / L (a key-frame offer) — taken by the track first.</summary>
    public static readonly ActionRef OfferK = Id("timeline.track.offer-k"), OfferJ = Id("timeline.track.offer-j"), OfferL = Id("timeline.track.offer-l");
    /// <summary>Play / pause.</summary>
    public static readonly ActionRef Play = Id("timeline.play");
    /// <summary>Fit the whole recording.</summary>
    public static readonly ActionRef Fit = Id("timeline.fit");
    /// <summary>Back to the previous view.</summary>
    public static readonly ActionRef Back = Id("timeline.back");
    /// <summary>Range: in at the playhead.</summary>
    public static readonly ActionRef RangeIn = Id("timeline.range.in");
    /// <summary>Range: out at the playhead.</summary>
    public static readonly ActionRef RangeOut = Id("timeline.range.out");
    /// <summary>Clear the range.</summary>
    public static readonly ActionRef RangeClear = Id("timeline.range.clear");
    /// <summary>Seek to the first / last frame.</summary>
    public static readonly ActionRef First = Id("timeline.first"), Last = Id("timeline.last");
    /// <summary>The view presets 0…3 (the last 30 s, 2 min, 10 min, everything).</summary>
    public static ActionRef Preset(int index) => PresetIds[index];
    private static readonly ActionRef[] PresetIds = { Id("timeline.preset.0"), Id("timeline.preset.1"), Id("timeline.preset.2"), Id("timeline.preset.3") };

    // ── Press, repeating ────────────────────────────────────────────────────────────────────
    /// <summary>Undo / redo a track edit.</summary>
    public static readonly ActionRef Undo = Id("timeline.undo"), Redo = Id("timeline.redo");
    /// <summary>Zoom in / out one step about the middle.</summary>
    public static readonly ActionRef ZoomIn = Id("timeline.zoom.in"), ZoomOut = Id("timeline.zoom.out");
    /// <summary>One frame back / forward (the selection on a live host).</summary>
    public static readonly ActionRef StepBack = Id("timeline.step.back"), StepForward = Id("timeline.step.forward");
    /// <summary>The previous / next event.</summary>
    public static readonly ActionRef EventPrevious = Id("timeline.event.previous"), EventNext = Id("timeline.event.next");

    // ── Axis / Hold, the mouse ─────────────────────────────────────────────────────────────
    /// <summary>The wheel zooms about the tick under the mouse — over the ruler and the overview.</summary>
    public static readonly ActionRef ZoomWheel = Id("timeline.zoom.wheel");
    /// <summary>Ctrl + the wheel zooms anywhere over the panel.</summary>
    public static readonly ActionRef ZoomWheelAnywhere = Id("timeline.zoom.wheel.anywhere");
    /// <summary>The horizontal wheel pans.</summary>
    public static readonly ActionRef PanWheelH = Id("timeline.pan.wheel-h");
    /// <summary>Shift + the wheel pans.</summary>
    public static readonly ActionRef PanWheel = Id("timeline.pan.wheel");
    /// <summary>The pan drag: the button held…</summary>
    public static readonly ActionRef PanDrag = Id("timeline.pan.drag");
    /// <summary>… and the movement along X it pans by.</summary>
    public static readonly ActionRef PanDragX = Id("timeline.pan.drag.x");

    /// <summary>Declares the timeline's rows into <paramref name="keymap"/> (idempotent; today's keys).</summary>
    public static void Declare(Keymap keymap)
    {
        var r = new RowDeclarer(keymap, InputScope.FocusedWindow, Kind);
        r.Press(EditorKey.K, OfferK, "Track: key-frame offer K").Press(EditorKey.J, OfferJ, "Track: key-frame offer J").Press(EditorKey.L, OfferL, "Track: key-frame offer L")
         .Press(EditorKey.Space, Play, "Play / pause").Press(EditorKey.F, Fit, "Fit everything").Press(EditorKey.Backspace, Back, "Back to the previous view")
         .Press(EditorKey.I, RangeIn, "Range in").Press(EditorKey.O, RangeOut, "Range out").Press(EditorKey.Escape, RangeClear, "Clear the range")
         .Press(EditorKey.Home, First, "First frame").Press(EditorKey.End, Last, "Last frame");
        string[] presets = { "View: last 30 s", "View: last 2 min", "View: last 10 min", "View: everything" };
        for (int i = 0; i < presets.Length; i++) r.Press(EditorKey.D0 + i, Preset(i), presets[i]);
        r.Press(EditorKey.Z, Undo, "Undo a track edit", KeyModifiers.Ctrl)
         .Press(EditorKey.Y, Redo, "Redo a track edit", KeyModifiers.Ctrl).Press(EditorKey.Z, Redo, "Redo a track edit", KeyModifiers.Ctrl | KeyModifiers.Shift)
         .Press(EditorKey.Equals, ZoomIn, "Zoom in").Press(EditorKey.KpAdd, ZoomIn, "Zoom in").Press(EditorKey.Minus, ZoomOut, "Zoom out").Press(EditorKey.KpSubtract, ZoomOut, "Zoom out")
         .Press(EditorKey.Left, StepBack, "One frame back").Press(EditorKey.Right, StepForward, "One frame forward")
         .Press(EditorKey.Left, EventPrevious, "Previous event", KeyModifiers.Shift).Press(EditorKey.Right, EventNext, "Next event", KeyModifiers.Shift);
        r.Axis(EditorKey.MouseWheel, ZoomWheel, "Zoom (over the ruler)")
         .Axis(EditorKey.MouseWheel, ZoomWheelAnywhere, "Zoom (anywhere)", 1f, KeyModifiers.Ctrl)
         .Axis(EditorKey.MouseWheelH, PanWheelH, "Pan")
         .Axis(EditorKey.MouseWheel, PanWheel, "Pan", 1f, KeyModifiers.Shift)
         .Hold(EditorKey.MouseMiddle, PanDrag, "Pan by dragging")
         .Axis(EditorKey.MouseDeltaX, PanDragX, "Pan by dragging");
    }
}
