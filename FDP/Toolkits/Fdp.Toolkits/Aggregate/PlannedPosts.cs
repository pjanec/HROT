using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>⭐ DP3-C — what a planned post is (D-2f's flags: the kind of position the defence planner chose).</summary>
    [System.Flags]
    public enum PlannedPostFlags : byte
    {
        NoFlags = 0, PostWindow = 1, PostRoof = 2, PostFrame = 4, PostAuthored = 8, PostGun = 16,
    }

    /// <summary>One planned post: where the man stands and where he watches (zero = the sector, D-2f's convention), and what it is.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlannedPost
    {
        public Vector3 Point;
        public Vector3 Watch;
        public PlannedPostFlags Flags;
    }

    /// <summary>The 13 posts — <c>UnitSlotTable.CapacityFor(0)</c>, a squad's men.</summary>
    [InlineArray(PlannedPosts.Capacity)]
    public struct PlannedPostArray
    {
#pragma warning disable CS0169
        private PlannedPost _post;
#pragma warning restore CS0169
    }

    /// <summary>
    /// ⭐⭐ <c>DP3-C</c> (R-4286; docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §6, §8 ②) — the posts a stochastic unit's planner
    /// (agg-behaviors' <c>UnitPostPlanningSystem</c>, reusing SquadDefend's own plan) chose for its split, in the order
    /// <see cref="BusResolutionSwitch.Materialise"/> walks the LIVE slots. The resolution manager (DP3-B) waits in <c>Planning</c> for
    /// <see cref="Ready"/> or its deadline, then puts each man AT his post (DP3-D ①); slots beyond <see cref="Count"/> get the fallback posts.
    /// ⭐ Recorded (so a replay sees the plan), ⛔ never saved (process state, like <see cref="AggregateChild"/>), no wire descriptor.
    /// ⚠ SV-12: every field is declared now.
    /// </summary>
    [ComponentId(GlobalComponentIds.PlannedPosts)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PlannedPosts
    {
        public const int Capacity = 13;

        public PlannedPostArray Posts;
        /// <summary>How many posts are planned (≤ <see cref="Capacity"/>).</summary>
        public byte Count;
        /// <summary>1 when the plan is complete — the manager materialises at once.</summary>
        public byte Ready;
        /// <summary>The planner's stage (its own meaning), so a seek mid-plan restarts the asks.</summary>
        public byte Stage;
        private byte _pad;
        /// <summary>When the plan was started.</summary>
        public SimInstant PlannedAt;
    }
}
