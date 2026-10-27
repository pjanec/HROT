using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CE-1364</c> T3 (docs/DESIGN_Director_Timeline_Editor.md §10, TE-2) — the camera of a new key from the pilot's view: the map camera's pose as a
/// look-at camera, or — when a unit is selected — a FOLLOW camera on that unit that keeps the SAME eye and now looks at the unit (the film's follow
/// camera looks at <c>unit + lift</c>, <see cref="FilmCaptureLayer"/>), so a key made "relative to a selected unit" still moves with it.
/// <paramref name="fov"/> (CE-1364) is the map camera's lens (<c>MapCamera3D.FovyDegrees</c>), stored in the key's <see cref="CameraSpec.Fov"/>.
/// </summary>
public static class PilotCamera
{
    private const float Deg = 180f / MathF.PI;

    /// <summary>
    /// ⭐ <c>CE-1303</c> P1 (§11j; was <c>EditorSubsystem.PilotCameraSpec</c>) — the pilot camera of a 3-D map camera: its pose and lens and — when a NAMED unit
    /// is <paramref name="selected"/> and alive in <paramref name="world"/> — a follow camera on it. Null when the map camera is not a 3-D one.
    /// </summary>
    public static CameraSpec? FromMap(Fdp.Toolkit.Vis2D.Components.MapCamera? camera, Fdp.Core.EntityRepository? world, Fdp.Core.Entity? selected)
    {
        if (camera is not MapCamera3D cam) return null;
        var pose = new CameraPose(cam.LookAt, cam.Distance, cam.Yaw, cam.Pitch);
        string? unit = null;
        Vector3? at = null;
        if (world != null && selected is { } e && !e.IsNull && world.IsAlive(e)
            && world.HasComponent<Fdp.Core.EntityInfo>(e) && world.HasComponent<Fdp.Core.SimTransform>(e))
        {
            var name = world.GetComponentRO<Fdp.Core.EntityInfo>(e).Name.ToString();
            if (name.Length > 0) { unit = name; at = world.GetComponentRO<Fdp.Core.SimTransform>(e).Position; }
        }
        return Of(pose, unit, at, fov: cam.FovyDegrees);   // CE-1364: K stores the map camera's lens
    }

    public static CameraSpec Of(CameraPose pose, string? unit = null, Vector3? unitAt = null, float lift = 1.5f, float? fov = null)
    {
        if (unit is null || unitAt is not { } at)
            return new CameraSpec { LookAt = pose.LookAt, Distance = pose.Distance, Yaw = pose.Yaw * Deg, Pitch = pose.Pitch * Deg, Fov = fov };
        var forward = new Vector3(MathF.Cos(pose.Pitch) * MathF.Cos(pose.Yaw), MathF.Cos(pose.Pitch) * MathF.Sin(pose.Yaw), MathF.Sin(pose.Pitch));
        var eye = pose.LookAt - forward * pose.Distance;
        var toUnit = at + new Vector3(0f, 0f, lift) - eye;
        float distance = toUnit.Length();
        if (!(distance > 1e-3f)) return new CameraSpec { Follow = unit, Distance = pose.Distance, Yaw = pose.Yaw * Deg, Pitch = pose.Pitch * Deg, Lift = lift, Fov = fov };
        return new CameraSpec
        {
            Follow = unit, Lift = lift, Distance = distance,
            Yaw = MathF.Atan2(toUnit.Y, toUnit.X) * Deg, Pitch = MathF.Asin(Math.Clamp(toUnit.Z / distance, -1f, 1f)) * Deg, Fov = fov,
        };
    }
}

/// <summary>
/// ⭐ <c>CE-1364</c> T3 (§10) — the director's two tracks on the panel, over ONE <see cref="TakeEditor"/>: <b>Camera</b> (a <see cref="CameraKey"/> at its
/// sim second) and <b>Time</b> (a <see cref="TimeSegment"/> From–To). A drag is an edit of the TAKE, never of the world (§2c row 2): the preview shows the
/// moved item without touching the take, the commit is one <see cref="TakeEditor.Apply"/>. <b>K</b> adds a cut key from the pilot camera; <b>J</b> / <b>L</b>
/// step the playback rate down / up; the key's right-click menu deletes it.
/// </summary>
public sealed class DirectorTracks
{
    public const string CameraId = "director.camera", TimeId = "director.time", SubtitlesId = "director.subtitles",
                        ViewId = "director.view", GraphicsId = "director.graphics";

