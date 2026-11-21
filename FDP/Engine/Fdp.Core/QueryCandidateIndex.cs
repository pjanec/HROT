using System;
using System.Numerics;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>CE-2418</c> — for every component type, the entities whose MASK carries it, in index order — built in ONE pass over the masks so a
    /// burst of queries (the egress pass: ~90 translators) walks only the entities of each query's rarest included type instead of every
    /// entity once per query. 📄 docs/DESIGN_Egress_Change_Detection.md §6.
    /// <para>⭐ Derived, never counted: it is rebuilt from the same masks the full walk reads, and a new frame, a structural change
    /// (<see cref="EntityRepository.StructuralChanges"/>) or a wholesale mask rewrite (<see cref="EntityIndex.MaskEpoch"/>) voids it.
    /// ⚠ A bit set by hand through a mask REF (ghost promotion, spawning — outside the Export phase) is caught by the next frame's build — so its candidates are always a superset of what the walk would match
    /// (the enumerator still applies every filter). Live only inside <see cref="EntityRepository.QueryIndexScope"/>.</para>
    /// </summary>
    internal sealed class QueryCandidateIndex
    {
        private const int Types = 512;
        private readonly int[] _start = new int[Types + 1];
        private readonly int[] _fill = new int[Types];
        private int[] _entities = Array.Empty<int>();
        private long _builtAtStructure = -1;
        private int _builtMax = -1;
        private uint _builtAtVersion;
        private int _builtAtEpoch = -1;

        /// <summary>Forget the build: the next scope builds afresh.</summary>
        public void Void() { _builtAtStructure = -1; _builtMax = -1; }

        /// <summary>Fresh while it is the same FRAME (<see cref="EntityRepository.GlobalVersion"/>), no structural change happened
        /// (<see cref="EntityRepository.StructuralChanges"/>), no mask was rewritten wholesale (<see cref="EntityIndex.MaskEpoch"/>) and no
        /// entity was issued past the build. ⇒ one build per frame serves every egress system of the Export phase.</summary>
        public bool IsFresh(EntityRepository repo)
            => _builtAtStructure == repo.StructuralChanges && _builtMax == repo.MaxEntityIndex
               && _builtAtVersion == repo.GlobalVersion && _builtAtEpoch == repo.GetEntityIndex().MaskEpoch;

        public void Build(EntityRepository repo)
        {
            var index = repo.GetEntityIndex();
            int max = index.MaxIssuedIndex;
            Array.Clear(_fill);
            int total = 0;
            for (int i = 0; i <= max; i++)   // ① count per type
            {
                ref readonly var m = ref index.GetComponentMaskUnsafe(i);
                for (int w = 0; w < 8; w++)
                {
                    ulong bits = m.Word(w);
                    while (bits != 0) { _fill[(w << 6) + BitOperations.TrailingZeroCount(bits)]++; bits &= bits - 1; total++; }
                }
            }
            int at = 0;
            for (int t = 0; t < Types; t++) { _start[t] = at; at += _fill[t]; _fill[t] = 0; }
            _start[Types] = at;
            if (_entities.Length < total) _entities = new int[Math.Max(total, _entities.Length * 2)];
            for (int i = 0; i <= max; i++)   // ② place, in index order
            {
                ref readonly var m = ref index.GetComponentMaskUnsafe(i);
                for (int w = 0; w < 8; w++)
                {
                    ulong bits = m.Word(w);
                    while (bits != 0)
                    {
                        int t = (w << 6) + BitOperations.TrailingZeroCount(bits);
                        _entities[_start[t] + _fill[t]++] = i;
                        bits &= bits - 1;
                    }
                }
            }
            _builtAtStructure = repo.StructuralChanges;
            _builtMax = max;
            _builtAtVersion = repo.GlobalVersion;
            _builtAtEpoch = index.MaskEpoch;
        }

        /// <summary>The candidates of the RAREST type set in <paramref name="include"/>: false when the mask includes nothing.</summary>
        public bool Rarest(in BitMask512 include, out int[] entities, out int from, out int count)
        {
            entities = _entities; from = 0; count = int.MaxValue;
            bool any = false;
            for (int w = 0; w < 8; w++)
            {
                ulong bits = include.Word(w);
                while (bits != 0)
                {
                    int t = (w << 6) + BitOperations.TrailingZeroCount(bits);
                    int n = _start[t + 1] - _start[t];
                    if (n < count) { count = n; from = _start[t]; }
                    any = true;
                    bits &= bits - 1;
                }
            }
            if (!any) count = 0;
            return any;
        }
    }
}
