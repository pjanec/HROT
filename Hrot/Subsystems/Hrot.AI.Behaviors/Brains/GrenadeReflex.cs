using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2360</c> — one live fragmentation grenade lying on this node: where, how far it kills, the fuze it has left (−1 =
    /// not known here: a replica), who threw it.</summary>
    public readonly record struct LiveGrenade(Entity Round, Vector3 At, long Ammo, float LethalMetres, float FuzeLeft, Vector3 From);

    /// <summary>
    /// ⭐⭐ <c>CE-2360</c> (AQ102 I-2, I-3, R-2072; 🔒 user <c>2026-10-23</c>: <i>"everyone who see it or hear it should be taking cover,
    /// meaning they should run away and lay down with heads far from the grenade if they cannot run far enough. And as a special counter
    /// reaction is to pick the grenade and throw it back … if the grenade fuse is still expected to last some time"</i>) — the danger of a
    /// lying live grenade, pure. A man within <see cref="DangerShare"/>× its lethal radius has HEARD it land (≤ <see cref="HeardMetres"/>);
    /// the nearest trained man with room in his hands and fuze enough throws it back, every other runs if he gets clear in time, else drops.
    /// 📄 docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md §3.3, §4 I.
    /// </summary>
    public static class GrenadeDanger
    {
        /// <summary>Within this many times its lethal radius a man reacts.</summary>
        public const float DangerShare = 2f;
        /// <summary>A grenade landing this near is heard (m).</summary>
        public const float HeardMetres = 15f;
        /// <summary>His run (m/s), flat out.</summary>
        public const float RunSpeed = 5f;
        /// <summary>How long before he moves (s).</summary>
        public const float ReactSeconds = 0.4f;
        /// <summary>The throw back: reaching down for it (s) · the throw (s) · what is left to spare (s).</summary>
        public const float PickSeconds = 1.0f, ThrowSeconds = 0.8f, SpareSeconds = 0.6f;
        /// <summary>How far past the lethal radius he runs (m).</summary>
        public const float ClearMetres = 2f;
        /// <summary>A replica's fuze is not known here: he assumes this much is left (s) — the run must be short to be worth it.</summary>
        public const float UnknownFuzeSeconds = 1.5f;

        /// <summary>The live fragmentation grenades lying on this node (the rounds it flies: their fuze is known).</summary>
        public static void Lying(EntityRepository world, List<LiveGrenade> into)
        {
            into.Clear();
            if (!world.IsComponentTypeRegistered<BallisticProjectile>()) return;
            var db = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
            foreach (var e in world.Query().With<BallisticProjectile>().With<SimTransform>().Build())
            {
                ref readonly var flight = ref world.GetComponentRO<BallisticProjectile>(e);
                const byte live = WarheadRound.TimeFuze | WarheadRound.Landed;
                if ((flight.Warhead & (live | WarheadRound.Spent | WarheadRound.Lingers)) != live) continue;
                var (warhead, _, _) = ParameterResolver.Warhead(db, flight.Ammo);
                if (warhead == null || warhead.FragmentRadiusM <= 0f) continue;
                var from = !flight.Shooter.IsNull && world.IsAlive(flight.Shooter) && world.HasComponent<SimTransform>(flight.Shooter)
                    ? world.GetComponentRO<SimTransform>(flight.Shooter).Position : flight.Muzzle;
                into.Add(new LiveGrenade(e, world.GetComponentRO<SimTransform>(e).Position, flight.Ammo,
                    warhead.FragmentRadiusM * GrenadeCheck.LethalShareOfReach, flight.FuzeRemaining, from));
            }
        }

        /// <summary>Is a man <paramref name="distance"/> m from <paramref name="g"/> in danger from it (and has he heard it)?</summary>
        public static bool Endangers(in LiveGrenade g, float distance)
            => distance <= g.LethalMetres * DangerShare && distance <= HeardMetres;

        /// <summary>The fuze he must have left to throw it back from <paramref name="distance"/> m (s).</summary>
        public static float ThrowBackNeeds(float distance) => ReactSeconds + distance / RunSpeed + PickSeconds + ThrowSeconds + SpareSeconds;

        /// <summary>⭐ I-3 — his answer: throw back (when he may and the fuze allows) › run (when he gets clear in time) › prone.</summary>
        public static GrenadeResponse Choose(in LiveGrenade g, float distance, bool mayThrowBack)
        {
            if (mayThrowBack && g.FuzeLeft > 0f && g.FuzeLeft >= ThrowBackNeeds(distance)) return GrenadeResponse.ThrowBack;
            float need = g.LethalMetres + ClearMetres - distance;
            if (need <= 0f) return GrenadeResponse.Prone;   // already outside its lethal radius: flat is the most he gains
            float fuze = g.FuzeLeft > 0f ? g.FuzeLeft : UnknownFuzeSeconds;
            return ReactSeconds + need / RunSpeed <= fuze ? GrenadeResponse.Run : GrenadeResponse.Prone;
        }

        /// <summary>May <paramref name="man"/> throw a grenade back: trained (Regular and up, J), hands that can take it.</summary>
        public static bool MayThrowBack(EntityRepository world, Entity man, long ammo)
            => ProficiencyOf.Tier(world, man) >= ProficiencyTier.Regular && PersonHands.MountIndex(world, man) >= 0
               && PersonHands.HasRoom(world, man, ammo);

        /// <summary>The point he runs to: straight away from <paramref name="round"/>, <paramref name="metres"/> from it.</summary>
        public static Vector3 Away(Vector3 man, Vector3 round, float metres)
        {
            var d = new Vector3(man.X - round.X, man.Y - round.Y, 0f);
            if (d.LengthSquared() < 1e-4f) d = new Vector3(1f, 0f, 0f);
            var at = round + Vector3.Normalize(d) * metres;
            at.Z = man.Z;
            return at;
        }
    }
}

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2360</c> (AQ102 I-3, R-2072) — <c>EvadeGrenade</c>: the reflex a live grenade near him starts, as a REACTION
    /// (<see cref="ReactionUrgency.Grenade"/>, the most urgent: it pauses whatever he was doing, an order included, and the task resumes
    /// after). <see cref="GrenadeResponse.Run"/> — flat out straight away from it, out of its lethal radius, and down there;
    /// <see cref="GrenadeResponse.Prone"/> — a step away (his back to it) and flat; <see cref="GrenadeResponse.ThrowBack"/> — to it, pick it
    /// up (<see cref="RoundPickUp.Ask"/>: the round's owner grants it, its fuze still burning in his hand) and throw it from his hands at
    /// where it came from (<see cref="Fdp.Toolkit.Civilians.Stones.ThrowHeld"/>), then down. Success a second after the burst.
    /// </summary>
    public static class EvadeGrenadeBehavior
    {
        /// <summary>He stays down this long after the burst (s).</summary>
        public const double AfterBurstSeconds = 1.0;
        /// <summary>The prone step (m) · how long he waits for the grant after asking (s) · the whole reflex's limit (s).</summary>
        public const float StepMetres = 1.5f;
        public const double GrantSeconds = 1.5, LimitSeconds = 12.0;
        /// <summary>A lying grenade this near the point he was told of is the one (m).</summary>
        public const float SameRoundMetres = 1.5f;

        [StructLayout(LayoutKind.Sequential)]
        public struct EvadeGrenadeParams
        {
            public byte Response;
            public Vector3 At;
            public float LethalMetres;
            /// <summary>The fuze it had left when he reacted (s; 0 = not known).</summary>
            public float FuzeSeconds;
            public Vector3 ThrowAt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EvadeGrenadeState
        {
            public double Started, BurstAt, AskedAt, ThrownAt, StepUntil;
            /// <summary>When the grenade was no longer where it lay (it burst, or a man took it).</summary>
            public double GoneAt;
            /// <summary>What he does now (a throw back can fall back to Prone).</summary>
            public byte Mode;
            public byte Moving, Asked, Thrown, Down;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EvadeGrenadeBlackboard
        {
            public EvadeGrenadeParams Params;
            public EvadeGrenadeState State;
        }

        [BehaviorResolver(BehaviorNames.EvadeGrenade)]
        public static void Resolve(in EvadeGrenadeParamsJsonDto authored, ref EvadeGrenadeBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new EvadeGrenadeParams
            {
                Response = (byte)authored.Response, At = authored.At, LethalMetres = authored.LethalMetres > 0f ? authored.LethalMetres : 5f,
                FuzeSeconds = authored.FuzeSeconds, ThrowAt = authored.ThrowAt,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.EvadeGrenade, Params = typeof(EvadeGrenadeParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent | BehaviorCategory.Civilian)]
        [BTreeDefinition(BehaviorNames.EvadeGrenade, Curated = true, Group = "Library/Reactions", ParamsType = typeof(EvadeGrenadeBlackboard))]
        public static BTreeBuilder<EvadeGrenadeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<EvadeGrenadeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Evade);

        /// <summary>One tick of the reflex.</summary>
        public static NodeStatus Evade(ref EvadeGrenadeParams p, ref EvadeGrenadeState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            if (ws.Started <= 0)
            {
                ws.Started = now;
                ws.BurstAt = now + (p.FuzeSeconds > 0f ? p.FuzeSeconds : GrenadeDanger.UnknownFuzeSeconds);
                ws.Mode = p.Response;
            }
            var me = world.GetComponentRO<SimTransform>(self).Position;
            var round = RoundAt(world, p.At);
            bool holding = PersonHands.HoldsLethal(world, self);
            if (ws.Thrown != 0 || ws.Mode != (byte)GrenadeResponse.ThrowBack)
            {
                // it burst (or went from where it lay) — or he threw it — and he stayed down a moment: back to what he was doing
                if (round.IsNull && ws.GoneAt <= 0) ws.GoneAt = now;
                double end = ws.Thrown != 0 ? ws.ThrownAt + AfterBurstSeconds
                           : (ws.GoneAt > 0 ? Math.Min(ws.GoneAt, ws.BurstAt) : ws.BurstAt) + AfterBurstSeconds;
                if (now >= end || now - ws.Started > LimitSeconds) return NodeStatus.Success;
            }
            else if (now - ws.Started > LimitSeconds) return NodeStatus.Success;

            switch ((GrenadeResponse)ws.Mode)
            {
                case GrenadeResponse.Run:
                    if (ws.Down != 0) return NodeStatus.Running;
                    if (ws.Moving == 0)
                    {
                        var to = GrenadeDanger.Away(me, p.At, p.LethalMetres + GrenadeDanger.ClearMetres);
                        ws.Moving = LocomotionMoveTo.Issue(world, self, to, GrenadeDanger.RunSpeed, 0.8f, MoveGait.Upright, onSurface: true,
                                                           source: MoveSource.Flee) ? (byte)1 : (byte)0;
                        if (ws.Moving == 0) { GoDown(world, self, ref ws); return NodeStatus.Running; }
                    }
                    if (LocomotionMoveTo.Status(world, self) != NodeStatus.Running || now >= ws.BurstAt - 0.5) GoDown(world, self, ref ws);
                    return NodeStatus.Running;

                case GrenadeResponse.Prone:
                    if (ws.Down != 0) return NodeStatus.Running;
                    if (ws.Moving == 0)
                    {
                        float d = Flat(me - p.At);
                        LocomotionMoveTo.Issue(world, self, GrenadeDanger.Away(me, p.At, d + StepMetres), GrenadeDanger.RunSpeed, 0.4f, MoveGait.Upright,
                                               onSurface: true, source: MoveSource.Flee);   // one step: his back to it, his head away
                        ws.Moving = 1;
                        ws.StepUntil = now + GrenadeDanger.ReactSeconds;
                    }
                    if (now >= ws.StepUntil || LocomotionMoveTo.Status(world, self) != NodeStatus.Running) GoDown(world, self, ref ws);
                    return NodeStatus.Running;

                default:   // ThrowBack
                    if (ws.Thrown != 0)
                    {
                        if (ws.Down == 0 && now - ws.ThrownAt > 0.4) GoDown(world, self, ref ws);
                        return NodeStatus.Running;
                    }
                    if (holding)
                    {
                        if (Fdp.Toolkit.Civilians.Stones.ThrowHeld(world, self, p.ThrowAt)) { ws.Thrown = 1; ws.ThrownAt = now; }
                        return NodeStatus.Running;
                    }
                    if (ws.Asked != 0)
                    {
                        if (now - ws.AskedAt > GrantSeconds) Fallback(ref ws);   // another man was faster, or it was not given: down
                        return NodeStatus.Running;
                    }
                    if (round.IsNull) { Fallback(ref ws); return NodeStatus.Running; }
                    if (RoundPickUp.CanAsk(world, self, round))
                    {
                        LocomotionMoveTo.Stop(world, self);
                        ws.Moving = 0;
                        if (RoundPickUp.Ask(world, self, round))
                        {
                            ws.Asked = 1;
                            ws.AskedAt = now;
                            StanceRequest.Set(world, self, StanceId.Crouched, 0.2f);
                        }
                        else Fallback(ref ws);
                        return NodeStatus.Running;
                    }
                    if (ws.Moving == 0)
                        ws.Moving = LocomotionMoveTo.Issue(world, self, world.GetComponentRO<SimTransform>(round).Position, GrenadeDanger.RunSpeed, 0.5f,
                                                           MoveGait.Upright, onSurface: true, source: MoveSource.Attack) ? (byte)1 : (byte)0;
                    return NodeStatus.Running;
            }
        }

        /// <summary>Leaving the reflex: he stops; the task he resumes sets his stance and his move.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EvadeGrenadeBehavior.Evade")]
        public static void Deactivate_Evade(ref EvadeGrenadeParams p, ref EvadeGrenadeState ws, Entity self, EntityRepository world)
        {
            if (ws.Moving != 0) LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        private static void GoDown(EntityRepository world, Entity self, ref EvadeGrenadeState ws)
        {
            if (ws.Moving != 0) LocomotionMoveTo.Stop(world, self);
            ws.Moving = 0;
            ws.Down = 1;
            StanceRequest.Set(world, self, StanceId.Prone, UnderFire.DropBlendSeconds);
        }

        private static void Fallback(ref EvadeGrenadeState ws)
        {
            ws.Mode = (byte)GrenadeResponse.Prone;
            ws.Moving = 0;
            ws.Down = 0;
        }

        /// <summary>The live grenade lying within <see cref="SameRoundMetres"/> of <paramref name="at"/> on this node, or null.</summary>
        internal static Entity RoundAt(EntityRepository world, Vector3 at)
        {
            if (!world.IsComponentTypeRegistered<BallisticProjectile>()) return Entity.Null;
            foreach (var e in world.Query().With<BallisticProjectile>().With<SimTransform>().Build())
            {
                if (!RoundPickUp.IsLying(world, e)) continue;
                if (Flat(world.GetComponentRO<SimTransform>(e).Position - at) <= SameRoundMetres) return e;
            }
            return Entity.Null;
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2360</c> (AQ102 I-2, R-2072) — every frame, on the brain host: the live grenades lying here and every PERSON they endanger
    /// (<see cref="GrenadeDanger.Endangers"/>, not possessed, in the fight). Per grenade the nearest man who may throw it back and has the
    /// fuze for it does; every other runs or drops (<see cref="GrenadeDanger.Choose"/>). Each answer is a REACTION
    /// (<see cref="ReactionUrgency.Grenade"/>) published once per man per grenade — the reflex is automatic for every man, under command
    /// or not: it is not an SOP row (R-2062 lets a man's own rows stand aside under a commander). Before the brain tick.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class GrenadeDangerSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private readonly List<LiveGrenade> _rounds = new();
        private readonly HashSet<(ulong Man, ulong Round)> _reacted = new();
        private readonly List<(Entity Man, float Distance)> _near = new();
        private EntityQuery? _people;

        public GrenadeDangerSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            GrenadeDanger.Lying(repo, _rounds);
            if (_rounds.Count == 0) { _reacted.Clear(); return; }
            _people ??= repo.Query().With<BehaviorState>().With<SimTransform>().Build();
            foreach (var g in _rounds)
            {
                _near.Clear();
                foreach (var man in _people)
                {
                    if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(man)) continue;
                    if (!StanceRequest.CanCarry(repo, man) || !Injury.IsInTheFight(repo, man)) continue;   // a person, able to move
                    if (repo.IsComponentTypeRegistered<IsEmbarkedTag>() && repo.HasComponent<IsEmbarkedTag>(man)) continue;   // aboard: his vehicle's
                    if (_reacted.Contains((man.PackedValue, g.Round.PackedValue))) continue;
                    float d = Flat(repo.GetComponentRO<SimTransform>(man).Position - g.At);
                    if (GrenadeDanger.Endangers(in g, d)) _near.Add((man, d));
                }
                if (_near.Count == 0) continue;
                _near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                bool thrower = false;
                foreach (var (man, d) in _near)
                {
                    bool may = !thrower && GrenadeDanger.MayThrowBack(repo, man, g.Ammo);
                    var answer = GrenadeDanger.Choose(in g, d, may);
                    thrower |= answer == GrenadeResponse.ThrowBack;
                    var dto = new EvadeGrenadeParamsJsonDto
                    {
                        Response = answer, At = g.At, LethalMetres = g.LethalMetres, FuzeSeconds = g.FuzeLeft > 0f ? g.FuzeLeft : 0f, ThrowAt = g.From,
                    };
                    if (SopActions.React(repo, man, BehaviorNames.EvadeGrenade, ReactionUrgency.Grenade, in dto) == NodeStatus.Success)
                        _reacted.Add((man.PackedValue, g.Round.PackedValue));
                }
            }
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}