    private readonly TakeEditor _editor;
    private readonly Func<CameraSpec?> _pilot;
    private readonly Action<int>? _stepRate;
    private readonly Func<double, EventAnchor?>? _eventAt;
    private readonly Func<FilmSegment?>? _segmentNow;
    private readonly Func<EventAnchor, ShotTemplate, double, CameraSpec?>? _template;
    private string? _selected;

    /// <summary>⭐ <c>CE-4059</c> / CR-7 (§11q K2) — a key scoped to a segment is drawn in this colour (ABGR) and its label names the segment.</summary>
    public const uint ScopedKeyColour = 0xFFE070D0;

    /// <param name="pilot">The pilot camera as a key's camera (<see cref="PilotCamera.Of"/>), null when there is none.</param>
    /// <param name="stepRate">J (−1) / L (+1): the playback rate seam; null = this host has none (J / L are then not taken).</param>
    /// <param name="eventAt">⭐ CR-6: the nearest index event at or before a sim second (<see cref="EventAnchors.NearestBefore"/> over the host's index), null when none
    /// precedes it — K then stores the key as that event + an offset; null function = this host has no index (K stores a fixed time).</param>
    /// <param name="segmentNow">⭐ <c>CE-4059</c> / CR-7 (§11q K3) — the segment on screen while the runner plays THIS take (<see cref="DirectorRunner.SegmentOf"/>); during a
    /// replay (<see cref="FilmSegment.Back"/>) K and <i>update key from view</i> scope the new key to it. Null = this host never plays segments.</param>
    /// <param name="template">⭐ <c>CE-1305</c> (§11v T2) — a shot template's camera for a key at a sim second hung on an event (<see cref="DirectorDrafts.TemplateCamera"/>:
    /// the drafter's rule); null = this host has no drafter (no templates offered).</param>
    public DirectorTracks(TakeEditor editor, Func<CameraSpec?> pilot, Action<int>? stepRate = null, Func<double, EventAnchor?>? eventAt = null,
                          Func<FilmSegment?>? segmentNow = null, Func<EventAnchor, ShotTemplate, double, CameraSpec?>? template = null)
    {
        _template = template;
        _eventAt = eventAt;
        _segmentNow = segmentNow;
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _pilot = pilot ?? throw new ArgumentNullException(nameof(pilot));
        _stepRate = stepRate;
        Camera = new CameraTrack(this);
        Time = MakeTimeTrack();
        Subtitles = MakeSubtitleTrack();
        View = new ViewTrack(this);
        Graphics = MakeGraphicsTrack();
        Tracks = new ITimelineTrack[] { Camera, Time, Subtitles, View, Graphics };
    }

    public ITimelineTrack Camera { get; }
    public ITimelineTrack Time { get; }
    /// <summary>⭐ <c>CE-1303</c> P2b — CR-11: the take's subtitles (its captions read as subtitles until the first subtitle edit — S8).</summary>
    public ITimelineTrack Subtitles { get; }
    /// <summary>⭐ <c>CE-1304</c> P6 (§11t V3): the View track — 2-D / 3-D and the layer set from a time (the take's layers read as views until the first view edit).</summary>
    public ITimelineTrack View { get; }
    /// <summary>⭐ <c>CE-1305</c> P6 (§11t G4): the film-only graphics (TE-9).</summary>
    public ITimelineTrack Graphics { get; }
    public IReadOnlyList<ITimelineTrack> Tracks { get; }
    public TakeEditor Editor => _editor;

    private double Shown(string id, double sim) => _editor.Preview is { } p && p.ItemId == id ? p.Sim : sim;

