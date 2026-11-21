using System;
using Fdp.Core;
using Fdp.Presentation.Icons;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// ⭐⭐⭐ R-3 / V3 (docs/UX/DESIGN_Review_Perspective.md §3 D8, §4 V3) — <b>the ONE replay transport drawer</b>, moved out of <see cref="ReplayTimelinePanel"/> (CE-4261's
/// <c>DrawRow1_Transport</c> + R-4153's seek bar) and made to call ONLY <see cref="IReplayTransport"/>. Two profiles (<see cref="ReplayTransportParts"/>): the Replay Browser draws
/// everything (history · rewind · play-pause · rate · axis · step ◀ ▶ with hold-to-scrub · the seek bar — look unchanged); the editor's AAR segment draws history · step back · the seek bar.
/// ⭐ 0 B per steady frame: labels are cached per frame / axis, tooltips are constants.
/// </summary>
public sealed class ReplayTransportDrawer
{
    /// <summary>The seek bar's least width; below it the bar is not drawn (a narrow window keeps the transport).</summary>
    public const float SeekBarMinWidth = 80f;

    private static readonly float[] TimeRates = { 0.1f, 0.5f, 1.0f, 1.5f, 2.0f, 5.0f, 10.0f };
    private static readonly string[] RateLabels = BuildRateLabels();
    private static string[] BuildRateLabels() { var l = new string[TimeRates.Length]; for (int i = 0; i < l.Length; i++) l[i] = $"{TimeRates[i]:F1}x"; return l; }

    private readonly IReplayTransport _t;
    private readonly ReplayTransportParts _parts;
    private readonly float _seekBarMaxWidth;
    private readonly string _idBack, _idFwd, _idRewind, _idPlay, _idRate, _idAxisSim, _idAxisWall, _idStepBack, _idStepFwd, _idSeek;

    private float _stepHoldTime;
    private int _stepHoldDirection;
    private float _autoStepAccumulator;
    private bool _first;
    private bool _dragging;

    private int _seekLabelFrame = -1, _seekEndFrames = -1, _seekHoverFrame = -1;
    private bool _seekLabelsSim;
    private string _seekNow = "", _seekEnd = "", _seekHover = "", _seekDetail = "";
    private int _detailFrame = -1; private double _detailWall = double.NaN, _detailSim = double.NaN;
    private float _rateShown = -1f; private string _rateLabel = "";

    /// <param name="transport">The one seam.</param>
    /// <param name="parts">The profile (default: everything).</param>
    /// <param name="idPrefix">Widget id prefix — two drawers on one bar must differ (the Replay Browser's is <c>rb</c>).</param>
    /// <param name="seekBarMaxWidth">0 = the bar fills the free width (the Replay Browser); &gt; 0 caps it (the editor's segment shares the bar).</param>
    public ReplayTransportDrawer(IReplayTransport transport, ReplayTransportParts parts = ReplayTransportParts.All, string idPrefix = "rb", float seekBarMaxWidth = 0f)
    {
        _t = transport ?? throw new ArgumentNullException(nameof(transport));
        _parts = parts; _seekBarMaxWidth = seekBarMaxWidth;
        _idBack = $"##{idPrefix}_hist_back"; _idFwd = $"##{idPrefix}_hist_fwd"; _idRewind = $"##{idPrefix}_rewind"; _idPlay = $"##{idPrefix}_play_pause";
        _idRate = $"##{idPrefix}_playback_rate"; _idAxisSim = $"S##{idPrefix}_axis"; _idAxisWall = $"W##{idPrefix}_axis";
        _idStepBack = $"##{idPrefix}_step_back"; _idStepFwd = $"##{idPrefix}_step_fwd"; _idSeek = $"##{idPrefix}_seekbar";
    }

    /// <summary>The profile this drawer draws.</summary>
    public ReplayTransportParts Parts => _parts;

    /// <summary>⭐ A probe for the frame rails (R-4138: layout is measured): the seek bar's screen rect and the playhead's fraction as last drawn; null when not drawn.</summary>
    public (System.Numerics.Vector2 Min, System.Numerics.Vector2 Max, float Fraction, string Now, string End)? LastSeekBar { get; private set; }

    /// <summary>The tooltip on the seek bar's current-time label (frame · wall · sim), as last built — for the rails.</summary>
    public string SeekDetailText => _seekDetail;

