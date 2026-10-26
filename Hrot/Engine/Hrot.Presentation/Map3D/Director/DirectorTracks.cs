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
/// </summary>
public static class PilotCamera
{
    private const float Deg = 180f / MathF.PI;

    public static CameraSpec Of(CameraPose pose, string? unit = null, Vector3? unitAt = null, float lift = 1.5f)
    {
        if (unit is null || unitAt is not { } at)
            return new CameraSpec { LookAt = pose.LookAt, Distance = pose.Distance, Yaw = pose.Yaw * Deg, Pitch = pose.Pitch * Deg };
        var forward = new Vector3(MathF.Cos(pose.Pitch) * MathF.Cos(pose.Yaw), MathF.Cos(pose.Pitch) * MathF.Sin(pose.Yaw), MathF.Sin(pose.Pitch));
        var eye = pose.LookAt - forward * pose.Distance;
        var toUnit = at + new Vector3(0f, 0f, lift) - eye;
        float distance = toUnit.Length();
        if (!(distance > 1e-3f)) return new CameraSpec { Follow = unit, Distance = pose.Distance, Yaw = pose.Yaw * Deg, Pitch = pose.Pitch * Deg, Lift = lift };
        return new CameraSpec
        {
            Follow = unit, Lift = lift, Distance = distance,
            Yaw = MathF.Atan2(toUnit.Y, toUnit.X) * Deg, Pitch = MathF.Asin(Math.Clamp(toUnit.Z / distance, -1f, 1f)) * Deg,
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
    public const string CameraId = "director.camera", TimeId = "director.time";

    private readonly TakeEditor _editor;
    private readonly Func<CameraSpec?> _pilot;
    private readonly Action<int>? _stepRate;
    private string? _selected;

    /// <param name="pilot">The pilot camera as a key's camera (<see cref="PilotCamera.Of"/>), null when there is none.</param>
    /// <param name="stepRate">J (−1) / L (+1): the playback rate seam; null = this host has none (J / L are then not taken).</param>
    public DirectorTracks(TakeEditor editor, Func<CameraSpec?> pilot, Action<int>? stepRate = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _pilot = pilot ?? throw new ArgumentNullException(nameof(pilot));
        _stepRate = stepRate;
        Camera = new CameraTrack(this);
        Time = new TimeTrack(this);
        Tracks = new ITimelineTrack[] { Camera, Time };
    }

    public ITimelineTrack Camera { get; }
    public ITimelineTrack Time { get; }
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
                    list.Add(new TimelineTrackItem(id, _d.Shown(id, at), null, Describe(keys[i]), TimelineTrackItemKind.Key, id == _d._selected));
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
                    int count = CameraKeys.Of(_d._editor.Take).Count;
                    if (!_d._editor.Apply(new TakeEdit.AddKey(new CameraKey { At = new AtExpr(Math.Max(atSimSeconds, 0d)), Camera = camera, Fov = camera.Fov, In = KeyTransition.Cut })))
                        return false;
                    _d._selected = KeyId(count);
                    return true;
                case 'J': if (_d._stepRate == null) return false; _d._stepRate(-1); return true;
                case 'L': if (_d._stepRate == null) return false; _d._stepRate(+1); return true;
                default: return false;
            }
        }

        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId)
        {
            if (itemId != null && IndexOf(itemId, "key:") is { } i)
                yield return new TimelineMenuItem("Delete key", () =>
                {
                    if (_d._editor.Apply(new TakeEdit.RemoveKey(i))) _d._selected = null;
                });
        }

        private static int? IndexOf(string id, string prefix) =>
            id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : null;
        private static string KeyId(int i) => "key:" + i.ToString(CultureInfo.InvariantCulture);

        private static string Describe(CameraKey k)
        {
            var c = k.Camera;
            string what = c.Follow != null ? "follow " + c.Follow
                : c.Over != null ? "over " + c.Over
                : c.LookAt != null ? "look-at"
                : c.Eye != null || c.Target != null ? "eye-target" : "camera";
            return k.In.Kind switch
            {
                KeyTransitionKind.Fly => $"{what} (fly {k.In.Seconds:0.#} s)",
                KeyTransitionKind.Dip => $"{what} (dip)",
                _ => what,
            };
        }
    }

    private sealed class TimeTrack : ITimelineTrack
    {
        private readonly DirectorTracks _d;
        public TimeTrack(DirectorTracks d) => _d = d;
        public string Id => TimeId;
        public string Label => "Time";

        public IReadOnlyList<TimelineTrackItem> Items
        {
            get
            {
                var segments = _d._editor.Take.Segments;
                var list = new List<TimelineTrackItem>(segments.Count);
                for (int i = 0; i < segments.Count; i++)
                {
                    if (_d._editor.Resolve(segments[i].From) is not { } from || _d._editor.Resolve(segments[i].To) is not { } to) continue;
                    string id = SegId(i);
                    double start = _d.Shown(id, from);
                    list.Add(new TimelineTrackItem(id, start, start + (to - from),
                        segments[i].Speed == 1d ? "segment" : $"segment x{segments[i].Speed:0.##}", TimelineTrackItemKind.Segment, id == _d._selected));
                }
                return list;
            }
        }

        public void Select(string itemId) => _d._selected = itemId;

        public void Move(string itemId, double toSimSeconds, bool commit)
        {
            if (!itemId.StartsWith("seg:", StringComparison.Ordinal)
                || !int.TryParse(itemId.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var i)) return;
            if (!commit) { _d._editor.SetPreview(itemId, toSimSeconds); return; }
            _d._editor.ClearPreview();
            _d._editor.Apply(new TakeEdit.MoveSegment(i, SegmentEdge.Whole, toSimSeconds));
        }

        // K adds a CAMERA key and J / L are the film's rate: both are the camera track's (OfferKey tries the other tracks when the selected one declines)
        public bool Key(char key, double atSimSeconds) => false;

        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Enumerable.Empty<TimelineMenuItem>();
        private static string SegId(int i) => "seg:" + i.ToString(CultureInfo.InvariantCulture);
    }
}
