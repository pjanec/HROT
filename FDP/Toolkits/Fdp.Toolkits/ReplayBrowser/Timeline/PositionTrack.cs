using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-4059</c> (docs/DESIGN_Director_Timeline_Editor.md §11 "P3 position track detail") — one sample of a networked unit's place: the
    /// index frame it was taken at, its <see cref="SimTransform"/> position (east, north, up — metres) and its heading (yaw about up, radians,
    /// 0 = east, +π/2 = north). The unit is the key it is stored under, not a field (20 bytes a sample).
    /// </summary>
    public readonly record struct PositionSample(int Frame, float X, float Y, float Z, float Heading);

    /// <summary>
    /// ⭐ <c>CE-4059</c> — an IMMUTABLE snapshot of the position track: per networked unit (its NETWORK id, as every other id in the index),
    /// its samples in frame order. Built from <see cref="SharedList{T}"/> views of the writer's grow-only buffers (CE-1361), so a snapshot never
    /// changes under a reader and publishing one copies no samples.
    /// <para>A unit that left simply has no more samples: <see cref="At"/> answers only INSIDE a unit's sampled span (never an extrapolation).</para>
    /// </summary>
    public sealed class PositionTrack
    {
        private readonly Dictionary<long, SharedList<PositionSample>> _units;

        public PositionTrack(IReadOnlyDictionary<long, SharedList<PositionSample>> units)
        {
            _units = new Dictionary<long, SharedList<PositionSample>>(units);
        }

        public PositionTrack(IReadOnlyDictionary<long, PositionSample[]> units)
        {
            _units = new Dictionary<long, SharedList<PositionSample>>(units.Count);
            foreach (var (id, samples) in units) _units[id] = new SharedList<PositionSample>(samples);
        }

        public static readonly PositionTrack Empty = new(new Dictionary<long, SharedList<PositionSample>>());

        /// <summary>The units that have at least one sample.</summary>
        public IReadOnlyCollection<long> UnitIds => _units.Keys;
        public int UnitCount => _units.Count;

        /// <summary>All samples of all units.</summary>
        public int SampleCount { get { int n = 0; foreach (var l in _units.Values) n += l.Count; return n; } }

        public bool Has(long unit) => _units.ContainsKey(unit);

        /// <summary>The unit's samples, in frame order (empty for a unit the track does not hold).</summary>
        public SharedList<PositionSample> Samples(long unit) => _units.TryGetValue(unit, out var l) ? l : SharedList<PositionSample>.Empty;

        /// <summary>The unit's samples in frames [<paramref name="from"/>, <paramref name="to"/>) — a slice of the shared array, no copy.</summary>
        public ReadOnlySpan<PositionSample> Between(long unit, int from, int to)
        {
            var a = Samples(unit).Span;
            int lo = LowerBound(a, from), hi = LowerBound(a, to);
            return a.Slice(lo, Math.Max(0, hi - lo));
        }

        /// <summary>
        /// The unit's place at <paramref name="frame"/>: the sample itself on a sampled frame, else LINEAR between the two samples around it
        /// (heading along the shorter arc), with <see cref="PositionSample.Frame"/> = <paramref name="frame"/>. <c>null</c> before the unit's first
        /// or after its last sample (it was not there, or has left — never an extrapolation).
        /// </summary>
        public PositionSample? At(long unit, int frame)
        {
            var a = Samples(unit).Span;
            int i = LowerBound(a, frame);
            if (i < a.Length && a[i].Frame == frame) return a[i];
            if (i == 0 || i == a.Length) return null;
            PositionSample p = a[i - 1], q = a[i];
            float t = (frame - p.Frame) / (float)(q.Frame - p.Frame);
            float dh = q.Heading - p.Heading;
            while (dh > MathF.PI) dh -= 2 * MathF.PI;
            while (dh < -MathF.PI) dh += 2 * MathF.PI;
            return new PositionSample(frame, p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t, p.Heading + dh * t);
        }

        /// <summary>
        /// Appends to <paramref name="results"/> (ascending id) the units whose place at <paramref name="frame"/> is within
        /// <paramref name="radius"/> metres of (<paramref name="x"/>, <paramref name="y"/>) on the ground plane.
        /// </summary>
        public void UnitsWithin(int frame, float x, float y, float radius, List<long> results)
        {
            int start = results.Count;
            float r2 = radius * radius;
            foreach (var id in _units.Keys)
            {
                if (At(id, frame) is not { } p) continue;
                float dx = p.X - x, dy = p.Y - y;
                if (dx * dx + dy * dy <= r2) results.Add(id);
            }
            results.Sort(start, results.Count - start, null);
        }

        private static int LowerBound(ReadOnlySpan<PositionSample> a, int frame)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid].Frame < frame) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> — the writer's side of a <see cref="PositionTrack"/>, owned by <see cref="TimelineFrameScanner"/> (so the recording's
    /// builder and the live source sample through the SAME step — R-4089; ⛔ never a second scanner). Every <see cref="IntervalSeconds"/> of SIM
    /// time (bucket = floor(sim / interval); a frame that starts a new bucket is a sample round) each networked unit that has a
    /// <see cref="SimTransform"/> appends one sample to its own grow-only buffer. A frame with no sim time (a v6 recording, a bare world) rounds
    /// every <see cref="FallbackFrameStride"/> frames instead.
    /// </summary>
    internal sealed class PositionTrackWriter
    {
        /// <summary>Two samples a second: a unit at 15 m/s moves 7.5 m between samples — enough to interpolate a framing, 20 bytes a unit-sample.</summary>
        public const double DefaultIntervalSeconds = 0.5;
        /// <summary>The frame stride when no sim time is known (≈ 2 Hz at 30 frames a second).</summary>
        public const int FallbackFrameStride = 15;

        private readonly double _interval;
        private readonly Dictionary<long, GrowOnlyBuffer<PositionSample>> _units = new();
        private long _lastBucket = long.MinValue;
        private int _lastFrame = -1;
        private int _version;
        private int _cachedVersion = -1;
        private PositionTrack _cached = PositionTrack.Empty;

        public PositionTrackWriter(double intervalSeconds = DefaultIntervalSeconds)
        {
            if (!(intervalSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            _interval = intervalSeconds;
        }

        /// <summary>Samples every networked unit when <paramref name="frame"/> starts a new interval (NaN <paramref name="sim"/> ⇒ the frame stride).</summary>
        public void Step(EntityRepository repo, int frame, double sim)
        {
            if (frame <= _lastFrame) return;   // a re-step of a frame already sampled
            long bucket = double.IsNaN(sim) ? frame / FallbackFrameStride : (long)Math.Floor(sim / _interval);
            if (bucket == _lastBucket) return;
            _lastBucket = bucket;
            _lastFrame = frame;
            if (!repo.IsComponentTypeRegistered<NetworkIdentity>() || !repo.IsComponentTypeRegistered<SimTransform>()) return;
            foreach (var e in repo.Query().With<NetworkIdentity>().With<SimTransform>().Build())
            {
                long id = repo.GetComponentRO<NetworkIdentity>(e).Value;
                if (id <= 0) continue;   // a unit with no network id is not a cross-node identity (the index's negative ids are entity indices)
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(e);
                var q = t.Rotation;
                float yaw = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
                if (!_units.TryGetValue(id, out var buffer)) _units[id] = buffer = new GrowOnlyBuffer<PositionSample>();
                buffer.Add(new PositionSample(frame, t.Position.X, t.Position.Y, t.Position.Z, yaw));
            }
            _version++;
        }

        /// <summary>An immutable view; the previous one again when nothing was sampled since (a publish between sample rounds costs nothing).</summary>
        public PositionTrack Snapshot()
        {
            if (_cachedVersion == _version) return _cached;
            var units = new Dictionary<long, SharedList<PositionSample>>(_units.Count);
            foreach (var (id, buffer) in _units) units[id] = buffer.Snapshot();
            _cached = new PositionTrack(units);
            _cachedVersion = _version;
            return _cached;
        }
    }
}