    /// <summary>⭐ A probe for the rails: the items this drawer actually drew on its last <see cref="Draw"/> (the seek bar only when it fitted).</summary>
    public ReplayTransportParts LastDrawn { get; private set; }

    private bool Has(ReplayTransportParts p) => (_parts & p) != 0;

    /// <summary>Draws the profile's items, then the seek bar when the profile has it. <paramref name="iconSize"/> ≤ 0 ⇒ the frame height.</summary>
    public void Draw(float iconSize = 0f)
    {
        _first = true;
        LastDrawn = ReplayTransportParts.None;
        DrawButtons(iconSize);
        if (Has(ReplayTransportParts.SeekBar)) DrawSeekBar();
        else LastSeekBar = null;
    }

    private void Next() { if (!_first) Gui.SameLine(); _first = false; }

    /// <summary>⭐ A JUMP made through the seam is a history waypoint (◀ goes back to where you were): the current frame, then the target. Steps are not jumps.</summary>
    private void RecordJump(int target)
    {
        int cur = _t.CurrentFrame;
        if (cur < 0 || cur == target) return;
        _t.Waypoints.PushWaypoint(cur, Entity.Null);
        _t.Waypoints.PushWaypoint(target, Entity.Null);
    }

    private void DrawButtons(float iconSize)
    {
        if (iconSize <= 0f) iconSize = Gui.GetFrameHeight();
        float dt = Gui.GetIO().DeltaTime;
        bool hasRecording = _t.HasRecording;
        var hist = _t.Waypoints;

        if (Has(ReplayTransportParts.History))
        {
            Next(); LastDrawn |= ReplayTransportParts.History;
            if (TransportIconRenderer.DrawButton(_idBack, iconSize, TransportShape.HistoryBack, hist.CanGoBack, out _, out _))
                hist.GoBack();
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip("Navigate backward in selection history");

            Next();
            if (TransportIconRenderer.DrawButton(_idFwd, iconSize, TransportShape.HistoryFwd, hist.CanGoForward, out _, out _))
                hist.GoForward();
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip("Navigate forward in selection history");
        }

        if (Has(ReplayTransportParts.Rewind))
        {
            Next(); LastDrawn |= ReplayTransportParts.Rewind;
            if (TransportIconRenderer.DrawButton(_idRewind, iconSize, TransportShape.Rewind, hasRecording, out _, out _))
            {
                _t.Pause();
                RecordJump(0);
                _t.Seek(0);
            }
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip("Rewind to start");
        }

        if (Has(ReplayTransportParts.PlayPause))
        {
            Next(); LastDrawn |= ReplayTransportParts.PlayPause;
            bool playing = _t.IsPlaying;
            TransportShape playPauseShape = playing ? TransportShape.Pause : TransportShape.Play;
            bool playEnabled = _t.CanPlay;
            if (!playEnabled) Gui.BeginDisabled();
            if (TransportIconRenderer.DrawButton(_idPlay, iconSize, playPauseShape, playEnabled, out _, out _))
            {
                if (playing) _t.Pause(); else _t.Play();
            }
            if (!playEnabled) Gui.EndDisabled();
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip(_t.PlayDisabledReason ?? (playing ? "Pause playback" : "Start playback"));
        }

        float comboOffsetY = (iconSize - Gui.GetFrameHeight()) * 0.5f;
        if (Has(ReplayTransportParts.Rate))
        {
            Next(); LastDrawn |= ReplayTransportParts.Rate;
            Gui.SetCursorPosY(Gui.GetCursorPosY() + comboOffsetY);
            float rate = _t.Rate;
            if (rate != _rateShown) { _rateShown = rate; _rateLabel = $"{rate:F1}x"; }
            Gui.SetNextItemWidth(60f);
            if (Gui.BeginCombo(_idRate, _rateLabel))
            {
                for (int i = 0; i < TimeRates.Length; i++)
                {
                    bool isSelected = Math.Abs(rate - TimeRates[i]) < 0.01f;
                    if (Gui.Selectable(RateLabels[i], isSelected))
                        _t.Rate = TimeRates[i];
                }
                Gui.EndCombo();
            }
            if (Gui.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip("Set playback speed");
            Gui.SetCursorPosY(Gui.GetCursorPosY() - comboOffsetY);
        }

        // ⭐ CE-3330 T13 (DAR §9c, row 34; R-3058) — the viewer's time axis, one small toggle: S = sim (pauses collapsed), W = wall
        if (Has(ReplayTransportParts.Axis) && _t.HasAxisToggle)
        {
            Next(); LastDrawn |= ReplayTransportParts.Axis;
            Gui.SetCursorPosY(Gui.GetCursorPosY() + comboOffsetY);
            bool sim = _t.AxisIsSim;
            bool can = _t.HasSimAxis;
            if (!can) Gui.BeginDisabled();
            if (Gui.SmallButton(sim && can ? _idAxisSim : _idAxisWall)) _t.ToggleAxis();
            if (!can) Gui.EndDisabled();
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip(!can ? "Wall time - this recording has no sim time (format v6)"
                             : sim ? "Sim time - pauses skipped (click: wall time)" : "Wall time - pauses shown (click: sim time)");
            Gui.SetCursorPosY(Gui.GetCursorPosY() - comboOffsetY);
        }

        bool stepBackHeld = false, stepFwdHeld = false;
        if (Has(ReplayTransportParts.StepBack))
        {
            Next(); LastDrawn |= ReplayTransportParts.StepBack;
            TransportIconRenderer.DrawButton(_idStepBack, iconSize, TransportShape.StepBack, hasRecording, out stepBackHeld, out bool stepBackActivated);
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip("Step backward (hold to scrub)");
            if (stepBackActivated)
            {
                _t.Pause();
                _t.Step(-1);
                _stepHoldTime = 0f; _stepHoldDirection = -1; _autoStepAccumulator = 0f;
            }
        }
        if (Has(ReplayTransportParts.StepForward))
        {
            Next(); LastDrawn |= ReplayTransportParts.StepForward;
            TransportIconRenderer.DrawButton(_idStepFwd, iconSize, TransportShape.StepFwd, hasRecording, out stepFwdHeld, out bool stepFwdActivated);
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip("Step forward (hold to scrub)");
            if (stepFwdActivated)
            {
                _t.Pause();
                _t.Step(1);
                _stepHoldTime = 0f; _stepHoldDirection = 1; _autoStepAccumulator = 0f;
            }
        }

        bool autoBackHeld = stepBackHeld && !stepFwdHeld;
        bool autoFwdHeld = stepFwdHeld && !stepBackHeld;

        if (hasRecording && (autoBackHeld || autoFwdHeld))
        {
            _stepHoldDirection = autoBackHeld ? -1 : 1;
            _stepHoldTime += dt;

            const float holdDebounceSec = 0.30f;
            const float holdRampSec = 2.00f;

            if (_stepHoldTime > holdDebounceSec)
            {
                _t.Pause();

                float rampProgress = Math.Clamp((_stepHoldTime - holdDebounceSec) / holdRampSec, 0f, 1f);
                float currentRate = 1.0f + 9.0f * rampProgress;

                _autoStepAccumulator += dt * currentRate;
                float frameTime = 1.0f / 60.0f;

                int framesToStep = 0;
                while (_autoStepAccumulator >= frameTime)
                {
                    _autoStepAccumulator -= frameTime;
                    framesToStep++;
                }

                if (framesToStep > 0)
                {
                    if (_stepHoldDirection > 0)
                    {
                        for (int i = 0; i < framesToStep; i++)
                            _t.Step(1);
                    }
                    else
                    {
                        int currentFrame = _t.CurrentFrame;
                        int targetFrame = Math.Max(0, currentFrame - framesToStep);
                        if (targetFrame < currentFrame)
                            _t.Seek(targetFrame);
                    }
                }
            }
        }
        else
        {
            _stepHoldTime = 0f;
            _stepHoldDirection = 0;
            _autoStepAccumulator = 0f;
        }
    }

    /// <summary>⭐ R-4153 / R-4155 — the main bar's seek bar: the current time, the bar (drag = scrub, click = jump, hover = "0:50.1 · frame 2 640"), the end time.
    /// ⛔ No ticks (the events are in the Event Timeline). Labels are cached per frame, so a steady frame allocates nothing.</summary>
    private void DrawSeekBar()
    {
        LastSeekBar = null;
        int total = _t.TotalFrames;
        if (!_t.HasRecording || total <= 0) { _dragging = false; return; }
        int max = total - 1;
        int current = Math.Clamp(_t.CurrentFrame, 0, max);
        bool sim = _t.SimAxisActive;
        if (sim != _seekLabelsSim) { _seekLabelsSim = sim; _seekLabelFrame = _seekEndFrames = _seekHoverFrame = -1; }   // the axis toggled: relabel
        if (current != _seekLabelFrame) { _seekLabelFrame = current; _seekNow = ReplayTimelinePanel.SeekTime(_t.SecondsOf(current)); }
        if (total != _seekEndFrames) { _seekEndFrames = total; _seekEnd = ReplayTimelinePanel.SeekTime(_t.SecondsOf(max)); }

        var style = Gui.GetStyle();
        float endW = Gui.CalcTextSize(_seekEnd).X;
        if (!_first) Gui.SameLine(0f, style.ItemSpacing.X * 2f);
        _first = false;
        float avail = Gui.GetContentRegionAvail().X;
        float nowW = Gui.CalcTextSize(_seekNow).X;
        float barW = avail - nowW - endW - style.ItemSpacing.X * 4f;
        if (_seekBarMaxWidth > 0f && barW > _seekBarMaxWidth) barW = _seekBarMaxWidth;
        if (barW < SeekBarMinWidth) return;

        Gui.AlignTextToFramePadding();
        Gui.TextUnformatted(_seekNow);
        if (Gui.IsItemHovered())
        {
            // ⭐ B-7 part 3 (R-4155) — the retired Replay Timeline window's frame / wall / sim readout: "frame 2 103 · wall 40.602 s · sim 39.997 s" (cached, 0 B steady)
            _t.FrameTimes(out _, out double wallSec, out double simSec);
            if (current != _detailFrame || wallSec != _detailWall || simSec != _detailSim)
            {
                _detailFrame = current; _detailWall = wallSec; _detailSim = simSec;
                _seekDetail = ReplayTimelinePanel.DetailText(current, wallSec, simSec);
            }
            Gui.SetTooltip(_seekDetail);
        }
        Gui.SameLine();
        float h = Gui.GetFrameHeight();
        var p0 = Gui.GetCursorScreenPos();
        Gui.InvisibleButton(_idSeek, new System.Numerics.Vector2(barW, h));
        bool hovered = Gui.IsItemHovered(), active = Gui.IsItemActive();
        float fx = Gui.GetIO().MousePos.X;
        if (active)
        {
            int target = ReplayTimelinePanel.FrameAt((fx - p0.X) / barW, max);
            if (target != current)
            {
                _t.Pause();
                if (!_dragging) RecordJump(target);   // ⭐ the drag's START is the waypoint (◀ returns to where the scrub began), not every frame of it
                _t.Seek(target); current = target;
            }
        }
        _dragging = active;

        var dl = Gui.GetWindowDrawList();
        float cy = p0.Y + h * 0.5f, track = 4f, knob = 6f;
        float px = p0.X + (max > 0 ? (float)current / max : 0f) * barW;
        dl.AddRectFilled(new System.Numerics.Vector2(p0.X, cy - track * 0.5f), new System.Numerics.Vector2(p0.X + barW, cy + track * 0.5f), Gui.GetColorU32(ImGuiCol.FrameBg), track * 0.5f);
        dl.AddRectFilled(new System.Numerics.Vector2(p0.X, cy - track * 0.5f), new System.Numerics.Vector2(px, cy + track * 0.5f), Gui.GetColorU32(ImGuiCol.SliderGrabActive), track * 0.5f);
        dl.AddCircleFilled(new System.Numerics.Vector2(px, cy), hovered || active ? knob + 1f : knob, Gui.GetColorU32(ImGuiCol.Text));
        LastDrawn |= ReplayTransportParts.SeekBar;
        LastSeekBar = (p0, new System.Numerics.Vector2(p0.X + barW, p0.Y + h), max > 0 ? (float)current / max : 0f, _seekNow, _seekEnd);

        if (hovered && !active)
        {
            int at = ReplayTimelinePanel.FrameAt((fx - p0.X) / barW, max);
            if (at != _seekHoverFrame) { _seekHoverFrame = at; _seekHover = $"{ReplayTimelinePanel.SeekTime(_t.SecondsOf(at))} · frame {at:N0}".Replace(',', ' '); }
            Gui.SetTooltip(_seekHover);
        }
        Gui.SameLine();
        Gui.AlignTextToFramePadding();
        Gui.TextDisabled(_seekEnd);
    }
}
