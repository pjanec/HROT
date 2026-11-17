using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>One planned post, as <see cref="PlannedPosts.TryGet"/> hands it out.</summary>
    public readonly record struct PlannedPost(Vector3 Point, Vector3 Watch, byte Flags)
    {
        public bool Window => (Flags & PlannedPosts.WindowFlag) != 0;
        public bool Roof => (Flags & PlannedPosts.RoofFlag) != 0;
        public bool Frame => (Flags & PlannedPosts.FrameFlag) != 0;
        public bool Authored => (Flags & PlannedPosts.AuthoredFlag) != 0;
        public bool Gun => (Flags & PlannedPosts.GunFlag) != 0;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8303</c> (D-P3 DP3-C, SV-5, R-4286; 📄 docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §3 / §6) — the REAL posts a stochastic
    /// squad's split will put its men on, planned during the split's <see cref="SplitStage.Planning"/> by agg-behaviors' <c>UnitPostPlanningSystem</c>
    /// (SquadDefend's own plan over the slot table) and read by the resolution manager's <c>Materialise</c> (agg-backend, DP3-B / DP3-D ①).
    /// ⭐ Entry <c>k</c> is the k-th LIVE slot in table order — the order <c>Materialise</c> walks (<see cref="UnitSlotTable.IsUp"/>); an entry without
    /// <see cref="PostedFlag"/> has no post (that man takes his <see cref="UnitSplits.FallbackPosts"/> point). ⭐ Unmanaged, ≤ 1024 B (RV-11), RECORDED
    /// (a split replays on its planned posts), <see cref="DataPolicy.NoScenario"/> (process state, like <see cref="AggregateChild"/>), NO wire
    /// descriptor — the owning AI node alone writes and reads it. ⚠ SV-12: every field is here now; a later one is a versioned break.
    /// </summary>
    [ComponentId(GlobalComponentIds.PlannedPosts)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PlannedPosts
    {
        /// <summary>A squad's slots (<see cref="UnitSlotTable.CapacityFor"/> of echelon 0).</summary>
        public const int Capacity = 13;

        /// <summary><see cref="Flags"/> bits: has a post · a window post (fought the window way) · the roof · a window frame post · authored · the automatic weapon.</summary>
        public const byte PostedFlag = 1, WindowFlag = 2, RoofFlag = 4, FrameFlag = 8, AuthoredFlag = 16, GunFlag = 32;

        /// <summary><see cref="Stage"/>: no plan · the asks are out · the posts are planned (<see cref="Ready"/>).</summary>
        public const byte StageNone = 0, StagePlanning = 1, StageReady = 2;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        /// <summary>The point he faces (zero = the sector — D-2f's convention).</summary>
        public fixed float WatchX[Capacity];
        public fixed float WatchY[Capacity];
        public fixed float WatchZ[Capacity];
        public fixed byte Flags[Capacity];
        /// <summary>Entries in use: the live slots when the plan finished.</summary>
        public byte Count;
        /// <summary>1 = the plan is done (the manager may materialise on it).</summary>
        public byte Ready;
        /// <summary><see cref="StageNone"/> / <see cref="StagePlanning"/> / <see cref="StageReady"/> — a seek back into a plan restarts its asks.</summary>
        public byte Stage;
        private byte _pad0;
        /// <summary>When the plan began (sim time).</summary>
        public SimInstant PlannedAt;

        /// <summary>⭐ DP3-D ① — the planned post of the <paramref name="k"/>-th live slot; <c>false</c> when the plan is not ready, <paramref name="k"/> is out of
        /// range or that man got no post (⇒ his fallback point).</summary>
        public bool TryGet(int k, out PlannedPost post)
        {
            post = default;
            if (Ready == 0 || k < 0 || k >= Count || k >= Capacity || (Flags[k] & PostedFlag) == 0) return false;
            post = new PlannedPost(new Vector3(X[k], Y[k], Z[k]), new Vector3(WatchX[k], WatchY[k], WatchZ[k]), Flags[k]);
            return true;
        }

        /// <summary>Writes entry <paramref name="k"/> (the planner's side).</summary>
        public void Set(int k, Vector3 point, Vector3 watch, byte flags)
        {
            if (k < 0 || k >= Capacity) return;
            X[k] = point.X; Y[k] = point.Y; Z[k] = point.Z;
            WatchX[k] = watch.X; WatchY[k] = watch.Y; WatchZ[k] = watch.Z;
            Flags[k] = flags;
        }
    }
}
