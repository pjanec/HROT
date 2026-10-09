using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Utility;
using Fbt.Kernel;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2092</c> — the tunables of <see cref="EqsTacticsNodes"/> (what a designer edits on the tree's node).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsTacticsParams
    {
        /// <summary>How far from the unit the query looks (m).</summary>
        public float SearchRadius;
        /// <summary>TakeCover only: a new best point closer than this to the current goal does not move the unit again (m).</summary>
        public float MinRepositionMetres;
        /// <summary>Travel speed (m/s).</summary>
        public float Speed;
        /// <summary>Distance from the point that counts as arrived (m).</summary>
        public float ArrivalRadius;
        /// <summary>The sensor publishes a new answer only when a top score moved by more than this (EQS ScoreDelta policy).</summary>
        public float ScoreDeltaThreshold;
        /// <summary>Which forces count as threats for the query's exposure scoring (bit N = force N; 0 = every acquired contact).</summary>
        public uint FactionFilter;
        /// <summary>⭐ <c>CE-2108</c> (§9.6a) — seconds between shots when <see cref="FireWhileMoving"/> (0 ⇒ 1 s).</summary>
        public float FireCooldownSeconds;
        /// <summary>⭐ <c>CE-2108</c> (§9.6a) — 1 = fire at the top threat while moving there (the ROE still gates every shot).
        /// A manoeuvre setting, not an ROE one: it does not outlive the run.</summary>
        public byte FireWhileMoving;
    }

    /// <summary>⭐ <c>CE-2092</c> — the node's own memory between ticks (a node-scoped working state).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsTacticsState
    {
        /// <summary>The run's own child sensor.</summary>
        public EqsSensorHandle Sensor;
        /// <summary>The threat the sensor is pointed at (slot 1).</summary>
        public Entity Threat;
        /// <summary>⭐ <c>CE-3063</c> ③ — the HEARD contact it is pointed at instead (its memory id; 0 = none) …</summary>
        public long HeardId;
        /// <summary>… and the point it was pointed at (the sensor's context point 1).</summary>
        public Vector3 HeardPoint;
        /// <summary>Where the unit was last sent.</summary>
        public Vector3 Goal;
        /// <summary>The answer stamp (<see cref="EqsCognitiveBuffer.LastUpdateTick"/>) last acted on — one look per answer.</summary>
        public uint LastAnswerTick;
        /// <summary>1 once a move was issued.</summary>
        public byte Moving;
        /// <summary>⭐ <c>CE-2108</c> — what the weapon is aimed at while moving (<see cref="EqsTacticsParams.FireWhileMoving"/>).</summary>
        public EngageState Fire;
        /// <summary>⭐ <c>CE-2108</c> (§9.8 G6) — where the threat was when the current move was issued: Flank / FiringPosition
        /// re-route only when the THREAT has moved, never because the unit's own walk changed a self-relative answer.</summary>
        public Vector3 ThreatAtMove;
    }

    /// <summary>
    /// ⭐ <c>CE-3063</c> ③ — what a sensor hides FROM: an identified threat (an entity), or a HEARD contact (its memory id and its
    /// fused position — 🔒 user <c>2026-10-05</c>: "hide from a point is OK"). 📄 <c>docs/DESIGN_Thermal_And_Acoustic_Sensing.md</c> §8.
    /// </summary>
    public readonly record struct ThreatAim(Entity Entity, long HeardId, Vector3 Point)
    {
        /// <summary>True when the aim is a heard contact's point.</summary>
        public bool IsPoint => Entity.IsNull && HeardId != 0;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2092</c> / <c>CE-2093</c> — take cover and fall back, as shared C# nodes a BTree (and an HSM) binds.
    /// 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §2 (R-204).
    /// <para>Each tick: the unit's top threat (the starter <c>ThreatRankingDecision</c> over its <c>TargetMemory</c>) → the
    /// run's own standing sensor pointed at it (re-pointed with a new epoch when the threat changes) → on each NEW answer,
    /// a pathed MoveTo to the best point (<see cref="LocomotionMoveTo"/>).</para>
    /// </summary>
    public static class EqsTacticsNodes
    {
        /// <summary>The sensor sites (with the run's owner stamp, CE-485, they find this node's sensor again).</summary>
        public const int TakeCoverSite = 0x20920001;
        /// <summary>See <see cref="TakeCoverSite"/>.</summary>
        public const int FallBackSite  = 0x20930001;
        /// <summary>⭐ <c>CE-2108</c> — see <see cref="TakeCoverSite"/>.</summary>
        public const int FlankSite = 0x21080001, FiringPositionSite = 0x21080002;

        /// <summary>⭐ <c>CE-2108</c> (§9.6 F4) — how the ONE body <see cref="Run"/> flies a manoeuvre.</summary>
        [System.Flags]
        private enum Mode : byte
        {
            /// <summary>TakeCover: re-position for as long as there is a threat; never ends on its own.</summary>
            None = 0,
            /// <summary>Success on arrival (the sensor goes, the unit stays).</summary>
            EndOnArrival = 1,
            /// <summary>A new answer ≥ <see cref="EqsTacticsParams.MinRepositionMetres"/> from the goal moves the unit again.</summary>
            Reposition = 2,
            /// <summary>Needs an IDENTIFIED threat (to see / shoot at); none ⇒ Failure. Else a heard point will do, none ⇒ Success.</summary>
            NeedsEntity = 4,
            /// <summary>⭐ §9.8 G6 — re-position only once the THREAT has moved ≥ <see cref="EqsTacticsParams.MinRepositionMetres"/>
            /// since the move was issued. 📐 Both templates score relative to the unit's CURRENT position (the flank's bearing
            /// reference is slot 0 = self; the firing position prefers near self), so as the unit walks the best point walks
            /// with it — re-routing on every such answer would chase it round the target.</summary>
            RepositionOnThreatMove = 8,
        }

        /// <summary>The starter threat ranking (<see cref="ThreatRankingDecision"/>'s asset id).</summary>
        public static readonly int ThreatRankingId = UtilityDecisionCatalog.ComputeId("1a4f7c20-3b9e-4d18-8a01-threat0000001");

        private static UtilityScorer? _scorer;

        /// <summary>
        /// Keeps the unit in cover from its top threat: Running while it has one, re-positioning only when a NEW answer's
        /// best point is at least <see cref="EqsTacticsParams.MinRepositionMetres"/> from the current goal. Success when
        /// it has nothing left to hide from; Failure when it cannot move.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus TakeCover(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Run(ref p, ref ws, self, world, FindCoverFromTarget.BlueprintId, TakeCoverSite, Mode.Reposition);

        /// <summary>
        /// Falls back once to a point hidden from the top threat and far from it: one move, Success on arrival. The sensor
        /// is re-pointed only until the move is issued.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus FallBack(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Run(ref p, ref ws, self, world, FindSafeRetreatPoint.BlueprintId, FallBackSite, Mode.EndOnArrival);

        /// <summary>
        /// ⭐ <c>CE-2108</c> — moves to a FLANK on the top identified threat (<see cref="FindFlankingPosition"/>: a point that sees
        /// it, side-on to the threat→unit line), re-positioning while it moves; Success on arrival (the tree then fires).
        /// Failure when no threat is identified (a heard point cannot be seen from anywhere) or the unit cannot move.
        /// 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §9.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Flank(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Run(ref p, ref ws, self, world, FindFlankingPosition.BlueprintId, FlankSite,
                   Mode.EndOnArrival | Mode.RepositionOnThreatMove | Mode.NeedsEntity);

        /// <summary>
        /// ⭐ <c>CE-2108</c> — moves to a FIRING POSITION on the top identified threat (<see cref="FindOpenFiringPosition"/>: a
        /// nearby point that sees it, higher and less exposed scoring more), as <see cref="Flank"/> does. 📄 §9.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus FiringPosition(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Run(ref p, ref ws, self, world, FindOpenFiringPosition.BlueprintId, FiringPositionSite,
                   Mode.EndOnArrival | Mode.RepositionOnThreatMove | Mode.NeedsEntity);

        /// <summary>
        /// ⭐⭐ <c>CE-2108</c> (§9.6 F4) — the ONE body of the four manoeuvres: the top threat → the run's own sensor with
        /// <paramref name="template"/> pointed at it → on each NEW answer a pathed MoveTo to the best point; <paramref name="mode"/>
        /// says whether it ends on arrival, re-positions while moving and needs an identified threat. With
        /// <see cref="EqsTacticsParams.FireWhileMoving"/> it also fires at the top threat while the move runs (§9.6a).
        /// </summary>
        private static NodeStatus Run(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world,
                                      uint template, int site, Mode mode)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            bool endOnArrival = (mode & Mode.EndOnArrival) != 0;
            bool onThreatMove = (mode & Mode.RepositionOnThreatMove) != 0;
            bool reposition = onThreatMove || (mode & Mode.Reposition) != 0;

            if (endOnArrival && ws.Moving == 1)
            {
                var status = LocomotionMoveTo.Status(world, self);
                if (status == NodeStatus.Success)
                {
                    Release(ref ws, self, world, stopMoving: false);
                    return NodeStatus.Success;
                }
                if (status == NodeStatus.Running && !reposition)
                {
                    FireOnTheMove(ref p, ref ws, self, world);
                    return NodeStatus.Running;
                }
                if (status != NodeStatus.Running)
                {
                    ws.Moving = 0;   // failed: ask again
                    ws.LastAnswerTick = 0;
                }
            }

            ThreatAim threat;
            if ((mode & Mode.NeedsEntity) != 0)
            {
                if (!TopThreat(world, self, ws.Threat, out var seen))
                {
                    Release(ref ws, self, world);
                    return NodeStatus.Failure;   // nothing identified to flank / shoot at: the parent picks something else
                }
                threat = new ThreatAim(seen, 0, default);
            }
            else if (!TopAim(world, self, new ThreatAim(ws.Threat, ws.HeardId, ws.HeardPoint), out threat))
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;
            }

            var child = EnsureSensor(ref p, ref ws, self, world, threat, template, site);
            if (child.IsNull) return NodeStatus.Running;

            bool hasThreatPos = ThreatPosition(world, self, threat, out var threatPos);
            if (TryNewAnswer(world, child, ref ws, out var best)
                && (ws.Moving == 0
                    || (reposition && Vector3.Distance(best, ws.Goal) >= p.MinRepositionMetres
                        && (!onThreatMove || (hasThreatPos && Vector3.Distance(threatPos, ws.ThreatAtMove) >= p.MinRepositionMetres)))))
            {
                LocomotionMoveTo.Issue(world, self, best, p.Speed, p.ArrivalRadius);
                ws.Goal = best;
                ws.Moving = 1;
                ws.ThreatAtMove = threatPos;
            }
            else if (!endOnArrival && ws.Moving == 1 && LocomotionMoveTo.Status(world, self) == NodeStatus.Failure)
            {
                ws.Moving = 0;   // the move failed (or was taken over): the next answer moves again
            }
            FireOnTheMove(ref p, ref ws, self, world);
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2108</c> — where the unit's memory places <paramref name="aim"/>: a heard contact's point, else the
        /// remembered entity's fused position (the Brain reads its memory, not the threat's transform).</summary>
        // ⭐ CE-3136 P-6 — internal: PeekAndFire's blind burst aims there.
        internal static unsafe bool ThreatPosition(EntityRepository world, Entity self, in ThreatAim aim, out Vector3 position)
        {
            position = aim.Point;
            if (aim.IsPoint) return true;
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            long id = (long)aim.Entity.PackedValue;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == id) { position = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]); return true; }
            return false;
        }

        /// <summary>⭐ <c>CE-2108</c> (§9.6a) — while a move runs and the params ask for it, fire at the top threat through the
        /// posture's ONE fire step (the ROE gates every shot in the executor); otherwise the weapon this node aimed stops.</summary>
        private static void FireOnTheMove(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
        {
            if (p.FireWhileMoving == 0) return;
            if (ws.Moving == 1 && LocomotionMoveTo.Status(world, self) == NodeStatus.Running)
                PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds > 0f ? p.FireCooldownSeconds : 1f);
            else
                PostureNodes.StopFiring(world, self, ref ws.Fire);
        }

        /// <summary>Leaving the node (abort / branch switch): its sensor goes, and a move it issued stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.TakeCover")]
        public static void Deactivate_TakeCover(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        /// <summary>See <see cref="Deactivate_TakeCover"/>.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.FallBack")]
        public static void Deactivate_FallBack(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        /// <summary>See <see cref="Deactivate_TakeCover"/> (the weapon it aimed stops too).</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.Flank")]
        public static void Deactivate_Flank(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        /// <summary>See <see cref="Deactivate_Flank"/>.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.FiringPosition")]
        public static void Deactivate_FiringPosition(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        // ── the shared steps ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The threat to hide from: the ranking's top. ⚠ When nothing scores above zero (nothing in sight — e.g. the unit is
        /// already hidden) the ranking ties and its order is the memory's; the CURRENT threat is then kept while it is still
        /// remembered, so the sensor is not re-pointed back and forth. False = nothing remembered.
        /// </summary>
        internal static bool TopThreat(EntityRepository world, Entity self, Entity current, out Entity threat)   // ⭐ CE-2073 — shared with PostureNodes
        {
            threat = Entity.Null;
            if (!world.HasComponent<TargetMemory>(self)) return false;
            if (world.GetComponentRO<TargetMemory>(self).Count == 0) return false;

            UtilityDecisionCatalog.EnsureRegistered();
            _scorer ??= new UtilityScorer(UtilityDecisionCatalog.Shared);
            if (!_scorer.TopCandidate(world, self, ThreatRankingId, 0, out Entity top, out float score)) return false;

            threat = score <= 0f && !current.IsNull && Remembers(world, self, current) ? current : top;
            return !threat.IsNull;
        }

        /// <summary>
        /// ⭐ <c>CE-3063</c> ③ — what to hide from: <see cref="TopThreat"/> when the unit knows an ENTITY; else a HEARD contact — the
        /// one it already hides from while still remembered (so the sensor is not re-pointed between two shots), else the freshest
        /// heard slot. False = nothing remembered at all. ⚠ An identified threat always wins over a heard one.
        /// </summary>
        internal static unsafe bool TopAim(EntityRepository world, Entity self, in ThreatAim current, out ThreatAim aim)
        {
            aim = default;
            if (TopThreat(world, self, current.Entity, out var threat)) { aim = new ThreatAim(threat, 0, default); return true; }
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);

            int slot = -1;
            for (int i = 0; current.HeardId != 0 && i < mem.Count; i++)
                if (mem.EntityIds[i] == current.HeardId && TargetMemory.IsAnonymous(in mem, i)) { slot = i; break; }
            for (int i = 0; slot < 0 && i < mem.Count; i++)
                if (TargetMemory.IsAnonymous(in mem, i)) slot = i;   // slots are sorted by score: the first is the freshest
            if (slot < 0) return false;

            aim = new ThreatAim(Entity.Null, mem.EntityIds[slot], new Vector3(mem.PositionsX[slot], mem.PositionsY[slot], mem.PositionsZ[slot]));
            return true;
        }

        /// <summary>⭐ <c>CE-3063</c> — points a sensor config at <paramref name="aim"/>: slot 1 for an entity, context point 1 for a
        /// heard contact.</summary>
        internal static void Point(ref EqsSensor config, in ThreatAim aim)
        {
            config.ContextSlot1 = aim.Entity;
            config.ContextPoint1 = aim.IsPoint ? aim.Point : default;
            config.ContextPointMask = aim.IsPoint ? EqsSensor.Point1Bit : (byte)0;
        }

        /// <summary>A heard contact's fused point moving this far re-points the sensor (m).</summary>
        internal const float RepointMetres = 2f;

        /// <summary>⭐ <c>CE-3063</c> — does the sensor need re-pointing from <paramref name="was"/> to <paramref name="now"/>?</summary>
        internal static bool Moved(in ThreatAim was, in ThreatAim now)
            => !now.Entity.Equals(was.Entity) || now.HeardId != was.HeardId
            || (now.IsPoint && Vector3.Distance(now.Point, was.Point) > RepointMetres);

        /// <summary>Is the sensor already pointed at <paramref name="aim"/>?</summary>
        internal static bool PointsAt(in EqsSensor s, in ThreatAim aim)
        {
            if (!s.ContextSlot1.Equals(aim.Entity)) return false;
            bool hasPoint = (s.ContextPointMask & EqsSensor.Point1Bit) != 0;
            return aim.IsPoint ? hasPoint && Vector3.Distance(s.ContextPoint1, aim.Point) <= RepointMetres : !hasPoint;
        }

        private static unsafe bool Remembers(EntityRepository world, Entity self, Entity e)
        {
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            for (int i = 0; i < mem.Count; i++)
                if (new Entity((ulong)mem.EntityIds[i]).Equals(e)) return true;
            return false;
        }

        private static EqsSensor Config(in EqsTacticsParams p, uint template, Entity self, in ThreatAim threat)
        {
            var config = new EqsSensor
            {
                BlueprintId         = template,
                Epoch               = 1,
                SearchRadius        = p.SearchRadius,
                FactionFilter       = p.FactionFilter,
                PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
                ScoreDeltaThreshold = p.ScoreDeltaThreshold,
                ContextSlot0        = self,
            };
            Point(ref config, in threat);
            return config;
        }

        /// <summary>The run's own sensor, created on first use and re-pointed (a new epoch) when the threat changes.</summary>
        private static Entity EnsureSensor(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world,
                                           in ThreatAim threat, uint template, int site)
        {
            var config = Config(in p, template, self, in threat);
            var child = ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId) ? ws.Sensor.ChildId : Entity.Null;
            bool repoint;
            if (child.IsNull)
            {
                child = EqsChildSensor.Ensure(world, self, site, in config);
                if (child.IsNull) return Entity.Null;
                ws.Sensor = new EqsSensorHandle(child);
                repoint = !PointsAt(world.GetComponentRO<EqsSensor>(child), in threat);   // re-found from an earlier tick
            }
            else repoint = Moved(new ThreatAim(ws.Threat, ws.HeardId, ws.HeardPoint), in threat);

            if (repoint)
            {
                EqsChildSensor.Refresh(world, child, in config);
                ws.LastAnswerTick = 0;
            }
            // ⚠ the point stored is the one the sensor was pointed at, so a slowly drifting heard contact re-points once it has
            //   moved RepointMetres in all, not never.
            if (repoint || ws.HeardId != threat.HeardId) ws.HeardPoint = threat.Point;
            ws.Threat = threat.Entity;
            ws.HeardId = threat.HeardId;
            return child;
        }

        /// <summary>True once per published answer that has a best point (the buffer's per-answer stamp, as CE-2089).</summary>
        private static bool TryNewAnswer(EntityRepository world, Entity child, ref EqsTacticsState ws, out Vector3 best)
        {
            best = default;
            if (!world.HasComponent<EqsCognitiveBuffer>(child)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(child);
            if (!buffer.IsReady || buffer.LastUpdateTick == ws.LastAnswerTick) return false;
            ws.LastAnswerTick = buffer.LastUpdateTick;
            if (buffer.Count == 0) return false;
            var top = buffer.GetTop();
            best = new Vector3(top.PositionX, top.PositionY, top.PositionZ);
            return true;
        }

        private static void Release(ref EqsTacticsState ws, Entity self, EntityRepository world, bool stopMoving = true)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);   // CE-2108: a weapon this node aimed stops with it
            if (ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId)) EqsChildSensor.Destroy(world, ws.Sensor.ChildId);
            if (stopMoving && ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            ws = default;
        }
    }
}
