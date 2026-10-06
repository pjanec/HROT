using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>How a behaviour run ended. 📄 <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D2.</summary>
    public enum BehaviorOutcome : byte
    {
        /// <summary>The root returned Success.</summary>
        Succeeded = 0,
        /// <summary>The root returned Failure — an ordinary end (a tree may fail on purpose, e.g. <c>CE-459</c>).</summary>
        Failed = 1,
        /// <summary>The behaviour could not do its job and SAID SO (<see cref="BehaviorFault.Raise"/>) — fail loud.</summary>
        Faulted = 2,
    }

    /// <summary>
    /// ⭐ <b><c>CE-482</c> — why a behaviour faulted.</b> 0 = no fault. Codes below <see cref="Custom"/> are the engine's own;
    /// a behaviour may use <see cref="Custom"/> and above.
    /// </summary>
    public enum BehaviorFaultCode : ushort
    {
        None            = 0,
        /// <summary>An input the behaviour cannot run without is missing or dead (e.g. the target area).</summary>
        MissingInput    = 1,
        /// <summary>A question (an EQS sensor, a request) was not answered in time.</summary>
        NoAnswerTimeout = 2,
        /// <summary>The behaviour's definition lacks what its tier needs (no BTree interpreter, no blueprint tick).</summary>
        NoDefinition    = 3,
        /// <summary>A behaviour-owned part could not be given a unique identity.</summary>
        PartIdCollision = 4,
        /// <summary>⭐ S6a — an event arrived for a blueprint Event graph whose fiber is still waiting. U-6: never dropped
        /// silently (<c>DESIGN_Unified_Behaviour_Run</c> §4a).</summary>
        EventOverflow   = 5,
        /// <summary>⭐ <c>CE-3035</c> — an SOP wrote a movement / weapon / interaction channel. The write is reverted (the
        /// task keeps its command) and the SOP stops: an SOP acts only by assigning behaviours (R-199, design §4.5).</summary>
        SopCommandedChannel = 6,
        /// <summary>⭐ <c>CE-3078</c> H3 (R-133) — a sensor node of one result FAMILY was pointed at a sensor kind of another (a
        /// ranked reader at a <c>DangerArea</c> sensor): it would read nothing for ever, so the run stops instead.
        /// 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §10.4 (the guard row).</summary>
        WrongSensorFamily = 7,
        /// <summary>First code free for behaviour-specific faults.</summary>
        Custom          = 1000,
    }

    /// <summary>
    /// ⭐ <b><c>CE-482</c> — the fault a running behaviour raised, until <c>BrainTickSystem</c> finishes the run with it.</b>
    /// Keyed to the run by <see cref="InstanceId"/>, so a latch left behind by an ended run is never applied to the next.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(BehaviorApplicationComponentIds.BehaviorFaultLatch)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BehaviorFaultLatch
    {
        /// <summary>The run that raised it (<see cref="BehaviorState.InstanceId"/>).</summary>
        public uint InstanceId;
        /// <summary>The first fault the run raised.</summary>
        public BehaviorFaultCode Code;
    }

    /// <summary>
    /// ⭐ <b><c>CE-482</c> — a behaviour faulted: the actionable notification.</b> 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c>
    /// §1 D2. Published once, when the fault is raised; consumers tell the user / the operator (<c>CE-484</c> carries it off
    /// the node). ⚠ The mission tier reacts to the run's <see cref="BehaviorFinishedEvent"/> (<see cref="BehaviorOutcome.Faulted"/>),
    /// not to this — this is for people.
    /// </summary>
    public sealed class BehaviorFaultNotification
    {
        public Entity Entity;
        /// <summary>The faulted behaviour (<see cref="BehaviorState.ActiveBehaviorHash"/>).</summary>
        public int BehaviorHash;
        /// <summary>The faulted run.</summary>
        public uint InstanceId;
        public BehaviorFaultCode Code;
        /// <summary>A human-readable reason.</summary>
        public string Message = string.Empty;
        /// <summary>Simulation time of the fault.</summary>
        public double SimTime;
    }

    /// <summary>
    /// ⭐ <b><c>CE-482</c> — FAIL LOUD.</b> 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D1/D2.
    ///
    /// <para>A behaviour that cannot do its job calls <see cref="Raise"/> — ⛔ not just a log line, and ⛔ not
    /// <c>NodeStatus.Failure</c> alone (an ordinary end). The fault is latched to the RUN, a
    /// <see cref="BehaviorFaultNotification"/> is published, and <c>BrainTickSystem</c> ends the run after its tick through
    /// the normal finish (<c>CE-449</c>) with <see cref="BehaviorOutcome.Faulted"/> — so the run's commands, channels and owned
    /// parts are released exactly as for any other end.</para>
    /// </summary>
    public static class BehaviorFault
    {
        /// <summary>Raise a fault for <paramref name="entity"/>'s current behaviour run. The FIRST fault of a run wins. No-op
        /// when the entity runs no behaviour. Returns true when this call latched the fault.</summary>
        public static bool Raise(ISimulationView view, Entity entity, BehaviorFaultCode code, string message)
        {
            if (code == BehaviorFaultCode.None) return false;
            if (view is not EntityRepository repo || !repo.IsAlive(entity) || !repo.HasComponent<BehaviorState>(entity))
                return false;
            var behavior = repo.GetComponentRO<BehaviorState>(entity);
            // ⭐ CE-3035 — inside the SOP slot's view the fault is the SOP run's, never the task's.
            if (BrainSlotScope.TryGetInstanceId(entity, out uint slotRun) && BrainSlotScope.TryGetHash(entity, out int slotHash))
            {
                behavior.InstanceId = slotRun;
                behavior.ActiveBehaviorHash = slotHash;
            }
            if (behavior.InstanceId == 0) return false;

            if (repo.HasComponent<BehaviorFaultLatch>(entity))
            {
                ref var latch = ref repo.GetComponentRW<BehaviorFaultLatch>(entity);
                if (latch.InstanceId == behavior.InstanceId && latch.Code != BehaviorFaultCode.None) return false;   // first wins
                latch.InstanceId = behavior.InstanceId;
                latch.Code       = code;
            }
            else
            {
                // ⚠ Lazily registered: a fault is rare, and a world that never faults pays nothing.
                if (!repo.TryGetTable(typeof(BehaviorFaultLatch), out _)) repo.RegisterComponent<BehaviorFaultLatch>();
                repo.AddComponent(entity, new BehaviorFaultLatch { InstanceId = behavior.InstanceId, Code = code });
            }

            // ⭐ CE-484: the operator's tab, on THIS node — the DDS egress carries it to the others (and the offline editor,
            //   which has no egress, still shows it). De-duplicated against the ingress by entity + run + behaviour.
            long entityKey = repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                             && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity)
                ? repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value
                : BehaviorFaultLog.LocalEntityKey(entity.Index);
            BehaviorFaultLog.Shared.Report(
                new BehaviorFaultLog.FaultKey(entityKey, behavior.InstanceId, behavior.ActiveBehaviorHash),
                $"#{behavior.ActiveBehaviorHash:X8}",
                entityKey >= 0 ? $"entity {entityKey}" : $"entity #{entity.Index}",
                (int)code, message ?? string.Empty);

            repo.Bus.PublishManaged(new BehaviorFaultNotification
            {
                Entity       = entity,
                BehaviorHash = behavior.ActiveBehaviorHash,
                InstanceId   = behavior.InstanceId,
                Code         = code,
                Message      = message ?? string.Empty,
                SimTime      = repo.SimulationTime,
            });
            return true;
        }

        /// <summary>The fault the run <paramref name="instanceId"/> raised, consumed (<see cref="BehaviorFaultCode.None"/> when
        /// there is none). Used by <c>BrainTickSystem</c>.</summary>
        public static BehaviorFaultCode Take(EntityRepository repo, Entity entity, uint instanceId)
        {
            if (!repo.HasComponent<BehaviorFaultLatch>(entity)) return BehaviorFaultCode.None;
            ref var latch = ref repo.GetComponentRW<BehaviorFaultLatch>(entity);
            if (latch.InstanceId != instanceId) return BehaviorFaultCode.None;
            var code = latch.Code;
            latch.Code = BehaviorFaultCode.None;
            return code;
        }

        /// <summary>Whether the run <paramref name="instanceId"/> has a fault waiting.</summary>
        public static bool IsPending(EntityRepository repo, Entity entity, uint instanceId)
            => repo.HasComponent<BehaviorFaultLatch>(entity)
               && repo.GetComponentRO<BehaviorFaultLatch>(entity) is var l
               && l.InstanceId == instanceId && l.Code != BehaviorFaultCode.None;
    }
}
