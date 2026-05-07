using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Concurrent-safe intern map; TryAdd/TryGetValue are lock-free.
    // Keyed by FNV-1a 32-bit hash. Allows the renderer to resolve full strings
    // from the hash stored in the DebugPrimitive StringHash overlay field.
    public sealed class StringInternMap
    {
        private readonly ConcurrentDictionary<uint, string> _map = new();

        // Registers the full text under the given hash.
        // Idempotent: subsequent calls with the same hash are silently ignored.
        public void Intern(uint hash, string fullText)
        {
            _map.TryAdd(hash, fullText);
        }

        // Returns the full string for the hash, or null if not present.
        // Does not allocate on the lookup path.
        public string? TryResolve(uint hash)
        {
            _map.TryGetValue(hash, out var s);
            return s;
        }

        // All currently interned entries (for network publication or diagnostics).
        public IReadOnlyDictionary<uint, string> Entries => _map;

        // Removes all interned entries.
        public void Flush() => _map.Clear();

        // FNV-1a 32-bit hash. Used by DrawTextLong and GizmoSettingsRegistry.
        public static uint Fnv1a32(string text)
        {
            uint h = 2166136261u;
            foreach (char c in text)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }
    }
}
