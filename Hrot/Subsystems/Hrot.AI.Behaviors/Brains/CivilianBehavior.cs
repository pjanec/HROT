using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2235</c> (civilians CIV-1, CV-3, R-3013; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §2.2, §5d) — <c>CivilianHsm</c>, a
    /// civilian's ONE behaviour. Slice 1: <b>Routine</b> (he wanders about his home, lingering), <b>Alarmed</b> (what he heard frightened
    /// him: he stops and crouches until his fear fades) and <b>Casualty</b> (wounded: he lies where he fell). His fear is
    /// <see cref="CivilianState.Fear"/>, raised by <see cref="CivilianFearSystem"/> from what he hears; this machine only reads it.
    /// ⭐ <c>CE-2236</c> (CV-7, R-4035; §5e): <b>Panic</b> (he runs from the fright and looks for a building he can enter —
    /// <see cref="FindEnterableShelter"/>), <b>Shelter</b> (he runs in through a door that is not locked), <b>Inside</b> (he hides a while,
    /// then deletes himself — he is gone from every node) and <b>Cower</b> (no shelter in reach: he crouches where he is, cowering, until
    /// the fear falls below panic). Protest and riot are states of this same machine in the later slices (CE-2240, CE-2238).
    /// ⭐ <c>CE-2237</c> (CV-5; §5f): <b>Gather</b> — calm, he walks over to an incident (a person hurt nearby,
    /// <see cref="CivilianState.Focus"/>, found by <see cref="CrowdSystem"/>) and stands looking on, on a ring round it; walking there is
    /// what makes the onlookers one crowd.
    /// ⭐ <c>CE-2240</c> (CV-13; §5g): <b>Protest</b> — told to protest at a point, he stands on a ring round it, milling, his fist raised
    /// or a sign held (<see cref="Gestures"/>, replicated — every map draws it); only fear past panic ends it.
    /// </summary>
    public static class CivilianBehavior
    {
        public const float DefaultWanderRadius = 12f;
        public const float DefaultWalkSpeed = 1.4f;
        public const float DefaultPauseMinSeconds = 3f;
        public const float DefaultPauseMaxSeconds = 10f;
        public const float DefaultAlarmAt = 0.3f;
        public const float DefaultCalmBelow = 0.1f;
        public const float DefaultPanicAt = 0.6f;
        public const float DefaultPanicSpeed = 3.5f;
        public const float DefaultShelterSearchRadius = 80f;
        public const float DefaultFleeMetres = 20f;
        public const float DefaultHideMinSeconds = 60f;
        public const float DefaultHideMaxSeconds = 180f;
        /// <summary>How long a panicking man waits for the shelter query's answer before he cowers instead (s).</summary>
        public const float ShelterWaitSeconds = 3f;
        /// <summary>The site his shelter query lives under (one EQS child sensor per site).</summary>
        public const int ShelterSite = 0x22360001;
        public const float CrouchSeconds = 0.4f;
        public const float FallSeconds = 0.8f;
        // ⭐ CE-2240 — the protest (§5g)
        public const float ProtestRingMetres = 8f, ProtestRingJitterMetres = 7f, MillMetres = 3f, MillSpeed = 0.8f;
        public const float MillMinSeconds = 8f, MillMaxSeconds = 15f, FistMinSeconds = 3f, FistMaxSeconds = 6f, RestMinSeconds = 2f, RestMaxSeconds = 5f;
        /// <summary>The share of protesters who carry a sign the whole protest.</summary>
        public const float SignShare = 0.4f;
        // ⭐ CE-2238 — the riot (§5i)
        public const float DefaultRiotAt = 0.6f;
        /// <summary>How far a rioter looks for a forces person to go for, m.</summary>
        public const float RiotReachMetres = 30f;
        /// <summary>How fast a rioter closes, m/s (a jog).</summary>
        public const float RiotSpeed = 2.5f;
        /// <summary>How often a rioter looks again for the nearest forces person, s.</summary>
        public const float RiotLookSeconds = 1f;
        /// <summary>⭐ <c>CE-2238</c> part 2 (§5i.2) — a stone is thrown at a forces person between these ranges (m); closer, he uses his fist.</summary>
        public const float ThrowMinMetres = 4f, ThrowMaxMetres = 20f;
        /// <summary>⭐ <c>CE-2238</c> part 2 — one stone every this many seconds.</summary>
        public const float ThrowCooldownSeconds = 3f;
        /// <summary>⭐ <c>CE-2238</c> part 2 — the bend for stones at a stone source (s), crouched (AW-8's 1.5 s).</summary>
        public const float BendSeconds = 1.5f;
        /// <summary>⭐ <c>CE-2241</c> ① — a surrendered man lowers his hands after this long with nobody covering him (s).</summary>
        public const float SurrenderHoldSeconds = 5f;

        [StructLayout(LayoutKind.Sequential)]
        public struct CivilianParams
        {
            public float WanderRadius, WalkSpeed, PauseMinSeconds, PauseMaxSeconds;
            public float AlarmAt, CalmBelow;
            // ⭐ CE-2236
            public float PanicAt, PanicSpeed, ShelterSearchRadius, FleeMetres, HideMinSeconds, HideMaxSeconds;
            // ⭐ CE-2240
            public Vector3 ProtestAt;
            public byte HasProtest;
            // ⭐ CE-2238
            public byte Instigator;
            public float RiotAt;
        }

        /// <summary>⭐ <c>CE-2238</c> — the rioter's working memory: the forces person he goes for, the point he last ran at, his blow's clock.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RiotState
        {
            public Entity Target;
            public byte Moving;
            public Vector3 Goal;
            public double NextLookAt, NextStrikeAt;
            public double NextThrowAt;   // ⭐ CE-2238 part 2
            public double BendUntil;     // ⭐ CE-2238 part 2 — 0 = not bending
        }

        /// <summary>⭐ <c>CE-2241</c> ① — when someone last covered him (sim seconds).</summary>
        public struct SurrenderState
        {
            public double CoveredAt;
        }

        /// <summary>⭐ <c>CE-2236</c> — the panic's working memory, shared by Panic, Shelter and Inside: the shelter query, its answer,
        /// the run to it and the hide time.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct PanicState
        {
            public EqsSensorHandle Sensor;
            public double Since;
            public byte Answered, Found, Moving, Arrived, Failed, Deleted;
            public Vector3 Shelter;
            public double HideUntil;
        }

        /// <summary>⭐ <c>CE-2240</c> — the protester's working memory: his spot on the ring, when he steps again, and his gesture's clock.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ProtestState
        {
            public byte Started, Moving, Sign, FistUp;
            public int Steps;
            public Vector3 Spot;
            public double NextMillAt, NextGestureAt;
        }

        /// <summary>⭐ <c>CE-2237</c> — the onlooker's working memory: the ring point he walks to round <see cref="FocusAt"/>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GatherState
        {
            public byte Moving, Arrived;
            public Vector3 Target, FocusAt;
        }

        /// <summary>The routine's working memory: walking to <see cref="Target"/>, or lingering until <see cref="PauseUntil"/>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RoutineState
        {
            public byte Walking;
            public int Legs;
            public double PauseUntil;
            public Vector3 Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CivilianBlackboard
        {
            public CivilianParams Params;
            public RoutineState Routine;
            public PanicState Panic;   // ⭐ CE-2236
            public GatherState Gather;   // ⭐ CE-2237
            public ProtestState Protest;   // ⭐ CE-2240
            public RiotState Riot;   // ⭐ CE-2238
            public SurrenderState Surrender;   // ⭐ CE-2241
        }

        public static CivilianParams Defaults() => new CivilianParams
        {
            WanderRadius = DefaultWanderRadius, WalkSpeed = DefaultWalkSpeed,
            PauseMinSeconds = DefaultPauseMinSeconds, PauseMaxSeconds = DefaultPauseMaxSeconds,
            AlarmAt = DefaultAlarmAt, CalmBelow = DefaultCalmBelow,
            PanicAt = DefaultPanicAt, PanicSpeed = DefaultPanicSpeed, ShelterSearchRadius = DefaultShelterSearchRadius,
            FleeMetres = DefaultFleeMetres, HideMinSeconds = DefaultHideMinSeconds, HideMaxSeconds = DefaultHideMaxSeconds,
            RiotAt = DefaultRiotAt,
        };

        /// <summary>⭐ The defaults, then the authored values — a zero field keeps its default (a TKB-started civilian authors nothing).</summary>
        [BehaviorResolver(BehaviorNames.CivilianHsm)]
        public static void Resolve(in CivilianParamsJsonDto authored, ref CivilianBlackboard block, EntityRepository world, Entity self)
        {
            var p = Defaults();
            if (authored.WanderRadius > 0f) p.WanderRadius = authored.WanderRadius;
            if (authored.WalkSpeed > 0f) p.WalkSpeed = authored.WalkSpeed;
            if (authored.PauseMinSeconds > 0f) p.PauseMinSeconds = authored.PauseMinSeconds;
            if (authored.PauseMaxSeconds > 0f) p.PauseMaxSeconds = authored.PauseMaxSeconds;
            if (p.PauseMaxSeconds < p.PauseMinSeconds) p.PauseMaxSeconds = p.PauseMinSeconds;
            if (authored.AlarmAt > 0f) p.AlarmAt = authored.AlarmAt;
            if (authored.CalmBelow > 0f) p.CalmBelow = authored.CalmBelow;
            if (authored.PanicAt > 0f) p.PanicAt = authored.PanicAt;
            if (authored.PanicSpeed > 0f) p.PanicSpeed = authored.PanicSpeed;
            if (authored.ShelterSearchRadius > 0f) p.ShelterSearchRadius = authored.ShelterSearchRadius;
            if (authored.HideMinSeconds > 0f) p.HideMinSeconds = authored.HideMinSeconds;
            if (authored.HideMaxSeconds > 0f) p.HideMaxSeconds = authored.HideMaxSeconds;
            if (p.HideMaxSeconds < p.HideMinSeconds) p.HideMaxSeconds = p.HideMinSeconds;
            if (authored.Protest) { p.ProtestAt = authored.ProtestAt; p.HasProtest = 1; }   // ⭐ CE-2240
            if (authored.Instigator) p.Instigator = 1;   // ⭐ CE-2238
            if (authored.RiotAt > 0f) p.RiotAt = authored.RiotAt;
            block = new CivilianBlackboard { Params = p };
        }

        /// <summary>📄 §5d / §5e / §5f: Routine ⇄ Alarmed on his fear; Routine ⇄ Gather on an incident nearby (fear first); Alarmed ⇒ Panic past <c>PanicAt</c>; Panic ⇒ Shelter (a building he can
        /// enter) or Cower (none); Shelter ⇒ Inside on arrival (or Cower when the way fails); Cower ⇒ Alarmed once his fear falls below
        /// panic. Wounded ⇒ Casualty from every state but Inside, first (priority) — a frightened wounded man lies down. Casualty and
        /// Inside have no way out (Inside ends in his deletion).</summary>
        [Behavior(BehaviorNames.CivilianHsm, Params = typeof(CivilianParamsJsonDto), Categories = BehaviorCategory.Civilian)]
        [HsmDefinition(BehaviorNames.CivilianHsm, Curated = true, Group = "Library/Civilian", ParamsType = typeof(CivilianBlackboard))]
        public static StateMachineGraph BuildCivilianMachine()
        {
            var builder = new HsmBuilder("CivilianHsm_HSM");
            var routine = builder.State("Routine").Initial()
                .Activity<CivilianBlackboard, CivilianParams, RoutineState>(bb => bb.Params, bb => bb.Routine, Routine);
            var alarmed = builder.State("Alarmed")
                .Activity<CivilianBlackboard, CivilianParams>(bb => bb.Params, Alarmed);
            var panic = builder.State("Panic")
                .Activity<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, Panic);
            var shelter = builder.State("Shelter")
                .Activity<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, Shelter);
            builder.State("Inside")
                .Activity<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, Inside);
            var cower = builder.State("Cower")
                .Activity<CivilianBlackboard, CivilianParams>(bb => bb.Params, Cower);
            builder.State("Casualty")
                .Activity<CivilianBlackboard, CivilianParams>(bb => bb.Params, Casualty);
            var gather = builder.State("Gather")
                .Activity<CivilianBlackboard, CivilianParams, GatherState>(bb => bb.Params, bb => bb.Gather, Gather);
            var protest = builder.State("Protest")
                .Activity<CivilianBlackboard, CivilianParams, ProtestState>(bb => bb.Params, bb => bb.Protest, Protest);
            var riot = builder.State("Riot")
                .Activity<CivilianBlackboard, CivilianParams, RiotState>(bb => bb.Params, bb => bb.Riot, Riot);   // ⭐ CE-2238
            var surrender = builder.State("Surrender")
                .Activity<CivilianBlackboard, CivilianParams, SurrenderState>(bb => bb.Params, bb => bb.Surrender, Surrender);   // ⭐ CE-2241

            foreach (var s in new[] { routine, alarmed, panic, shelter, cower, gather, protest, riot, surrender })
                s.On(0).GoTo("Casualty").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Wounded).Polled().Priority(2);
            // ⭐ CE-2241 ① (§5h.3) — a man who committed a hostile act raises his hands while the forces cover him: ahead of every other
            //    way out (a wound still wins, priority 2)
            foreach (var s in new[] { routine, alarmed, panic, shelter, cower, gather, protest, riot })
                s.On(0).GoTo("Surrender").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Covered).Polled().Priority(1);
            surrender.On(0).GoTo("Alarmed").Guard<CivilianBlackboard, CivilianParams, SurrenderState>(bb => bb.Params, bb => bb.Surrender, Uncovered).Polled();
            routine.On(0).GoTo("Alarmed").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Frightened).Polled();
            routine.On(0).GoTo("Protest").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Protesting).Polled();   // ⭐ CE-2240
            protest.On(0).GoTo("Panic").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Panicked).Polled();
            protest.On(0).GoTo("Riot").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Rioting).Polled();   // ⭐ CE-2238
            riot.On(0).GoTo("Panic").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Panicked).Polled();
            riot.On(0).GoTo("Protest").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Calmed).Polled();
            routine.On(0).GoTo("Gather").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Attracted).Polled();   // ⭐ CE-2237
            gather.On(0).GoTo("Alarmed").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Frightened).Polled();
            gather.On(0).GoTo("Routine").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, NotAttracted).Polled();
            alarmed.On(0).GoTo("Panic").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Panicked).Polled();
            alarmed.On(0).GoTo("Routine").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Calm).Polled();
            panic.On(0).GoTo("Shelter").Guard<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, ShelterFound).Polled();
            panic.On(0).GoTo("Cower").Guard<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, NoShelter).Polled();
            shelter.On(0).GoTo("Inside").Guard<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, InsideNow).Polled();
            shelter.On(0).GoTo("Cower").Guard<CivilianBlackboard, CivilianParams, PanicState>(bb => bb.Params, bb => bb.Panic, ShelterFailed).Polled();
            cower.On(0).GoTo("Alarmed").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, BelowPanic).Polled();
            return builder.Build();
        }

        // ── states ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>He wanders about his home: walks to a random spot within <see cref="CivilianParams.WanderRadius"/>, lingers a random
        /// while, walks on. A man with no locomotion just stands. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Routine(ref CivilianParams p, ref RoutineState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Routine);
            double now = world.SimulationTime;
            if (ws.Walking != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Walking = 0;
                var rng = SimRng.FromSim((int)self.Index, 0x2235 + ws.Legs, (float)now);
                ws.PauseUntil = now + p.PauseMinSeconds + rng.NextSingle() * (p.PauseMaxSeconds - p.PauseMinSeconds);
                return NodeStatus.Running;
            }
            if (now < ws.PauseUntil) return NodeStatus.Running;
            if (!world.HasComponent<CivilianState>(self)) return NodeStatus.Running;
            ref readonly var mind = ref world.GetComponentRO<CivilianState>(self);
            if (mind.HasHome == 0) return NodeStatus.Running;
            ws.Target = WanderPoint(mind.Home, p.WanderRadius, (int)self.Index, ws.Legs, now);
            ws.Legs++;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.WalkSpeed, 1f, MoveGait.Upright, onSurface: true)) ws.Walking = 1;
            else ws.PauseUntil = now + p.PauseMaxSeconds;   // nothing to walk with: look again later
            return NodeStatus.Running;
        }

        /// <summary>Leaving the routine: he stops where he is; the next routine starts with a walk.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Routine")]
        public static void Deactivate_Routine(ref CivilianParams p, ref RoutineState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws.Walking = 0;
            ws.PauseUntil = 0;
        }

        /// <summary>Alarmed: he stops and crouches where he is. Running until his fear fades (the machine's guard).</summary>
        [SharedAiAction]
        public static NodeStatus Alarmed(ref CivilianParams p, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Alarmed);
            LocomotionMoveTo.Stop(world, self);
            StanceRequest.Set(world, self, StanceId.Crouched, CrouchSeconds);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the alarm: he stands up.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Alarmed")]
        public static void Deactivate_Alarmed(ref CivilianParams p, Entity self, EntityRepository world)
            => StanceRequest.Set(world, self, StanceId.Standing, CrouchSeconds);

        /// <summary>A casualty: he lies where he fell and does nothing else. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Casualty(ref CivilianParams p, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Casualty);
            LocomotionMoveTo.Stop(world, self);
            StanceRequest.Set(world, self, StanceId.Prone, FallSeconds);
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2236</c> — Panic: he runs <see cref="CivilianParams.FleeMetres"/> away from where the fright came from and asks
        /// for a building he can enter (<see cref="FindEnterableShelter"/>). The answer — a point inside, or none — waits in his working
        /// state for the machine's guards. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Panic(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Panic);
            double now = world.SimulationTime;
            if (ws.Since == 0)
            {
                var sensor = ws.Sensor;
                ws = default;
                ws.Sensor = sensor;
                ws.Since = now > 0 ? now : 1e-6;
                StanceRequest.Set(world, self, StanceId.Standing, CrouchSeconds);
                Flee(in p, self, world);
            }
            var cfg = new PostureSensorsParams { SearchRadius = p.ShelterSearchRadius, ScoreDeltaThreshold = 0.05f };
            ws.Sensor = PostureNodes.Keep(ws.Sensor, world, self, ShelterSite, FindEnterableShelter.BlueprintId, in cfg,
                                          new ThreatAim(Entity.Null, 0, default), retarget: false);
            if (ws.Answered == 0 && ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId) && world.HasComponent<EqsCognitiveBuffer>(ws.Sensor.ChildId))
            {
                ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.Sensor.ChildId);
                if (buffer.IsReady)
                {
                    ws.Answered = 1;
                    if (buffer.Count > 0)
                    {
                        var r = buffer.GetSpanRO()[0];
                        ws.Shelter = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                        ws.Found = 1;
                    }
                }
            }
            if (ws.Answered == 0 && now - ws.Since >= ShelterWaitSeconds) ws.Answered = 1;   // no answer in time: none
            return NodeStatus.Running;
        }

        /// <summary>Leaving the panic: the shelter query is dropped; the answer stays for Shelter.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Panic")]
        public static void Deactivate_Panic(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
        {
            PostureNodes.Drop(world, ws.Sensor);
            ws.Sensor = default;
            ws.Since = 0;
        }

        /// <summary>⭐ <c>CE-2236</c> — Shelter: he runs upright to the point inside; the mover opens a closed door on the way
        /// (<c>DoorPassageSystem</c>). Arrived — or simply inside the footprint — he is in; the way failing sends him to cower. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Shelter(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Shelter);
            if (ws.Arrived != 0 || ws.Failed != 0) return NodeStatus.Running;
            if (IsIndoors(world, self)) { ws.Arrived = 1; LocomotionMoveTo.Stop(world, self); return NodeStatus.Running; }
            if (ws.Moving == 0)
            {
                if (LocomotionMoveTo.Issue(world, self, ws.Shelter, p.PanicSpeed, 1f, MoveGait.Upright, onSurface: true)) ws.Moving = 1;
                else ws.Failed = 1;
                return NodeStatus.Running;
            }
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Success) ws.Arrived = 1;
            else if (status == NodeStatus.Failure) ws.Failed = 1;
            return NodeStatus.Running;
        }

        /// <summary>Leaving the run (in, or failed): he stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Shelter")]
        public static void Deactivate_Shelter(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws.Moving = 0;
        }

        /// <summary>⭐ <c>CE-2236</c> (R-4035) — Inside: he stays where he is for a random hide time, then deletes himself
        /// (<see cref="DestroyEntityCommand"/> — CGF created him, so the delete goes out on the wire, §5a M-1). Running.</summary>
        [SharedAiAction]
        public static NodeStatus Inside(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Inside);
            LocomotionMoveTo.Stop(world, self);
            double now = world.SimulationTime;
            if (ws.HideUntil == 0)
            {
                var rng = SimRng.FromSim((int)self.Index, 0x22361, (float)now);
                ws.HideUntil = now + p.HideMinSeconds + rng.NextSingle() * (p.HideMaxSeconds - p.HideMinSeconds);
            }
            if (ws.Deleted == 0 && now >= ws.HideUntil && world.HasComponent<NetworkIdentity>(self))
            {
                world.Bus.PublishManaged(new DestroyEntityCommand
                {
                    NetworkId = world.GetComponentRO<NetworkIdentity>(self).Value,
                    Reason = "civilian hid inside a building (CE-2236)",
                });
                ws.Deleted = 1;
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2236</c> — Cower: no shelter in reach, he crouches where he is and cowers (<see cref="Effects.SetCowering"/>,
        /// replicated — every map draws it). Running until his fear falls below panic.</summary>
        [SharedAiAction]
        public static NodeStatus Cower(ref CivilianParams p, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Cower);
            LocomotionMoveTo.Stop(world, self);
            StanceRequest.Set(world, self, StanceId.Crouched, CrouchSeconds);
            Effects.SetCowering(world, self, true);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the cower: he stops cowering and stands up.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Cower")]
        public static void Deactivate_Cower(ref CivilianParams p, Entity self, EntityRepository world)
        {
            Effects.SetCowering(world, self, false);
            StanceRequest.Set(world, self, StanceId.Standing, CrouchSeconds);
        }

        /// <summary>⭐ <c>CE-2237</c> — Gather: he walks to a spot on a ring round the incident (his own bearing from it, the ring
        /// <see cref="CrowdRules.RingMetres"/> plus a jitter that is his) and stands there looking on. A focus that moved sends him again. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Gather(ref CivilianParams p, ref GatherState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Gather);
            if (!world.HasComponent<CivilianState>(self) || !world.HasComponent<SimTransform>(self)) return NodeStatus.Running;
            ref readonly var mind = ref world.GetComponentRO<CivilianState>(self);
            if (mind.HasFocus == 0) return NodeStatus.Running;
            if (ws.Moving != 0 || ws.Arrived != 0)
            {
                if (Vector3.DistanceSquared(ws.FocusAt, mind.Focus) > 9f) { ws.Moving = 0; ws.Arrived = 0; }   // the focus moved: go again
                else if (ws.Moving != 0)
                {
                    var status = LocomotionMoveTo.Status(world, self);
                    if (status != NodeStatus.Running) { ws.Moving = 0; ws.Arrived = 1; }
                    return NodeStatus.Running;
                }
                else return NodeStatus.Running;
            }
            ws.FocusAt = mind.Focus;
            ws.Target = RingPoint(mind.Focus, world.GetComponentRO<SimTransform>(self).Position, (int)self.Index);
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.WalkSpeed, 1f, MoveGait.Upright, onSurface: true)) ws.Moving = 1;
            else ws.Arrived = 1;   // nothing to walk with: he looks on from where he is
            return NodeStatus.Running;
        }

        /// <summary>Leaving the onlookers: he stops; a next gathering starts with a walk.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Gather")]
        public static void Deactivate_Gather(ref CivilianParams p, ref GatherState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>⭐ <c>CE-2240</c> — Protest: he walks to his spot on a ring round the protest point, then mills — a short slow step
        /// every few seconds — with a sign held the whole time or his fist raised now and then (<see cref="Gestures"/>). Running until fear
        /// past panic (or a wound) takes him out.</summary>
        [SharedAiAction]
        public static NodeStatus Protest(ref CivilianParams p, ref ProtestState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Protest);
            MarkInstigator(in p, self, world);   // ⭐ CE-2238
            double now = world.SimulationTime;
            if (ws.Started == 0)
            {
                if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Running;
                ws = default;
                ws.Started = 1;
                var rng = SimRng.FromSim((int)self.Index, 0x22400, 0f);
                ws.Sign = rng.NextSingle() < SignShare ? (byte)1 : (byte)0;
                ws.Spot = RingPoint(p.ProtestAt, world.GetComponentRO<SimTransform>(self).Position, (int)self.Index, ProtestRingMetres, ProtestRingJitterMetres);
                ws.Moving = LocomotionMoveTo.Issue(world, self, ws.Spot, p.WalkSpeed, 1f, MoveGait.Upright, onSurface: true) ? (byte)1 : (byte)0;
                Gestures.Set(world, self, ws.Sign != 0 ? GestureKind.HoldSign : GestureKind.None);
            }
            if (ws.Moving != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return Gesture(ref ws, self, world, now);
                ws.Moving = 0;
                var rng = SimRng.FromSim((int)self.Index, 0x22401 + ws.Steps, (float)now);
                ws.NextMillAt = now + MillMinSeconds + rng.NextSingle() * (MillMaxSeconds - MillMinSeconds);
            }
            else if (now >= ws.NextMillAt && ws.NextMillAt > 0)
            {
                var rng = SimRng.FromSim((int)self.Index, 0x22402 + ws.Steps, (float)now);
                float a = rng.NextSingle() * MathF.PI * 2f, r = MillMetres * MathF.Sqrt(rng.NextSingle());
                var step = new Vector3(ws.Spot.X + r * MathF.Cos(a), ws.Spot.Y + r * MathF.Sin(a), ws.Spot.Z);
                ws.Steps++;
                if (LocomotionMoveTo.Issue(world, self, step, MillSpeed, 0.5f, MoveGait.Upright, onSurface: true)) ws.Moving = 1;
                else ws.NextMillAt = now + MillMaxSeconds;
            }
            return Gesture(ref ws, self, world, now);
        }

        /// <summary>A sign carrier holds it; the others raise a fist a few seconds, then rest a few, on their own clock.</summary>
        private static NodeStatus Gesture(ref ProtestState ws, Entity self, EntityRepository world, double now)
        {
            if (ws.Sign != 0 || now < ws.NextGestureAt) return NodeStatus.Running;
            var rng = SimRng.FromSim((int)self.Index, 0x22403 + ws.Steps * 7 + ws.FistUp, (float)now);
            ws.FistUp = ws.FistUp != 0 ? (byte)0 : (byte)1;
            ws.NextGestureAt = now + (ws.FistUp != 0
                ? FistMinSeconds + rng.NextSingle() * (FistMaxSeconds - FistMinSeconds)
                : RestMinSeconds + rng.NextSingle() * (RestMaxSeconds - RestMinSeconds));
            Gestures.Set(world, self, ws.FistUp != 0 ? GestureKind.FistRaised : GestureKind.None);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the protest: he stops, lowers his arms (the gesture goes out as None) and a next protest starts afresh.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Protest")]
        public static void Deactivate_Protest(ref CivilianParams p, ref ProtestState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            Gestures.Set(world, self, GestureKind.None);
            ws = default;
        }

        /// <summary>⭐ <c>CE-2238</c> (§5i) — Riot: fist raised, he goes for the nearest forces person within <see cref="RiotReachMetres"/> and,
        /// within <see cref="Melee.ReachMetres"/>, strikes him every <see cref="Melee.CooldownSeconds"/> (not while he is down). His first
        /// blow is his HOSTILE ACT (<see cref="HostileActs.Commit"/>: his side flips to Hostile). With nobody in reach he stands. Running
        /// until his anger falls, his fear passes panic, or a wound.</summary>
        [SharedAiAction]
        public static NodeStatus Riot(ref CivilianParams p, ref RiotState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Riot);
            MarkInstigator(in p, self, world);
            if (Gestures.Of(world, self) != GestureKind.FistRaised) Gestures.Set(world, self, GestureKind.FistRaised);
            double now = world.SimulationTime;
            if (now >= ws.NextLookAt || !Standing(world, ws.Target))
            {
                ws.Target = NearestForces(world, self, RiotReachMetres);
                ws.NextLookAt = now + RiotLookSeconds;
            }
            // ⭐ CE-2238 part 2 (§5i.2) — empty hands in a stone source: bend for stones first
            if (ws.BendUntil > 0)
            {
                if (now < ws.BendUntil) return NodeStatus.Running;
                ws.BendUntil = 0;
                ItemSources.Refill(world, self);
                StanceRequest.Set(world, self, StanceId.Standing, CrouchSeconds);
            }
            else if (!Stones.InHand(world, self) && CanFill(world, self))
            {
                if (ws.Moving != 0) { LocomotionMoveTo.Stop(world, self); ws.Moving = 0; }
                StanceRequest.Set(world, self, StanceId.Crouched, CrouchSeconds);
                ws.BendUntil = now + BendSeconds;
                return NodeStatus.Running;
            }
            if (ws.Target.IsNull)
            {
                if (ws.Moving != 0) { LocomotionMoveTo.Stop(world, self); ws.Moving = 0; }
                return NodeStatus.Running;
            }
            var target = world.GetComponentRO<SimTransform>(ws.Target).Position;
            float range = Vector2.Distance(new Vector2(target.X, target.Y), new Vector2(world.GetComponentRO<SimTransform>(self).Position.X, world.GetComponentRO<SimTransform>(self).Position.Y));
            if (range >= ThrowMinMetres && range <= ThrowMaxMetres && Stones.InHand(world, self))
            {
                if (ws.Moving != 0) { LocomotionMoveTo.Stop(world, self); ws.Moving = 0; }
                if (now >= ws.NextThrowAt && Stones.Throw(world, self, target))
                {
                    HostileActs.Commit(world, self);   // the first stone is his act, as the blow is
                    ws.NextThrowAt = now + ThrowCooldownSeconds;
                }
                return NodeStatus.Running;
            }
            if (Melee.InReach(world, self, ws.Target))
            {
                if (ws.Moving != 0) { LocomotionMoveTo.Stop(world, self); ws.Moving = 0; }
                if (now >= ws.NextStrikeAt && !Effects.IsKnockedDown(world, ws.Target) && Melee.Strike(world, self, ws.Target))
                {
                    HostileActs.Commit(world, self);
                    ws.NextStrikeAt = now + Melee.CooldownSeconds;
                }
                return NodeStatus.Running;
            }
            var at = world.GetComponentRO<SimTransform>(ws.Target).Position;
            if (ws.Moving == 0 || Vector2.Distance(new Vector2(ws.Goal.X, ws.Goal.Y), new Vector2(at.X, at.Y)) > 1f)
                if (LocomotionMoveTo.Issue(world, self, at, RiotSpeed, 0.8f, MoveGait.Upright, onSurface: true)) { ws.Moving = 1; ws.Goal = at; }
            return NodeStatus.Running;
        }

        /// <summary>Leaving the riot: he stops and lowers his fist; a next riot looks afresh.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Riot")]
        public static void Deactivate_Riot(ref CivilianParams p, ref RiotState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            if (ws.BendUntil > 0) StanceRequest.Set(world, self, StanceId.Standing, CrouchSeconds);   // left mid-bend: he stands up
            Gestures.Set(world, self, GestureKind.None);
            ws = default;
        }

        /// <summary>⭐ <c>CE-2241</c> ① (§5h.3, CV-13a) — Surrender: covered by the forces after his hostile act, he stops and raises his
        /// hands (<see cref="GestureKind.HandsUp"/> — the AI holds its fire at him, <c>HoldSurrender</c>). His side stays Hostile. Running
        /// until nobody has covered him for <see cref="SurrenderHoldSeconds"/>, or a wound.</summary>
        [SharedAiAction]
        public static NodeStatus Surrender(ref CivilianParams p, ref SurrenderState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Surrender);
            double now = world.SimulationTime;
            if (ws.CoveredAt <= 0 || !Covering.By(world, self).IsNull) ws.CoveredAt = now;
            LocomotionMoveTo.Stop(world, self);
            if (Gestures.Of(world, self) != GestureKind.HandsUp) Gestures.Set(world, self, GestureKind.HandsUp);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the surrender: his hands come down.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Surrender")]
        public static void Deactivate_Surrender(ref CivilianParams p, ref SurrenderState ws, Entity self, EntityRepository world)
        {
            Gestures.Set(world, self, GestureKind.None);
            ws = default;
        }

        /// <summary>⭐ <c>CE-2238</c> — the nearest living, standing PERSON of the forces (Friend and not of the Civilian class,
        /// <see cref="Armament.IsCombatant"/>) within <paramref name="reach"/> of <paramref name="self"/>, or <see cref="Entity.Null"/>.</summary>
        public static Entity NearestForces(EntityRepository world, Entity self, float reach)
        {
            if (!world.HasComponent<SimTransform>(self) || !world.IsComponentTypeRegistered<EntityInfo>()) return Entity.Null;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            float best = reach * reach;
            var found = Entity.Null;
            foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
            {
                if (e == self || world.GetComponentRO<EntityInfo>(e).ForceId != ForceId.Friend) continue;
                if (!Standing(world, e) || !Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.IsPerson(world, e) || !Armament.IsCombatant(world, e)) continue;
                var at = world.GetComponentRO<SimTransform>(e).Position;
                float d2 = (at.X - me.X) * (at.X - me.X) + (at.Y - me.Y) * (at.Y - me.Y);
                if (d2 >= best) continue;
                best = d2;
                found = e;
            }
            return found;
        }

        /// <summary>⭐ <c>CE-2238</c> part 2 — he stands in a stone source and his hands have room for what it gives.</summary>
        private static bool CanFill(EntityRepository world, Entity self)
            => world.HasComponent<SimTransform>(self)
               && ItemSources.At(world, world.GetComponentRO<SimTransform>(self).Position, out var source, out _)
               && PersonHands.HasRoom(world, self, source.Ammo);

        /// <summary>Alive, with health left, and not out of the fight.</summary>
        private static bool Standing(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && !(world.HasComponent<Health>(e) && world.GetComponentRO<Health>(e).Current <= 0f)
               && !Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(world, e);

        /// <summary>⭐ <c>CE-2238</c> — an instigator says so on his mind, where the <see cref="CrowdSystem"/> reads it.</summary>
        private static void MarkInstigator(in CivilianParams p, Entity self, EntityRepository world)
        {
            if (p.Instigator == 0 || !world.HasComponent<CivilianState>(self)) return;
            ref var mind = ref world.GetComponentRW<CivilianState>(self);
            if (mind.Instigator == 0) { mind.Instigator = 1; mind.Anger = 1f; }
        }

        /// <summary>⭐ <c>CE-2237</c> — where an onlooker stands: on his own bearing from the focus, <see cref="CrowdRules.RingMetres"/> out plus
        /// a jitter of up to <see cref="CrowdRules.RingJitterMetres"/> in distance and ±30° in bearing, the same for the same man on every run.</summary>
        public static Vector3 RingPoint(Vector3 focus, Vector3 me, int entityIndex)
            => RingPoint(focus, me, entityIndex, CrowdRules.RingMetres, CrowdRules.RingJitterMetres);

        /// <summary>A spot on a ring <paramref name="ring"/> + up to <paramref name="jitter"/> m round <paramref name="focus"/>, on his own bearing ±30°.</summary>
        public static Vector3 RingPoint(Vector3 focus, Vector3 me, int entityIndex, float ring, float jitter)
        {
            var rng = SimRng.FromSim(entityIndex, 0x22370, 0f);
            float bearing = MathF.Atan2(me.Y - focus.Y, me.X - focus.X);
            if (float.IsNaN(bearing)) bearing = 0f;
            bearing += (rng.NextSingle() - 0.5f) * (MathF.PI / 3f);
            float r = ring + rng.NextSingle() * jitter;
            return new Vector3(focus.X + r * MathF.Cos(bearing), focus.Y + r * MathF.Sin(bearing), focus.Z);
        }

        /// <summary>One leg away from where the fright came from (or straight on when it is unknown), upright, at panic speed.</summary>
        private static void Flee(in CivilianParams p, Entity self, EntityRepository world)
        {
            if (p.FleeMetres <= 0f || !world.HasComponent<SimTransform>(self) || !world.HasComponent<CivilianState>(self)) return;
            ref readonly var mind = ref world.GetComponentRO<CivilianState>(self);
            if (mind.HasFrightFrom == 0) return;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            var away = new Vector2(me.X - mind.FrightFrom.X, me.Y - mind.FrightFrom.Y);
            if (away.LengthSquared() < 1e-4f) return;
            away = Vector2.Normalize(away) * p.FleeMetres;
            LocomotionMoveTo.Issue(world, self, new Vector3(me.X + away.X, me.Y + away.Y, me.Z), p.PanicSpeed, 2f, MoveGait.Upright, onSurface: true);
        }

        /// <summary>He stands inside a building he could hide in (the shelter query's <see cref="InsideTest"/> question).</summary>
        public static bool IsIndoors(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<SimTransform>(self) || !world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()) return false;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            return InsideTest.IsInside(world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()!, new Vector2(at.X, at.Y), out _);
        }

        // ── guards ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>His fear has reached <see cref="CivilianParams.AlarmAt"/>.</summary>
        [SharedAiCondition]
        public static bool Frightened(ref CivilianParams p, Entity self, EntityRepository world)
            => CivilianMind.FearOf(world, self) >= p.AlarmAt;

        /// <summary>His fear has fallen below <see cref="CivilianParams.CalmBelow"/>.</summary>
        [SharedAiCondition]
        public static bool Calm(ref CivilianParams p, Entity self, EntityRepository world)
            => CivilianMind.FearOf(world, self) < p.CalmBelow;

        /// <summary>⭐ <c>CE-2236</c> — his fear has reached <see cref="CivilianParams.PanicAt"/>.</summary>
        [SharedAiCondition]
        public static bool Panicked(ref CivilianParams p, Entity self, EntityRepository world)
            => CivilianMind.FearOf(world, self) >= p.PanicAt;

        /// <summary>⭐ <c>CE-2236</c> — his fear has fallen below panic (the cowering ends).</summary>
        [SharedAiCondition]
        public static bool BelowPanic(ref CivilianParams p, Entity self, EntityRepository world)
            => CivilianMind.FearOf(world, self) < p.PanicAt;

        /// <summary>⭐ <c>CE-2241</c> ① — he has committed a hostile act and a forces person covers him (<see cref="Covering.By"/>).</summary>
        [SharedAiCondition]
        public static bool Covered(ref CivilianParams p, Entity self, EntityRepository world)
            => HostileActs.Committed(world, self) && !Covering.By(world, self).IsNull;

        /// <summary>⭐ <c>CE-2241</c> ① — nobody has covered him for <see cref="SurrenderHoldSeconds"/>: his hands come down.</summary>
        [SharedAiCondition]
        public static bool Uncovered(ref CivilianParams p, ref SurrenderState ws, Entity self, EntityRepository world)
            => ws.CoveredAt > 0 && world.SimulationTime - ws.CoveredAt >= SurrenderHoldSeconds && Covering.By(world, self).IsNull;

        /// <summary>⭐ <c>CE-2238</c> — a protester whose anger has reached <see cref="CivilianParams.RiotAt"/> riots.</summary>
        [SharedAiCondition]
        public static bool Rioting(ref CivilianParams p, Entity self, EntityRepository world)
            => p.HasProtest != 0 && world.HasComponent<CivilianState>(self) && world.GetComponentRO<CivilianState>(self).Anger >= p.RiotAt;

        /// <summary>⭐ <c>CE-2238</c> — his anger has fallen below half of <see cref="CivilianParams.RiotAt"/>: back to the protest.</summary>
        [SharedAiCondition]
        public static bool Calmed(ref CivilianParams p, Entity self, EntityRepository world)
            => !world.HasComponent<CivilianState>(self) || world.GetComponentRO<CivilianState>(self).Anger < p.RiotAt * 0.5f;

        /// <summary>⭐ <c>CE-2240</c> — he was told to protest.</summary>
        [SharedAiCondition]
        public static bool Protesting(ref CivilianParams p, Entity self, EntityRepository world) => p.HasProtest != 0;

        /// <summary>⭐ <c>CE-2237</c> — an incident draws him (<see cref="CrowdSystem"/> set his focus).</summary>
        [SharedAiCondition]
        public static bool Attracted(ref CivilianParams p, Entity self, EntityRepository world)
            => world.HasComponent<CivilianState>(self) && world.GetComponentRO<CivilianState>(self).HasFocus != 0;

        /// <summary>⭐ <c>CE-2237</c> — nothing draws him any more (the incident is gone, or out of reach).</summary>
        [SharedAiCondition]
        public static bool NotAttracted(ref CivilianParams p, Entity self, EntityRepository world)
            => !Attracted(ref p, self, world);

        /// <summary>⭐ <c>CE-2236</c> — the shelter query found a building he can enter.</summary>
        [SharedAiCondition]
        public static bool ShelterFound(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
            => ws.Answered != 0 && ws.Found != 0;

        /// <summary>⭐ <c>CE-2236</c> — the shelter query answered none (or not in time).</summary>
        [SharedAiCondition]
        public static bool NoShelter(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
            => ws.Answered != 0 && ws.Found == 0;

        /// <summary>⭐ <c>CE-2236</c> — he is in.</summary>
        [SharedAiCondition]
        public static bool InsideNow(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
            => ws.Arrived != 0;

        /// <summary>⭐ <c>CE-2236</c> — the way in failed (no path, the door locked under him).</summary>
        [SharedAiCondition]
        public static bool ShelterFailed(ref CivilianParams p, ref PanicState ws, Entity self, EntityRepository world)
            => ws.Failed != 0 && ws.Arrived == 0;

        /// <summary>He is hurt: his injury is past <see cref="InjuryState.Ok"/> (wounded, incapacitated or dead — the type's thresholds).</summary>
        [SharedAiCondition]
        public static bool Wounded(ref CivilianParams p, Entity self, EntityRepository world)
            => world.HasComponent<Health>(self) && Injury.StateOf(world, self) != InjuryState.Ok;

        /// <summary>A spot within <paramref name="radius"/> of <paramref name="home"/>, uniform over the disc, the same for the same
        /// man, leg and time on every run.</summary>
        public static Vector3 WanderPoint(Vector3 home, float radius, int entityIndex, int leg, double now)
        {
            var rng = SimRng.FromSim(entityIndex, 0x22350 + leg, (float)now);
            float angle = rng.NextSingle() * MathF.PI * 2f;
            float r = radius * MathF.Sqrt(rng.NextSingle());
            return new Vector3(home.X + r * MathF.Cos(angle), home.Y + r * MathF.Sin(angle), home.Z);
        }
    }
}
