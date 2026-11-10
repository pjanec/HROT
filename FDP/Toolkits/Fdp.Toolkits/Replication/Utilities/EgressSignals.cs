using System;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// ⭐ CE-2407 (docs/DESIGN_Egress_Change_Detection.md §5) — the per-world "republish this" signals that move NO component version:
    /// <see cref="SmartEgressUtil.MarkDirty"/> (H8: it edits a set in place) and <see cref="SmartEgressUtil.ForceMarkAllDirty"/> /
    /// <c>WorldEpoch</c> (H3: a seek, restore or wipe). Each is stamped with the world's <see cref="EntityRepository.GlobalVersion"/>, so an
    /// <see cref="EgressVisitGate"/> visits what was named since its last scan. Written off the hot path (a mark is rare); read per visit
    /// (one array read).
    /// </summary>
    public static class EgressSignals
    {
        internal sealed class State
        {
            public uint[] DirtyAt = new uint[256];
            public uint AllDirtyAt;
        }

        private static readonly ConditionalWeakTable<EntityRepository, State> s_states = new();

        internal static State Of(EntityRepository repo) => s_states.GetValue(repo, static _ => new State());

        /// <summary>The entity at <paramref name="index"/> must be revisited by every gated translator.</summary>
        public static void Dirty(EntityRepository repo, int index)
        {
            if (index < 0) return;
            var s = Of(repo);
            if (index >= s.DirtyAt.Length) Array.Resize(ref s.DirtyAt, Math.Max(index + 1, s.DirtyAt.Length * 2));
            s.DirtyAt[index] = repo.GlobalVersion;
        }

        /// <summary>Every gated translator scans everything on its next run (a seek, restore, wipe or "re-flood").</summary>
        public static void AllDirty(EntityRepository repo) => Of(repo).AllDirtyAt = repo.GlobalVersion;

        /// <summary>
        /// ⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7, O-D) — gives <paramref name="replica"/> the signals of
        /// <paramref name="live"/>: the off-frame egress scans a replica synced from the live world, and a gated translator reads the signals
        /// OF THE WORLD IT SCANS. The versions are the live world's, which the replica's <c>GlobalVersion</c> copies. No allocation once
        /// the replica's array has grown.
        /// </summary>
        public static void CopyTo(EntityRepository live, EntityRepository replica)
        {
            var from = Of(live);
            var to = Of(replica);
            if (to.DirtyAt.Length < from.DirtyAt.Length) Array.Resize(ref to.DirtyAt, from.DirtyAt.Length);
            Array.Copy(from.DirtyAt, to.DirtyAt, from.DirtyAt.Length);
            if (to.DirtyAt.Length > from.DirtyAt.Length) Array.Clear(to.DirtyAt, from.DirtyAt.Length, to.DirtyAt.Length - from.DirtyAt.Length);
            to.AllDirtyAt = from.AllDirtyAt;
        }

        internal static uint DirtyAt(State s, int index) => (uint)index < (uint)s.DirtyAt.Length ? s.DirtyAt[index] : 0u;
    }
}
