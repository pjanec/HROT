using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;

using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2275</c> D-1 — the commander's defend order (resolved from <see cref="SquadDefendParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadDefendParams
    {
        public Vector3 Position;
        public float SearchRadius;
        public float Speed;
        /// <summary>Runtime: the posts and each man's count of rounds at him (the play's own memory).</summary>
        public DefencePosts Posts;
    }

    /// <summary>
    /// ⭐ <c>CE-2275</c> D-1 (DA-34, DA-38) — the squad defence's memory: the candidate points the terrain gave (windows first, then cover),
    /// the post each man holds (an index into them, <see cref="None"/> = no post), and per man the hits / near misses counted in the current
    /// 10 s window. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DefencePosts
    {
        public const int Capacity = 16;
        public const byte None = 255;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        public byte Count;
        /// <summary>⭐ D-2a — the first this many candidates came from the window ask (windows), the rest are cover.</summary>
        public byte WindowCount;
        /// <summary>Per roster slot: the candidate he holds, or <see cref="None"/>.</summary>
        public fixed byte PostOf[Capacity];
        /// <summary>Per roster slot: rounds at him counted since <see cref="WindowStart"/>.</summary>
        public fixed byte Hits[Capacity];
        public fixed double WindowStart[Capacity];
        /// <summary>Per roster slot: the newest hit / near-miss time already counted.</summary>
        public fixed double LastSense[Capacity];
        /// <summary>0 = not asked · 1 = asked · 2 = done (answered, empty, or timed out).</summary>
        public byte WindowStage, CoverStage;
        public double StageSince;
        /// <summary>1 = the posts are given (orders carry them).</summary>
        public byte Ready;
        /// <summary>1 = PROGRESS Set went up (every man at his post).</summary>
        public byte ReportedSet;
        /// <summary>1 = this play lifted its squad's own hold-fire (DA-35) — once.</summary>
        public byte HoldLifted;

        public Vector3 Point(int k) => new(X[k], Y[k], Z[k]);

        public void Add(Vector3 p)
        {
            if (Count >= Capacity) return;
            X[Count] = p.X; Y[Count] = p.Y; Z[Count] = p.Z;
            Count++;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2275</c> D-1 (DA-34, DA-38) — pure: one post per man from the terrain's ranked points, and a man's alternate. Spacing is 3-D, so
    /// the same window on two storeys is two posts (⚠ <see cref="SquadCover.Pick"/> spaces in 2-D, which would merge them).
    /// </summary>
    public static class DefencePostPicker
    {
        /// <summary>Two posts at least this far apart (m) — 3-D.</summary>
        public const float SpacingMetres = 2.5f;

        /// <summary>
        /// Each candidate in rank order goes to the nearest live man still without a post, unless it is within <see cref="SpacingMetres"/> of
        /// a post already held. A man who holds one (<paramref name="postOf"/> ≠ -1) keeps it.
        /// </summary>
        public static void Pick(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<Vector3> men, ReadOnlySpan<bool> live, Span<int> postOf)
        {
            for (int k = 0; k < candidates.Length; k++)
            {
                if (Taken(candidates, postOf, k) || Crowded(candidates, postOf, candidates[k], skipMan: -1)) continue;
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < men.Length; i++)
                {
                    if (!live[i] || postOf[i] >= 0) continue;
                    float d = Vector3.Distance(men[i], candidates[k]);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
                if (best < 0) return;
                postOf[best] = k;
            }
        }

        /// <summary>The candidate nearest <paramref name="man"/>'s post that no one holds and that keeps the spacing from every OTHER post;
        /// -1 when there is none.</summary>
        public static int Alternate(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<int> postOf, int man)
        {
            if (postOf[man] < 0) return -1;
            var from = candidates[postOf[man]];
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < candidates.Length; k++)
            {
                if (k == postOf[man] || Taken(candidates, postOf, k) || Crowded(candidates, postOf, candidates[k], skipMan: man)) continue;
                float d = Vector3.Distance(from, candidates[k]);
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            return best;
        }

        private static bool Taken(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<int> postOf, int k)
        {
            foreach (int p in postOf) if (p == k) return true;
            return false;
        }

        private static bool Crowded(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<int> postOf, Vector3 at, int skipMan)
        {
            for (int i = 0; i < postOf.Length; i++)
                if (i != skipMan && postOf[i] >= 0 && Vector3.Distance(candidates[postOf[i]], at) < SpacingMetres) return true;
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2275</c> D-1 — the squad DEFENDS a position: it moves there, asks the terrain for posts (the windows of every storey that see
    /// the squad's engagement area, then cover for the men left over), gives each man his post with an arc toward the area, reports PROGRESS
    /// Set once they are all there, and moves a man under fire (3 rounds at him within 10 s) to his alternate post. Hold fire is the squad's
    /// own sector (DA-35): a man chooses only inside it. Runs until the next order. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.
    /// </summary>
    public static class SquadDefendBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SquadDefendBlackboard
        {
            public SquadDefendParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadDefend)]
        public static void Resolve(in SquadDefendParamsJsonDto authored, ref SquadDefendBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = Defaults(authored.Position, authored.SearchRadius, authored.Speed);
            block.State = default;
        }

        internal static SquadDefendParams Defaults(Vector3 position, float radius, float speed) => new()
        {
            Position     = position,
            SearchRadius = radius > 0f ? radius : 30f,
            Speed        = speed > 0f ? speed : 3f,
        };
[Behavior(BehaviorNames.SquadDefend, Params = typeof(global::Hrot.Map.Definitions.Behavior.SquadDefendParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.SquadDefend, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadDefendBlackboard))]
        public static BTreeBuilder<SquadDefendBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadDefendBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadDefendParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadDefendPlay, SquadDefendParams>(ref p, ref ws, self, world);

        /// <summary>Leaving the node: the post query goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.SquadDefendBehavior.Run")]
        public static void Deactivate_Run(ref SquadDefendParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);
    }

    /// <summary>⭐ <c>CE-2275</c> D-1 — the defence as a play over <see cref="SquadDefendManeuver"/>: Moving, then Posting (§8c).</summary>
    public struct SquadDefendPlay : ISquadPlay<SquadDefendParams>
    {
        /// <summary>The commander's post-query sites (the window ask, then the cover ask).</summary>
        public const int WindowSite = 0x22750001;
        public const int CoverSite  = 0x22750002;

        /// <summary>Everyone within this of the position ⇒ the posts are asked for (m).</summary>
        public const float ArrivedMetres = 6f;
        /// <summary>The move ends after this long even if a man never arrives (s).</summary>
        public const float MoveTimeoutSeconds = 90f;
        /// <summary>A post query unanswered this long is given up (s): the men left without a post hold where they are.</summary>
        public const float AskTimeoutSeconds = 5f;
        /// <summary>A man this close to his post is manned (m).</summary>
        public const float MannedMetres = 1.5f;
        /// <summary>DA-38 — this many rounds at a man within <see cref="UntenableSeconds"/> ⇒ his alternate post.</summary>
        public const int UntenableHits = 3;
        public const float UntenableSeconds = 10f;
        /// <summary>DA-37 — a man's arc half-width toward the engagement area / outward all-round (°).</summary>
        public const float ArcHalfWidth = 45f;
        public const float AllRoundHalfWidth = 60f;
        /// <summary>⭐ D-2a (PC-14, DA-40) — a hide point this close to his post is one of his post's points (m): the windows of his room.</summary>
        public const float PostRadius = 4f;

        private static readonly SquadHsmShell s_shell = new(SquadDefendManeuver.BuildTransitionTable(), SquadDefendManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadDefendManeuver.ManeuverKind;
        public ushort StartPhase => SquadDefendManeuver.PhaseMoving;
        public ushort AbortPhase => SquadDefendManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            unsafe { for (int i = 0; i < DefencePosts.Capacity; i++) p.Posts.PostOf[i] = DefencePosts.None; }
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            LiftOwnHold(ref p, ref ws, in ctx);
            if (ctx.Squad.PhaseId != SquadDefendManeuver.PhasePosting) return;
            if (p.Posts.Ready == 0) { Plan(ref p, ref ws, in ctx); return; }
            Manned(ref p, in ctx);
            Displace(ref p, ref ws, in ctx);
        }

        public NodeStatus Outcome(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => SquadManeuverRunner.AnyLive(in ctx) ? NodeStatus.Running : NodeStatus.Failure;

        public bool PhaseComplete(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (ctx.Squad.PhaseId != SquadDefendManeuver.PhaseMoving || ws.Reissue != 0) return false;
            if (ctx.Now - ws.IssuedAt >= MoveTimeoutSeconds) { ws.CompletedByTimeout = 1; return true; }
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (ctx.Live(i, out var m) && Flat(ctx.At(m) - p.Position) > ArrivedMetres) return false;
            return true;
        }

        public void BeginIssue(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public unsafe MemberOrder OrderFor(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            if (ctx.Squad.PhaseId == SquadDefendManeuver.PhaseMoving)
                return new MemberOrder
                {
                    Kind = MemberOrderKind.AdvanceTo, Point = p.Position, Speed = p.Speed, ArrivalRadius = ArrivedMetres * 0.5f,
                    RoleId = SquadDefendManeuver.RoleMoving,
                };
            bool hasEa = EngagementAreas.TryAllowPoint(ctx.World, ctx.Commander, out var ea);
            byte post = member < DefencePosts.Capacity ? p.Posts.PostOf[member] : DefencePosts.None;
            if (p.Posts.Ready == 0 || post == DefencePosts.None || post >= p.Posts.Count)
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadDefendManeuver.RolePosted,
                    Sector = hasEa && p.Posts.Ready != 0 ? MemberArc.Around(LookAt.BearingTo(at, ea), ArcHalfWidth) : default,
                };
            var spot = p.Posts.Point(post);
            int alternate = AlternateOf(ref p, in ctx, member);
            return new MemberOrder
            {
                Kind = MemberOrderKind.Watch, Point = spot, Speed = p.Speed, RoleId = SquadDefendManeuver.RolePosted, Cover = 1,
                Sector = hasEa ? MemberArc.Around(LookAt.BearingTo(spot, ea), ArcHalfWidth)
                               : MemberArc.Around(LookAt.BearingTo(p.Position, spot), AllRoundHalfWidth),
                Post = new MemberPost   // ⭐ D-2a (PC-14, DA-40) — he fights from his post: its windows or his alternate, never elsewhere
                {
                    Set = 1, Window = post < p.Posts.WindowCount ? (byte)1 : (byte)0, Radius = PostRadius,
                    HasAlternate = alternate >= 0 ? (byte)1 : (byte)0, Alternate = alternate >= 0 ? p.Posts.Point(alternate) : default,
                },
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        // ── Posting: plan, manned, displace ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The window ask, then — for the men left without a post, and only with an engagement area — the cover ask; then the posts
        /// are given (a re-issue).</summary>
        private static void Plan(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            bool hasEa = EngagementAreas.TryAllowPoint(ctx.World, ctx.Commander, out var ea);
            var from = SquadManeuverRunner.FirstLive(in ctx);
            if (from.IsNull) return;
            if (p.Posts.WindowStage < 2)
            {
                if (p.Posts.WindowStage == 0) { p.Posts.WindowStage = 1; p.Posts.StageSince = ctx.Now; }
                bool answered = hasEa
                    ? SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindDefensiveWindowPosts.BlueprintId, WindowSite, p.SearchRadius, from, ea, out var buffer)
                    : SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindWindowPostsAllRound.BlueprintId, WindowSite, p.SearchRadius, from, p.Position, out buffer);
                if (!Settle(ref p, ref ws, in ctx, answered, in buffer)) return;
                p.Posts.WindowStage = 2;
                p.Posts.WindowCount = p.Posts.Count;
                PickAll(ref p, in ctx);
            }
            if (p.Posts.CoverStage < 2 && hasEa && AnyWithoutPost(ref p, in ctx))
            {
                if (p.Posts.CoverStage == 0) { p.Posts.CoverStage = 1; p.Posts.StageSince = ctx.Now; }
                bool answered = SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindCoverFromTarget.BlueprintId, CoverSite, p.SearchRadius, from, ea, out var buffer);
                if (!Settle(ref p, ref ws, in ctx, answered, in buffer)) return;
                p.Posts.CoverStage = 2;
                PickAll(ref p, in ctx);
            }
            p.Posts.Ready = 1;
            unsafe
            {
                for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)   // only rounds from now on count (DA-38)
                    p.Posts.LastSense[i] = ctx.Live(i, out var m) ? LastRoundAt(ctx.World, m) : double.NegativeInfinity;
            }
            ws.Reissue = 1;
        }

        /// <summary>True once the ask is over: its points kept (append), or it timed out (the sensor goes).</summary>
        private static bool Settle(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, bool answered, in EqsCognitiveBuffer buffer)
        {
            if (!answered)
            {
                if (ctx.Now - p.Posts.StageSince < AskTimeoutSeconds) return false;
                SquadManeuverRunner.DropSensor(ref ws, ctx.World);
                return true;
            }
            var points = buffer.GetSpanRO();
            for (int k = 0; k < buffer.Count && k < points.Length; k++)
                p.Posts.Add(new Vector3(points[k].PositionX, points[k].PositionY, points[k].PositionZ));
            return true;
        }

        private static unsafe void PickAll(ref SquadDefendParams p, in SquadPlayContext ctx)
        {
            int n = Math.Min(ctx.Roster.Count, DefencePosts.Capacity);
            Span<Vector3> candidates = stackalloc Vector3[DefencePosts.Capacity];
            for (int k = 0; k < p.Posts.Count; k++) candidates[k] = p.Posts.Point(k);
            Span<Vector3> men = stackalloc Vector3[DefencePosts.Capacity];
            Span<bool> live = stackalloc bool[DefencePosts.Capacity];
            Span<int> postOf = stackalloc int[DefencePosts.Capacity];
            for (int i = 0; i < n; i++)
            {
                live[i] = ctx.Live(i, out var m);
                men[i] = live[i] ? ctx.At(m) : default;
                postOf[i] = p.Posts.PostOf[i] == DefencePosts.None ? -1 : p.Posts.PostOf[i];
            }
            DefencePostPicker.Pick(candidates[..p.Posts.Count], men[..n], live[..n], postOf[..n]);
            for (int i = 0; i < n; i++) p.Posts.PostOf[i] = postOf[i] < 0 ? DefencePosts.None : (byte)postOf[i];
        }

        private static unsafe bool AnyWithoutPost(ref SquadDefendParams p, in SquadPlayContext ctx)
        {
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
                if (ctx.Live(i, out _) && p.Posts.PostOf[i] == DefencePosts.None) return true;
            return false;
        }

        /// <summary>Every live man with a post is at it ⇒ PROGRESS Set, once.</summary>
        private static unsafe void Manned(ref SquadDefendParams p, in SquadPlayContext ctx)
        {
            if (p.Posts.ReportedSet != 0) return;
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                if (Vector3.Distance(ctx.At(m), p.Posts.Point(p.Posts.PostOf[i])) > MannedMetres) return;
            }
            p.Posts.ReportedSet = 1;
            Report(in ctx, p.Position, ProgressKind.Set);
        }

        /// <summary>DA-38 — each new hit / near-miss time on a man counts one; 3 within 10 s ⇒ his alternate post and PROGRESS Displacing.</summary>
        private static unsafe void Displace(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                double last = LastRoundAt(ctx.World, m);
                if (last <= p.Posts.LastSense[i]) continue;
                p.Posts.LastSense[i] = last;
                if (ctx.Now - p.Posts.WindowStart[i] > UntenableSeconds) { p.Posts.WindowStart[i] = ctx.Now; p.Posts.Hits[i] = 1; }
                else p.Posts.Hits[i]++;
                if (p.Posts.Hits[i] < UntenableHits) continue;
                p.Posts.Hits[i] = 0;
                int alternate = AlternateOf(ref p, in ctx, i);
                if (alternate < 0) continue;
                p.Posts.PostOf[i] = (byte)alternate;
                ws.Reissue = 1;
                Report(in ctx, p.Posts.Point(alternate), ProgressKind.Displacing);
            }
        }

        private static unsafe int AlternateOf(ref SquadDefendParams p, in SquadPlayContext ctx, int man)
        {
            int n = Math.Min(ctx.Roster.Count, DefencePosts.Capacity);
            Span<Vector3> candidates = stackalloc Vector3[DefencePosts.Capacity];
            for (int k = 0; k < p.Posts.Count; k++) candidates[k] = p.Posts.Point(k);
            Span<int> postOf = stackalloc int[DefencePosts.Capacity];
            for (int i = 0; i < n; i++) postOf[i] = p.Posts.PostOf[i] == DefencePosts.None || !ctx.Live(i, out _) ? -1 : p.Posts.PostOf[i];
            return DefencePostPicker.Alternate(candidates[..p.Posts.Count], postOf[..n], man);
        }

        /// <summary>
        /// ⭐ DA-35 (corrected 2026-10-19; Platoon §6a "alone, it lifts its own hold-fire", DA-7) — a squad defending ALONE lifts its hold:
        /// a <see cref="RoeFire.HoldFire"/> set on the squad commander ITSELF by a non-Superior order (the operator's) goes to
        /// <see cref="RoeFire.FireAtWill"/> on the first pooled contact inside the squad's sector, or the first round at one of its men since the
        /// order. ⛔ A hold a SUPERIOR set (a platoon play), or one inherited from above, is the platoon's to lift (§5 Engage); a TKB default
        /// (<see cref="BehaviorOrigin.Unmarked"/>) is no order. Origin <see cref="BehaviorOrigin.Self"/>: the lift carries the rank of the order
        /// that set the hold. The play never SETS a hold (no withdraw exists — it would outlive a cleared defence).
        /// </summary>
        private static void LiftOwnHold(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var world = ctx.World;
            if (p.Posts.HoldLifted != 0 || !world.IsComponentTypeRegistered<Roe>() || !world.HasComponent<Roe>(ctx.Commander)) return;
            ref readonly var roe = ref world.GetComponentRO<Roe>(ctx.Commander);
            if (roe.Fire != RoeFire.HoldFire || roe.SetBy == BehaviorOrigin.Superior || roe.SetBy == BehaviorOrigin.Unmarked) return;
            if (!ContactInSector(in ctx) && !RoundAtAMan(in ctx, ws.StartedAt)) return;
            if (!world.Bus.IsRegistered<SetRoeEvent>()) return;
            world.Bus.Publish(new SetRoeEvent { Entity = ctx.Commander, Fire = RoeFire.FireAtWill, Origin = BehaviorOrigin.Self });
            p.Posts.HoldLifted = 1;
        }

        /// <summary>Any pooled contact (seen or heard) inside the squad's sector — with no sector, any contact.</summary>
        private static bool ContactInSector(in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int i = 0; i < squad.Contacts.Count && i < 16; i++)
                if (EngagementAreas.AllowsUnit(ctx.World, ctx.Commander, new Vector3(pool[i].PositionX, pool[i].PositionY, pool[i].PositionZ)))
                    return true;
            return false;
        }

        private static bool RoundAtAMan(in SquadPlayContext ctx, double since)
        {
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (ctx.Live(i, out var m) && LastRoundAt(ctx.World, m) >= since) return true;
            return false;
        }

        /// <summary>The newest hit or near-miss time on the man (his own senses), −∞ when none.</summary>
        private static double LastRoundAt(EntityRepository world, Entity man)
        {
            if (!world.HasComponent<RecentSenses>(man)) return double.NegativeInfinity;
            ref readonly var senses = ref world.GetComponentRO<RecentSenses>(man);
            senses.TryGetLast(SensorChange.NearMiss, out double nearMiss);
            senses.TryGetLast(SensorChange.Hit, out double hit);
            return Math.Max(nearMiss, hit);
        }

        private static void Report(in SquadPlayContext ctx, Vector3 at, ProgressKind kind)
        {
            float now = (float)ctx.Now;
            ReportQueue.Enqueue(ctx.World, ctx.Commander, new CommandReport
            {
                Kind = ReportKind.Progress, Subtype = (byte)kind, At = at, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}
