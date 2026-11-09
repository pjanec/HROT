using System;
using System.Collections.Generic;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// ⭐ Review §6.13 (RW-1, RW-4) — ONE watched item of a unit: a vocabulary attribute (<c>Health.Current</c>, a wildcard with its <see cref="SubIndex"/>) read through <c>AttributeReaders</c>, or — <see cref="Expert"/> —
    /// a component field by path (<c>Component.field.path</c>, read through <c>ComponentJsonPath</c> over the component's JSON). <see cref="Tolerance"/> is in the attribute's unit; 0 = exact.
    /// </summary>
    public sealed record ReplicaWatchItem(string Path, int SubIndex, bool Expert, double Tolerance, uint Colour, string Label)
    {
        /// <summary>The lane's colours (ABGR), one per watched item in the order added.</summary>
        public static readonly uint[] Palette = { 0xFF33B3FF, 0xFFE0B04A, 0xFF7FC97F, 0xFFC490E0, 0xFF5A8CFF, 0xFFB4B43C };

        /// <summary>The default tolerance of an attribute (RW-4): discrete (integer / bool / text) exact; a length 0.5 m, an angle 1°, any other continuous value the diff's ε.</summary>
        public static double DefaultToleranceOf(string path)
        {
            if (!AttributeReaders.TryResolve(path, out var id, out _) || AttributeReaders.DefinitionOf(id) is not { } d) return ReplicaViews.Epsilon;   // expert path: a continuous guess
            return d.Kind switch
            {
                AttributeValueKind.CsInt32 or AttributeValueKind.CsInt64 or AttributeValueKind.Bool or AttributeValueKind.CsString => 0d,
                _ => d.Unit == "m" ? 0.5d : d.Unit == "°" ? 1d : ReplicaViews.Epsilon,
            };
        }
    }

    /// <summary>⭐ One persistent disagreement (RW-2): <see cref="Item"/> (index into the items scanned) was believed differently by <see cref="Nodes"/> than by the owner for longer than the threshold.</summary>
    public sealed record ReplicaSpan(int Item, int OwnerNode, string OwnerValue, IReadOnlyList<(int Node, string Value)> Believers, int StartFrame, int EndFrame, double StartSeconds, double EndSeconds)
    {
        public double Duration => EndSeconds - StartSeconds;
    }

    /// <summary>
    /// ⭐ Review §6.13 (RW-1, RW-3, RW-5) — the session's watch: per selected unit (network id) the watched items, plus ONE persistence threshold per lane (default 200 ms ≈ 2 × the time-sync tolerance).
    /// Session state, never saved to the scenario. <see cref="Version"/> moves on every change (the lane restarts its scan, a reader rebuilds).
    /// </summary>
    public sealed class ReplicaWatchList
    {
        public const double DefaultThresholdSeconds = 0.2;

        private readonly Dictionary<long, List<ReplicaWatchItem>> _byUnit = new();
        private double _threshold = DefaultThresholdSeconds;
        private int _version;

        public int Version => _version;
        public double ThresholdSeconds { get => _threshold; set { value = Math.Max(0, value); if (value != _threshold) { _threshold = value; _version++; } } }

        private static readonly IReadOnlyList<ReplicaWatchItem> None = Array.Empty<ReplicaWatchItem>();
        public IReadOnlyList<ReplicaWatchItem> ItemsOf(long unit) => _byUnit.TryGetValue(unit, out var l) ? l : None;

        public bool IsWatched(long unit, string path, int subIndex = 0) => IndexOf(unit, path, subIndex) >= 0;

        private int IndexOf(long unit, string path, int sub)
        {
            if (!_byUnit.TryGetValue(unit, out var l)) return -1;
            for (int i = 0; i < l.Count; i++) if (l[i].SubIndex == sub && string.Equals(l[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>Watches <paramref name="path"/> on <paramref name="unit"/> (no-op when already watched). Returns the item.</summary>
        public ReplicaWatchItem Add(long unit, string path, int subIndex = 0, bool expert = false, string? label = null)
        {
            int at = IndexOf(unit, path, subIndex);
            if (at >= 0) return _byUnit[unit][at];
            if (!_byUnit.TryGetValue(unit, out var l)) _byUnit[unit] = l = new List<ReplicaWatchItem>();
            var item = new ReplicaWatchItem(path, subIndex, expert, ReplicaWatchItem.DefaultToleranceOf(path),
                ReplicaWatchItem.Palette[l.Count % ReplicaWatchItem.Palette.Length], label ?? (subIndex != 0 ? path + "." + subIndex : path));
            l.Add(item); _version++;
            return item;
        }

        public void Remove(long unit, string path, int subIndex = 0)
        {
            int at = IndexOf(unit, path, subIndex);
            if (at < 0) return;
            _byUnit[unit].RemoveAt(at);
            if (_byUnit[unit].Count == 0) _byUnit.Remove(unit);
            _version++;
        }

        /// <summary>The eye: watched ⇒ unwatched and back. Returns whether it is watched now.</summary>
        public bool Toggle(long unit, string path, int subIndex = 0, bool expert = false)
        {
            if (IsWatched(unit, path, subIndex)) { Remove(unit, path, subIndex); return false; }
            Add(unit, path, subIndex, expert); return true;
        }

        public void SetTolerance(long unit, string path, int subIndex, double tolerance)
        {
            int at = IndexOf(unit, path, subIndex);
            if (at < 0) return;
            var l = _byUnit[unit];
            tolerance = Math.Max(0, tolerance);
            if (l[at].Tolerance == tolerance) return;
            l[at] = l[at] with { Tolerance = tolerance }; _version++;
        }
    }
}
