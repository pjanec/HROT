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
    /// the fear falls below panic). Protest and riot are states of this same machine in the later slices (CE-2237, CE-2238).
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

        [StructLayout(LayoutKind.Sequential)]
        public struct CivilianParams
        {
            public float WanderRadius, WalkSpeed, PauseMinSeconds, PauseMaxSeconds;
            public float AlarmAt, CalmBelow;
            // ⭐ CE-2236
            public float PanicAt, PanicSpeed, ShelterSearchRadius, FleeMetres, HideMinSeconds, HideMaxSeconds;
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
        }

        public static CivilianParams Defaults() => new CivilianParams
        {
            WanderRadius = DefaultWanderRadius, WalkSpeed = DefaultWalkSpeed,
            PauseMinSeconds = DefaultPauseMinSeconds, PauseMaxSeconds = DefaultPauseMaxSeconds,
            AlarmAt = DefaultAlarmAt, CalmBelow = DefaultCalmBelow,
            PanicAt = DefaultPanicAt, PanicSpeed = DefaultPanicSpeed, ShelterSearchRadius = DefaultShelterSearchRadius,
            FleeMetres = DefaultFleeMetres, HideMinSeconds = DefaultHideMinSeconds, HideMaxSeconds = DefaultHideMaxSeconds,
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
            block = new CivilianBlackboard { Params = p };
        }

        /// <summary>📄 §5d / §5e: Routine ⇄ Alarmed on his fear; Alarmed ⇒ Panic past <c>PanicAt</c>; Panic ⇒ Shelter (a building he can
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

            foreach (var s in new[] { routine, alarmed, panic, shelter, cower })
                s.On(0).GoTo("Casualty").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Wounded).Polled().Priority(1);
            routine.On(0).GoTo("Alarmed").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Frightened).Polled();
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