    private sealed class CameraTrack : ITimelineTrack
    {
        private readonly DirectorTracks _d;
        public CameraTrack(DirectorTracks d) => _d = d;
        public string Id => CameraId;
        public string Label => "Camera";

        public IReadOnlyList<TimelineTrackItem> Items
        {
            get
            {
                var keys = CameraKeys.Of(_d._editor.Take);
                var list = new List<TimelineTrackItem>(keys.Count);
                for (int i = 0; i < keys.Count; i++)
                {
                    if (_d._editor.Resolve(keys[i].At) is not { } at) continue;   // an anchor with no baked time: nowhere to draw it
                    string id = KeyId(i);
                    // ⭐ CR-7 (§11q K2) — a key of one segment: its own colour, and the label says which
                    list.Add(new TimelineTrackItem(id, _d.Shown(id, at), null, Describe(keys[i]), TimelineTrackItemKind.Key, id == _d._selected,
                                                   keys[i].Segment != null ? ScopedKeyColour : null));
                }
                return list;
            }
        }

        public void Select(string itemId) => _d._selected = itemId;

        public void Move(string itemId, double toSimSeconds, bool commit)
        {
            if (IndexOf(itemId, "key:") is not { } i) return;
            if (!commit) { _d._editor.SetPreview(itemId, toSimSeconds); return; }
            _d._editor.ClearPreview();
            _d._editor.Apply(new TakeEdit.MoveKey(i, toSimSeconds));
        }

        public bool Key(char key, double atSimSeconds)
        {
            switch (char.ToUpperInvariant(key))
            {
                case 'K':
                    if (_d._pilot() is not { } camera) return false;
                    double sim = Math.Max(atSimSeconds, 0d);
                    return _d.AddKeyAt(sim, camera, _d._eventAt?.Invoke(sim));
                case 'U': return _d.UpdateKeyFromView(atSimSeconds);   // ⭐ CE-1303 P2b (§11o S11)
                case 'J': if (_d._stepRate == null) return false; _d._stepRate(-1); return true;
                case 'L': if (_d._stepRate == null) return false; _d._stepRate(+1); return true;
                default: return false;
            }
        }

