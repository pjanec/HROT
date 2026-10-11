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
            block.Order = new SuppressAndManeuverParams
            {
                FlankSearchRadius   = authored.FlankSearchRadius > 0f ? authored.FlankSearchRadius : 40f,
                Speed               = authored.Speed > 0f ? authored.Speed : 3f,
                ArrivalRadius       = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : 8f,
                PhaseTimeoutSeconds = authored.PhaseTimeoutSeconds > 0f ? authored.PhaseTimeoutSeconds : 90f,
            };
            block.State = default;
        }

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
        public const byte NoFlank = 2;

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
            if (!TopContact(in ctx, 0, out var contact)) return PlayStart.Wait;
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
        {
            var world = ctx.World;
            var child = ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId) ? ws.Sensor.ChildId : Entity.Null;
            if (child.IsNull)
            {
                var from = MeasureFrom(in ctx);
                if (from.IsNull) { ws.HasAnswer = NoFlank; return; }
                var config = new EqsSensor
                {
                    BlueprintId         = FindFlankingPosition.BlueprintId,
                    Epoch               = 1,
                    SearchRadius        = p.FlankSearchRadius,
                    PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
                    ScoreDeltaThreshold = 0.05f,
                    ContextSlot0        = from,
                    ContextSlot1        = new Entity((ulong)ws.ThreatId),
                };
                child = EqsChildSensor.Ensure(world, ctx.Commander, FlankSite, in config);
                if (child.IsNull) return;
                ws.Sensor = new EqsSensorHandle(child);
                ws.LastAnswerTick = 0;
            }
            if (!world.HasComponent<EqsCognitiveBuffer>(child)) return;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(child);
            if (!buffer.IsReady || buffer.LastUpdateTick == ws.LastAnswerTick) return;
            ws.LastAnswerTick = buffer.LastUpdateTick;
            if (buffer.Count == 0) ws.HasAnswer = NoFlank;
            else
            {
                var top = buffer.GetTop();
                ws.Answer = new Vector3(top.PositionX, top.PositionY, top.PositionZ);
                ws.HasAnswer = 1;
                ws.Reissue = 1;
            }
            SquadManeuverRunner.DropSensor(ref ws, world);
        }

        /// <summary>The member the flank is measured from: the first live base-of-fire member (it sees the enemy).</summary>
        private static Entity MeasureFrom(in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (squad.Elements.MemberElements[i] == SuppressAndManeuverManeuver.ElementBaseOfFire && ctx.Live(i, out var m)) return m;
            return Entity.Null;
        }

        /// <summary>The squad's top IDENTIFIED pooled contact (highest threat), preferring <paramref name="keep"/> while it is pooled.</summary>
        private static bool TopContact(in SquadPlayContext ctx, long keep, out SquadContact contact)
        {
            contact = default;
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            bool found = false;
            for (int i = 0; i < squad.Contacts.Count && i < 16; i++)
            {
                var c = pool[i];
                if ((c.Flags & SquadContact.AnonymousFlag) != 0 || c.EntityId <= 0) continue;
                if (keep != 0 && c.EntityId == keep) { contact = c; return true; }
                if (!found || c.ThreatScore > contact.ThreatScore) { contact = c; found = true; }
            }
            return found;
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}
