using System;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2319</c> / <c>CE-2320</c> (AQ96 A, B, C, C3) — how a soldier moves under fire, as ONE reflex over the move every play issues
    /// (<see cref="LocomotionMoveTo.Issue"/> stamps <see cref="UnderFireMove"/>): the rush speed while threatened, the gait re-picked when the
    /// threat changes, and the DROP — a near miss or hit in the open puts him prone with the move halted; he rises when the fire on him lapses
    /// or his half is covered, after a side step (never up where he dropped), or crawls to a cover point near him first. The plays never see
    /// it: the channel stays Running (the move's executor ignores the reflex's intents, <see cref="UnderFireMove.HoldIntentId"/>).
    /// 📄 docs/blueprints/Architect_Question_96_Movement_Under_Fire.md §2a–§3.
    /// </summary>
    public static class UnderFire
    {
        /// <summary>A rush (m/s) — Squad Tactics §7.6 (3–5 s over 10–15 m); the crouched cap makes it 3.</summary>
        public const float RushSpeed = 4f;
        /// <summary>No near miss or hit for this long (s) ⇒ the fire on him has lapsed: he may rise.</summary>
        public const double LapseSeconds = 3.0;
        /// <summary>He does not drop again until he has been up this long (s) — one rush.</summary>
        public const double UpBeforeDropSeconds = 3.0;
        /// <summary>A near miss older than this (s) is not one to drop for.</summary>
        public const double FreshFireSeconds = 1.0;
        /// <summary>Within this of where the move ends (m) he is arriving at his cover, not out in the open.</summary>
        public const float ArrivingMetres = 3f;
        /// <summary>The side step before he rises (m).</summary>
        public const float ShiftMetres = 1.5f;
        /// <summary>A cover point this near (m) is worth crawling to while the fire goes on.</summary>
        public const float CrawlCoverMetres = 10f;
        /// <summary>A side step or a crawl gives up after this long (s).</summary>
        public const double ShiftSeconds = 3.0, CrawlSeconds = 12.0;
        /// <summary>⭐ C3 — how long one stamp of "your half is covered" lasts (s): the covering gate's own window.</summary>
        public const double CoveredSeconds = SquadGates.CoveringSeconds;
        /// <summary>The stance blend into and out of the drop (s).</summary>
        public const float DropBlendSeconds = 0.3f;

        /// <summary>True when <paramref name="gait"/> is a soldier's move the reflex keeps (Tactical or Crouched).</summary>
        public static bool Keeps(MoveGait gait) => gait == MoveGait.Tactical || gait == MoveGait.Crouched;

        /// <summary>
        /// Called by <see cref="LocomotionMoveTo.Issue"/> after it wrote the channel: stamps the move (or lets go of a move it does not keep).
        /// A man already DOWN who is handed a new move stays down — the system halts the new move's intent on its next pass.
        /// </summary>
        public static void Stamp(EntityRepository world, Entity self, uint actionInstanceId, MoveGait gait, float askedSpeed, bool threatened)
        {
            if (!world.IsComponentTypeRegistered<UnderFireMove>()) return;
            bool keeps = Keeps(gait) && StanceRequest.CanCarry(world, self);
            if (!world.HasComponent<UnderFireMove>(self))
            {
                if (!keeps) return;
                world.AddComponent(self, new UnderFireMove());
            }
            ref var u = ref world.GetComponentRW<UnderFireMove>(self);
            if (!keeps)
            {
                u.ActionInstanceId = 0;
                u.HoldIntentId = 0;
                u.Phase = UnderFirePhase.Moving;
                return;
            }
            u.ActionInstanceId = actionInstanceId;
            u.HoldIntentId = 0;   // the executor's OnEnter writes the move's own intent; a man down is re-halted by the system
            u.Gait = (byte)gait;
            u.AskedSpeed = askedSpeed;
            u.Threatened = threatened ? (byte)1 : (byte)0;
            if (u.Phase != UnderFirePhase.Moving) u.Phase = UnderFirePhase.Down;   // a side step or crawl in hand is dropped: down again
        }

        /// <summary>⭐ AQ96 C3 — the play that checks the covering gate tells a mover his half is covered (for <see cref="CoveredSeconds"/>).</summary>
        public static void MarkCovered(EntityRepository world, Entity member, double now)
        {
            if (!world.IsComponentTypeRegistered<UnderFireMove>() || !world.HasComponent<UnderFireMove>(member)) return;
            world.GetComponentRW<UnderFireMove>(member).CoveredUntil = now + CoveredSeconds;
        }

        /// <summary>The move speed for a threatened soldier: at least the rush.</summary>
        public static float Pace(float asked, bool threatened) => threatened ? MathF.Max(asked, RushSpeed) : asked;

        /// <summary>The time of the last near miss or hit on <paramref name="self"/> (−∞ when none).</summary>
        public static double LastFire(EntityRepository world, Entity self)
        {
            if (!world.IsComponentTypeRegistered<RecentSenses>() || !world.HasComponent<RecentSenses>(self)) return double.NegativeInfinity;
            ref readonly var r = ref world.GetComponentRO<RecentSenses>(self);
            double t = Math.Max(r.NearMiss, r.Hit);
            return t > 0 ? t : double.NegativeInfinity;
        }

        /// <summary>One frame of the reflex for <paramref name="self"/> (the system's body; public for the rails).</summary>
        public static unsafe void Step(EntityRepository world, Entity self, double now)
        {
            ref var u = ref world.GetComponentRW<UnderFireMove>(self);
            if (u.ActionInstanceId == 0 || !world.HasComponent<LocomotionChannel>(self)) return;
            ref readonly var ch = ref world.GetComponentRO<LocomotionChannel>(self);
            if (ch.ActiveAction != NavigationConstants.ActionIdMoveTo || ch.ActionInstanceId != u.ActionInstanceId || ch.Status != NodeStatus.Running)
            {
                // the play moved on (arrived, failed, another action): the reflex lets go — the next move re-stamps
                u.ActionInstanceId = 0;
                u.HoldIntentId = 0;
                if (u.Phase != UnderFirePhase.Moving) { u.Phase = UnderFirePhase.Moving; u.UpSince = now; }
                return;
            }
            if (!world.HasComponent<NavigationIntent>(self) || !world.HasComponent<SimTransform>(self)) return;
            MoveToParams move;
            fixed (byte* src = ch.Params) move = *(MoveToParams*)src;
            var gait = (MoveGait)u.Gait;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            bool threatened = TacticalGait.Threatened(world, self);
            double lastFire = LastFire(world, self);
            uint intentId = world.GetComponentRO<NavigationIntent>(self).IntentId;

            switch (u.Phase)
            {
                case UnderFirePhase.Moving:
                {
                    // ── C: the drop — a fresh near miss or hit, out in the open, not straight after he rose, not carrying a man ──
                    if (lastFire > u.ReactedFireAt && now - lastFire <= FreshFireSeconds && now - u.UpSince >= UpBeforeDropSeconds
                        && Flat(move.Destination - at).Length() > ArrivingMetres && CarryLoad.Factor(world, self) >= 1f)
                    {
                        u.ReactedFireAt = lastFire;
                        GoDown(world, self, ref u, now);
                        return;
                    }
                    // ── B + A: the threat changed mid-move ⇒ the gait and the pace follow it ──
                    byte t = threatened ? (byte)1 : (byte)0;
                    if (t != u.Threatened)
                    {
                        u.Threatened = t;
                        if (TacticalGait.StanceFor(world, self, gait) is StanceId stance)
                            StanceRequest.Set(world, self, stance, TacticalGait.BlendSeconds);
                        float pace = Pace(u.AskedSpeed, threatened);
                        if (MathF.Abs(pace - move.Speed) > 0.01f) Repace(world, self, ref move, pace);
                    }
                    return;
                }
                case UnderFirePhase.Down:
                {
                    if (intentId != u.HoldIntentId) u.HoldIntentId = MoveToExecutor.Halt(world, self);   // a new move was handed to a man down
                    if (lastFire > u.ReactedFireAt) u.ReactedFireAt = lastFire;
                    bool lapsed = now - lastFire >= LapseSeconds;
                    bool covered = u.CoveredUntil > now;
                    if (lapsed || covered)
                    {
                        u.SubTarget = SideStep(at, move.Destination, self);
                        Sub(world, self, ref u, in move, u.SubTarget, UnderFirePhase.Shifting, now);
                        return;
                    }
                    if (u.Crawled == 0 && CoverNear(world, self, at, out var cover))
                    {
                        u.Crawled = 1;
                        u.SubTarget = cover;
                        Sub(world, self, ref u, in move, cover, UnderFirePhase.Crawling, now);
                    }
                    return;   // still under fire, nothing near: he stays down (his play keeps firing where it fires on the move)
                }
                case UnderFirePhase.Shifting:
                case UnderFirePhase.Crawling:
                {
                    if (lastFire > u.ReactedFireAt) u.ReactedFireAt = lastFire;
                    bool there = Flat(u.SubTarget - at).Length() < 0.6f || SubEnded(world, self, u.HoldIntentId)
                                 || now - u.SubSince > (u.Phase == UnderFirePhase.Shifting ? ShiftSeconds : CrawlSeconds);
                    if (!there) return;
                    if (u.Phase == UnderFirePhase.Crawling)
                    {
                        u.HoldIntentId = MoveToExecutor.Halt(world, self);
                        u.Phase = UnderFirePhase.Down;
                        u.DownSince = now;
                        return;
                    }
                    Rise(world, self, ref u, ref move, now, threatened);
                    return;
                }
            }
        }

        private static void GoDown(EntityRepository world, Entity self, ref UnderFireMove u, double now)
        {
            StanceRequest.Set(world, self, StanceId.Prone, DropBlendSeconds);
            u.HoldIntentId = MoveToExecutor.Halt(world, self);
            u.Phase = UnderFirePhase.Down;
            u.DownSince = now;
            u.Crawled = 0;
        }

        private static void Sub(EntityRepository world, Entity self, ref UnderFireMove u, in MoveToParams move, Vector3 to, UnderFirePhase phase, double now)
        {
            var sub = move;
            sub.Destination = to;
            sub.ArrivalRadius = 0.5f;
            sub.Speed = RushSpeed;   // the prone cap makes it a crawl
            u.HoldIntentId = MoveToExecutor.WriteIntent(world, self, in sub);
            u.Phase = phase;
            u.SubSince = now;
        }

        private static void Rise(EntityRepository world, Entity self, ref UnderFireMove u, ref MoveToParams move, double now, bool threatened)
        {
            var gait = (MoveGait)u.Gait;
            StanceRequest.Set(world, self, gait == MoveGait.Crouched || threatened ? StanceId.Crouched : StanceId.Standing, DropBlendSeconds);
            u.Threatened = threatened ? (byte)1 : (byte)0;
            u.HoldIntentId = 0;
            u.Phase = UnderFirePhase.Moving;
            u.UpSince = now;
            u.CoveredUntil = 0;
            Repace(world, self, ref move, MathF.Max(Pace(u.AskedSpeed, threatened), RushSpeed));   // up and away: a rush
        }

        /// <summary>The move's own intent again, at <paramref name="speed"/> (a new intent id — the muscle re-plans once).</summary>
        private static unsafe void Repace(EntityRepository world, Entity self, ref MoveToParams move, float speed)
        {
            move.Speed = speed;
            ref var ch = ref world.GetComponentRW<LocomotionChannel>(self);
            fixed (byte* dst = ch.Params) *(MoveToParams*)dst = move;   // the move's params say what it now does; its instance is unchanged
            MoveToExecutor.WriteIntent(world, self, in move);
        }

        /// <summary>A side step of <see cref="ShiftMetres"/> across his line of advance (the side fixed per man).</summary>
        public static Vector3 SideStep(Vector3 at, Vector3 destination, Entity self)
        {
            var d = Flat(destination - at);
            d = d.LengthSquared() > 1e-4f ? Vector3.Normalize(d) : Vector3.UnitX;
            var side = new Vector3(-d.Y, d.X, 0f) * ((self.Index & 1) == 0 ? ShiftMetres : -ShiftMetres);
            return at + side;
        }

        /// <summary>The nearest cover point within <see cref="CrawlCoverMetres"/> that faces his nearest remembered threat (any, when he
        /// knows none), not where he lies.</summary>
        public static bool CoverNear(EntityRepository world, Entity self, Vector3 at, out Vector3 cover)
        {
            cover = default;
            if (!world.HasSingletonManaged<ICoverProvider>()) return false;
            var provider = world.GetSingletonManaged<ICoverProvider>();
            if (provider == null) return false;
            Span<CoverPoint> points = stackalloc CoverPoint[16];
            int n = provider.GetCoverPointsInRadius(new Vector2(at.X, at.Y), CrawlCoverMetres, points);
            bool threatKnown = NearestThreat(world, self, at, out var threat);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(points[i].PositionX, points[i].PositionY, points[i].PositionZ);
                var off = Flat(p - at);
                float d = off.Length();
                if (d < 1f || d >= best) continue;
                if (threatKnown)
                {
                    var toThreat = Flat(threat - p);
                    if (toThreat.X * points[i].DirectionX + toThreat.Y * points[i].DirectionY <= 0f) continue;   // it covers the other way
                }
                best = d;
                cover = p;
            }
            return best < float.MaxValue;
        }

        private static unsafe bool NearestThreat(EntityRepository world, Entity self, Vector3 at, out Vector3 threat)
        {
            threat = default;
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            float best = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                var p = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                float d = Flat(p - at).LengthSquared();
                if (d < best) { best = d; threat = p; }
            }
            return best < float.MaxValue;
        }

        private static bool SubEnded(EntityRepository world, Entity self, uint hold)
        {
            if (hold == 0 || !world.IsComponentTypeRegistered<NavigationStatus>() || !world.HasComponent<NavigationStatus>(self)) return false;
            ref readonly var s = ref world.GetComponentRO<NavigationStatus>(self);
            return s.IntentId == hold && s.Result != NavigationResult.InProgress && s.Result != NavigationResult.PathFound;
        }

        private static Vector3 Flat(Vector3 v) => new(v.X, v.Y, 0f);
    }

    /// <summary>
    /// ⭐ <c>CE-2319</c> / <c>CE-2320</c> (AQ96 §2c) — runs <see cref="UnderFire.Step"/> for every stamped move, on the brain host, AFTER the
    /// action dispatchers (so a move issued this frame already has its intent when the reflex halts it). Gated on the brain's authority.
    /// </summary>
    public sealed class UnderFireMoveSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _movers;

        public UnderFireMoveSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<UnderFireMove>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;
            _movers ??= repo.Query().With<UnderFireMove>().Build();
            foreach (var e in _movers)
            {
                if (_gateOnAuthority && !(repo.HasComponent<BehaviorState>(e) && repo.HasAuthority<BehaviorState>(e))) continue;
                UnderFire.Step(repo, e, now);
            }
        }
    }
}
