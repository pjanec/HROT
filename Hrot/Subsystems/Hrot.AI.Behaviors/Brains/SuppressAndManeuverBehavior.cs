using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2123</c> — the commander's suppress-and-manoeuvre order (resolved from <see cref="SuppressAndManeuverParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SuppressAndManeuverParams
    {
        public float FlankSearchRadius;
        public float Speed;
        public float ArrivalRadius;
        public float PhaseTimeoutSeconds;
        /// <summary>⭐ <c>CE-2141</c> (D2-G) — the enemy to attack (a local entity's packed id), as an ATTACK order names it; 0 = the
        /// squad's top identified contact. A target the squad has not pooled is attacked where it stands when ordered (the order's
        /// designation is the knowledge), and followed once pooled.</summary>
        public long TargetId;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> (S1) — suppress and manoeuvre, on the commander: the members who already SEE the squad's top identified contact
    /// are the base of fire and hold; the rest are the assault — the COMMANDER asks the terrain for a flank (its own
    /// <see cref="FindFlankingPosition"/> query, measured from a base-of-fire member, aimed at the enemy), the assault goes there, then
    /// through the enemy's position. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §3.
    /// </summary>
    public static class SuppressAndManeuverBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SuppressAndManeuverBlackboard
        {
            public SuppressAndManeuverParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SuppressAndManeuver)]
        public static void Resolve(in SuppressAndManeuverParamsJsonDto authored, ref SuppressAndManeuverBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = Defaults(authored.FlankSearchRadius, authored.Speed, authored.ArrivalRadius, authored.PhaseTimeoutSeconds);
            block.State = default;
        }
