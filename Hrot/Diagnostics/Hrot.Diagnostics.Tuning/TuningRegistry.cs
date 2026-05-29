using System;
using System.Collections.Generic;

namespace Hrot.Diagnostics.Tuning
{
    // Thread-safe registration; drain must be called on the simulation thread.
    public sealed class TuningRegistry
    {
        private readonly Dictionary<uint, Tunable>      _tunables  = new();
        private readonly Queue<(uint id, float value)>  _applyQueue = new();
        private readonly object                          _queueLock  = new();
        // Warnings emitted when out-of-range commits are clamped.
        private readonly Action<string>?                _warn;

        public TuningRegistry(Action<string>? warn = null) { _warn = warn; }

        // Register a tunable. Overwrites any existing entry with the same key.
        public void Register(TuningKey key, Tunable tunable)
        {
            tunable.Key = key;
            _tunables[key.Id] = tunable;
        }

        // Enqueue a value change. Does NOT apply immediately.
        // Called from any thread (e.g., OnStructUpdate callback from the network layer).
        public bool Apply(TuningKey key, float value)
        {
            if (!_tunables.TryGetValue(key.Id, out _)) return false;
            lock (_queueLock)
                _applyQueue.Enqueue((key.Id, value));
            return true;
        }

        // Drain the apply queue and write all pending changes.
        // Must be called at frame top, before any system reads config.
        public void BeginFrame()
        {
            (uint id, float value)[] pending;
            lock (_queueLock)
            {
                if (_applyQueue.Count == 0) return;
                pending = _applyQueue.ToArray();
                _applyQueue.Clear();
            }
            foreach (var (id, value) in pending)
            {
                if (!_tunables.TryGetValue(id, out var tunable)) continue;
                float clamped = Math.Clamp(value, tunable.Min, tunable.Max);
                if (clamped != value)
                    _warn?.Invoke($"Tuning value for '{tunable.Key.Name}' clamped {value} -> {clamped}");
                tunable.Write(clamped);
            }
        }

        // Returns groups as (prefix, tunables) pairs. Prefix is the dotted namespace up to
        // the third segment, e.g. "utility.CombatPosture".
        public IEnumerable<(string prefix, IReadOnlyList<Tunable> tunables)> GetGroups()
        {
            var groups = new Dictionary<string, List<Tunable>>();
            foreach (var t in _tunables.Values)
            {
                string prefix = GetGroupPrefix(t.Key.Name);
                if (!groups.TryGetValue(prefix, out var list))
                    groups[prefix] = list = new List<Tunable>();
                list.Add(t);
            }
            foreach (var kv in groups)
                yield return (kv.Key, kv.Value);
        }

        public bool TryGet(TuningKey key, out Tunable? tunable)
            => _tunables.TryGetValue(key.Id, out tunable);

        private static string GetGroupPrefix(string name)
        {
            // Return the first two segments of a dotted name, e.g.
            // "utility.CombatPosture.0.0.weight" -> "utility.CombatPosture"
            int first = name.IndexOf('.');
            if (first < 0) return name;
            int second = name.IndexOf('.', first + 1);
            return second < 0 ? name : name[..second];
        }
    }
}
