using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2152</c> C-c (R-c) — the casualty-care play's params; the four runtime fields are the play's own memory.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CasualtyCareParams
    {
        /// <summary>Walking speed (m/s).</summary>
        public float Speed;
        /// <summary>How far around the casualty the cover query looks (m).</summary>
        public float CoverSearchRadius;
        /// <summary>The medic goes after this long even when not every coverer has fired (s). ⭐ <c>CE-2227</c>: 5 s (was 3) — live, the
        /// slowest coverer's first round comes 2–3.8 s after the order (turn, raise, aim), so 3 s sent the medic before the covering
        /// in about half the runs of <c>ua-casualty-care</c>.</summary>
        public float CoveringSeconds;
        /// <summary>The whole drill gives up after this long in one phase (s).</summary>
        public float PhaseTimeoutSeconds;
        /// <summary>Runtime: the man being cared for.</summary>
        public Entity Casualty;
        /// <summary>Runtime: the man sent for him.</summary>
        public Entity Medic;
        /// <summary>Runtime: where the medic waited (in cover) — where he goes back to when called back.</summary>
        public Vector3 MedicFrom;
        /// <summary>Runtime: the casualty's roster slot.</summary>
        public byte CasualtySlot;
        /// <summary>Runtime: the medic's roster slot.</summary>
        public byte MedicSlot;
        /// <summary>⭐ <c>CE-2173</c> slice 3c (Air Operations §9j, <c>SquadRecover</c>) — where to bring him when
        /// <see cref="HasDestination"/> (no cover query).</summary>
        public Vector3 Destination;
        /// <summary>1 ⇒ <see cref="Destination"/> is the point.</summary>
        public byte HasDestination;
        /// <summary>1 ⇒ the medic keeps carrying him at the point: Done when he CARRIES him there, not when he lies there.</summary>
        public byte Keep;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2152</c> C-c (R-2012 K1–K6 / R-2013, slice R-c; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.5.2–§7.5.3) — a man
    /// is down: the squad covers, the medic brings him in. A play on the generic runner: the coverers <c>Suppress</c> the squad's top
    /// contact while the medic HOLDS in cover; when every coverer has fired (<see cref="SquadGates.Covering"/>) the medic GOES —
    /// <c>CarryCasualty</c> to the nearest cover point around the casualty hidden from the threat (<see cref="FindCoverFromTarget"/>, slot 0
    /// the casualty, slot 1 the threat); the man lying there ⇒ Done (the squad remembers him as cared for). Fire picking up while the medic
    /// is still WALKING to him ⇒ he is called back to where he waited and the play fails (the reaction decides again); once he is
    /// CARRYING he keeps going. Medic: the member designated <c>Medic</c>, else the nearest member in the fight who is not the leader.
    /// </summary>
    public struct SquadCasualtyCarePlay : ISquadPlay<CasualtyCareParams>
    {
        /// <summary>The commander's cover query for the casualty.</summary>
        public const int CoverSite = 0x21520001;

        /// <summary>The casualty "lies at the cover point" within this (m).</summary>
        public const float AtCoverMetres = 2.5f;

        /// <summary>The recalled medic is back within this (m).</summary>
        public const float BackMetres = 3f;

        private static readonly SquadHsmShell s_shell = new(CasualtyCareManeuver.BuildTransitionTable(), CasualtyCareManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => CasualtyCareManeuver.ManeuverKind;
        public ushort StartPhase => CasualtyCareManeuver.PhaseSuppress;
        public ushort AbortPhase => CasualtyCareManeuver.PhaseAborted;
        public byte ElementCount => 2;

        public PlayStart Start(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            int casualty = CasualtyCare.Uncared(in ctx, p.Casualty);
            // ⭐ CE-2173 slice 3c — a man down who is NOT of this squad (a downed pilot): no roster slot
            if (casualty < 0 && CasualtyCare.OutsiderToCareFor(ctx.World, p.Casualty)) casualty = CasualtyCare.Outsider;
            if (casualty < 0) return PlayStart.Fail;
            var casualtyEntity = casualty == CasualtyCare.Outsider ? p.Casualty : ctx.Roster.SubordinateEntities[casualty];
            int medic = CasualtyCare.MedicFor(in ctx, casualty, casualtyEntity);
            if (medic < 0) return PlayStart.Fail;
            p.Casualty = casualtyEntity;
            p.CasualtySlot = (byte)casualty;
            p.Medic = ctx.Roster.SubordinateEntities[medic];
            p.MedicSlot = (byte)medic;
            p.MedicFrom = ctx.At(p.Medic);
            if (SquadManeuverRunner.TopContact(in ctx, 0, out var contact))
            {
                ws.ThreatId = contact.EntityId;
                ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            }
            for (int i = 0; i < ctx.Roster.Count; i++)
                partition[i] = i == medic ? new MemberPartitionInput(0.1f, 1f) : new MemberPartitionInput(1f, 0.1f);
            return PlayStart.Go;
        }

        public void Update(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (ws.ThreatId != 0 && SquadManeuverRunner.TopContact(in ctx, ws.ThreatId, out var contact) && contact.EntityId == ws.ThreatId)
                ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            if (ws.HasAnswer == 0)
            {
                if (ws.ThreatId == 0 || p.HasDestination != 0) ws.HasAnswer = SquadManeuverRunner.NoAnswer;   // nothing to hide from: bring him to where the medic waited
                else SquadManeuverRunner.AskTerrain(ref ws, in ctx, FindCoverFromTarget.BlueprintId, CoverSite, p.CoverSearchRadius,
                                                    p.Casualty, new Entity((ulong)ws.ThreatId));
            }
            SquadGates.Track(ref ws.Gate, in ctx);
            ushort coverers = (ushort)(SquadGates.Element(in ctx, CasualtyCareManeuver.ElementCoverers) & ~(1 << p.CasualtySlot));
            SquadDrillTrace.Gates(ctx.World, ctx.Commander, SquadGates.Covering(in ws.Gate, in ctx, coverers), LiesInCover(ref p, ref ws, in ctx));
            SquadDrillTrace.CareChanged(ctx.World, ctx.Commander,
                new SquadDrillTrace.CareState(p.Casualty, p.Medic, CoverPoint(ref p, ref ws), ws.HasAnswer != 0, (byte)ctx.Squad.PhaseId));
        }

        public NodeStatus Outcome(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!ctx.World.IsAlive(p.Casualty) || !CombatLife.IsAlive(ctx.World, p.Casualty)) return NodeStatus.Failure;   // he died
            if (ctx.Squad.PhaseId == CasualtyCareManeuver.PhaseDone)
            {
                CasualtyCare.MarkCaredFor(ctx.World, ctx.Commander, p.CasualtySlot);
                return NodeStatus.Success;
            }
            if (!Injury.IsInTheFight(ctx.World, p.Medic)) return NodeStatus.Failure;   // the medic is down too: decide again
            return NodeStatus.Running;
        }

        public bool PhaseComplete(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            ushort phase = ctx.Squad.PhaseId;
            bool timedOut = ctx.Now - ws.IssuedAt >= p.PhaseTimeoutSeconds;
            if (phase == CasualtyCareManeuver.PhaseSuppress)
            {
                evt = PhaseEventKind.ShotFired;
                if (ws.HasAnswer == 0) { if (!timedOut) return false; evt = PhaseEventKind.Abort; return true; }
                ushort coverers = (ushort)(SquadGates.Element(in ctx, CasualtyCareManeuver.ElementCoverers) & ~(1 << p.CasualtySlot));
                if (SquadGates.Covering(in ws.Gate, in ctx, coverers)) return true;
                if (ctx.Now - ws.IssuedAt < p.CoveringSeconds) return false;
                ws.CompletedByTimeout = 1;   // the medic goes anyway: the covering never came
                return true;
            }
            if (phase == CasualtyCareManeuver.PhaseGo)
            {
                evt = PhaseEventKind.DefiladeReached;
                if (p.Keep != 0 ? CarriedThere(ref p, ref ws, in ctx) : LiesInCover(ref p, ref ws, in ctx)) return true;
                // ⭐ CE-2152 R-e — 📐 measured live: the medic's carry ended (he could not reach the man) and the drill waited in Go
                //   while the man bled out. Not carrying him and no longer running the carry ⇒ send him again.
                if (!Carrying(ref p, in ctx) && ctx.Now - ws.IssuedAt >= ResendSeconds && !RunsTheCarry(in ctx, p.Medic)) ws.Reissue = 1;
                evt = PhaseEventKind.BoundComplete;   // fire picked up while he was still on the way: back to cover
                if (!Carrying(ref p, in ctx) && RecentSensesOf.WithinSince(ctx.World, ctx.Commander, SensorChange.SquadUnderFire, 3600d, ws.IssuedAt))
                    return true;
                evt = PhaseEventKind.Abort;
                return timedOut;
            }
            evt = PhaseEventKind.BoundComplete;   // Recall: done when he is back where he waited
            var d = ctx.At(p.Medic) - p.MedicFrom; d.Z = 0f;
            return d.Length() <= BackMetres || timedOut;
        }

        public void BeginIssue(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ushort phase = ctx.Squad.PhaseId;
            if (member == p.MedicSlot)
            {
                if (phase == CasualtyCareManeuver.PhaseGo)
                    return new MemberOrder
                    {
                        Kind = MemberOrderKind.CarryCasualty, Target = p.Casualty, Point = CoverPoint(ref p, ref ws), Speed = p.Speed,
                        Cover = ws.HasAnswer == 1 ? (byte)1 : (byte)0, RoleId = CasualtyCareManeuver.RoleMedic, Keep = p.Keep,
                    };
                if (phase == CasualtyCareManeuver.PhaseRecall)
                    return new MemberOrder
                    {
                        Kind = MemberOrderKind.AdvanceTo, Point = p.MedicFrom, Speed = p.Speed,
                        ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres, RoleId = CasualtyCareManeuver.RoleMedic,
                    };
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = CasualtyCareManeuver.RoleMedic };
            }
            if (ws.ThreatId == 0) return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = CasualtyCareManeuver.RoleCoverer };
            return new MemberOrder
            {
                Kind = MemberOrderKind.Suppress, Target = new Entity((ulong)ws.ThreatId), Point = ws.ThreatPoint, Speed = p.Speed,
                RoleId = CasualtyCareManeuver.RoleCoverer,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>Where the medic brings him: the cover query's answer, else where the medic waited.</summary>
        private static Vector3 CoverPoint(ref CasualtyCareParams p, ref SquadPlayState ws)
            => p.HasDestination != 0 ? p.Destination : ws.HasAnswer == 1 ? ws.Answer : p.MedicFrom;

        /// <summary>⭐ <c>CE-2173</c> slice 3c — the medic CARRIES him within <see cref="AtCoverMetres"/> of the point.</summary>
        private static bool CarriedThere(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!Carrying(ref p, in ctx) || !ctx.World.HasComponent<SimTransform>(p.Medic)) return false;
            var d = ctx.At(p.Medic) - CoverPoint(ref p, ref ws); d.Z = 0f;
            return d.Length() <= AtCoverMetres;
        }

        /// <summary>⭐ <c>CE-2152</c> R-e — a medic who stopped carrying out the order is sent again after this long (s).</summary>
        public const double ResendSeconds = 2d;

        private static readonly int CarryCasualtyBehaviorId = BehaviorHash.FromName(BehaviorNames.CarryCasualty);

        /// <summary>The medic still runs the carry he was given (true when this node cannot tell: he has no brain state here).</summary>
        private static bool RunsTheCarry(in SquadPlayContext ctx, Entity medic)
            => !ctx.World.HasComponent<BehaviorState>(medic) || ctx.World.GetComponentRO<BehaviorState>(medic).ActiveBehaviorHash == CarryCasualtyBehaviorId;

        private static bool Carrying(ref CasualtyCareParams p, in SquadPlayContext ctx)
            => ctx.World.HasComponent<IsEmbarkedTag>(p.Casualty) && ctx.World.GetComponentRO<IsEmbarkedTag>(p.Casualty).VehicleEntity == p.Medic;

        /// <summary>The man lies (put down, not carried) at the cover point.</summary>
        private static bool LiesInCover(ref CasualtyCareParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (ws.HasAnswer == 0 || !ctx.World.HasComponent<SimTransform>(p.Casualty) || ctx.World.HasComponent<IsEmbarkedTag>(p.Casualty)) return false;
            var d = ctx.At(p.Casualty) - CoverPoint(ref p, ref ws); d.Z = 0f;
            return d.Length() <= AtCoverMetres;
        }
    }

    /// <summary>⭐ <c>CE-2152</c> C-c — who is down and uncared for, and who goes for him (K3′).</summary>
    public static class CasualtyCare
    {
        /// <summary>The roster slot of the man to care for: <paramref name="prefer"/> while he is still down and uncared for, else the
        /// first such member; −1 when nobody is.</summary>
        public static int Uncared(in SquadPlayContext ctx, Entity prefer)
        {
            int first = -1;
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                var m = ctx.Roster.SubordinateEntities[i];
                if (!SquadCasualties.ToCareFor(ctx.World, ctx.Commander, i, m)) continue;
                if (!prefer.IsNull && m == prefer) return i;
                if (first < 0) first = i;
            }
            return first;
        }

        /// <summary>The medic for the man in slot <paramref name="casualty"/>: the live <c>Medic</c>, else the nearest member in the fight
        /// who is not the squad's leader; −1 when nobody can go.</summary>
        /// <summary>⭐ <c>CE-2173</c> slice 3c — the "slot" of a casualty who is not a member (no roster bit, never marked cared for).</summary>
        public const int Outsider = 255;

        /// <summary>⭐ <c>CE-2173</c> slice 3c — <paramref name="man"/> is down (incapacitated, alive) and nobody carries him yet.</summary>
        public static bool OutsiderToCareFor(EntityRepository world, Entity man)
            => !man.IsNull && world.IsAlive(man) && world.HasComponent<SimTransform>(man) && !world.HasComponent<IsEmbarkedTag>(man)
               && Injury.StateOf(world, man) == InjuryState.Incapacitated;

        public static int MedicFor(in SquadPlayContext ctx, int casualty)
            => MedicFor(in ctx, casualty, casualty >= 0 && casualty < ctx.Roster.Count ? ctx.Roster.SubordinateEntities[casualty] : Entity.Null);

        /// <summary>The medic for <paramref name="man"/> (roster slot <paramref name="casualty"/>, or <see cref="Outsider"/>).</summary>
        public static int MedicFor(in SquadPlayContext ctx, int casualty, Entity man)
        {
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (i != casualty && ctx.Live(i, out var m) && Designation(ctx.World, in ctx.Roster, i, m) == TacticalDesignation.Medic) return i;
            var leader = ctx.World.HasComponent<SquadLeadership>(ctx.Commander) ? ctx.World.GetComponentRO<SquadLeadership>(ctx.Commander).Leader : Entity.Null;
            var him = !man.IsNull && ctx.World.HasComponent<SimTransform>(man) ? ctx.At(man) : Vector3.Zero;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < ctx.Roster.Count; i++)
            {
                if (i == casualty || !ctx.Live(i, out var m) || m == leader) continue;
                float d = Vector3.DistanceSquared(ctx.At(m), him);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static void MarkCaredFor(EntityRepository world, Entity commander, int slot)
        {
            if (slot < 0 || slot >= 16 || !world.IsComponentTypeRegistered<SquadLeadership>()) return;
            if (!world.HasComponent<SquadLeadership>(commander)) world.AddComponent(commander, new SquadLeadership());   // succession fills the leader
            world.GetComponentRW<SquadLeadership>(commander).CaredFor |= (ushort)(1 << slot);
        }

        /// <summary>The number of members down and not yet cared for (<see cref="SquadCasualties.Count"/>).</summary>
        public static int UncaredCount(EntityRepository world, Entity commander) => SquadCasualties.Count(world, commander);

        private static TacticalDesignation Designation(EntityRepository world, in UnitRoster roster, int i, Entity m)
            => Fdp.Toolkit.Squad.SquadRoles.Of(world, in roster, i);   // ⭐ CE-2281 — the one role reader
    }

    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-c) — the library <c>CasualtyCare</c>: the care play as a commander's behaviour, for a man who is down. The
    /// squad's contact reaction runs the same play when its <c>Rescue</c> option wins (R-d); this is the play on its own.
    /// </summary>
    public static class CasualtyCareBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct CasualtyCareBlackboard
        {
            public CasualtyCareParams Order;
            public SquadPlayState State;
        }

        /// <summary>The play's defaults (shared by the reaction's <c>Rescue</c>).</summary>
        public static CasualtyCareParams Defaults(float speed) => new()
        {
            Speed = speed > 0f ? speed : 3f, CoverSearchRadius = 25f, CoveringSeconds = 5f, PhaseTimeoutSeconds = 90f,
        };

        [BehaviorResolver(BehaviorNames.CasualtyCare)]
        public static void Resolve(in CasualtyCareParamsJsonDto authored, ref CasualtyCareBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = Defaults(authored.Speed);
            if (authored.CoverSearchRadius > 0f) block.Order.CoverSearchRadius = authored.CoverSearchRadius;
            block.Order.Casualty = authored.Casualty.Resolve(world);
            block.State = default;
        }

        [Behavior(BehaviorNames.CasualtyCare, Params = typeof(CasualtyCareParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.CasualtyCare, Curated = true, Group = "Library/Squad", ParamsType = typeof(CasualtyCareBlackboard))]
        public static BTreeBuilder<CasualtyCareBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CasualtyCareBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref CasualtyCareParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadCasualtyCarePlay, CasualtyCareParams>(ref p, ref ws, self, world);

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CasualtyCareBehavior.Run")]
        public static void Deactivate_Run(ref CasualtyCareParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3c (📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9j) — the library <c>SquadRecover</c>: the care play
    /// pointed at a named man who is down — of the squad or NOT (a downed pilot) — brought to a named point and, by default, KEPT on his
    /// carrier there, so the next step (<c>SquadEmbark</c>) loads him. The same play as <c>CasualtyCare</c>; a mission plan sequences it.
    /// </summary>
    public static class SquadRecoverBehavior
    {
        [BehaviorResolver(BehaviorNames.SquadRecover)]
        public static void Resolve(in SquadRecoverParamsJsonDto authored, ref CasualtyCareBehavior.CasualtyCareBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = CasualtyCareBehavior.Defaults(authored.Speed);
            block.Order.Casualty = authored.Casualty.Resolve(world);
            if (authored.To != Vector3.Zero) { block.Order.Destination = authored.To; block.Order.HasDestination = 1; }
            block.Order.Keep = authored.PutDown ? (byte)0 : (byte)1;
            block.State = default;
        }

        [Behavior(BehaviorNames.SquadRecover, Params = typeof(SquadRecoverParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SquadRecover, Curated = true, Group = "Library/Squad", ParamsType = typeof(CasualtyCareBehavior.CasualtyCareBlackboard))]
        public static BTreeBuilder<CasualtyCareBehavior.CasualtyCareBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CasualtyCareBehavior.CasualtyCareBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, CasualtyCareBehavior.Run);
    }
}