[Behavior(BehaviorNames.SuppressAndManeuver, Params = typeof(global::Hrot.Map.Definitions.Behavior.SuppressAndManeuverParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        /// <summary>The authored values, a zero keeping its default (one place: the drill and the ATTACK order share them).</summary>
        internal static SuppressAndManeuverParams Defaults(float radius, float speed, float arrival, float timeout) => new()
        {
            FlankSearchRadius   = radius > 0f ? radius : 40f,
            Speed               = speed > 0f ? speed : 3f,
            ArrivalRadius       = arrival > 0f ? arrival : 8f,
            PhaseTimeoutSeconds = timeout > 0f ? timeout : 90f,
        };

        [BTreeDefinition(BehaviorNames.SuppressAndManeuver, Curated = true, Group = "Library/Squad", ParamsType = typeof(SuppressAndManeuverBlackboard))]
        public static BTreeBuilder<SuppressAndManeuverBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SuppressAndManeuverBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SuppressAndManeuverParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SuppressAndManeuverPlay, SuppressAndManeuverParams>(ref p, ref ws, self, world);

        /// <summary>Leaving the node (abort, a reaction's pause ends it, the behaviour replaced): the commander's flank query goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.SuppressAndManeuverBehavior.Run")]
        public static void Deactivate_Run(ref SuppressAndManeuverParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);
    }

    /// <summary>⭐ <c>CE-2123</c> — suppress-and-manoeuvre as a play over <see cref="SuppressAndManeuverManeuver"/>'s table.</summary>
    public struct SuppressAndManeuverPlay : ISquadPlay<SuppressAndManeuverParams>
    {
        /// <summary>The commander's flank-query site (with the run's owner stamp it finds the sensor again).</summary>
        public const int FlankSite = 0x21230001;

        /// <summary>The assault is "at the flank" when its centre is this close to the answer (m) — members do not each need the point.</summary>
        public const float FlankArrivalMetres = 5f;

        /// <summary>The enemy moving this far re-sends the assault's orders (m).</summary>
        public const float ThreatMovedMetres = 10f;

        /// <summary><see cref="SquadPlayState.HasAnswer"/> when the flank query found no point: the assault goes straight through.</summary>
        public const byte NoFlank = SquadManeuverRunner.NoAnswer;

        private static readonly SquadHsmShell s_shell = new(
            SuppressAndManeuverManeuver.BuildTransitionTable(), SuppressAndManeuverManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SuppressAndManeuverManeuver.ManeuverKind;
        public ushort StartPhase => SuppressAndManeuverManeuver.PhaseSuppressing;
        public ushort AbortPhase => SuppressAndManeuverManeuver.PhaseAborted;
        public byte ElementCount => 2;

        /// <summary>⭐ Ordered before contact, the commander WAITS (no orders) until the squad pools an identified enemy.</summary>
        public PlayStart Start(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            if (!Threat(ref p, in ctx, out var contact)) return PlayStart.Wait;
            ws.ThreatId = contact.EntityId;
            ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);

            // ⭐ S1-B — the members that REPORTED the threat see it: they are the base of fire (element 0); the rest assault.
            int bases = 0, live = 0;
            for (int i = 0; i < ctx.Roster.Count; i++)
            {
                bool sees = (contact.SourceMembersMask & (1 << i)) != 0;
                partition[i] = sees ? new MemberPartitionInput(1f, 0.1f) : new MemberPartitionInput(0.1f, 1f);
                if (!ctx.Live(i, out _)) continue;
                live++;
                if (sees) bases++;
            }
            if (live < 2) return PlayStart.Fail;
            if (bases == 0 || bases == live) SuppressAndManeuverManeuver.ComputePartitionInputs(ctx.Roster.Count, partition);
            return PlayStart.Go;
        }

        public void Update(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (TopContact(in ctx, ws.ThreatId, out var contact) && contact.EntityId == ws.ThreatId)
                ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);

            ushort phase = ctx.Squad.PhaseId;
            if (phase == SuppressAndManeuverManeuver.PhaseSuppressing && ws.HasAnswer == 0) AskForFlank(ref p, ref ws, in ctx);
            if (phase == SuppressAndManeuverManeuver.PhaseAssaulting
                && Vector3.Distance(ws.ThreatPoint, ws.IssuedThreatPoint) > ThreatMovedMetres) ws.Reissue = 1;
        }

        public NodeStatus Outcome(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            var threat = new Entity((ulong)ws.ThreatId);
            if (!CombatLife.IsAlive(ctx.World, threat)) return NodeStatus.Success;   // the enemy is down: the drill is done
            return ctx.Squad.PhaseId == SuppressAndManeuverManeuver.PhaseAssaultComplete ? NodeStatus.Success : NodeStatus.Running;
        }

        public bool PhaseComplete(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            bool timedOut = ctx.Now - ws.IssuedAt >= p.PhaseTimeoutSeconds;
            var assault = ctx.Centre(SuppressAndManeuverManeuver.ElementAssault, out bool any);
            if (ctx.Squad.PhaseId == SuppressAndManeuverManeuver.PhaseSuppressing)
            {
                evt = PhaseEventKind.FarSideReached;
                if (ws.HasAnswer == NoFlank || timedOut || !any) return true;
                return ws.HasAnswer == 1 && ws.Reissue == 0 && Flat(assault - ws.Answer) <= FlankArrivalMetres;
            }
            evt = PhaseEventKind.BoundComplete;
            return timedOut || !any || Flat(assault - ws.ThreatPoint) <= p.ArrivalRadius;
        }

        public void BeginIssue(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            if (element == SuppressAndManeuverManeuver.ElementBaseOfFire)
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SuppressAndManeuverManeuver.RoleBaseOfFire };

            var order = new MemberOrder { Speed = p.Speed, RoleId = SuppressAndManeuverManeuver.RoleAssault };
            if (ctx.Squad.PhaseId == SuppressAndManeuverManeuver.PhaseAssaulting)
            {
                order.Kind = MemberOrderKind.AdvanceTo;          // through the enemy's position — a point he stands on, so walkable
                order.Point = ws.ThreatPoint;
                order.ArrivalRadius = p.ArrivalRadius;
            }
            else if (ws.HasAnswer == 1)
            {
                order.Kind = MemberOrderKind.AdvanceTo;          // the flank: a walkable point that sees the enemy (the query's answer)
                order.Point = ws.Answer;
                order.ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres;
            }
            else order.Kind = MemberOrderKind.Hold;              // until the terrain answers, the assault holds and fights too
            return order;
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>⭐ S1-C — the COMMANDER asks the terrain: its own flank query, measured from a base-of-fire member (slot 0), aimed
        /// at the enemy (slot 1). Solved on SimHost; the first answer is the flank, and the query goes.</summary>
        private static void AskForFlank(ref SuppressAndManeuverParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => SquadManeuverRunner.AskTerrain(ref ws, in ctx, FindFlankingPosition.BlueprintId, FlankSite, p.FlankSearchRadius,
                SquadManeuverRunner.FirstLive(in ctx, SuppressAndManeuverManeuver.ElementBaseOfFire), new Entity((ulong)ws.ThreatId));

        /// <summary>⭐ <c>CE-2141</c> (D2-G) — what the drill attacks: the named target (pooled, or where it stands — the order's
        /// designation), else the squad's top identified contact. False = nothing yet (wait).</summary>
        private static bool Threat(ref SuppressAndManeuverParams p, in SquadPlayContext ctx, out SquadContact contact)
        {
            if (p.TargetId == 0) return TopContact(in ctx, 0, out contact);
            if (TopContact(in ctx, p.TargetId, out contact) && contact.EntityId == p.TargetId) return true;
            var target = new Entity((ulong)p.TargetId);
            contact = new SquadContact { EntityId = p.TargetId };      // reported by nobody: the partition falls back to halves
            if (!ctx.World.IsAlive(target) || !ctx.World.HasComponent<SimTransform>(target)) return true;   // gone: Outcome ends the drill
            var at = ctx.World.GetComponentRO<SimTransform>(target).Position;
            contact.PositionX = at.X; contact.PositionY = at.Y; contact.PositionZ = at.Z;
            return true;
        }

        private static bool TopContact(in SquadPlayContext ctx, long keep, out SquadContact contact)
            => SquadManeuverRunner.TopContact(in ctx, keep, out contact);

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}
