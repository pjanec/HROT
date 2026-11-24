using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Squad.Systems;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-8303</c> (D-P3 DP3-A, R-4286; 📄 docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §3) — WHO the defence plan posts:
    /// the crew <see cref="DefencePlan.Step{TCrew}"/> reads instead of a roster. Two implementations, both structs (a constrained generic, no
    /// boxing — R-2084): <see cref="RosterCrew"/> (the physical squad, its live men) and <see cref="SlotCrew"/> (a stochastic squad's slot table,
    /// every virtual man on his fallback point). Index <c>i</c> is the crew's own index (a roster slot / a unit slot) — the plan's
    /// <see cref="DefencePosts.PostOf"/> is per that index.
    /// </summary>
    public interface IDefenceCrew
    {
        EntityRepository World { get; }
        /// <summary>The entity that owns the asks' child sensors (EQS / survey) and whose engagement area aims them.</summary>
        Entity Carrier { get; }
        double Now { get; }
        /// <summary>Crew indices 0 … Count−1 (the plan reads at most <see cref="DefencePosts.Capacity"/>).</summary>
        int Count { get; }
        /// <summary>Is member <paramref name="i"/> in the fight?</summary>
        bool Live(int i);
        /// <summary>As <see cref="Live(int)"/>, and where he stands.</summary>
        bool Live(int i, out Vector3 at);
        /// <summary>Does member <paramref name="i"/> carry the automatic weapon (D-2h)?</summary>
        bool Support(int i);
        /// <summary>Slot 0 of the asks (the origin they are measured from); null ⇒ nothing to plan this frame.</summary>
        Entity Origin();
        /// <summary>The tier the window way is chosen by (D-2b, D-2j, D-2l).</summary>
        ProficiencyTier MedianTier();
        /// <summary>⭐ D-11 B — the point the posts FACE (a sector plan: the window / roof / cover asks and the gun fan aim at it); false ⇒ an
        /// all-round plan. A defence's is its engagement area (<see cref="EngagementAreas.TryAllowPoint"/>); a base of fire's, its reported posts.</summary>
        bool Facing(out Vector3 at);
        /// <summary>⭐ D-13 — does a man left WITHOUT a post get a dispersal spot (<see cref="DefencePlan.DispersalMetres"/> apart)? True for real men;
        /// ⛔ false for a stochastic squad's slots: their "no post" IS a designed place, the D-P5 fallback line the manager materialises (DP3).</summary>
        bool DispersesUnposted { get; }
    }

    /// <summary>⭐ <c>CE-8303</c> (DP3-A) — the PHYSICAL squad's crew: its roster, exactly what <see cref="SquadDefendPlay"/> read before the
    /// extract (live = in the fight with a body, the first live man the origin, the commander the carrier, the median live man's tier).</summary>
    public readonly struct RosterCrew : IDefenceCrew
    {
        private readonly SquadPlayContext _ctx;
        public RosterCrew(in SquadPlayContext ctx) { _ctx = ctx; }

        public EntityRepository World => _ctx.World;
        public Entity Carrier => _ctx.Commander;
        public double Now => _ctx.Now;
        public int Count => _ctx.Roster.Count;
        public bool Live(int i) => _ctx.Live(i, out _);
        public bool Live(int i, out Vector3 at)
        {
            bool live = _ctx.Live(i, out var m);
            at = live ? _ctx.At(m) : default;
            return live;
        }
        public bool Support(int i) => SquadRoles.CarriesSupportWeapon(SquadRoles.Of(_ctx.World, in _ctx.Roster, i));
        public Entity Origin() => SquadManeuverRunner.FirstLive(in _ctx);
        public bool Facing(out Vector3 at) => EngagementAreas.TryAllowPoint(_ctx.World, _ctx.Commander, out at);
        public bool DispersesUnposted => true;

        /// <summary>⭐ D-2b (DA-6, DA-16, DA-42) — the squad's median live man's tier.</summary>
        public ProficiencyTier MedianTier()
        {
            Span<Entity> men = stackalloc Entity[DefencePosts.Capacity];
            int n = 0;
            for (int i = 0; i < _ctx.Roster.Count && n < men.Length; i++)
                if (_ctx.Live(i, out var m)) men[n++] = m;
            return ProficiencyOf.MedianTier(_ctx.World, men[..n]);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8303</c> (DP3-A / DP3-B, R-4286) — a STOCHASTIC squad's crew: the unit entity is the carrier AND the origin (EQS §19.5: slot 0 may
    /// be any placed entity), its slot table the men — slot <c>i</c> live while <see cref="UnitSlotTable.IsUp"/>, standing on the
    /// <see cref="UnitSplits.FallbackPosts"/> point of its live rank (the order <c>Materialise</c> walks), the automatic weapon its
    /// <see cref="Fdp.Toolkit.Aggregate.SlotRole.MachineGunner"/>, the tier the unit's own proficiency (else Regular). ⚠ Squad echelon only (≤ 13 slots): the caller
    /// fills <paramref name="fallback"/> from the same table.
    /// </summary>
    public readonly struct SlotCrew : IDefenceCrew
    {
        /// <summary>The slots a squad's table may use (<see cref="UnitSlotTable.CapacityFor"/> of echelon 0).</summary>
        public const int MaxSlots = 13;

        private readonly EntityRepository _world;
        private readonly Entity _unit;
        private readonly double _now;
        private readonly UnitSlotTable _slots;   // a copy (≤ 968 B, on the stack) — the table may move while the plan runs
        private readonly Vector3[] _fallback;

        public SlotCrew(EntityRepository world, Entity unit, double now, in UnitSlotTable slots, Vector3[] fallback)
        {
            _world = world; _unit = unit; _now = now; _slots = slots; _fallback = fallback ?? Array.Empty<Vector3>();
        }

        public EntityRepository World => _world;
        public Entity Carrier => _unit;
        public double Now => _now;
        public int Count => Math.Min((int)_slots.Count, MaxSlots);
        public bool Live(int i) => i < Count && _slots.IsUp(i);
        public bool Live(int i, out Vector3 at)
        {
            at = default;
            if (!Live(i)) return false;
            int rank = 0;
            for (int j = 0; j < i; j++) if (_slots.IsUp(j)) rank++;
            if (rank < _fallback.Length) at = _fallback[rank];
            return true;
        }
        public bool Support(int i) => _slots.Slots[i].Role == Fdp.Toolkit.Aggregate.SlotRole.MachineGunner;
        public Entity Origin() => _unit;
        public ProficiencyTier MedianTier() => ProficiencyOf.Tier(_world, _unit);
        public bool Facing(out Vector3 at) => EngagementAreas.TryAllowPoint(_world, _unit, out at);
        public bool DispersesUnposted => false;   // ⭐ D-13 — the slots' no-post is the D-P5 fallback line (DP3)

    }

    /// <summary>⭐ <c>CE-8303</c> — the one outstanding EQS ask of a plan (what <see cref="SquadPlayState.Sensor"/> /
    /// <see cref="SquadPlayState.LastAnswerTick"/> hold for the physical play).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DefenceAsk
    {
        public EqsSensorHandle Sensor;
        public uint LastAnswerTick;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-8303</c> (D-P3 DP3-A, R-4286; 📄 docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §3 / §4) — THE squad defence's post
    /// plan, ONE implementation over an <see cref="IDefenceCrew"/>: the authored posts, the window ask (+ the frame survey), the roof ask and the
    /// cover ask for the men still without a post, the coverage fan (all-round) or the gun fan (sector). ⭐ The body of <c>SquadDefendPlay.Plan</c>
    /// and its helpers MOVED here unchanged (D-2a … D-2m, 📄 DESIGN_Defend_And_Attack_Position.md §8c) — only the roster became the crew and the
    /// commander the crew's carrier. Called every frame until it returns <c>true</c> (the posts are Ready).
    /// </summary>
    public static class DefencePlan
    {
        /// <summary>One step of the plan. True once the posts are given (<see cref="DefencePosts.Ready"/> = 1 this call).</summary>
        public static bool Step<TCrew>(ref DefencePosts posts, ref DefenceAsk ask, in TCrew crew, in AuthoredPosts authored, Vector3 position, float radius)
            where TCrew : struct, IDefenceCrew
        {
            bool hasEa = crew.Facing(out var ea);   // ⭐ D-11 B — the crew says where its posts face
            var from = crew.Origin();
            if (from.IsNull) return false;
            if (posts.AuthoredPlaced == 0)
            {
                // ⭐ D-2f (DA-5, DA-46) — the authored fighting positions first: the picker hands them out before any terrain point, and
                //   when every live man holds one the terrain is not asked at all
                posts.AuthoredPlaced = 1;
                for (int k = 0; k < authored.Count; k++)
                    unsafe { posts.AddAuthored(authored.Point(k), authored.Window[k] != 0, authored.Watch(k)); }
                if (posts.Count > 0) PickAll(ref posts, in crew);   // both asks below run only for the men still without a post
            }
            if (posts.WindowStage < 2 && AnyWithoutPost(ref posts, in crew))
            {
                if (posts.WindowStage == 0)
                {
                    posts.WindowStage = 1; posts.StageSince = (SimInstant)crew.Now;
                    var tier = crew.MedianTier();   // ⭐ D-2b (DA-42) — once, by the squad's median man
                    // ⭐ D-2l (DA-77, R-2059) — Regular or better fights a window from beside its frame; a Veteran keeps the set-back post
                    //   where the room is deep enough (D-2b / D-2j) and stands at the frame elsewhere. ⛔ SUPERSEDED: Regular set back (D-2b).
                    posts.SetBack = tier >= ProficiencyTier.Veteran ? (byte)1 : (byte)0;
                    posts.SetBackOnly = tier >= ProficiencyTier.Veteran ? (byte)1 : (byte)0;   // ⭐ D-2j (DA-16) — "set-back posts everywhere"
                    posts.FrameWanted = tier >= ProficiencyTier.Regular ? (byte)1 : (byte)0;
                    posts.WindowFrom = posts.Count;
                    if (posts.FrameWanted != 0)   // DA-78 — ONE survey of the position's buildings gives every window post its frame
                        WorldSurveyChildSensor.Ensure(crew.World, crew.Carrier, SquadDefendPlay.FrameSite,
                            WorldSurveyRequest.OfArea(new Vector2(position.X, position.Y), radius));
                }
                if (posts.WindowSettled == 0)
                {
                    bool back = posts.SetBack != 0;
                    bool answered = hasEa
                        ? SquadManeuverRunner.AskTerrainAll(crew.World, crew.Carrier, ref ask.Sensor, ref ask.LastAnswerTick,
                            back ? FindDefensiveWindowSetBackPosts.BlueprintId : FindDefensiveWindowPosts.BlueprintId,
                            SquadDefendPlay.WindowSite, radius, from, ea, out var buffer)
                        : SquadManeuverRunner.AskTerrainAll(crew.World, crew.Carrier, ref ask.Sensor, ref ask.LastAnswerTick,
                            back ? FindWindowSetBackPostsAllRound.BlueprintId : FindWindowPostsAllRound.BlueprintId,
                            SquadDefendPlay.WindowSite, radius, from, position, out buffer);
                    if (!Settle(ref posts, ref ask, in crew, answered, in buffer, window: true)) return false;
                    posts.WindowSettled = 1;
                }
                if (!Frames(ref posts, in crew, position, hasEa, ea)) return false;
                posts.WindowStage = 2;
                PickAll(ref posts, in crew);
            }
            if (posts.RoofStage < 2 && hasEa && AnyWithoutPost(ref posts, in crew))
            {
                // ⭐ D-2g (DA-5, DA-22, DA-47) — the roof, behind its parapet, for the men the windows did not take: points the cover ask
                //   never gives (📌 measured on OBJ: 22 roof points, none hidden from the sector)
                if (posts.RoofStage == 0) { posts.RoofStage = 1; posts.StageSince = (SimInstant)crew.Now; }
                bool answered = SquadManeuverRunner.AskTerrainAll(crew.World, crew.Carrier, ref ask.Sensor, ref ask.LastAnswerTick,
                    FindRoofPosts.BlueprintId, SquadDefendPlay.RoofSite, radius, from, ea, out var buffer);
                if (!Settle(ref posts, ref ask, in crew, answered, in buffer, window: false, roof: true)) return false;
                posts.RoofStage = 2;
                PickAll(ref posts, in crew);
            }
            if (posts.CoverStage < 2 && hasEa && AnyWithoutPost(ref posts, in crew))
            {
                if (posts.CoverStage == 0) { posts.CoverStage = 1; posts.StageSince = (SimInstant)crew.Now; }
                bool answered = SquadManeuverRunner.AskTerrainAll(crew.World, crew.Carrier, ref ask.Sensor, ref ask.LastAnswerTick,
                    FindCoverPosts.BlueprintId, SquadDefendPlay.CoverSite, radius, from, ea, out var buffer);   // ⭐ CE-2342 (R-2066 A)
                if (!Settle(ref posts, ref ask, in crew, answered, in buffer, window: false)) return false;
                posts.CoverStage = 2;
                PickAll(ref posts, in crew);
            }
            if (posts.CoverageStage < 2 && !hasEa && !Coverage(ref posts, in crew, position, radius)) return false;   // ⭐ CE-2373 (D-2m, DL-1) — all-round only
            if (posts.FanStage < 2 && !GunOnTheLongestView(ref posts, in crew, position, hasEa, ea)) return false;
            if (crew.DispersesUnposted) Disperse(ref posts, in crew, position, radius, hasEa, ea);   // ⭐ D-13 — a man the asks gave no post still never stands next to another
            posts.Ready = 1;
            posts.RoundSince = (SimInstant)crew.Now;
            return true;
        }

        /// <summary>⭐ D-13 (CE-4666, user-approved 2026-11-19) — the least distance between a dispersal spot and any other man's post (m): doctrine's
        /// 5–10 m interval between men in the open.</summary>
        public const float DispersalMetres = 5f;

        /// <summary>⭐ <c>CE-8322</c> — the line's own spots are exactly <see cref="DispersalMetres"/> apart; measured they read 4.99 and only every
        /// other spot was usable. This much short of the interval still counts as the interval (m).</summary>
        public const float SpacingTolerance = 0.05f;

        /// <summary>
        /// ⭐⭐ D-13 (CE-4666, user-approved 2026-11-19; Defend &amp; Attack §8c D-2n) — every live man the asks left WITHOUT a post (too few window /
        /// roof / cover points in reach) gets a DISPERSAL spot: on the line across the facing through the position (a ring round it when all-round),
        /// inside the radius, at least <see cref="DispersalMetres"/> from every other man's post or spot — the nearest such spot to where he stands.
        /// 📌 Live (`pc-corridor-fight`): the reserve's wait found cover for two men; the other two held where they stood, 1.6 m apart, and read as a
        /// knot. ⛔ Was: no post ⇒ <c>Hold</c> where he stands. No candidate left (a tiny radius, a full table) ⇒ he keeps no post, as before.
        /// Allocation-free (stack spans).
        /// </summary>
        private static unsafe void Disperse<TCrew>(ref DefencePosts posts, in TCrew crew, Vector3 position, float radius, bool hasEa, Vector3 facing)
            where TCrew : struct, IDefenceCrew
        {
            int n = Math.Min(crew.Count, DefencePosts.Capacity);
            Span<Vector3> taken = stackalloc Vector3[DefencePosts.Capacity];
            int t = 0;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (!crew.Live(i)) continue;
                if (posts.PostOf[i] != DefencePosts.None && posts.PostOf[i] < posts.Count) taken[t++] = posts.Point(posts.PostOf[i]);
                else any = true;
            }
            if (!any) return;
            // the candidates: along the line across the facing (0, ±5, ±10 …), or two rings round the point when all-round
            Span<Vector3> cand = stackalloc Vector3[16];
            int c = 0;
            var f = new Vector2(facing.X - position.X, facing.Y - position.Y);
            if (hasEa && f.LengthSquared() > 1e-4f)
            {
                f = Vector2.Normalize(f);
                var across = new Vector2(-f.Y, f.X);
                for (int k = 0; c < cand.Length; k++)
                {
                    float off = (k + 1) / 2 * DispersalMetres * (k % 2 == 0 ? 1f : -1f);
                    if (MathF.Abs(off) > radius) break;
                    cand[c++] = position + new Vector3(across * off, 0f);
                }
            }
            else
            {
                cand[c++] = position;
                for (int ring = 1; ring <= 2 && ring * DispersalMetres <= radius; ring++)
                    for (int k = 0; k < 6 && c < cand.Length; k++)
                    {
                        float a = (k + ring * 0.5f) * MathF.PI / 3f;
                        cand[c++] = position + new Vector3(MathF.Cos(a), MathF.Sin(a), 0f) * (ring * DispersalMetres);
                    }
            }
            // ⭐⭐ CE-8322 — DEFILADE first: a spot the facing (the enemy's expected position) cannot see is taken before one it can — doctrine's
            //   "out of the enemy's view". 📐 live village-street-mg-flank: the line across the facing ran over the open street, and a man waited
            //   prone IN it for 75 s in the gun's field of fire. Judged from the facing's eye to a crouched man's middle; no world ⇒ every spot alike.
            Span<bool> hidden = stackalloc bool[16];
            var query = Fdp.Toolkit.World.WorldQuery.Of(crew.World);
            if (hasEa && query != null)
            {
                float zHint = position.Z;
                if (zHint == 0f) for (int i = 0; i < n; i++) if (crew.Live(i, out var anyAt)) { zHint = anyAt.Z; break; }
                var from = Grounded(query, facing, zHint) + new Vector3(0f, 0f, DefiladeEyeMetres);
                for (int k = 0; k < c; k++)
                    hidden[k] = query.SightBlocked(from, Grounded(query, cand[k], zHint) + new Vector3(0f, 0f, DefiladeBodyMetres));
            }
            for (int i = 0; i < n && posts.Count < DefencePosts.Capacity; i++)
            {
                if (posts.PostOf[i] != DefencePosts.None && posts.PostOf[i] < posts.Count) continue;
                if (!crew.Live(i, out var at)) continue;
                int best = -1; float bestD = float.MaxValue; bool bestHidden = false;
                for (int k = 0; k < c; k++)
                {
                    bool free = true;
                    for (int j = 0; j < t && free; j++)
                        if (new Vector2(cand[k].X - taken[j].X, cand[k].Y - taken[j].Y).Length() < DispersalMetres - SpacingTolerance) free = false;   // ⭐ CE-8322 — spots ON the 5 m line are 5 m apart (float: 4.99)
                    if (!free) continue;
                    float d = new Vector2(cand[k].X - at.X, cand[k].Y - at.Y).Length();
                    if ((hidden[k] && !bestHidden) || (hidden[k] == bestHidden && d < bestD)) { bestD = d; best = k; bestHidden = hidden[k]; }
                }
                if (best < 0) continue;
                var spot = new Vector3(cand[best].X, cand[best].Y, at.Z);
                posts.Add(spot);
                posts.Open[posts.Count - 1] = 1;   // ⭐ D-13 (the prone half) — no cover there: he holds it prone
                posts.PostOf[i] = (byte)(posts.Count - 1);
                taken[t++] = spot;
            }
        }

        /// <summary>⭐ <c>CE-8322</c> — the defilade test's heights: the enemy's eye over his ground, a crouched man's middle over the spot's (m).</summary>
        public const float DefiladeEyeMetres = 1.6f, DefiladeBodyMetres = 0.9f;

        /// <summary>⭐ <c>CE-8322</c> — a 2-D point (Z 0) on the ground near <paramref name="zHint"/>; a point with a height as given.</summary>
        private static Vector3 Grounded(Fdp.Toolkit.World.IWorldQuery query, Vector3 p, float zHint)
            => p.Z != 0f ? p : query.TryStandAt(p.X, p.Y, zHint, out float z) ? p with { Z = z } : p with { Z = zHint };

        /// <summary>The asks' child sensors of <paramref name="carrier"/> go (a plan abandoned mid-stage).</summary>
        public static void Abandon(EntityRepository world, Entity carrier, ref DefenceAsk ask)
        {
            SquadManeuverRunner.DropSensor(ref ask.Sensor, world);
            ReleaseSurvey(world, carrier, SquadDefendPlay.FrameSite);
            ReleaseSurvey(world, carrier, SquadDefendPlay.FanSite);
            ReleaseSurvey(world, carrier, SquadDefendPlay.CoverageSite);
        }

        private static void ReleaseSurvey(EntityRepository world, Entity carrier, int site)
        {
            var child = EqsChildSensor.Find(world, carrier, site);
            if (!child.IsNull) WorldSurveyChildSensor.Release(world, child);
        }

        /// <summary>
        /// ⭐ D-2h (DA-19, DA-25, DA-48) — the squad's automatic weapon (AR / MG) on the post with the longest view down the sector: one
        /// visibility fan from every held non-roof post toward the EA point; a post's view is the mean free distance of its rays; the gun
        /// swaps posts with the holder of a view longer by <see cref="SquadDefendPlay.LongerViewMetres"/> (never with another gun). True once
        /// the stage is over — answered, nothing to compare, or timed out (posts as picked). ⛔ Roof posts are not origins: a horizontal ray over
        /// a parapet measures the sky, not grazing fire. 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2h.
        /// </summary>
        private static unsafe bool GunOnTheLongestView<TCrew>(ref DefencePosts posts, in TCrew crew, Vector3 position, bool hasEa, Vector3 ea)
            where TCrew : struct, IDefenceCrew
        {
            int gun = -1, n = 0;
            Span<int> held = stackalloc int[WorldSurveyRequest.MaxOrigins];
            for (int i = 0; i < crew.Count && i < DefencePosts.Capacity; i++)
            {
                byte post = posts.PostOf[i];
                if (!crew.Live(i) || post == DefencePosts.None || posts.Roof[post] != 0) continue;
                if (gun < 0 && crew.Support(i)) gun = i;
                if (n < held.Length) held[n++] = post;
            }
            int gunOrigin = gun < 0 ? -1 : held[..n].IndexOf(posts.PostOf[gun]);
            if (!hasEa || gunOrigin < 0 || n < 2) { posts.FanStage = 2; return true; }
            if (posts.FanStage == 0)
            {
                var origins = new List<SurveyOrigin>(n);
                for (int k = 0; k < n; k++)
                {
                    var at = posts.Point(held[k]);
                    origins.Add(new SurveyOrigin { X = at.X, Y = at.Y, Z = at.Z, Bearing = MathF.Atan2(ea.Y - at.Y, ea.X - at.X), HalfArc = MathF.PI / 4f });
                }
                float range = Math.Clamp(Flat(ea - position) * 1.5f, 50f, 300f);
                WorldSurveyChildSensor.Ensure(crew.World, crew.Carrier, SquadDefendPlay.FanSite, WorldSurveyRequest.Fan(origins, SquadDefendPlay.FanRays, range));
                posts.FanStage = 1; posts.StageSince = (SimInstant)crew.Now;
                return false;
            }
            var child = EqsChildSensor.Find(crew.World, crew.Carrier, SquadDefendPlay.FanSite);
            var read = WorldSurveyChildSensor.Read(crew.World, child, crew.Now);
            if (!read.Ready || read.Stale || read.Payload == null)
            {
                if (crew.Now - posts.StageSince < SquadDefendPlay.AskTimeoutSeconds) return false;
                WorldSurveyChildSensor.Release(crew.World, child);
                posts.FanStage = 2;
                return true;
            }
            Span<float> view = stackalloc float[WorldSurveyRequest.MaxOrigins];
            Span<int> rays = stackalloc int[WorldSurveyRequest.MaxOrigins];
            foreach (var ray in read.Payload.Rays)
                if (ray.Origin >= 0 && ray.Origin < n) { view[ray.Origin] += ray.Free; rays[ray.Origin]++; }
            for (int k = 0; k < n; k++) view[k] = rays[k] == 0 ? 0f : view[k] / rays[k];   // the mean free distance of its rays
            int best = gunOrigin;
            for (int k = 0; k < n; k++) if (view[k] > view[best]) best = k;
            if (best != gunOrigin && view[best] > view[gunOrigin] + SquadDefendPlay.LongerViewMetres)
                for (int i = 0; i < crew.Count && i < DefencePosts.Capacity; i++)
                {
                    if (i == gun || !crew.Live(i) || posts.PostOf[i] != held[best]) continue;
                    if (crew.Support(i)) break;   // never displace another gun
                    (posts.PostOf[gun], posts.PostOf[i]) = (posts.PostOf[i], posts.PostOf[gun]);
                    break;
                }
            WorldSurveyChildSensor.Release(crew.World, child);
            posts.FanStage = 2;
            return true;
        }

        /// <summary>True once the ask is over: its points kept (append), or it timed out (the sensor goes).</summary>
        private static bool Settle<TCrew>(ref DefencePosts posts, ref DefenceAsk ask, in TCrew crew, bool answered, in EqsCognitiveBuffer buffer, bool window, bool roof = false)
            where TCrew : struct, IDefenceCrew
        {
            if (!answered)
            {
                if (crew.Now - posts.StageSince < SquadDefendPlay.AskTimeoutSeconds) return false;
                SquadManeuverRunner.DropSensor(ref ask.Sensor, crew.World);
                return true;
            }
            var points = buffer.GetSpanRO();
            for (int k = 0; k < buffer.Count && k < points.Length; k++)
            {
                // ⭐ D-2j (DA-16, DA-50) — a Veteran squad keeps only the windows its set-back pass really moved back from the sill;
                //   ⭐ D-2l (DA-77) — the others wait for the frame pass, which keeps those that have a frame (Frames)
                bool shallow = window && posts.SetBackOnly != 0 && (points[k].FlagsMeaningful & CoverPointsGenerator.FlagSetBack) != 0
                    && (points[k].Flags & CoverPointsGenerator.FlagSetBack) == 0;
                if (shallow && posts.FrameWanted == 0) continue;
                int added = posts.Count;
                posts.Add(new Vector3(points[k].PositionX, points[k].PositionY, points[k].PositionZ), window, roof);
                if (shallow && posts.Count > added) unsafe { posts.Shallow[added] = 1; }
            }
            return true;
        }

        /// <summary>
        /// ⭐ D-2l (DA-78, DA-84; 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2l) — the frame pass over the window ask's points: each one
        /// that is fought from the frame (every one at Regular; a Veteran's shallow ones) and has a window within
        /// <see cref="WindowFrame.MatchMetres"/> in the survey becomes a FRAME post at the frame point away from the engagement area
        /// (<see cref="WindowFrame.Facing"/>); a Veteran's shallow window with no frame is no post (D-2j); the rest stay as they were.
        /// True once the survey answered or timed out (<see cref="SquadDefendPlay.AskTimeoutSeconds"/> from the window stage) — then the survey goes.
        /// </summary>
        private static unsafe bool Frames<TCrew>(ref DefencePosts posts, in TCrew crew, Vector3 position, bool hasEa, Vector3 ea)
            where TCrew : struct, IDefenceCrew
        {
            if (posts.FrameWanted == 0) return true;
            var child = EqsChildSensor.Find(crew.World, crew.Carrier, SquadDefendPlay.FrameSite);
            var read = WorldSurveyChildSensor.Read(crew.World, child, crew.Now);
            bool ready = read.Ready && !read.Stale && read.Payload != null;
            if (!ready && crew.Now - posts.StageSince < SquadDefendPlay.AskTimeoutSeconds) return false;
            var answer = ready ? read.Payload : null;
            int keep = posts.WindowFrom;
            for (int k = posts.WindowFrom; k < posts.Count; k++)
            {
                bool shallow = posts.Shallow[k] != 0;
                var at = posts.Point(k);
                Vector3 a = default, b = default, opening = default;
                bool framed = (posts.SetBack == 0 || shallow) && WindowFrame.FromSurvey(answer, at, out a, out b, out opening);
                if (shallow && !framed) continue;   // DA-84 / D-2j — a Veteran's shallow window without a frame is no post
                if (framed)
                {
                    var target = hasEa ? ea : opening + (opening - position);   // all-round: straight out
                    at = WindowFrame.Facing(a, b, opening, target);
                    posts.FrameAX[keep] = a.X; posts.FrameAY[keep] = a.Y;
                    posts.FrameBX[keep] = b.X; posts.FrameBY[keep] = b.Y;
                    posts.OpeningX[keep] = opening.X; posts.OpeningY[keep] = opening.Y;
                }
                posts.X[keep] = at.X; posts.Y[keep] = at.Y; posts.Z[keep] = at.Z;
                posts.Window[keep] = 1;
                posts.Authored[keep] = posts.Roof[keep] = posts.Shallow[keep] = posts.Open[keep] = 0;
                posts.WatchX[keep] = posts.WatchY[keep] = posts.WatchZ[keep] = 0f;
                posts.Frame[keep] = framed ? (byte)1 : (byte)0;
                keep++;
            }
            posts.Count = (byte)keep;
            WorldSurveyChildSensor.Release(crew.World, child);
            return true;
        }

        private static unsafe void PickAll<TCrew>(ref DefencePosts posts, in TCrew crew)
            where TCrew : struct, IDefenceCrew
        {
            int n = Math.Min(crew.Count, DefencePosts.Capacity);
            int count = posts.Count;
            // ⭐ CE-2373 (D-2m) — once the coverage pass ranked them, the picker gets the candidates in THAT order (the views' greedy union),
            //   the unranked rest after them in the ask's order; slot j of the picker is candidate order[j]
            Span<int> order = stackalloc int[DefencePosts.Capacity];
            Span<int> slotOf = stackalloc int[DefencePosts.Capacity];
            int ranked = Math.Min(posts.RankCount, count), next = 0;
            for (int r = 0; r < ranked; r++) order[next++] = posts.Rank[r];
            for (int k = 0; k < count; k++)
            {
                bool inRank = false;
                for (int r = 0; r < ranked; r++) inRank |= posts.Rank[r] == k;
                if (!inRank) order[next++] = k;
            }
            for (int j = 0; j < count; j++) slotOf[order[j]] = j;
            Span<Vector3> candidates = stackalloc Vector3[DefencePosts.Capacity];
            for (int j = 0; j < count; j++) candidates[j] = posts.Point(order[j]);
            Span<Vector3> men = stackalloc Vector3[DefencePosts.Capacity];
            Span<bool> live = stackalloc bool[DefencePosts.Capacity];
            Span<int> postOf = stackalloc int[DefencePosts.Capacity];
            for (int i = 0; i < n; i++)
            {
                live[i] = crew.Live(i, out var at);
                men[i] = at;
                postOf[i] = posts.PostOf[i] == DefencePosts.None || posts.PostOf[i] >= count ? -1 : slotOf[posts.PostOf[i]];
            }
            Span<bool> cover = stackalloc bool[DefencePosts.Capacity];
            CoverPosts(ref posts, cover);
            Span<bool> spread = stackalloc bool[DefencePosts.Capacity];
            for (int j = 0; j < count; j++) spread[j] = cover[order[j]];
            // ⭐ CE-2345 (R-2066 B) — cover posts 8 m apart; the men still without one then take them 5 m apart, never closer
            DefencePostPicker.Pick(candidates[..count], men[..n], live[..n], postOf[..n], spread[..count], DefencePostPicker.CoverSpacingMetres);
            DefencePostPicker.Pick(candidates[..count], men[..n], live[..n], postOf[..n], spread[..count], DefencePostPicker.CoverSpacingShortMetres);
            for (int i = 0; i < n; i++) posts.PostOf[i] = postOf[i] < 0 ? DefencePosts.None : (byte)order[postOf[i]];
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2373</c> (AQ104 L, D-2m) — the all-round defence's posts for COVERAGE: one visibility fan from up to
        /// <see cref="WorldSurveyRequest.MaxOrigins"/> candidates, each scored by the probes of the area it sees (<see cref="CoveragePosts"/>);
        /// the candidates are ranked by the greedy union of their views, the posts given again in that order, and the first few that each add
        /// enough are a lone man's round (DL-4). True once over — answered, nothing to rank, or timed out (DL-5: the ask's own order).
        /// </summary>
        private static unsafe bool Coverage<TCrew>(ref DefencePosts posts, in TCrew crew, Vector3 position, float radius)
            where TCrew : struct, IDefenceCrew
        {
            int m = Math.Min((int)posts.Count, WorldSurveyRequest.MaxOrigins);
            if (m < 2) { posts.CoverageStage = 2; return true; }
            if (posts.CoverageStage == 0)
            {
                var origins = new List<SurveyOrigin>(m);
                float farthest = 0f;
                for (int k = 0; k < m; k++)
                {
                    var at = CoverageEye(ref posts, k);   // ⭐ CE-8312 — where he WATCHES from (a frame post's opening), not where he hides
                    farthest = MathF.Max(farthest, Flat(at - position));
                    origins.Add(new SurveyOrigin { X = at.X, Y = at.Y, Z = at.Z, Bearing = 0f, HalfArc = CoveragePosts.HalfArc });
                }
                WorldSurveyChildSensor.Ensure(crew.World, crew.Carrier, SquadDefendPlay.CoverageSite,
                    WorldSurveyRequest.Fan(origins, CoveragePosts.Rays, CoveragePosts.Range(position, radius, farthest)));
                posts.CoverageStage = 1; posts.StageSince = (SimInstant)crew.Now;
                return false;
            }
            var child = EqsChildSensor.Find(crew.World, crew.Carrier, SquadDefendPlay.CoverageSite);
            var read = WorldSurveyChildSensor.Read(crew.World, child, crew.Now);
            if (!read.Ready || read.Stale || read.Payload == null)
            {
                if (crew.Now - posts.StageSince < SquadDefendPlay.AskTimeoutSeconds) return false;
                WorldSurveyChildSensor.Release(crew.World, child);
                posts.CoverageStage = 2;
                return true;
            }
            Span<Vector3> candidates = stackalloc Vector3[WorldSurveyRequest.MaxOrigins];
            for (int k = 0; k < m; k++) candidates[k] = CoverageEye(ref posts, k);   // ⭐ CE-8312 — the same eye as the fan's origins
            Span<Vector3> probes = stackalloc Vector3[CoveragePosts.ProbeCount];
            CoveragePosts.Probes(position, radius, probes);
            Span<uint> sees = stackalloc uint[WorldSurveyRequest.MaxOrigins];
            CoveragePosts.Seen(candidates[..m], read.Payload.Rays, probes, sees[..m]);
            Span<int> order = stackalloc int[WorldSurveyRequest.MaxOrigins];
            int round = CoveragePosts.Greedy(sees[..m], order[..m]);
            for (int k = 0; k < m; k++) { posts.Rank[k] = (byte)order[k]; posts.Sees[k] = sees[k]; }
            posts.RankCount = (byte)m;
            posts.RoundCount = (byte)round;
            for (int i = 0; i < DefencePosts.Capacity; i++) posts.PostOf[i] = DefencePosts.None;   // given again, in coverage order
            PickAll(ref posts, in crew);
            WorldSurveyChildSensor.Release(crew.World, child);
            posts.CoverageStage = 2;
            return true;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-8312</c> — where a post WATCHES from, for coverage: a FRAME post's opening (he leans out of it to watch and to fire, D-2l DA-80) at
        /// the post's height; any other post its own point. 📐 live <c>ia-area-defend</c>: the frame pass runs BEFORE coverage, so every Regular's
        /// window post was a frame point 0.5 m inside the wall beside its opening — the coverage fan looked from there through the WALL, every window
        /// saw almost nothing, and the lone guard's round was two posts on the south wall: Blue East walked in from the east unseen. ⛔ Was: the post point.
        /// </summary>
        internal static unsafe Vector3 CoverageEye(ref DefencePosts posts, int k)
            => posts.Frame[k] != 0 ? new Vector3(posts.OpeningX[k], posts.OpeningY[k], posts.Z[k]) : posts.Point(k);

        /// <summary>⭐ <c>CE-2345</c> — which posts are COVER posts (not a window, the roof, or authored): those are spread wider.</summary>
        internal static unsafe void CoverPosts(ref DefencePosts posts, Span<bool> spread)
        {
            for (int k = 0; k < posts.Count; k++)
                spread[k] = posts.Window[k] == 0 && posts.Roof[k] == 0 && posts.Authored[k] == 0;
        }

        private static unsafe bool AnyWithoutPost<TCrew>(ref DefencePosts posts, in TCrew crew)
            where TCrew : struct, IDefenceCrew
        {
            for (int i = 0; i < crew.Count && i < DefencePosts.Capacity; i++)
                if (crew.Live(i) && posts.PostOf[i] == DefencePosts.None) return true;
            return false;
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}
