using System;
using System.Collections.Generic;
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
using Fdp.Toolkit.Perception.Survey;
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
        /// <summary>⭐ D-2f (DA-5, DA-46) — the order's authored fighting positions, placed before the terrain is asked.</summary>
        public AuthoredPosts Authored;
        /// <summary>Runtime: the posts and each man's count of rounds at him (the play's own memory).</summary>
        public DefencePosts Posts;
        /// <summary>⭐ <c>CE-2306</c> (DA-96) — an ambush's initiating charge (its network id; 0 = not an ambush).</summary>
        public long InitiatedBy;
    }

    /// <summary>⭐ <c>CE-2275</c> D-2f (DA-5, DA-46) — the authored fighting positions of a <c>SquadDefend</c> order (from
    /// <see cref="SquadDefendPostJsonDto"/>): a point, a point he faces (zero = the sector), a window flag.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct AuthoredPosts
    {
        public const int Capacity = SquadDefendPostJsonDto.MaxPosts;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        public fixed float WatchX[Capacity];
        public fixed float WatchY[Capacity];
        public fixed float WatchZ[Capacity];
        public fixed byte Window[Capacity];
        public byte Count;

        public static AuthoredPosts From(SquadDefendPostJsonDto[]? posts)
        {
            var a = default(AuthoredPosts);
            if (posts == null) return a;
            for (int i = 0; i < posts.Length && a.Count < Capacity; i++)
            {
                var post = posts[i];
                int k = a.Count++;
                a.X[k] = post.Point.X; a.Y[k] = post.Point.Y; a.Z[k] = post.Point.Z;
                a.WatchX[k] = post.WatchTowards.X; a.WatchY[k] = post.WatchTowards.Y; a.WatchZ[k] = post.WatchTowards.Z;
                a.Window[k] = post.Window ? (byte)1 : (byte)0;
            }
            return a;
        }

        public Vector3 Point(int k) => new(X[k], Y[k], Z[k]);
        public Vector3 Watch(int k) => new(WatchX[k], WatchY[k], WatchZ[k]);
    }

    /// <summary>
    /// ⭐ <c>CE-2275</c> D-1 (DA-34, DA-38) — the squad defence's memory: the candidate points the terrain gave (authored, windows, roof, cover),
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
        /// <summary>⭐ D-2a / D-2f — per candidate: 1 = a window post (fought the window way), from the window ask or authored.</summary>
        public fixed byte Window[Capacity];
        /// <summary>⭐ D-2f (DA-46) — per candidate: 1 = authored, with <see cref="WatchX"/>… the point he faces (0 there = the sector).</summary>
        public fixed byte Authored[Capacity];
        /// <summary>⭐ D-2g (DA-5, DA-22) — per candidate: 1 = a roof post (from the roof ask), fought prone behind the parapet.</summary>
        public fixed byte Roof[Capacity];
        public fixed float WatchX[Capacity];
        public fixed float WatchY[Capacity];
        public fixed float WatchZ[Capacity];
        /// <summary>⭐ D-2f — 1 once the order's authored posts were placed (first, before any ask).</summary>
        public byte AuthoredPlaced;
        /// <summary>⭐ D-2b (DA-6, DA-42) — 1 = the window posts are SET BACK in the room (the squad's median man is Regular or better).</summary>
        public byte SetBack;
        /// <summary>⭐ D-2j (DA-16, DA-50) — 1 = ONLY set-back window posts (Veteran or better): a window whose room is too shallow is no post.</summary>
        public byte SetBackOnly;
        /// <summary>Per roster slot: the candidate he holds, or <see cref="None"/>.</summary>
        public fixed byte PostOf[Capacity];
        /// <summary>Per roster slot: rounds at him counted since <see cref="WindowStart"/>.</summary>
        public fixed byte Hits[Capacity];
        public fixed double WindowStart[Capacity];
        /// <summary>Per roster slot: the newest hit / near-miss time already counted.</summary>
        public fixed double LastSense[Capacity];
        /// <summary>⭐ <c>CE-2304</c> — when each man was last sent to a new post (DA-38's count waits <see cref="SquadDefendPlay.DisplacedGraceSeconds"/>).</summary>
        public fixed double MovedAt[Capacity];
        /// <summary>⭐ <c>CE-2304</c> (DA-58) — 1 once each man has stood within <see cref="SquadDefendPlay.PostRadius"/> of his post since he was
        /// last sent to one (Set counts it).</summary>
        public fixed byte Reached[Capacity];
        /// <summary>0 = not asked · 1 = asked · 2 = done (answered, empty, or timed out).</summary>
        public byte WindowStage, RoofStage, CoverStage;
        /// <summary>⭐ D-2h (DA-48) — the visibility fan that puts the automatic weapon on the longest view: 0 · 1 asked · 2 done.</summary>
        public byte FanStage;
        public double StageSince;
        /// <summary>1 = the posts are given (orders carry them).</summary>
        public byte Ready;
        /// <summary>1 = PROGRESS Set went up (every man at his post).</summary>
        public byte ReportedSet;
        /// <summary>1 = this play lifted its squad's own hold-fire (DA-35) — once.</summary>
        public byte HoldLifted;
        /// <summary>⭐ D-2c (DA-43) — per roster slot: 1 = his arc faces <see cref="FaceBearing"/> (a flanker), not the engagement area.</summary>
        public fixed byte Faces[Capacity];
        public fixed float FaceBearing[Capacity];
        /// <summary>⭐ D-2c — per roster slot: the contact he last turned to (its id) — once per contact.</summary>
        public fixed long Flanker[Capacity];
        /// <summary>⭐ D-2l (DA-77, DA-78, R-2059) — 1 = the squad turns its window points into FRAME posts (its median man is Regular or
        /// better) · the first candidate the window ask added · 1 once that ask's points are in.</summary>
        public byte FrameWanted, WindowFrom, WindowSettled;
        /// <summary>⭐ D-2l — per candidate: 1 = a FRAME post (the point is one of its window's frame points) · 1 = a window the set-back pass
        /// could not move back (a Veteran's shallow room, D-2j): a frame post when it has a frame, else no post (DA-84).</summary>
        public fixed byte Frame[Capacity];
        public fixed byte Shallow[Capacity];
        /// <summary>⭐ D-2l — per candidate: its window's two frame points and the opening's centre (flat; Z = the post's).</summary>
        public fixed float FrameAX[Capacity];
        public fixed float FrameAY[Capacity];
        public fixed float FrameBX[Capacity];
        public fixed float FrameBY[Capacity];
        public fixed float OpeningX[Capacity];
        public fixed float OpeningY[Capacity];

        public Vector3 Point(int k) => new(X[k], Y[k], Z[k]);

        public void Add(Vector3 p, bool window = false, bool roof = false)
        {
            if (Count >= Capacity) return;
            X[Count] = p.X; Y[Count] = p.Y; Z[Count] = p.Z;
            Window[Count] = window ? (byte)1 : (byte)0;
            Roof[Count] = roof ? (byte)1 : (byte)0;
            Frame[Count] = Shallow[Count] = 0;   // ⭐ D-2l — a slot freed by the frame pass may hold an old candidate's flags
            Count++;
        }

        public Vector3 FrameA(int k) => new(FrameAX[k], FrameAY[k], Z[k]);
        public Vector3 FrameB(int k) => new(FrameBX[k], FrameBY[k], Z[k]);
        public Vector3 Opening(int k) => new(OpeningX[k], OpeningY[k], Z[k]);

        /// <summary>⭐ D-2f — an authored candidate (<paramref name="watch"/> zero = he faces the sector).</summary>
        public void AddAuthored(Vector3 p, bool window, Vector3 watch)
        {
            if (Count >= Capacity) return;
            Authored[Count] = 1;
            WatchX[Count] = watch.X; WatchY[Count] = watch.Y; WatchZ[Count] = watch.Z;
            Add(p, window);
        }

        public Vector3 Watch(int k) => new(WatchX[k], WatchY[k], WatchZ[k]);
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
            block.Order.Authored = AuthoredPosts.From(authored.Posts);   // ⭐ D-2f (DA-46)
            block.Order.InitiatedBy = authored.InitiatedBy.NetworkId;      // ⭐ CE-2306 (DA-96)
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
        /// <summary>The commander's post-query sites (the window ask, the roof ask, then the cover ask).</summary>
        public const int WindowSite = 0x22750001;
        public const int CoverSite  = 0x22750002;
        public const int RoofSite   = 0x22750003;   // ⭐ D-2g
        /// <summary>⭐ D-2h (DA-48) — the visibility fan's site; its rays per post; how much longer a view must be to move the gun (m).</summary>
        public const int FanSite    = 0x22750004;
        public const int FanRays = 9;
        /// <summary>⭐ D-2l (DA-78) — the BuildingSurvey that gives each window post its window's frame.</summary>
        public const int FrameSite  = 0x22750005;
        public const float LongerViewMetres = 5f;

        /// <summary>Everyone within this of the position ⇒ the posts are asked for (m).</summary>
        public const float ArrivedMetres = 6f;
        /// <summary>The move ends after this long even if a man never arrives (s).</summary>
        public const float MoveTimeoutSeconds = 90f;
        /// <summary>A post query unanswered this long is given up (s): the men left without a post hold where they are.</summary>
        public const float AskTimeoutSeconds = 5f;
        /// <summary>DA-38 — this many rounds at a man within <see cref="UntenableSeconds"/> ⇒ his alternate post.</summary>
        public const int UntenableHits = 3;
        public const float UntenableSeconds = 10f;
        /// <summary>⭐ <c>CE-2304</c> (DA-56) — the rounds at a man in this long after he was sent to a new post do not count toward DA-38:
        /// he is moving there, not holding it. 📌 Measured live (<c>sq-defend-house</c>): without it each displaced man was sent straight
        /// back by the rounds of the fire he moved through — 43 displacements in 14 s, every man ping-ponging between two posts.</summary>
        public const float DisplacedGraceSeconds = 10f;
        /// <summary>DA-37 — a man's arc half-width toward the engagement area / outward all-round (°).</summary>
        public const float ArcHalfWidth = 45f;
        public const float AllRoundHalfWidth = 60f;
        /// <summary>⭐ D-2a (PC-14, DA-40) — a hide point this close to his post is one of his post's points (m): the windows of his room.</summary>
        public const float PostRadius = 4f;
        /// <summary>⭐ D-2c (DA-21, DA-43) — a seen contact this close to a posted man, outside his arc, flanks him (m).</summary>
        public const float FlankedMetres = 50f;

        private static readonly SquadHsmShell s_shell = new(SquadDefendManeuver.BuildTransitionTable(), SquadDefendManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadDefendManeuver.ManeuverKind;
        public ushort StartPhase => SquadDefendManeuver.PhaseMoving;
        public ushort AbortPhase => SquadDefendManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            unsafe { for (int i = 0; i < DefencePosts.Capacity; i++) { p.Posts.PostOf[i] = DefencePosts.None; p.Posts.MovedAt[i] = double.NegativeInfinity; } }
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            LiftOwnHold(ref p, ref ws, in ctx);
            if (ctx.Squad.PhaseId != SquadDefendManeuver.PhasePosting) return;
            if (p.Posts.Ready == 0) { Plan(ref p, ref ws, in ctx); return; }
            Manned(ref p, in ctx);
            Displace(ref p, ref ws, in ctx);
            Flanked(ref p, ref ws, in ctx);
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
                Sector = ArcOf(ref p, in ctx, member, spot),
                Post = new MemberPost   // ⭐ D-2a (PC-14, DA-40) — he fights from his post: its windows or his alternate, never elsewhere
                {
                    Set = 1, Window = p.Posts.Window[post], Radius = PostRadius,
                    // ⭐ D-2b — his own hide ask sets back too; ⭐ D-2f — not at an authored post (the author placed him there);
                    //   ⭐ D-2l — not at a frame post (he stands beside the frame instead)
                    SetBack = p.Posts.Window[post] != 0 && p.Posts.Authored[post] == 0 && p.Posts.Frame[post] == 0 ? p.Posts.SetBack : (byte)0,
                    Frame = p.Posts.Frame[post],   // ⭐ D-2l (DA-78) — beside the frame, leaning out
                    FrameA = p.Posts.FrameA(post), FrameB = p.Posts.FrameB(post), Opening = p.Posts.Opening(post),
                    Roof = p.Posts.Roof[post],   // ⭐ D-2g — prone behind the parapet, crouched to fire
                    HasAlternate = alternate >= 0 ? (byte)1 : (byte)0, Alternate = alternate >= 0 ? p.Posts.Point(alternate) : default,
                },
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>A posted man's arc from <paramref name="spot"/> (DA-37): toward the engagement area ±45°, else outward ±60° — or, once a
        /// flanker turned him (D-2c, DA-43), toward that flanker ±45°.</summary>
        private static unsafe MemberArc ArcOf(ref SquadDefendParams p, in SquadPlayContext ctx, int member, Vector3 spot)
        {
            if (member < DefencePosts.Capacity && p.Posts.Faces[member] != 0) return MemberArc.Around(p.Posts.FaceBearing[member], ArcHalfWidth);
            // ⭐ D-2f (DA-46) — an authored post the author pointed: his arc faces that point
            byte post = member < DefencePosts.Capacity ? p.Posts.PostOf[member] : DefencePosts.None;
            if (post != DefencePosts.None && post < p.Posts.Count && p.Posts.Authored[post] != 0 && p.Posts.Watch(post) != Vector3.Zero)
                return MemberArc.Around(LookAt.BearingTo(spot, p.Posts.Watch(post)), ArcHalfWidth);
            return EngagementAreas.TryAllowPoint(ctx.World, ctx.Commander, out var ea)
                ? MemberArc.Around(LookAt.BearingTo(spot, ea), ArcHalfWidth)
                : MemberArc.Around(LookAt.BearingTo(p.Position, spot), AllRoundHalfWidth);
        }

        // ── Posting: plan, manned, displace ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The authored posts, the window ask, then — for the men left without a post, and only with an engagement area — the roof
        /// ask (D-2g) and the cover ask; then the posts are given (a re-issue).</summary>
        private static void Plan(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            bool hasEa = EngagementAreas.TryAllowPoint(ctx.World, ctx.Commander, out var ea);
            var from = SquadManeuverRunner.FirstLive(in ctx);
            if (from.IsNull) return;
            if (p.Posts.AuthoredPlaced == 0)
            {
                // ⭐ D-2f (DA-5, DA-46) — the authored fighting positions first: the picker hands them out before any terrain point, and
                //   when every live man holds one the terrain is not asked at all
                p.Posts.AuthoredPlaced = 1;
                for (int k = 0; k < p.Authored.Count; k++)
                    unsafe { p.Posts.AddAuthored(p.Authored.Point(k), p.Authored.Window[k] != 0, p.Authored.Watch(k)); }
                if (p.Posts.Count > 0) PickAll(ref p, in ctx);   // both asks below run only for the men still without a post
            }
            if (p.Posts.WindowStage < 2 && AnyWithoutPost(ref p, in ctx))
            {
                if (p.Posts.WindowStage == 0)
                {
                    p.Posts.WindowStage = 1; p.Posts.StageSince = ctx.Now;
                    var tier = MedianTier(in ctx);   // ⭐ D-2b (DA-42) — once, by the squad's median man
                    // ⭐ D-2l (DA-77, R-2059) — Regular or better fights a window from beside its frame; a Veteran keeps the set-back post
                    //   where the room is deep enough (D-2b / D-2j) and stands at the frame elsewhere. ⛔ SUPERSEDED: Regular set back (D-2b).
                    p.Posts.SetBack = tier >= ProficiencyTier.Veteran ? (byte)1 : (byte)0;
                    p.Posts.SetBackOnly = tier >= ProficiencyTier.Veteran ? (byte)1 : (byte)0;   // ⭐ D-2j (DA-16) — "set-back posts everywhere"
                    p.Posts.FrameWanted = tier >= ProficiencyTier.Regular ? (byte)1 : (byte)0;
                    p.Posts.WindowFrom = p.Posts.Count;
                    if (p.Posts.FrameWanted != 0)   // DA-78 — ONE survey of the position's buildings gives every window post its frame
                        WorldSurveyChildSensor.Ensure(ctx.World, ctx.Commander, FrameSite,
                            WorldSurveyRequest.OfArea(new Vector2(p.Position.X, p.Position.Y), p.SearchRadius));
                }
                if (p.Posts.WindowSettled == 0)
                {
                    bool back = p.Posts.SetBack != 0;
                    bool answered = hasEa
                        ? SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, back ? FindDefensiveWindowSetBackPosts.BlueprintId : FindDefensiveWindowPosts.BlueprintId,
                            WindowSite, p.SearchRadius, from, ea, out var buffer)
                        : SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, back ? FindWindowSetBackPostsAllRound.BlueprintId : FindWindowPostsAllRound.BlueprintId,
                            WindowSite, p.SearchRadius, from, p.Position, out buffer);
                    if (!Settle(ref p, ref ws, in ctx, answered, in buffer, window: true)) return;
                    p.Posts.WindowSettled = 1;
                }
                if (!Frames(ref p, in ctx, hasEa, ea)) return;
                p.Posts.WindowStage = 2;
                PickAll(ref p, in ctx);
            }
            if (p.Posts.RoofStage < 2 && hasEa && AnyWithoutPost(ref p, in ctx))
            {
                // ⭐ D-2g (DA-5, DA-22, DA-47) — the roof, behind its parapet, for the men the windows did not take: points the cover ask
                //   never gives (📌 measured on OBJ: 22 roof points, none hidden from the sector)
                if (p.Posts.RoofStage == 0) { p.Posts.RoofStage = 1; p.Posts.StageSince = ctx.Now; }
                bool answered = SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindRoofPosts.BlueprintId, RoofSite, p.SearchRadius, from, ea, out var buffer);
                if (!Settle(ref p, ref ws, in ctx, answered, in buffer, window: false, roof: true)) return;
                p.Posts.RoofStage = 2;
                PickAll(ref p, in ctx);
            }
            if (p.Posts.CoverStage < 2 && hasEa && AnyWithoutPost(ref p, in ctx))
            {
                if (p.Posts.CoverStage == 0) { p.Posts.CoverStage = 1; p.Posts.StageSince = ctx.Now; }
                bool answered = SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindCoverFromTarget.BlueprintId, CoverSite, p.SearchRadius, from, ea, out var buffer);
                if (!Settle(ref p, ref ws, in ctx, answered, in buffer, window: false)) return;
                p.Posts.CoverStage = 2;
                PickAll(ref p, in ctx);
            }
            if (p.Posts.FanStage < 2 && !GunOnTheLongestView(ref p, in ctx, hasEa, ea)) return;
            p.Posts.Ready = 1;
            unsafe
            {
                for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)   // only rounds from now on count (DA-38)
                    p.Posts.LastSense[i] = ctx.Live(i, out var m) ? LastRoundAt(ctx.World, m) : double.NegativeInfinity;
            }
            ws.Reissue = 1;
        }

        /// <summary>
        /// ⭐ D-2h (DA-19, DA-25, DA-48) — the squad's automatic weapon (AR / MG) on the post with the longest view down the sector: one
        /// visibility fan from every held non-roof post toward the EA point; a post's view is the mean free distance of its rays; the gun
        /// swaps posts with the holder of a view longer by <see cref="LongerViewMetres"/> (never with another gun). True once the stage is
        /// over — answered, nothing to compare, or timed out (posts as picked). ⛔ Roof posts are not origins: a horizontal ray over a
        /// parapet measures the sky, not grazing fire. 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2h.
        /// </summary>
        private static unsafe bool GunOnTheLongestView(ref SquadDefendParams p, in SquadPlayContext ctx, bool hasEa, Vector3 ea)
        {
            int gun = -1, n = 0;
            Span<int> posts = stackalloc int[WorldSurveyRequest.MaxOrigins];
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                byte post = p.Posts.PostOf[i];
                if (!ctx.Live(i, out _) || post == DefencePosts.None || p.Posts.Roof[post] != 0) continue;
                if (gun < 0 && SquadRoles.CarriesSupportWeapon(SquadRoles.Of(ctx.World, in ctx.Roster, i))) gun = i;
                if (n < posts.Length) posts[n++] = post;
            }
            int gunOrigin = gun < 0 ? -1 : posts[..n].IndexOf(p.Posts.PostOf[gun]);
            if (!hasEa || gunOrigin < 0 || n < 2) { p.Posts.FanStage = 2; return true; }
            if (p.Posts.FanStage == 0)
            {
                var origins = new List<SurveyOrigin>(n);
                for (int k = 0; k < n; k++)
                {
                    var at = p.Posts.Point(posts[k]);
                    origins.Add(new SurveyOrigin { X = at.X, Y = at.Y, Z = at.Z, Bearing = MathF.Atan2(ea.Y - at.Y, ea.X - at.X), HalfArc = MathF.PI / 4f });
                }
                float range = Math.Clamp(Flat(ea - p.Position) * 1.5f, 50f, 300f);
                WorldSurveyChildSensor.Ensure(ctx.World, ctx.Commander, FanSite, WorldSurveyRequest.Fan(origins, FanRays, range));
                p.Posts.FanStage = 1; p.Posts.StageSince = ctx.Now;
                return false;
            }
            var child = EqsChildSensor.Find(ctx.World, ctx.Commander, FanSite);
            var read = WorldSurveyChildSensor.Read(ctx.World, child, ctx.Now);
            if (!read.Ready || read.Stale || read.Payload == null)
            {
                if (ctx.Now - p.Posts.StageSince < AskTimeoutSeconds) return false;
                WorldSurveyChildSensor.Release(ctx.World, child);
                p.Posts.FanStage = 2;
                return true;
            }
            Span<float> view = stackalloc float[WorldSurveyRequest.MaxOrigins];
            Span<int> rays = stackalloc int[WorldSurveyRequest.MaxOrigins];
            foreach (var ray in read.Payload.Rays)
                if (ray.Origin >= 0 && ray.Origin < n) { view[ray.Origin] += ray.Free; rays[ray.Origin]++; }
            for (int k = 0; k < n; k++) view[k] = rays[k] == 0 ? 0f : view[k] / rays[k];   // the mean free distance of its rays
            int best = gunOrigin;
            for (int k = 0; k < n; k++) if (view[k] > view[best]) best = k;
            if (best != gunOrigin && view[best] > view[gunOrigin] + LongerViewMetres)
                for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
                {
                    if (i == gun || !ctx.Live(i, out _) || p.Posts.PostOf[i] != posts[best]) continue;
                    if (SquadRoles.CarriesSupportWeapon(SquadRoles.Of(ctx.World, in ctx.Roster, i))) break;   // never displace another gun
                    (p.Posts.PostOf[gun], p.Posts.PostOf[i]) = (p.Posts.PostOf[i], p.Posts.PostOf[gun]);
                    break;
                }
            WorldSurveyChildSensor.Release(ctx.World, child);
            p.Posts.FanStage = 2;
            return true;
        }

        /// <summary>⭐ D-2b (DA-6, DA-16, DA-42) — the squad's median live man's tier: Regular or better ⇒ its window posts are set back,
        /// ⭐ D-2j Veteran or better ⇒ ONLY set-back ones.</summary>
        private static ProficiencyTier MedianTier(in SquadPlayContext ctx)
        {
            Span<Entity> men = stackalloc Entity[DefencePosts.Capacity];
            int n = 0;
            for (int i = 0; i < ctx.Roster.Count && n < men.Length; i++)
                if (ctx.Live(i, out var m)) men[n++] = m;
            return ProficiencyOf.MedianTier(ctx.World, men[..n]);
        }

        /// <summary>True once the ask is over: its points kept (append), or it timed out (the sensor goes).</summary>
        private static bool Settle(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, bool answered, in EqsCognitiveBuffer buffer, bool window, bool roof = false)
        {
            if (!answered)
            {
                if (ctx.Now - p.Posts.StageSince < AskTimeoutSeconds) return false;
                SquadManeuverRunner.DropSensor(ref ws, ctx.World);
                return true;
            }
            var points = buffer.GetSpanRO();
            for (int k = 0; k < buffer.Count && k < points.Length; k++)
            {
                // ⭐ D-2j (DA-16, DA-50) — a Veteran squad keeps only the windows its set-back pass really moved back from the sill;
                //   ⭐ D-2l (DA-77) — the others wait for the frame pass, which keeps those that have a frame (Frames)
                bool shallow = window && p.Posts.SetBackOnly != 0 && (points[k].FlagsMeaningful & CoverPointsGenerator.FlagSetBack) != 0
                    && (points[k].Flags & CoverPointsGenerator.FlagSetBack) == 0;
                if (shallow && p.Posts.FrameWanted == 0) continue;
                int added = p.Posts.Count;
                p.Posts.Add(new Vector3(points[k].PositionX, points[k].PositionY, points[k].PositionZ), window, roof);
                if (shallow && p.Posts.Count > added) unsafe { p.Posts.Shallow[added] = 1; }
            }
            return true;
        }

        /// <summary>
        /// ⭐ D-2l (DA-78, DA-84; 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2l) — the frame pass over the window ask's points: each one
        /// that is fought from the frame (every one at Regular; a Veteran's shallow ones) and has a window within
        /// <see cref="WindowFrame.MatchMetres"/> in the survey becomes a FRAME post at the frame point away from the engagement area
        /// (<see cref="WindowFrame.Facing"/>); a Veteran's shallow window with no frame is no post (D-2j); the rest stay as they were.
        /// True once the survey answered or timed out (<see cref="AskTimeoutSeconds"/> from the window stage) — then the survey goes.
        /// </summary>
        private static unsafe bool Frames(ref SquadDefendParams p, in SquadPlayContext ctx, bool hasEa, Vector3 ea)
        {
            if (p.Posts.FrameWanted == 0) return true;
            var child = EqsChildSensor.Find(ctx.World, ctx.Commander, FrameSite);
            var read = WorldSurveyChildSensor.Read(ctx.World, child, ctx.Now);
            bool ready = read.Ready && !read.Stale && read.Payload != null;
            if (!ready && ctx.Now - p.Posts.StageSince < AskTimeoutSeconds) return false;
            var answer = ready ? read.Payload : null;
            int keep = p.Posts.WindowFrom;
            for (int k = p.Posts.WindowFrom; k < p.Posts.Count; k++)
            {
                bool shallow = p.Posts.Shallow[k] != 0;
                var at = p.Posts.Point(k);
                Vector3 a = default, b = default, opening = default;
                bool framed = (p.Posts.SetBack == 0 || shallow) && WindowFrame.FromSurvey(answer, at, out a, out b, out opening);
                if (shallow && !framed) continue;   // DA-84 / D-2j — a Veteran's shallow window without a frame is no post
                if (framed)
                {
                    var target = hasEa ? ea : opening + (opening - p.Position);   // all-round: straight out
                    at = WindowFrame.Facing(a, b, opening, target);
                    p.Posts.FrameAX[keep] = a.X; p.Posts.FrameAY[keep] = a.Y;
                    p.Posts.FrameBX[keep] = b.X; p.Posts.FrameBY[keep] = b.Y;
                    p.Posts.OpeningX[keep] = opening.X; p.Posts.OpeningY[keep] = opening.Y;
                }
                p.Posts.X[keep] = at.X; p.Posts.Y[keep] = at.Y; p.Posts.Z[keep] = at.Z;
                p.Posts.Window[keep] = 1;
                p.Posts.Authored[keep] = p.Posts.Roof[keep] = p.Posts.Shallow[keep] = 0;
                p.Posts.WatchX[keep] = p.Posts.WatchY[keep] = p.Posts.WatchZ[keep] = 0f;
                p.Posts.Frame[keep] = framed ? (byte)1 : (byte)0;
                keep++;
            }
            p.Posts.Count = (byte)keep;
            WorldSurveyChildSensor.Release(ctx.World, child);
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

        /// <summary>Every live man with a post has reached it ⇒ PROGRESS Set, once. ⭐ D-2d — "reached" is within <see cref="PostRadius"/>: the post
        /// is the man's room (D-2a), and his fight moves him between its points. ⭐ <c>CE-2304</c> (DA-58) — once reached it STAYS reached until
        /// he is sent to another post: measured live, a man fighting from his post stood at a hide point up to 4 m out and peeked further,
        /// so "all within 4 m in the same frame" held for no frame in 4½ minutes while every man was at his post.</summary>
        private static unsafe void Manned(ref SquadDefendParams p, in SquadPlayContext ctx)
        {
            if (p.Posts.ReportedSet != 0) return;
            bool all = true;
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                if (Vector3.Distance(ctx.At(m), p.Posts.Point(p.Posts.PostOf[i])) <= PostRadius) p.Posts.Reached[i] = 1;
                all &= p.Posts.Reached[i] != 0;
            }
            if (!all) return;
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
                if (ctx.Now - p.Posts.MovedAt[i] < DisplacedGraceSeconds) continue;   // ⭐ CE-2304 (DA-56) — still moving to it
                if (ctx.Now - p.Posts.WindowStart[i] > UntenableSeconds) { p.Posts.WindowStart[i] = ctx.Now; p.Posts.Hits[i] = 1; }
                else p.Posts.Hits[i]++;
                if (p.Posts.Hits[i] < UntenableHits) continue;
                p.Posts.Hits[i] = 0;
                int alternate = AlternateOf(ref p, in ctx, i);
                if (alternate < 0) continue;
                p.Posts.PostOf[i] = (byte)alternate;
                p.Posts.MovedAt[i] = ctx.Now;
                p.Posts.Reached[i] = 0;
                ws.Reissue = 1;
                Report(in ctx, p.Posts.Point(alternate), ProgressKind.Displacing);
            }
        }

        /// <summary>
        /// ⭐ D-2c (DA-21 <c>ContactOutsideSector</c>, DA-4, DA-43) — a SEEN hostile pooled contact within <see cref="FlankedMetres"/> of a posted
        /// man's post and outside his arc flanks him: he takes his alternate post (none ⇒ he stays) with his arc facing the contact, PROGRESS
        /// Displacing; once per contact. Counted here until the member sensor lands (<c>CE-2283</c>), as DA-38 counts rounds.
        /// </summary>
        private static unsafe void Flanked(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out _) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                var post = p.Posts.Point(p.Posts.PostOf[i]);
                var arc = ArcOf(ref p, in ctx, i, post);
                for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
                {
                    if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0 || pool[c].EntityId == p.Posts.Flanker[i]) continue;
                    var at = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
                    if (Vector3.Distance(at, post) > FlankedMetres
                        || EngagementAreas.InArc(post, arc.LeftBearing, arc.RightBearing, arc.MinRange, arc.MaxRange, at)) continue;
                    int alternate = AlternateOf(ref p, in ctx, i);
                    if (alternate >= 0) { p.Posts.PostOf[i] = (byte)alternate; p.Posts.MovedAt[i] = ctx.Now; p.Posts.Reached[i] = 0; }
                    var from = p.Posts.Point(p.Posts.PostOf[i]);
                    p.Posts.Faces[i] = 1;
                    p.Posts.FaceBearing[i] = LookAt.BearingTo(from, at);
                    p.Posts.Flanker[i] = pool[c].EntityId;
                    ws.Reissue = 1;
                    Report(in ctx, from, ProgressKind.Displacing);
                    break;
                }
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
            // ⭐ CE-2306 (DA-96) — an ambush waits for its initiation (the charge gone), not for the first enemy in the kill zone
            bool trigger = p.InitiatedBy != 0 ? Initiated(world, p.InitiatedBy) : ContactInSector(in ctx);
            if (!trigger && !RoundAtAMan(in ctx, ws.StartedAt)) return;
            if (!world.Bus.IsRegistered<SetRoeEvent>()) return;
            world.Bus.Publish(new SetRoeEvent { Entity = ctx.Commander, Fire = RoeFire.FireAtWill, Origin = BehaviorOrigin.Self });
            p.Posts.HoldLifted = 1;
        }

        /// <summary>⭐ <c>CE-2306</c> (DA-96) — the ambush's charge is gone: set off (spent, then torn down on every node) or destroyed.</summary>
        private static bool Initiated(EntityRepository world, long charge)
        {
            var e = new Fdp.Toolkit.Replication.EntityRef(charge).Resolve(world);
            return e.IsNull || !Fdp.Toolkit.Combat.Charges.IsCharge(world, e);
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
