using System;

namespace Hrot.Diagnostics.Tuning
{
    public readonly struct TuningKey : IEquatable<TuningKey>
    {
        public readonly uint Id;     // FNV-1a-32 of Name
        public readonly string Name; // dotted name, e.g. "utility.CombatPosture.0.0.weight"

        public TuningKey(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Id   = Fnv1a32(name);
        }

        public bool Equals(TuningKey other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is TuningKey k && Equals(k);
        public override int GetHashCode() => (int)Id;

        private static uint Fnv1a32(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