        /// <summary>⭐ <c>CE-1305</c> (§11v T2) — empty Camera track space: <i>"Add key: &lt;template&gt;"</i> for each shot template, at the sim second the click landed,
        /// disabled where there is no event before it to frame.</summary>
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId, double atSimSeconds)
        {
            if (itemId != null || _d._template == null) return MenuItems(itemId);
            double sim = Math.Max(atSimSeconds, 0d);
            var anchor = _d._eventAt?.Invoke(sim);
            return Drafter.Templates.Select(t => new TimelineMenuItem(TemplateMenuPrefix + Drafter.LabelOf(t), () => _d.AddTemplateKey(t, sim),
                                                                      anchor != null && _d._template(anchor, t, sim) != null)).ToList();
        }

        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId)
        {
            if (itemId != null && IndexOf(itemId, "key:") is { } i)
            {
                yield return new TimelineMenuItem("Delete key", () =>
                {
                    if (_d._editor.Apply(new TakeEdit.RemoveKey(i))) _d._selected = null;
                });
                // ⭐ CE-1303 P2b (§11o S11) — this key takes the pilot camera's view (one undo step)
                if (_d._pilot() is { } view)
                    yield return new TimelineMenuItem("Update key from view (U)", () => _d._editor.Apply(new TakeEdit.SetKeyCamera(i, view)));
            }
        }

        private static int? IndexOf(string id, string prefix) =>
            id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : null;
        private static string KeyId(int i) => "key:" + i.ToString(CultureInfo.InvariantCulture);
        internal static string KeyIdOf(int i) => KeyId(i);

        private static string Describe(CameraKey k)
        {
            var c = k.Camera;
            string what = c.Follow != null ? "follow " + c.Follow
                : c.Over != null ? "over " + c.Over
                : c.LookAt != null ? "look-at"
                : c.Eye != null || c.Target != null ? "eye-target" : "camera";
            string text = k.In.Kind switch
            {
                KeyTransitionKind.Fly => $"{what} (fly {k.In.Seconds:0.#} s)",
                KeyTransitionKind.Dip => $"{what} (dip)",
                _ => what,
            };
            if (EventAnchors.Label(k.At) is { } anchor) text += " @ " + anchor;   // ⭐ CR-6: the key shows what it is tied to
            return k.Segment is { } seg ? text + " | segment " + seg : text;    // ⭐ CR-7 (§11q K2): and the one segment it plays in
        }
    }

    /// <summary>The Camera track's empty-space menu prefix for a shot template.</summary>
    public const string TemplateMenuPrefix = "Add key: ";

    /// <summary>
    /// ⭐ <c>CE-1305</c> (§11v T2) — a cut key at <paramref name="atSim"/> with <paramref name="template"/>'s camera, framed on the event K would hang the key on (the
    /// nearest preceding index event): the drafter's rule, hung on that event the way K hangs one (CR-6) — ONE undo step. False (nothing added) when this host has
    /// no drafter, no event precedes the second, or the drafter cannot frame it.
    /// </summary>
    public bool AddTemplateKey(ShotTemplate template, double atSim)
    {
        double sim = Math.Max(atSim, 0d);
        if (_template == null || _eventAt?.Invoke(sim) is not { } anchor || _template(anchor, template, sim) is not { } camera) return false;
        return AddKeyAt(sim, camera, anchor);
    }

    /// <summary>Whether a template key can be added at <paramref name="atSim"/> (an event precedes it that the drafter frames).</summary>
    public bool CanAddTemplateKey(ShotTemplate template, double atSim)
        => _template != null && _eventAt?.Invoke(Math.Max(atSim, 0d)) is { } a && _template(a, template, Math.Max(atSim, 0d)) != null;

    /// <summary>K's add, shared with the templates: a cut key with <paramref name="camera"/> at <paramref name="sim"/>, hung on <paramref name="anchor"/> when there is one
    /// (CR-6), scoped to the replay on screen (CR-7), selected; one undo step.</summary>
    private bool AddKeyAt(double sim, CameraSpec camera, EventAnchor? anchor)
    {
        int count = CameraKeys.Of(_editor.Take).Count;
        var at = new AtExpr(sim);
        if (anchor != null)   // ⭐ CR-6: the nearest preceding event + the sim seconds since it (against the take's baked time for it, so the key lands on the playhead)
        {
            double basis = _editor.Take.Baked is { } baked && baked.Times.TryGetValue(anchor.Name, out var t) ? t : anchor.EventSim;
            at = new AtExpr(Math.Round(sim - basis, 3), anchor.Name);
        }
        // ⭐ CR-7 (§11q K3) — a key added during a replay pass belongs to that replay (at its real source time), never to the main pass
        if (!_editor.Apply(new TakeEdit.AddKey(new CameraKey { At = at, Camera = camera, Fov = camera.Fov, In = KeyTransition.Cut }, anchor, ReplayOnScreen())))
            return false;
        _selected = CameraTrack.KeyIdOf(count);
        return true;
    }

    /// <summary>⭐ <c>CE-1303</c> P2b (§11o S11) — "update key from view": the SELECTED camera key — else the key on screen at <paramref name="playheadSim"/>
    /// (the last whose time is at or before it) — takes the pilot camera (its lens too), one undo step; false when there is no pilot or no such key.</summary>
    public bool UpdateKeyFromView(double playheadSim)
    {
        if (_pilot() is not { } camera) return false;
        var keys = CameraKeys.Of(_editor.Take);
        int? replay = ReplayOnScreen();
        string? replayId = replay is { } r ? _editor.Take.Segments[r].Id : null;
        int? index = _selected is { } sel && SpanTrack.IndexOf(sel, "key:") is { } i && i < keys.Count ? i : null;
        if (index is null)
        {
            // the key on screen: among the keys the segment playing shows (§11q K1) — in a replay its own once they start, else the unscoped ones
            var visible = CameraKeys.OnScreenIn(keys, replay is null ? null : replayId ?? "", n => _editor.Resolve(new AtExpr(0d, n)));
            double best = double.NegativeInfinity;
            for (int k = 0; k < keys.Count; k++)
                if (visible.Contains(keys[k]) && _editor.Resolve(keys[k].At) is { } at && at <= playheadSim + 1e-9 && at >= best) { best = at; index = k; }
        }
        // ⭐ CR-7 (§11q K3) — during a replay, a key that is not the replay's own is NOT changed (the main pass would change too): the replay gets its own key
        //   with the view, where that key took the screen (not before the replay's start), or at the playhead when none did
        if (replay is { } seg && (index is null || keys[index.Value].Segment is null || keys[index.Value].Segment != replayId))
        {
            double from = _editor.Take.Segments[seg].From is var f && _editor.Resolve(f) is { } fs ? fs : playheadSim;
            double sim = index is { } k0 && _editor.Resolve(keys[k0].At) is { } ka ? Math.Max(ka, from) : Math.Max(playheadSim, from);
            var at = keys.Count > 0 && index is { } k1 && keys[k1].At.Anchor is { } a && _editor.Take.Baked is { } b && b.Times.TryGetValue(a, out var t)
                ? new AtExpr(Math.Round(sim - t, 3), a) : new AtExpr(sim);
            return _editor.Apply(new TakeEdit.AddKey(new CameraKey { At = at, Camera = camera, Fov = camera.Fov, In = KeyTransition.Cut }, null, seg));
        }
        return index is { } target && _editor.Apply(new TakeEdit.SetKeyCamera(target, camera));
    }

    /// <summary>⭐ CR-7 (§11q K3) — the index into the open take's segments of the REPLAY on screen (the runner plays this take, in a segment that goes back); else null.</summary>
    internal int? ReplayOnScreen()
    {
        if (_segmentNow?.Invoke() is not { Back: true } seg) return null;
        var segments = _editor.Take.Segments;
        if (seg.Id != null)
        {
            for (int i = 0; i < segments.Count; i++) if (segments[i].Id == seg.Id) return i;
            return null;
        }
        return seg.Number - 1 < segments.Count ? seg.Number - 1 : null;
    }

    /// <summary>⭐ P2b — the selected item's index into the take's segments / subtitles (the Director window edits its speed, gap, text, position, fade).</summary>
    public int? SelectedSegment => _selected is { } s ? SpanTrack.IndexOf(s, "seg:") : null;
    public int? SelectedSubtitle => _selected is { } s ? SpanTrack.IndexOf(s, "sub:") : null;
    /// <summary>⭐ CE-1304 / CE-1305 P6 — the selected view item / graphic (the Director window edits its map and layers / its kind, text, radius, fade).</summary>
    public int? SelectedView => _selected is { } s ? SpanTrack.IndexOf(s, "view:") : null;
    public int? SelectedGraphic => _selected is { } s ? SpanTrack.IndexOf(s, "gfx:") : null;

    /// <summary>
    /// ⭐⭐ <c>CE-1304</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §11t V3) — the View track: one POINT item per <see cref="ViewChange"/> (2-D / 3-D and its layer set
    /// from that source second). The items are <see cref="TakeViews.Of"/> — a take with no views shows the layers it already plays — and the first edit writes
    /// them (one undo step). Empty space offers "2-D map here" / "3-D map here"; an item offers delete and the other map; a drag moves it. Its layers are
    /// edited in the Director window.
    /// </summary>
    private sealed class ViewTrack : ITimelineTrack
    {
        private readonly DirectorTracks _d;
        public ViewTrack(DirectorTracks d) => _d = d;
        public string Id => ViewId;
        public string Label => "View";

        public IReadOnlyList<TimelineTrackItem> Items
        {
            get
            {
                var views = Views(_d._editor.Take);
                var list = new List<TimelineTrackItem>(views.Count);
                for (int i = 0; i < views.Count; i++)
                {
                    if (_d._editor.Resolve(views[i].At) is not { } at) continue;
                    string id = ItemId(i);
                    list.Add(new TimelineTrackItem(id, _d.Shown(id, at), null, TakeViews.Describe(views[i]), TimelineTrackItemKind.Key, id == _d._selected));
                }
                return list;
            }
        }

        public void Select(string itemId) => _d._selected = itemId;

        public void Move(string itemId, double toSimSeconds, bool commit)
        {
            if (SpanTrack.IndexOf(itemId, "view:") is not { } i) return;
            if (!commit) { _d._editor.SetPreview(itemId, toSimSeconds); return; }
            _d._editor.ClearPreview();
            _d._editor.Apply(new TakeEdit.MoveView(i, toSimSeconds));
        }

        public bool Key(char key, double atSimSeconds) => false;

        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => MenuItems(itemId, 0d);

        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId, double atSimSeconds)
        {
            if (itemId != null)
            {
                var views = Views(_d._editor.Take);
                if (SpanTrack.IndexOf(itemId, "view:") is not { } i || i >= views.Count) yield break;
                var v = views[i];
                yield return new TimelineMenuItem("Delete view change", () => { if (_d._editor.Apply(new TakeEdit.RemoveView(i))) _d._selected = null; });
                bool twoD = v.TwoD == true;
                yield return new TimelineMenuItem(twoD ? "Switch to the 3-D map" : "Switch to the 2-D map",
                    () => _d._editor.Apply(new TakeEdit.SetView(i, v with { TwoD = !twoD })));
                yield break;
            }
            double from = Math.Max(atSimSeconds, 0d);
            foreach (var (label, twoD) in new[] { ("2-D map here", true), ("3-D map here", false) })
                yield return new TimelineMenuItem(label, () =>
                {
                    if (_d._editor.Apply(new TakeEdit.AddView(new ViewChange { At = new AtExpr(from), TwoD = twoD })))
                        _d._selected = ItemId(_d._editor.Take.Views.Count - 1);
                });
        }

        private static IReadOnlyList<ViewChange> Views(Take take) => TakeViews.Of(take, Layout(take));
        private static string ItemId(int i) => "view:" + i.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ⭐ <c>CE-1364</c> / <c>CE-1303</c> P2b (§11o S10) — THE one span row: the Time track (segments) and the Subtitles track are this class with their own
    /// list and edits. A body drag moves the whole span, an edge drag only that edge (<see cref="TakeEditor.MinSegmentSeconds"/>); empty space offers "add
    /// one here", a span "delete" (+ the track's own items); every edit is one <see cref="TakeEditor.Apply"/>.
    /// </summary>
    private sealed class SpanTrack : ITimelineTrack
    {
        private readonly DirectorTracks _d;
        private readonly string _prefix, _thing;
        private readonly Func<Take, IReadOnlyList<(AtExpr From, AtExpr To, string Label)>> _spans;
        private readonly Func<int, SegmentEdge, double, TakeEdit> _move;
        private readonly Func<int, TakeEdit> _remove;
        private readonly Func<double, TakeEdit> _add;
        private readonly Func<Take, TakeEdit, int> _addedIndex;
        private readonly Func<int?, double, IEnumerable<TimelineMenuItem>>? _more;

        public SpanTrack(DirectorTracks d, string id, string label, string prefix, string thing,
                         Func<Take, IReadOnlyList<(AtExpr From, AtExpr To, string Label)>> spans, Func<int, SegmentEdge, double, TakeEdit> move,
                         Func<int, TakeEdit> remove, Func<double, TakeEdit> add, Func<Take, TakeEdit, int> addedIndex,
                         Func<int?, double, IEnumerable<TimelineMenuItem>>? more = null)
        {
            _d = d; Id = id; Label = label; _prefix = prefix; _thing = thing;
            _spans = spans; _move = move; _remove = remove; _add = add; _addedIndex = addedIndex; _more = more;
        }

        public string Id { get; }
        public string Label { get; }

        public IReadOnlyList<TimelineTrackItem> Items
        {
            get
            {
                var spans = _spans(_d._editor.Take);
                var list = new List<TimelineTrackItem>(spans.Count);
                for (int i = 0; i < spans.Count; i++)
                {
                    if (_d._editor.Resolve(spans[i].From) is not { } from || _d._editor.Resolve(spans[i].To) is not { } to) continue;
                    string id = ItemId(i);
                    double start = from, end = to;
                    if (_d._editor.Preview is { } pv && pv.ItemId == id)
                        switch (pv.Edge)
                        {
                            case SegmentEdge.From: start = Math.Min(pv.Sim, to - TakeEditor.MinSegmentSeconds); break;
                            case SegmentEdge.To: end = Math.Max(pv.Sim, from + TakeEditor.MinSegmentSeconds); break;
                            default: start = pv.Sim; end = pv.Sim + (to - from); break;
                        }
                    list.Add(new TimelineTrackItem(id, start, end, spans[i].Label, TimelineTrackItemKind.Segment, id == _d._selected));
                }
                return list;
            }
        }

        public void Select(string itemId) => _d._selected = itemId;

        public void Move(string itemId, double toSimSeconds, bool commit) => Move(itemId, toSimSeconds, commit, TrackItemPart.Body);

        /// <summary>CE-1364: the body moves the whole span, an edge only that edge (it cannot cross the other: <see cref="TakeEditor.MinSegmentSeconds"/>).</summary>
        public void Move(string itemId, double toSimSeconds, bool commit, TrackItemPart part)
        {
            if (IndexOf(itemId, _prefix) is not { } i) return;
            var edge = part switch { TrackItemPart.StartEdge => SegmentEdge.From, TrackItemPart.EndEdge => SegmentEdge.To, _ => SegmentEdge.Whole };
            if (!commit) { _d._editor.SetPreview(itemId, toSimSeconds, edge); return; }
            _d._editor.ClearPreview();
            _d._editor.Apply(_move(i, edge, toSimSeconds));
        }

        // K adds a CAMERA key and J / L are the film's rate: both are the camera track's (OfferKey tries the other tracks when the selected one declines)
        public bool Key(char key, double atSimSeconds) => false;

        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Enumerable.Empty<TimelineMenuItem>();

        /// <summary>CE-1364: a span offers "Delete …" (+ the track's own); empty track space offers "Add a … here" at the clicked time (+ the track's own).</summary>
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId, double atSimSeconds)
        {
            if (itemId != null)
            {
                if (IndexOf(itemId, _prefix) is { } i)
                {
                    yield return new TimelineMenuItem("Delete " + _thing, () =>
                    {
                        if (_d._editor.Apply(_remove(i))) _d._selected = null;
                    });
                    if (_more != null) foreach (var m in _more(i, atSimSeconds)) yield return m;
                }
                yield break;
            }
            double from = Math.Max(atSimSeconds, 0d);
            yield return new TimelineMenuItem($"Add a {_thing} here", () =>
            {
                var edit = _add(from);
                if (_d._editor.Apply(edit)) _d._selected = ItemId(_addedIndex(_d._editor.Take, edit));
            });
            if (_more != null) foreach (var m in _more(null, from)) yield return m;
        }

        internal static int? IndexOf(string id, string prefix) =>
            id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : null;
        private string ItemId(int i) => _prefix + i.ToString(CultureInfo.InvariantCulture);
    }

    // ── the two span tracks ──────────────────────────────────────────────────────────────────

    private SpanTrack MakeTimeTrack() => new(this, TimeId, "Time", "seg:", "segment",
        take =>
        {
            var map = FilmTimeMap.Of(take, n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null);
            return take.Segments.Select((g, i) =>
            {
                bool back = map.Segments.FirstOrDefault(s => s.Number == i + 1)?.Back == true;
                string label = (back ? "replay" : "segment") + (g.Speed == 1d ? "" : $" x{g.Speed:0.##}");
                return (g.From, g.To, label);
            }).ToList();
        },
        (i, edge, sim) => new TakeEdit.MoveSegment(i, edge, sim),
        i => new TakeEdit.RemoveSegment(i),
        sim => new TakeEdit.AddSegment(new TimeSegment { From = new AtExpr(sim), To = new AtExpr(sim + TakeEditor.DefaultSegmentSeconds) }),
        (take, edit) => take.Segments.ToList().IndexOf(((TakeEdit.AddSegment)edit).Segment),   // inserted in source order (S12)
        (i, _) => i is { } index
            ? new[] { new TimelineMenuItem("Replay this segment", () => { if (_editor.Apply(new TakeEdit.ReplaySegment(index))) _selected = "seg:" + (index + 1).ToString(CultureInfo.InvariantCulture); }) }
            : Enumerable.Empty<TimelineMenuItem>());

    private SpanTrack MakeSubtitleTrack() => new(this, SubtitlesId, "Subtitles", "sub:", "subtitle",
        take => TakeSubtitles.Of(take, Layout(take)).Select(u => (u.At, u.Until, u.Text)).ToList(),
        (i, edge, sim) => new TakeEdit.MoveSubtitle(i, edge, sim),
        i => new TakeEdit.RemoveSubtitle(i),
        sim => new TakeEdit.AddSubtitle(new Subtitle { At = new AtExpr(sim), Until = new AtExpr(sim + DefaultSubtitleSeconds), Text = DefaultSubtitleText }),
        (take, _) => take.Subtitles.Count - 1,
        (i, sim) => i is null && Layout(_editor.Take).At(sim) is { Shot.Why: { Length: > 0 } why } shot
            // ⭐ CR-11 (§11o S8): the shot's note is offered, never seeded — it is the director's note until someone puts it on screen
            ? new[] { new TimelineMenuItem("Add the shot's note as a subtitle", () =>
                _editor.Apply(new TakeEdit.AddSubtitle(new Subtitle { At = new AtExpr(shot.Start), Until = new AtExpr(shot.End), Text = why }))) }
            : Enumerable.Empty<TimelineMenuItem>());

    /// <summary>
    /// ⭐ <c>CE-1305</c> P6 (§11t G4) — the Graphics track: the subtitle's span row over <see cref="Take.Graphics"/>. "Add a graphic here" puts a RING on the
    /// unit the pilot camera follows (a LABEL at its look-at when it follows none) for <see cref="DefaultGraphicSeconds"/>; the window edits the rest.
    /// </summary>
    private SpanTrack MakeGraphicsTrack() => new(this, GraphicsId, "Graphics", "gfx:", "graphic",
        take => take.Graphics.Select(g => (g.At, g.Until, FilmGraphics.Describe(g))).ToList(),
        (i, edge, sim) => new TakeEdit.MoveGraphic(i, edge, sim),
        i => new TakeEdit.RemoveGraphic(i),
        sim => new TakeEdit.AddGraphic(NewGraphic(sim)),
        (take, _) => take.Graphics.Count - 1);

    /// <summary>⭐ CE-1305 — the graphic "Add a graphic here" makes at <paramref name="sim"/>: a ring on the pilot's unit, else a label where it looks.</summary>
    public FilmGraphic NewGraphic(double sim)
    {
        var pilot = _pilot();
        var g = new FilmGraphic { At = new AtExpr(sim), Until = new AtExpr(sim + DefaultGraphicSeconds), Fade = 0.3 };
        if (pilot?.Follow is { } unit) return g with { Kind = GraphicKind.Ring, Who = unit };
        return g with { Kind = GraphicKind.Label, Points = new[] { pilot?.LookAt ?? Vector3.Zero }, Text = DefaultGraphicText };
    }

    /// <summary>⭐ CE-1305 — where the pilot camera looks (a look-at camera's point; null when it follows a unit or there is none): a graphic's next point.</summary>
    public Vector3? PilotLookAt => _pilot()?.LookAt;

    public const double DefaultGraphicSeconds = 3d;
    public const string DefaultGraphicText = "Label";

    /// <summary>CE-1303 P2b: a subtitle added from the menu — 3 sim seconds long, with a placeholder the window's text field replaces.</summary>
    public const double DefaultSubtitleSeconds = 3d;
    public const string DefaultSubtitleText = "Subtitle";

    private static TakeTimeline Layout(Take take) => TakeTimeline.Plan(take, n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null);
}
