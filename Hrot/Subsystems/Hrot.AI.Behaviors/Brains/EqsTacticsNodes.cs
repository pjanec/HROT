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
    }

    /// <summary>⭐ <c>CE-2092</c> — the node's own memory between ticks (a node-scoped working state).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsTacticsState
    {
        /// <summary>The run's own child sensor.</summary>
        public EqsSensorHandle Sensor;
        /// <summary>The threat the sensor is pointed at (slot 1).</summary>
        public Entity Threat;
        /// <summary>Where the unit was last sent.</summary>
        public Vector3 Goal;
        /// <summary>The answer stamp (<see cref="EqsCognitiveBuffer.LastUpdateTick"/>) last acted on — one look per answer.</summary>
        public uint LastAnswerTick;
        /// <summary>1 once a move was issued.</summary>
        public byte Moving;
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
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            if (!TopThreat(world, self, ws.Threat, out var threat))
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;
            }

            var child = EnsureSensor(ref p, ref ws, self, world, threat, FindCoverFromTarget.BlueprintId, TakeCoverSite);
            if (child.IsNull) return NodeStatus.Running;

            if (TryNewAnswer(world, child, ref ws, out var best)
                && (ws.Moving == 0 || Vector3.Distance(best, ws.Goal) >= p.MinRepositionMetres))
            {
                LocomotionMoveTo.Issue(world, self, best, p.Speed, p.ArrivalRadius);
                ws.Goal = best;
                ws.Moving = 1;
            }
            else if (ws.Moving == 1 && LocomotionMoveTo.Status(world, self) == NodeStatus.Failure)
            {
                ws.Moving = 0;   // the move failed (or was taken over): the next answer moves again
            }
            return NodeStatus.Running;
        }

        /// <summary>
        /// Falls back once to a point hidden from the top threat and far from it: one move, Success on arrival. The sensor
        /// is re-pointed only until the move is issued.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus FallBack(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;

            if (ws.Moving == 1)
            {
                var status = LocomotionMoveTo.Status(world, self);
                if (status == NodeStatus.Success)
                {
                    Release(ref ws, self, world, stopMoving: false);
                    return NodeStatus.Success;
                }
                if (status == NodeStatus.Running) return NodeStatus.Running;
                ws.Moving = 0;   // failed: ask again
                ws.LastAnswerTick = 0;
            }

            if (!TopThreat(world, self, ws.Threat, out var threat))
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;
            }

            var child = EnsureSensor(ref p, ref ws, self, world, threat, FindSafeRetreatPoint.BlueprintId, FallBackSite);
            if (child.IsNull) return NodeStatus.Running;

            if (TryNewAnswer(world, child, ref ws, out var best))
            {
                LocomotionMoveTo.Issue(world, self, best, p.Speed, p.ArrivalRadius);
                ws.Goal = best;
                ws.Moving = 1;
            }
            return NodeStatus.Running;
        }

        /// <summary>Leaving the node (abort / branch switch): its sensor goes, and a move it issued stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.TakeCover")]
        public static void Deactivate_TakeCover(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        /// <summary>See <see cref="Deactivate_TakeCover"/>.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsTacticsNodes.FallBack")]
        public static void Deactivate_FallBack(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        // ── the shared steps ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The threat to hide from: the ranking's top. ⚠ When nothing scores above zero (nothing in sight — e.g. the unit is
        /// already hidden) the ranking ties and its order is the memory's; the CURRENT threat is then kept while it is still
        /// remembered, so the sensor is not re-pointed back and forth. False = nothing remembered.
        /// </summary>
        private static bool TopThreat(EntityRepository world, Entity self, Entity current, out Entity threat)
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

        private static unsafe bool Remembers(EntityRepository world, Entity self, Entity e)
        {
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            for (int i = 0; i < mem.Count; i++)
                if (new Entity((ulong)mem.EntityIds[i]).Equals(e)) return true;
            return false;
        }

        private static EqsSensor Config(in EqsTacticsParams p, uint template, Entity self, Entity threat) => new EqsSensor
        {
            BlueprintId         = template,
            Epoch               = 1,
            SearchRadius        = p.SearchRadius,
            FactionFilter       = p.FactionFilter,
            PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
            ScoreDeltaThreshold = p.ScoreDeltaThreshold,
            ContextSlot0        = self,
            ContextSlot1        = threat,
        };

        /// <summary>The run's own sensor, created on first use and re-pointed (a new epoch) when the threat changes.</summary>
        private static Entity EnsureSensor(ref EqsTacticsParams p, ref EqsTacticsState ws, Entity self, EntityRepository world,
                                           Entity threat, uint template, int site)
        {
            var config = Config(in p, template, self, threat);
            var child = ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId) ? ws.Sensor.ChildId : Entity.Null;
            if (child.IsNull)
            {
                child = EqsChildSensor.Ensure(world, self, site, in config);
                if (child.IsNull) return Entity.Null;
                ws.Sensor = new EqsSensorHandle(child);
                if (world.GetComponentRO<EqsSensor>(child).ContextSlot1.Equals(threat))
                {
                    ws.Threat = threat;
                    return child;
                }
                ws.Threat = Entity.Null;   // re-found from an earlier tick with another threat: re-point below
            }

            if (!threat.Equals(ws.Threat))
            {
                EqsChildSensor.Refresh(world, child, in config);
                ws.Threat = threat;
                ws.LastAnswerTick = 0;
            }
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
            if (ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId)) EqsChildSensor.Destroy(world, ws.Sensor.ChildId);
            if (stopMoving && ws.Moving == 1 && world.HasComponent<LocomotionChannel>(self))
            {
                ref var loco = ref world.GetComponentRW<LocomotionChannel>(self);
                if (loco.ActiveAction == NavigationConstants.ActionIdMoveTo)
                {
                    loco.ActiveAction = 0;
                    unchecked { loco.ActionInstanceId++; }
                }
            }
            ws = default;
        }
    }
}
