using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
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
using Fdp.Toolkit.Squad.Systems;

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
        public int InitiatedBy;
        /// <summary>⭐ <c>CE-7007</c> — shoot and move: seconds at one post in contact before he changes (0 = off).</summary>
        public float ShootAndMove;
        /// <summary>⭐ <c>CE-8327</c> — 1 = a re-send to the point already held: posts from where the squad stands (see <c>AlreadyInTheArea</c>).</summary>
        public byte InPlace;
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
                a.X[k] = post.Point.Local.X; a.Y[k] = post.Point.Local.Y; a.Z[k] = post.Point.Local.Z;
                a.WatchX[k] = post.WatchTowards.Local.X; a.WatchY[k] = post.WatchTowards.Local.Y; a.WatchZ[k] = post.WatchTowards.Local.Z;
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
        /// <summary>⭐ D-13 (CE-4666, the prone half) — per candidate: 1 = a DISPERSAL spot (<see cref="DefencePlan"/>'s Disperse: no cover in
        /// reach), held PRONE.</summary>
        public fixed byte Open[Capacity];
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
        /// <summary>Per roster slot: the newest <c>PositionUntenable</c> time already answered (⭐ CE-2283 — the count is the sensor's).</summary>
        public fixed double LastSense[Capacity];
        /// <summary>⭐ <c>CE-2304</c> — when each man was last sent to a new post (DA-38's count waits <see cref="SquadDefendPlay.DisplacedGraceSeconds"/>).</summary>
        public fixed double MovedAt[Capacity];
        /// <summary>⭐ <c>CE-7007</c> — when each man began his stay at his current post in contact (shoot and move counts from here; −∞ none).</summary>
        public fixed double DwellFrom[Capacity];
        /// <summary>⭐ <c>CE-2304</c> (DA-58) — 1 once each man has stood within <see cref="SquadDefendPlay.PostRadius"/> of his post since he was
        /// last sent to one (Set counts it).</summary>
        public fixed byte Reached[Capacity];
        /// <summary>0 = not asked · 1 = asked · 2 = done (answered, empty, or timed out).</summary>
        public byte WindowStage, RoofStage, CoverStage;
        /// <summary>⭐ D-2h (DA-48) — the visibility fan that puts the automatic weapon on the longest view: 0 · 1 asked · 2 done.</summary>
        public byte FanStage;
        public SimInstant StageSince;
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
        /// <summary>⭐ <c>CE-2359</c> (AQ102 H) — the HUNTER's roster slot (<see cref="None"/> = no hunt), the contact he hunts (its id), when the
        /// hunt began; and the last contact hunted (once per contact).</summary>
        public byte Hunter;
        public long Hunted, LastHunted;
        public SimInstant HuntSince;
        /// <summary>⭐ <c>CE-2373</c> (D-2m) — the coverage pass: 0 not started · 1 the fan asked · 2 done.</summary>
        public byte CoverageStage;
        /// <summary>⭐ D-2m — the candidates in coverage order (the first <see cref="RankCount"/>; the rest keep their ask order).</summary>
        public fixed byte Rank[Capacity];
        public byte RankCount;
        /// <summary>⭐ D-2m — a bit per probe each candidate sees (<see cref="CoveragePosts"/>).</summary>
        public fixed uint Sees[Capacity];
        /// <summary>⭐ D-2m (DL-4) — a lone man's round: the first <see cref="RoundCount"/> ranked posts; when he reached his current one.</summary>
        public byte RoundCount;
        public SimInstant RoundAt, RoundSince;
        /// <summary>⭐ D-2m — when each probe was last watched from a round post.</summary>
        public fixed double WatchedAt[CoveragePosts.ProbeCount];
        /// <summary>⭐ <c>CE-2359</c> — where the hunted contact was last known when the hunt began (the hunter's arc faces it).</summary>
        public float HuntX, HuntY, HuntZ;

        public Vector3 Point(int k) => new(X[k], Y[k], Z[k]);

        public void Add(Vector3 p, bool window = false, bool roof = false)
        {
            if (Count >= Capacity) return;
            X[Count] = p.X; Y[Count] = p.Y; Z[Count] = p.Z;
            Window[Count] = window ? (byte)1 : (byte)0;
            Roof[Count] = roof ? (byte)1 : (byte)0;
            Frame[Count] = Shallow[Count] = Open[Count] = 0;   // ⭐ D-2l — a slot freed by the frame pass may hold an old candidate's flags
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
    /// ⭐ <c>CE-2345</c> (R-2066 B) — two COVER posts (in the open, behind a wall or a berm) are spread wider: <see cref="CoverSpacingMetres"/>,
    /// or <see cref="CoverSpacingShortMetres"/> where the cover is too short for every man (§9's "posts spread ≥ 8 m"); windows, roof and
    /// authored posts keep <see cref="SpacingMetres"/> — a room's two windows are its posts.
    /// </summary>
    public static class DefencePostPicker
    {
        /// <summary>Two posts at least this far apart (m) — 3-D.</summary>
        public const float SpacingMetres = 2.5f;

        /// <summary>⭐ <c>CE-2345</c> — two cover posts at least this far apart (m), first pass.</summary>
        public const float CoverSpacingMetres = 8f;

        /// <summary>⭐ <c>CE-2345</c> — the second pass, for the men the first left without a post (m): never closer.</summary>
        public const float CoverSpacingShortMetres = 5f;

        /// <summary>
        /// Each candidate in rank order goes to the nearest live man still without a post, unless it is within <see cref="SpacingMetres"/> of
        /// a post already held (<paramref name="coverSpacing"/> when both are cover posts, <paramref name="spread"/>). A man who holds one
        /// (<paramref name="postOf"/> ≠ -1) keeps it.
        /// </summary>
        public static void Pick(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<Vector3> men, ReadOnlySpan<bool> live, Span<int> postOf,
                                ReadOnlySpan<bool> spread = default, float coverSpacing = SpacingMetres)
        {
            for (int k = 0; k < candidates.Length; k++)
            {
                if (Taken(candidates, postOf, k) || Crowded(candidates, postOf, k, skipMan: -1, spread, coverSpacing)) continue;
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
        public static int Alternate(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<int> postOf, int man,
                                    ReadOnlySpan<bool> spread = default, float coverSpacing = SpacingMetres)
        {
            if (postOf[man] < 0) return -1;
            var from = candidates[postOf[man]];
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < candidates.Length; k++)
            {
                if (k == postOf[man] || Taken(candidates, postOf, k) || Crowded(candidates, postOf, k, skipMan: man, spread, coverSpacing)) continue;
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

        private static bool Crowded(ReadOnlySpan<Vector3> candidates, ReadOnlySpan<int> postOf, int k, int skipMan, ReadOnlySpan<bool> spread,
                                    float coverSpacing)
        {
            for (int i = 0; i < postOf.Length; i++)
            {
                if (i == skipMan || postOf[i] < 0) continue;
                bool wide = k < spread.Length && postOf[i] < spread.Length && spread[k] && spread[postOf[i]];
                if (Vector3.Distance(candidates[postOf[i]], candidates[k]) < (wide ? coverSpacing : SpacingMetres)) return true;
            }
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
            block.Order.InitiatedBy = authored.InitiatedBy.Ref.NetworkId;      // ⭐ CE-2306 (DA-96)
            block.Order.ShootAndMove = authored.ShootAndMoveSeconds;           // ⭐ CE-7007
            block.Order.InPlace = authored.InPlace ? (byte)1 : (byte)0;        // ⭐ CE-8327
            block.State = default;
        }

        private static readonly int DefendHash = BehaviorHash.FromName(BehaviorNames.SquadDefend);

        /// <summary>⭐ <c>CE-2359</c> (AQ102 H; DA-55 at the squad level, as the platoon's <c>PlatoonDefendBehavior.NotDefending</c>) — the
        /// squad's task is NOT a defence. <c>BasicSquadSop</c>'s contact rows ask it first: a defending squad's own play answers a contact
        /// (its posts, displacement, flank turns, the hunter), and <c>ReactToContact</c> would pause it and assault out. 📐 Live
        /// (<c>rf-wall-hugger</c>): a grenade, then a contact, and two posted defenders were sent 50–60 m out of the house.</summary>
        [SharedAiCondition]
        public static bool NotDefending(Entity self, EntityRepository world)
            => Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, self).Kind != Hrot.AI.Behaviors.Orders.OrderKind.Defend   // ⭐ CE-4612 — the order's kind
               && !SupportByFireBehavior.Supporting(world, self)   // ⭐ CE-2436 — a squad supporting by fire is already fighting its contact
               && !CheckpointBehavior.Manning(world, self)          // ⭐ CE-4622 K-7 — a squad manning a checkpoint answers a vehicle by its own watch and ladder
               && !ReactToContactBehavior.PressesTheAttack(world, self);   // ⭐ CE-2467 — a superior squad on an attack order presses it: the drill answers the contact

        internal static SquadDefendParams Defaults(Vector3 position, float radius, float speed) => new()
        {
            Position     = position,
            SearchRadius = radius > 0f ? radius : 30f,
            Speed        = speed > 0f ? speed : 3f,
        };
[Behavior(BehaviorNames.SquadDefend, Params = typeof(global::Hrot.Map.Definitions.Behavior.SquadDefendParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Defend), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.SquadDefend, Curated = true, Group = "Library/Combat", ParamsType = typeof(SquadDefendBlackboard))]
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
        public const int CoverageSite = 0x22750006;   // ⭐ CE-2373 (D-2m)
        /// <summary>⭐ D-2m (DL-4) — a lone man's time on each round post once he reached it (s). ⭐ 30 s. 📐 12 s was tried (user `2026-11-20`, CE-3463:
        /// "window rotation drop from 30 s per window to about 12 s") and MEASURED WORSE: ia-area-clear 4/4 → 2/4 (House B never entered, Red 1 alive) and
        /// ia-area-defend 6/6 → 4/6 (the guard first fired at Blue East at 238 s, not 118 s) — a man who moves every 12 s is on the stairs more than at a
        /// window, so he meets an attacker less often, not more. Reverted the same day (Defend &amp; Attack DL-4).</summary>
        public const double RoundDwellSeconds = 30.0;
        /// <summary>⭐ D-2m — a round move he has not finished in this long counts as arrived (s).</summary>
        public const double RoundMoveSeconds = 60.0;
        public const float LongerViewMetres = 5f;

        /// <summary>Everyone within this of the position ⇒ the posts are asked for (m).</summary>
        public const float ArrivedMetres = 6f;
        /// <summary>The move ends after this long even if a man never arrives (s).</summary>
        public const float MoveTimeoutSeconds = 90f;
        /// <summary>A post query unanswered this long is given up (s): the men left without a post hold where they are.</summary>
        public const float AskTimeoutSeconds = 5f;
        /// <summary>DA-38 — this many rounds at a man within <see cref="UntenableSeconds"/> ⇒ his alternate post (⭐ CE-2283 — the sensor's own).</summary>
        public const int UntenableHits = SquadSenseChanges.UntenableRounds;
        public const float UntenableSeconds = SquadSenseChanges.UntenableSeconds;
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
        public const float FlankedMetres = SquadSenseChanges.FlankedMetres;
        /// <summary>⭐ <c>CE-2283</c> — a flank change this old is still answered (the contact may take a merge to stand where the rule sees it, s).</summary>
        public const float FlankedFreshSeconds = 2f;
        /// <summary>⭐ <c>CE-2359</c> (AQ102 H) — a known hostile this near the defended position (flat, m), seen by none of the squad for
        /// <see cref="HuntUnseenSeconds"/>, is hunted by ONE man for <see cref="HuntSeconds"/>.</summary>
        public const float HuntMetres = 30f;
        /// <summary>⭐ <c>CE-2371</c> (AQ104 D) — the defended area a known enemy is kept alerted within: at least this (m), else the posts' search radius.</summary>
        public const float AlertMinMetres = 15f;
        public const float HuntUnseenSeconds = 2f;
        public const float HuntSeconds = 20f;

        private static readonly SquadHsmShell s_shell = new(SquadDefendManeuver.BuildTransitionTable(), SquadDefendManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadDefendManeuver.ManeuverKind;
        public ushort StartPhase => SquadDefendManeuver.PhaseMoving;
        public ushort AbortPhase => SquadDefendManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            unsafe { for (int i = 0; i < DefencePosts.Capacity; i++) { p.Posts.PostOf[i] = DefencePosts.None; p.Posts.MovedAt[i] = double.NegativeInfinity; p.Posts.DwellFrom[i] = double.NegativeInfinity; } }
            p.Posts.Hunter = DefencePosts.None;   // ⭐ CE-2359
            if (!SquadManeuverRunner.AnyLive(in ctx)) return PlayStart.Fail;
            var march = MarchTo(in p);
            default(SquadMarchPlay).Start(ref march, ref ws, in ctx, partition);   // ⭐ D-13 — the move IN is the squad's march (a wedge on its lead)
            return PlayStart.Go;
        }

        public void Update(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            AreaAlert.KeepInArea(in ctx, p.Position, MathF.Max(p.SearchRadius, AlertMinMetres));   // ⭐ CE-2371 (AQ104 D) — an enemy in the area stays known until down
            LiftOwnHold(ref p, ref ws, in ctx);
            if (ctx.Squad.PhaseId == SquadDefendManeuver.PhaseMoving)
            {
                var march = MarchTo(in p);
                default(SquadMarchPlay).Update(ref march, ref ws, in ctx);   // ⭐ D-13 — keeps the wedge, re-slots, waits for stragglers
                return;
            }
            if (ctx.Squad.PhaseId != SquadDefendManeuver.PhasePosting) return;
            if (p.Posts.Ready == 0) { Plan(ref p, ref ws, in ctx); return; }
            Manned(ref p, in ctx);
            Displace(ref p, ref ws, in ctx);
            Flanked(ref p, ref ws, in ctx);
            Hunt(ref p, ref ws, in ctx);
            ShootAndMove(ref p, ref ws, in ctx);   // ⭐ CE-7007
            Round(ref p, ref ws, in ctx);
        }

        public NodeStatus Outcome(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => SquadManeuverRunner.AnyLive(in ctx) ? NodeStatus.Running : NodeStatus.Failure;

        public bool PhaseComplete(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (ctx.Squad.PhaseId != SquadDefendManeuver.PhaseMoving || ws.Reissue != 0) return false;
            if (ctx.Now - ws.IssuedAt >= MoveTimeoutSeconds) { ws.CompletedByTimeout = 1; return true; }
            if (p.InPlace != 0 && AlreadyInTheArea(in p, in ctx)) return true;   // ⭐ CE-8327 — on its position already: posts from where it stands, no march
            var march = MarchTo(in p);
            if (default(SquadMarchPlay).Outcome(ref march, ref ws, in ctx) == NodeStatus.Success) return true;   // ⭐ D-13 — the wedge is there
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (ctx.Live(i, out var m) && Flat(ctx.At(m) - p.Position) > ArrivedMetres) return false;
            return true;
        }

        /// <summary>⭐ <c>CE-8327</c> — applies to an IN-PLACE re-send only (<see cref="SquadDefendParams.InPlace"/>): every live man already stands inside the defended area (within its <c>SearchRadius</c>, at least
        /// <see cref="ArrivedMetres"/>): the squad is ON its position, so it takes posts from where it stands instead of marching in to the centre
        /// point (doctrine: a unit on its position occupies it; it does not leave its cover to form up). 📐 live village-street-fight: a flank turn
        /// re-sent Red Squad 1's SquadDefend (DA-60, by design — its posts face the new slice), the restart marched the posted defenders out
        /// of cover to the position's centre, in the street, where they were shot — the CE-2346 shape, from inside the play.</summary>
        private static bool AlreadyInTheArea(in SquadDefendParams p, in SquadPlayContext ctx)
        {
            float reach = MathF.Max(p.SearchRadius, ArrivedMetres);
            bool any = false;
            for (int i = 0; i < ctx.Roster.Count; i++)
            {
                if (!ctx.Live(i, out var m)) continue;
                any = true;
                if (Flat(ctx.At(m) - p.Position) > reach) return false;
            }
            return any;
        }

        public void BeginIssue(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        /// <summary>⭐ D-13 — the interval between the men of the march IN (m): the march's own default.</summary>
        public const float MarchSpacingMetres = 5f;

        /// <summary>⭐ D-13 — the march that carries the squad to its position: a wedge on its lead, arriving within half <see cref="ArrivedMetres"/>.</summary>
        public static SquadMarchParams MarchTo(in SquadDefendParams p) => new()
        {
            Objective = p.Position, Shape = SquadMarchManeuver.ShapeWedge, Spacing = MarchSpacingMetres,
            Speed = p.Speed > 0f ? p.Speed : 1.5f, ArrivalRadius = ArrivedMetres * 0.5f, MaxLagMetres = 10f,
        };

        public unsafe MemberOrder OrderFor(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            if (ctx.Squad.PhaseId == SquadDefendManeuver.PhaseMoving)
            {
                // ⭐⭐ D-13 (CE-4666, user-approved: dispersion) — the move IN is the squad's MARCH (SquadMarchPlay, CE-2123): the lead walks to the
                //   position, the others keep their WEDGE slot on him (MarchSpacingMetres), a stuck man walks in file on the lead's trail. 📌 live
                //   (pc-corridor-fight): two men sent to points 3.7 m apart still walked their 60 m side by side, 1–1.4 m apart — a spread DESTINATION
                //   does not space the TRAVEL. ⛔ Was: every man AdvanceTo p.Position (then: MovingPoint, a spread destination — superseded)
                var march = MarchTo(in p);
                var order = default(SquadMarchPlay).OrderFor(ref march, ref ws, in ctx, member, element, at);
                order.RoleId = SquadDefendManeuver.RoleMoving;
                return order;
            }
            bool hasEa = EngagementAreas.TryAllowPoint(ctx.World, ctx.Commander, out var ea);
            byte post = member < DefencePosts.Capacity ? p.Posts.PostOf[member] : DefencePosts.None;
            if (p.Posts.Ready == 0 || post == DefencePosts.None || post >= p.Posts.Count)
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadDefendManeuver.RolePosted,
                    Sector = hasEa && p.Posts.Ready != 0 ? MemberArc.Around(LookAt.BearingTo(at, ea), ArcHalfWidth) : default,
                };
            var spot = p.Posts.Point(post);
            if (member == p.Posts.Hunter)   // ⭐⭐ CE-2359 (AQ102 H) — the hunter fights from ANY opening of the building, facing the hunted
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Watch, Point = spot, Speed = p.Speed, RoleId = SquadDefendManeuver.RolePosted, Cover = 1,
                    Sector = MemberArc.Around(LookAt.BearingTo(spot, new Vector3(p.Posts.HuntX, p.Posts.HuntY, p.Posts.HuntZ)), ArcHalfWidth),
                    // his post widened to the squad's own window search: his window ask (which must SEE the threat) picks the opening
                    Post = new MemberPost { Set = 1, Window = 1, Radius = p.SearchRadius },
                };
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
                    Open = p.Posts.Open[post],   // ⭐ D-13 — a dispersal spot: no cover, he lies prone on it
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
            if (EngagementAreas.TryAllowPoint(ctx.World, ctx.Commander, out var ea)) return MemberArc.Around(LookAt.BearingTo(spot, ea), ArcHalfWidth);
            // ⭐⭐ CE-3463 — all-round at a FRAME post: out through his window (DA-79 "his body is turned to face out"). ⛔ "Outward from the
            //   centre" faced a window on the centre's side of its house INTO the house (📐 ia-area-defend: the guard at House A's south
            //   frame looked and could fire only north, through the rooms, while Blue South walked up the open ground to the south)
            if (post != DefencePosts.None && post < p.Posts.Count && p.Posts.Frame[post] != 0)
            {
                var mid = (p.Posts.FrameA(post) + p.Posts.FrameB(post)) * 0.5f;
                var opening = p.Posts.Opening(post);
                if (Flat(opening - mid) > 0.05f) return MemberArc.Around(LookAt.BearingTo(mid, opening), AllRoundHalfWidth);
            }
            return MemberArc.Around(LookAt.BearingTo(p.Position, spot), AllRoundHalfWidth);
        }

        // ── Posting: plan, manned, displace ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ <c>CE-8303</c> (D-P3 DP3-A) — the plan itself is <see cref="DefencePlan.Step{TCrew}"/> over this squad's roster (moved there
        /// unchanged: the authored posts, the window ask, the roof and cover asks for the men left without a post, the coverage or gun fan);
        /// once it is done, only rounds from now on count (DA-38) and the posts are given (a re-issue).</summary>
        private static void Plan(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var crew = new RosterCrew(in ctx);
            var ask = new DefenceAsk { Sensor = ws.Sensor, LastAnswerTick = ws.LastAnswerTick };
            bool ready = DefencePlan.Step(ref p.Posts, ref ask, in crew, in p.Authored, p.Position, p.SearchRadius);
            ws.Sensor = ask.Sensor; ws.LastAnswerTick = ask.LastAnswerTick;
            if (!ready) return;
            unsafe
            {
                for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)   // only rounds from now on count (DA-38)
                    p.Posts.LastSense[i] = ctx.Live(i, out var m) ? UntenableAt(ctx.World, m) : double.NegativeInfinity;
            }
            ws.Reissue = 1;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2373</c> (AQ104 L, DL-4) — a LONE man's guard round: <see cref="RoundDwellSeconds"/> on each round post once he is
        /// there, then the round post whose probes were watched longest ago — never while the squad knows an enemy in the area or hunts one
        /// (he fights from where he is). Marks the probes his post sees as watched while he is on it.
        /// </summary>
        private static unsafe void Round(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Posts.RoundCount < 2 || p.Posts.Hunter != DefencePosts.None) return;
            int man = -1, live = 0;
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
                if (ctx.Live(i, out _)) { live++; man = i; }
            if (live != 1) return;
            byte post = p.Posts.PostOf[man];
            if (post == DefencePosts.None) return;
            int current = -1;
            for (int r = 0; r < p.Posts.RoundCount; r++) if (p.Posts.Rank[r] == post) current = r;
            ctx.Live(man, out var who);
            bool there = Vector3.Distance(ctx.At(who), p.Posts.Point(post)) <= PostRadius || ctx.Now - p.Posts.MovedAt[man] >= RoundMoveSeconds;
            if (!there) return;
            if (p.Posts.RoundAt < p.Posts.MovedAt[man] || p.Posts.RoundAt == 0) p.Posts.RoundAt = (SimInstant)ctx.Now;   // arrived
            if (current >= 0)
                for (int q = 0; q < CoveragePosts.ProbeCount; q++)
                    if ((p.Posts.Sees[post] & (1u << q)) != 0) p.Posts.WatchedAt[q] = ctx.Now;
            if (ctx.Now - p.Posts.RoundAt < RoundDwellSeconds || EnemyInArea(ref p, in ctx)) return;
            Span<uint> roundSees = stackalloc uint[CoveragePosts.MaxRound];
            for (int r = 0; r < p.Posts.RoundCount; r++) roundSees[r] = p.Posts.Sees[p.Posts.Rank[r]];
            Span<double> watched = stackalloc double[CoveragePosts.ProbeCount];
            for (int q = 0; q < CoveragePosts.ProbeCount; q++) watched[q] = p.Posts.WatchedAt[q];
            int next = CoveragePosts.NextRound(roundSees[..p.Posts.RoundCount], current, watched, ctx.Now, p.Posts.RoundSince);
            if (next < 0) return;
            p.Posts.PostOf[man] = p.Posts.Rank[next];
            p.Posts.MovedAt[man] = ctx.Now;
            p.Posts.Reached[man] = 0;
            ws.Reissue = 1;
        }

        /// <summary>⭐ D-2m — the squad knows of someone in the area or its approaches (search radius + <see cref="CoveragePosts.ApproachMetres"/>):
        /// a heard, unidentified contact, or an identified one still in the fight and not neutral.</summary>
        private static bool EnemyInArea(ref SquadDefendParams p, in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            float reach = MathF.Max(p.SearchRadius, 5f) + CoveragePosts.ApproachMetres;
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                var where = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
                if (Flat(where - p.Position) > reach) continue;
                if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0 || Fighting(ctx.World, pool[c].EntityId)) return true;
            }
            return false;
        }

        /// <summary>⭐ <c>CE-2345</c> — which posts are COVER posts (one rule: <see cref="DefencePlan.CoverPosts"/>).</summary>
        private static void CoverPosts(ref SquadDefendParams p, Span<bool> spread) => DefencePlan.CoverPosts(ref p.Posts, spread);

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

        /// <summary>DA-38 — ⭐ <c>CE-2283</c> (SC-7): a <see cref="SensorChange.PositionUntenable"/> on a man (3 rounds in 10 s, or a friend down
        /// beside him — counted by <c>SquadSenseChanges</c>, no longer here) newer than the last one handled ⇒ his alternate post and PROGRESS
        /// Displacing; ⭐ CE-2304 (DA-56) — not while he is still moving to a post he was just sent to.</summary>
        private static unsafe void Displace(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                double last = UntenableAt(ctx.World, m);
                if (last <= p.Posts.LastSense[i]) continue;
                p.Posts.LastSense[i] = last;
                if (ctx.Now - p.Posts.MovedAt[i] < DisplacedGraceSeconds) continue;   // ⭐ CE-2304 (DA-56) — still moving to it
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
        /// ⭐⭐ <c>CE-7007</c> (City Battle §3a — active defence: "not just sitting in cover") — SHOOT AND MOVE: while the squad holds a contact,
        /// a man who has stood at his post for his dwell (<see cref="SquadDefendParams.ShootAndMove"/> × 1 – 1.6, seeded by his entity so
        /// the squad does not move as one) takes his alternate post and reports Displacing; a man with no alternate stays. The clock starts
        /// when he is at his post in contact, and again at each move. Off when <see cref="SquadDefendParams.ShootAndMove"/> is 0. Nothing here
        /// writes another entity: the post plan is the squad's own, the move an order the runner sends.
        /// </summary>
        private static unsafe void ShootAndMove(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.ShootAndMove <= 0f) return;
            bool contact = ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander).Contacts.Count > 0;
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None || i == p.Posts.Hunter) continue;
                if (!contact || p.Posts.Reached[i] == 0) { p.Posts.DwellFrom[i] = double.NegativeInfinity; continue; }
                if (double.IsNegativeInfinity(p.Posts.DwellFrom[i])) { p.Posts.DwellFrom[i] = ctx.Now; continue; }
                float spread = 1f + 0.6f * ((uint)m.Index * 2654435761u >> 24) / 255f;   // per man, fixed: 1 … 1.6
                if (ctx.Now - p.Posts.DwellFrom[i] < p.ShootAndMove * spread) continue;
                int alternate = AlternateOf(ref p, in ctx, i);
                if (alternate < 0) { p.Posts.DwellFrom[i] = ctx.Now; continue; }
                p.Posts.PostOf[i] = (byte)alternate;
                p.Posts.MovedAt[i] = ctx.Now;
                p.Posts.Reached[i] = 0;
                p.Posts.DwellFrom[i] = double.NegativeInfinity;
                ws.Reissue = 1;
                Report(in ctx, p.Posts.Point(alternate), ProgressKind.Displacing);
            }
        }

        /// <summary>
        /// ⭐ D-2c (DA-21 <c>ContactOutsideSector</c>, DA-4, DA-43) — ⭐ <c>CE-2283</c> (SC-7): a <see cref="SensorChange.ContactOutsideSector"/>
        /// on a posted man newer than his post ⇒ the contact that flanks him (the sensor's own rule, <see cref="SquadSenseChanges.Flanks"/>,
        /// against his arc) — he takes his alternate post (none ⇒ he stays) with his arc facing it, PROGRESS Displacing; once per contact.
        /// </summary>
        private static unsafe void Flanked(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                if (!RecentSensesOf.WithinSince(ctx.World, m, SensorChange.ContactOutsideSector, FlankedFreshSeconds, p.Posts.MovedAt[i])
                    || !EngagementAreas.TryArc(ctx.World, m, out var arc)) continue;
                for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
                {
                    if (pool[c].EntityId == p.Posts.Flanker[i] || !SquadSenseChanges.Flanks(ctx.World, in arc, in pool[c])) continue;
                    var at = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
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

        /// <summary>
        /// ⭐⭐ <c>CE-2359</c> (AQ102 H, R-2072) — a hostile the squad KNOWS of near the defended position (within <see cref="HuntMetres"/>)
        /// that none of its men has seen for <see cref="HuntUnseenSeconds"/> (under the wall, round the corner): ONE man — the posted man
        /// nearest him — fights from ANY opening of the building for <see cref="HuntSeconds"/> (his post widened to the squad's window search,
        /// so his own window ask, which must SEE the threat, picks the opening), then he goes back to his post. Everyone else keeps his
        /// sector; nobody leaves the building. Once per contact; it ends early when the contact is gone or out of the fight.
        /// </summary>
        private static unsafe void Hunt(ref SquadDefendParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Posts.Hunter != DefencePosts.None)
            {
                if (ctx.Now - p.Posts.HuntSince < HuntSeconds && ctx.Live(p.Posts.Hunter, out _) && Pooled(in ctx, p.Posts.Hunted, out _)) return;
                p.Posts.Hunter = DefencePosts.None;   // back to his post
                ws.Reissue = 1;
                return;
            }
            if (!Hunted(ref p, in ctx, out long id, out var at)) return;
            int hunter = -1;
            float best = float.MaxValue;
            for (int i = 0; i < ctx.Roster.Count && i < DefencePosts.Capacity; i++)
            {
                if (!ctx.Live(i, out var m) || p.Posts.PostOf[i] == DefencePosts.None) continue;
                // ⭐ CE-2376 (AQ104 C, DS-2) — a man who HOLDS stays on his post and lets him come (no temperament ⇒ as before)
                if (!Disposition.MaySearch(ctx.World, m, orderedAssault: false, Disposition.QuietSeconds(ctx.World, m, new Entity((ulong)id)))) continue;
                float d = Flat(ctx.At(m) - at);
                if (d < best) { best = d; hunter = i; }
            }
            if (hunter < 0) return;
            p.Posts.Hunter = (byte)hunter;
            p.Posts.Hunted = p.Posts.LastHunted = id;
            p.Posts.HuntSince = (SimInstant)ctx.Now;
            p.Posts.HuntX = at.X; p.Posts.HuntY = at.Y; p.Posts.HuntZ = at.Z;
            ws.Reissue = 1;
            Report(in ctx, at, ProgressKind.Displacing);
        }

        /// <summary>⭐ <c>CE-2359</c> — the contact to hunt: identified, hostile, in the fight, within <see cref="HuntMetres"/> of the position,
        /// unseen by the squad for <see cref="HuntUnseenSeconds"/>, and not the one last hunted.</summary>
        private static bool Hunted(ref SquadDefendParams p, in SquadPlayContext ctx, out long id, out Vector3 at)
        {
            id = 0; at = default;
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(ctx.World);   // the clock LastSeenTick is stamped in (CE-3292)
            uint unseen = (uint)(HuntUnseenSeconds * 60f);
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0 || pool[c].EntityId == p.Posts.LastHunted) continue;
                if (pool[c].LastSeenTick <= now && now - pool[c].LastSeenTick <= unseen) continue;   // somebody sees him: his sector's man fights him
                var where = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
                if (Flat(where - p.Position) > HuntMetres || !Fighting(ctx.World, pool[c].EntityId)) continue;
                id = pool[c].EntityId; at = where;
                return true;
            }
            return false;
        }

        /// <summary>⭐ <c>CE-2359</c> — <paramref name="id"/> is still in the squad's picture and still in the fight.</summary>
        private static bool Pooled(in SquadPlayContext ctx, long id, out Vector3 at)
        {
            at = default;
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
                if (pool[c].EntityId == id)
                {
                    at = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
                    return Fighting(ctx.World, id);
                }
            return false;
        }

        private static bool Fighting(EntityRepository world, long id)
        {
            var e = new Entity((ulong)id);
            return world.IsAlive(e) && !Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(world, e) && !Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(world, e);
        }

        private static unsafe int AlternateOf(ref SquadDefendParams p, in SquadPlayContext ctx, int man)
        {
            int n = Math.Min(ctx.Roster.Count, DefencePosts.Capacity);
            Span<Vector3> candidates = stackalloc Vector3[DefencePosts.Capacity];
            for (int k = 0; k < p.Posts.Count; k++) candidates[k] = p.Posts.Point(k);
            Span<int> postOf = stackalloc int[DefencePosts.Capacity];
            for (int i = 0; i < n; i++) postOf[i] = p.Posts.PostOf[i] == DefencePosts.None || !ctx.Live(i, out _) ? -1 : p.Posts.PostOf[i];
            Span<bool> spread = stackalloc bool[DefencePosts.Capacity];
            CoverPosts(ref p, spread);
            return DefencePostPicker.Alternate(candidates[..p.Posts.Count], postOf[..n], man, spread[..p.Posts.Count], DefencePostPicker.CoverSpacingShortMetres);
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
        private static bool Initiated(EntityRepository world, int charge)
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

        /// <summary>⭐ <c>CE-2283</c> — when the man's place was last untenable (his own senses), −∞ when never.</summary>
        private static double UntenableAt(EntityRepository world, Entity man)
            => world.HasComponent<RecentSenses>(man) && world.GetComponentRO<RecentSenses>(man).TryGetLast(SensorChange.PositionUntenable, out double at)
                ? at : double.NegativeInfinity;

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
            SimInstant now = (SimInstant)ctx.Now;
            ReportQueue.Enqueue(ctx.World, ctx.Commander, new CommandReport
            {
                Kind = ReportKind.Progress, Subtype = (byte)kind, At = at, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}
