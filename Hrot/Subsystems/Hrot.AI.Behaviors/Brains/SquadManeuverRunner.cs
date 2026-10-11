using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2123</c> — how a play's first tick went.</summary>
    public enum PlayStart : byte
    {
        /// <summary>The play cannot run (too few members): Failure.</summary>
        Fail = 0,
        /// <summary>Not yet (e.g. no contact to act on): Running, no orders, asked again next tick.</summary>
        Wait = 1,
        /// <summary>Started: the partition inputs are filled.</summary>
        Go = 2,
    }

    /// <summary>⭐ <c>CE-2123</c> — what one member is told to do for the current phase.</summary>
    public enum MemberOrderKind : byte
    {
        /// <summary>Stay where it stands and fight (CombatPosture at its own position).</summary>
        Hold = 0,
        /// <summary>Go to <see cref="MemberOrder.Point"/>, firing on the way (the library <c>AdvanceTo</c> — no posture decision: 📐 a
        /// CombatPosture with the enemy in sight chooses Suppress and stays put, measured live on <c>tt-suppress-flank</c>).</summary>
        AdvanceTo = 1,
    }

    /// <summary>⭐ <c>CE-2123</c> — one member's order, as a play computes it. The runner turns a hold into the library CombatPosture at
    /// the member's own position and a move into the library AdvanceTo.</summary>
    public struct MemberOrder
    {
        public MemberOrderKind Kind;
        public Vector3 Point;
        public float Speed;
        public float ArrivalRadius;
        /// <summary>The role written into <see cref="SquadCognitiveState.Roles"/> for the member.</summary>
        public byte RoleId;
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> — the commander's working memory for any squad play (generalises the first runner's state, CE-3162): the phase
    /// whose orders went out, when, each member's point, and the play's threat and terrain answer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SquadPlayState
    {
        public byte Started;
        /// <summary>⭐ S2 — sim time the play went (its <see cref="PlayStart.Go"/>), for a play with a time limit.</summary>
        public double StartedAt;
        /// <summary>1 = send every member's order again this tick (a resume after a pause, a moved threat, a new answer).</summary>
        public byte Reissue;
        public ushort IssuedPhase;
        public double IssuedAt;
        public double LastTickAt;
        /// <summary>The threat the play is about (a pooled contact's entity id; 0 = none).</summary>
        public long ThreatId;
        public Vector3 ThreatPoint;
        public Vector3 IssuedThreatPoint;
        /// <summary>The commander's own EQS sensor (a play that asks the terrain).</summary>
        public EqsSensorHandle Sensor;
        public uint LastAnswerTick;
        public byte HasAnswer;
        public Vector3 Answer;
        /// <summary>A value the play computes once per issue (e.g. the bound's step).</summary>
        public Vector3 Scratch;
        public fixed float TargetX[UnitRoster.Capacity];
        public fixed float TargetY[UnitRoster.Capacity];
        public fixed float TargetZ[UnitRoster.Capacity];
        /// <summary>⭐ <c>CE-2123</c> S2-G — the squad state this play LEFT at the end of its last tick (kind, phase, partition).
        /// When the squad no longer holds it, another drill ran on the squad meanwhile (this one was paused under a reaction):
        /// the runner restores it and re-sends every order.</summary>
        public ushort OwnKind, OwnPhase;
        public ElementPartition OwnElements;
    }

    /// <summary>⭐ <c>CE-2123</c> — what a play sees each tick.</summary>
    public readonly struct SquadPlayContext
    {
        public readonly EntityRepository World;
        public readonly Entity Commander;
        public readonly double Now;
        public readonly UnitRoster Roster;

        public SquadPlayContext(EntityRepository world, Entity commander, double now, in UnitRoster roster)
        {
            World = world; Commander = commander; Now = now; Roster = roster;
        }

        public ref SquadCognitiveState Squad => ref World.GetComponentRW<SquadCognitiveState>(Commander);

        /// <summary>Member <paramref name="i"/> when it is alive and has a body.</summary>
        public bool Live(int i, out Entity member)
        {
            member = Roster.SubordinateEntities[i];
            return !member.IsNull && CombatLife.IsAlive(World, member) && World.HasComponent<SimTransform>(member);
        }

        public Vector3 At(Entity member) => World.GetComponentRO<SimTransform>(member).Position;

        /// <summary>The mean position of the live members of <paramref name="element"/>.</summary>
        public Vector3 Centre(byte element, out bool any)
        {
            ref readonly var squad = ref World.GetComponentRO<SquadCognitiveState>(Commander);
            var sum = Vector3.Zero; int n = 0;
            for (int i = 0; i < Roster.Count; i++)
            {
                if (squad.Elements.MemberElements[i] != element || !Live(i, out var m)) continue;
                sum += At(m); n++;
            }
            any = n > 0;
            return n > 0 ? sum / n : Vector3.Zero;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> — ONE squad manoeuvre as configuration over the library: its phase table (<see cref="Shell"/>), how it
    /// partitions the roster, what each member is told per phase, when a phase is complete and when the whole play ends.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §3.2.
    /// </summary>
    public interface ISquadPlay<TParams> where TParams : unmanaged
    {
        SquadHsmShell Shell { get; }
        ushort ManeuverKind { get; }
        ushort StartPhase { get; }
        ushort AbortPhase { get; }
        byte ElementCount { get; }

        /// <summary>First tick(s): fill the partition inputs and Go, Wait for a precondition, or Fail.</summary>
        PlayStart Start(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition);
        /// <summary>Every tick, before anything is decided (track the threat, own a sensor, set <see cref="SquadPlayState.Reissue"/>).</summary>
        void Update(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx);
        /// <summary>Running, or how the play ended.</summary>
        NodeStatus Outcome(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx);
        /// <summary>The current phase's orders are done ⇒ the event the library shell advances on.</summary>
        bool PhaseComplete(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt);
        /// <summary>Once per issue, before the orders (compute what all members share).</summary>
        void BeginIssue(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx);
        MemberOrder OrderFor(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at);
        /// <summary>The play is left (ended, aborted, deactivated): release what it owns.</summary>
        void Release(ref SquadPlayState ws, EntityRepository world);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> (S1) — runs ANY squad play on the commander. Nothing here decides a phase — the library shell does, with the
    /// play's own table; the runner TRANSLATES a phase into one CombatPosture order per member (Superior) and FEEDS the shell what the
    /// play detects. It re-sends every order after a pause (a resumed task keeps its state but sends nothing again — S1-G).
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §3.
    /// </summary>
    public static class SquadManeuverRunner
    {
        /// <summary>A mover this close to its point has arrived (m).</summary>
        public const float MoverArrivalMetres = 3f;

        /// <summary>A gap this long between two ticks means the task was paused and resumed (s).</summary>
        public const double ResumeGapSeconds = 0.5;

        public static NodeStatus Tick<TPlay, TParams>(ref TParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            where TPlay : struct, ISquadPlay<TParams>
            where TParams : unmanaged
        {
            var play = default(TPlay);
            if (!world.HasComponent<UnitRoster>(self) || !world.HasComponent<SquadCognitiveState>(self)) return NodeStatus.Failure;
            var roster = world.GetComponentRO<UnitRoster>(self);
            if (roster.Count == 0) return NodeStatus.Failure;
            double now = Now(world);
            var ctx = new SquadPlayContext(world, self, now, in roster);

            if (ws.Started == 0)
            {
                Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[roster.Count];
                var start = play.Start(ref p, ref ws, in ctx, inputs);
                if (start == PlayStart.Fail) return NodeStatus.Failure;
                if (start == PlayStart.Wait) { ws.LastTickAt = now; return NodeStatus.Running; }
                ref var squad0 = ref ctx.Squad;
                ElementPartitionPrimitive.Partition(ref squad0, inputs, play.ElementCount, 0f, out _);
                squad0.ManeuverKind = play.ManeuverKind;
                squad0.PhaseId = play.StartPhase;
                ws.Started = 1;
                ws.StartedAt = now;
                ws.IssuedPhase = ushort.MaxValue;
            }
            else
            {
                if (now - ws.LastTickAt > ResumeGapSeconds) ws.Reissue = 1;   // ⭐ S1-G — back from a pause
                if (!StillOurs(in ws, in ctx.Squad)) Retake(ref ws, ref ctx.Squad);   // ⭐ S2-G — another drill used the squad
            }
            ws.LastTickAt = now;

            if (ctx.Squad.PhaseId == play.AbortPhase) { play.Release(ref ws, world); return NodeStatus.Failure; }
            play.Update(ref p, ref ws, in ctx);
            var outcome = play.Outcome(ref p, ref ws, in ctx);
            if (outcome != NodeStatus.Running) { play.Release(ref ws, world); return outcome; }

            // ⭐ CE-2125 (§7.2 L2) — the leader fell: the commander is SILENT (no new orders, no phase change); members keep their
            //   orders and their own SOP. When it ends, every order goes out again.
            if (SquadLeadership.Silent(world, self, now)) { ws.Reissue = 1; Remember(ref ws, in ctx.Squad); return NodeStatus.Running; }

            if (ws.IssuedPhase == ctx.Squad.PhaseId && play.PhaseComplete(ref p, ref ws, in ctx, out var evt))
            {
                Span<PhaseEvent> events = stackalloc PhaseEvent[1];
                events[0] = new PhaseEvent(evt);
                play.Shell.Tick(ref ctx.Squad, events, (uint)world.GetSingleton<GlobalTime>().FrameNumber);
                if (ctx.Squad.PhaseId == play.AbortPhase) { play.Release(ref ws, world); return NodeStatus.Failure; }
                outcome = play.Outcome(ref p, ref ws, in ctx);
                if (outcome != NodeStatus.Running) { play.Release(ref ws, world); return outcome; }
            }
            if (ctx.Squad.PhaseId != ws.IssuedPhase || ws.Reissue != 0) Issue<TPlay, TParams>(ref p, ref ws, in ctx);
            Remember(ref ws, in ctx.Squad);
            return NodeStatus.Running;
        }

        /// <summary>⭐ S2-G — does the squad still hold what this play left there?</summary>
        private static unsafe bool StillOurs(in SquadPlayState ws, in SquadCognitiveState squad)
        {
            if (squad.ManeuverKind != ws.OwnKind || squad.PhaseId != ws.OwnPhase) return false;
            var mine = ws.OwnElements;
            var now  = squad.Elements;
            return new ReadOnlySpan<byte>(&mine, sizeof(ElementPartition)).SequenceEqual(new ReadOnlySpan<byte>(&now, sizeof(ElementPartition)));
        }

        /// <summary>⭐ S2-G — put this play's kind, phase and partition back and re-send every order.</summary>
        private static void Retake(ref SquadPlayState ws, ref SquadCognitiveState squad)
        {
            squad.ManeuverKind = ws.OwnKind;
            squad.PhaseId = ws.OwnPhase;
            squad.Elements = ws.OwnElements;
            ws.Reissue = 1;
        }

        private static void Remember(ref SquadPlayState ws, in SquadCognitiveState squad)
        {
            ws.OwnKind = squad.ManeuverKind;
            ws.OwnPhase = squad.PhaseId;
            ws.OwnElements = squad.Elements;
        }

        /// <summary>One order per live member for the current phase.</summary>
        private static unsafe void Issue<TPlay, TParams>(ref TParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            where TPlay : struct, ISquadPlay<TParams>
            where TParams : unmanaged
        {
            var play = default(TPlay);
            play.BeginIssue(ref p, ref ws, in ctx);
            ref var squad = ref ctx.Squad;
            for (int i = 0; i < ctx.Roster.Count; i++)
            {
                if (!ctx.Live(i, out var m)) continue;
                var at = ctx.At(m);
                var order = play.OrderFor(ref p, ref ws, in ctx, i, squad.Elements.MemberElements[i], at);
                squad.Roles[i].RoleId = order.RoleId;
                var point = order.Kind == MemberOrderKind.Hold ? at : order.Point;
                ws.TargetX[i] = point.X; ws.TargetY[i] = point.Y; ws.TargetZ[i] = point.Z;
                if (order.Kind == MemberOrderKind.Hold) Hold(ctx.World, m, point, order.Speed);
                else Move(ctx.World, m, point, order.Speed, order.ArrivalRadius);
            }
            ws.IssuedPhase = squad.PhaseId;
            ws.IssuedAt = ctx.Now;
            ws.IssuedThreatPoint = ws.ThreatPoint;
            ws.Reissue = 0;
        }

        /// <summary>HOLD: the library CombatPosture at the member's own position — with the enemy in sight it suppresses (or lies prone)
        /// and stays (measured, §2). From the commander; the intent id IS the behaviour (no mapper).</summary>
        public static void Hold(EntityRepository world, Entity member, Vector3 at, float speed)
            => Publish(world, member, BehaviorNames.CombatPosture, JsonSerializer.Serialize(new CombatPostureParamsJsonDto
            {
                Advance = new CombatPostureAdvanceJsonDto { Objective = at, Speed = speed, ArrivalRadius = 1f },
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed));

        /// <summary>MOVE: the library AdvanceTo — there, firing on the way, whatever the posture decision would say.</summary>
        public static void Move(EntityRepository world, Entity member, Vector3 point, float speed, float arrival)
            => Publish(world, member, BehaviorNames.AdvanceTo, JsonSerializer.Serialize(new AdvanceToParamsJsonDto
            {
                Objective = point, Speed = speed, ArrivalRadius = arrival,
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed));

        private static void Publish(EntityRepository world, Entity member, string behaviour, string json)
            => world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = member, IntentId = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior,
            });

        /// <summary>Every live member of <paramref name="element"/> is within <see cref="MoverArrivalMetres"/> of its point.</summary>
        public static unsafe bool ElementArrived(in SquadPlayState ws, in SquadPlayContext ctx, byte element)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            for (int i = 0; i < ctx.Roster.Count; i++)
            {
                if (squad.Elements.MemberElements[i] != element || !ctx.Live(i, out var m)) continue;
                var at = ctx.At(m);
                float dx = at.X - ws.TargetX[i], dy = at.Y - ws.TargetY[i];
                if (dx * dx + dy * dy > MoverArrivalMetres * MoverArrivalMetres) return false;
            }
            return true;
        }

        /// <summary>True when any member is alive.</summary>
        public static bool AnyLive(in SquadPlayContext ctx)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) if (ctx.Live(i, out _)) return true;
            return false;
        }

        /// <summary><see cref="SquadPlayState.HasAnswer"/> when the terrain query found no point.</summary>
        public const byte NoAnswer = 2;

        /// <summary>
        /// ⭐ <c>CE-2123</c> — the COMMANDER asks the terrain: its own child EQS sensor at <paramref name="site"/> running
        /// <paramref name="blueprintId"/>, measured from <paramref name="from"/> (slot 0), aimed at <paramref name="at"/> (slot 1). Solved
        /// on SimHost. The first answer lands in <see cref="SquadPlayState.Answer"/> (<see cref="SquadPlayState.HasAnswer"/> = 1, and a
        /// re-issue), an empty one sets <see cref="NoAnswer"/>; either way the sensor goes. Call every tick until it answers.
        /// ⭐ The one implementation the flank (S1) and the retreat (S2) plays share.
        /// </summary>
        public static void AskTerrain(ref SquadPlayState ws, in SquadPlayContext ctx, uint blueprintId, int site, float radius, Entity from, Entity at)
        {
            var world = ctx.World;
            var child = ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId) ? ws.Sensor.ChildId : Entity.Null;
            if (child.IsNull)
            {
                if (from.IsNull || at.IsNull) { ws.HasAnswer = NoAnswer; return; }
                var config = new EqsSensor
                {
                    BlueprintId         = blueprintId,
                    Epoch               = 1,
                    SearchRadius        = radius,
                    PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
                    ScoreDeltaThreshold = 0.05f,
                    ContextSlot0        = from,
                    ContextSlot1        = at,
                };
                child = EqsChildSensor.Ensure(world, ctx.Commander, site, in config);
                if (child.IsNull) return;
                ws.Sensor = new EqsSensorHandle(child);
                ws.LastAnswerTick = 0;
            }
            if (!world.HasComponent<EqsCognitiveBuffer>(child)) return;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(child);
            if (!buffer.IsReady || buffer.LastUpdateTick == ws.LastAnswerTick) return;
            ws.LastAnswerTick = buffer.LastUpdateTick;
            if (buffer.Count == 0) ws.HasAnswer = NoAnswer;
            else
            {
                var top = buffer.GetTop();
                ws.Answer = new Vector3(top.PositionX, top.PositionY, top.PositionZ);
                ws.HasAnswer = 1;
                ws.Reissue = 1;
            }
            DropSensor(ref ws, world);
        }

        /// <summary>⭐ <c>CE-2123</c> — the squad's top IDENTIFIED pooled contact (highest threat), preferring <paramref name="keep"/>
        /// while it is pooled. A heard-only contact has no entity to aim at and is skipped.</summary>
        public static bool TopContact(in SquadPlayContext ctx, long keep, out SquadContact contact)
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

        /// <summary>The first live member of <paramref name="element"/> (or any element when 255).</summary>
        public static Entity FirstLive(in SquadPlayContext ctx, byte element = 255)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            for (int i = 0; i < ctx.Roster.Count; i++)
                if ((element == 255 || squad.Elements.MemberElements[i] == element) && ctx.Live(i, out var m)) return m;
            return Entity.Null;
        }

        /// <summary>The sensor the play owns goes.</summary>
        public static void DropSensor(ref SquadPlayState ws, EntityRepository world)
        {
            if (ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId)) EqsChildSensor.Destroy(world, ws.Sensor.ChildId);
            ws.Sensor = default;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }
}
