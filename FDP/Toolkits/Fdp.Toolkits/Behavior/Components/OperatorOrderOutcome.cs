using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐ <c>CE-3167</c> (<c>docs/DESIGN_Entity_Control.md</c> §4.1) — how the unit's last OPERATOR order ended, so an operator
    /// (or the AI player over HTTP) can tell "finished" from "failed" after the run has been cleared. Written by
    /// <c>BrainTickSystem</c> when a run of origin <see cref="BehaviorOrigin.Operator"/> finishes; on the brain node only.
    /// <para>A running order is read from <see cref="BehaviorState"/> itself; this records only the END of one.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.OperatorOrderOutcome)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct OperatorOrderOutcome
    {
        /// <summary>The finished behaviour (<see cref="BehaviorState.ActiveBehaviorHash"/> of the run).</summary>
        public int BehaviorHash;

        /// <summary>The finished run (<see cref="BehaviorState.InstanceId"/>).</summary>
        public uint InstanceId;

        /// <summary>Succeeded · Failed · Faulted.</summary>
        public BehaviorOutcome Outcome;

        /// <summary>Why it faulted (<see cref="BehaviorFaultCode.None"/> otherwise).</summary>
        public BehaviorFaultCode FaultCode;

        /// <summary>Sim time it ended (<c>GlobalTime.TotalTime</c>; 0 on a world without the clock).</summary>
        public double FinishedAt;

        /// <summary>Records the end of an operator run on <paramref name="entity"/> (registers the type on first use).</summary>
        public static void Record(EntityRepository repo, Entity entity, int behaviorHash, uint instanceId,
            BehaviorOutcome outcome, BehaviorFaultCode fault)
        {
            if (!repo.TryGetTable(typeof(OperatorOrderOutcome), out _)) repo.RegisterComponent<OperatorOrderOutcome>();
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0;
            var value = new OperatorOrderOutcome
            {
                BehaviorHash = behaviorHash, InstanceId = instanceId, Outcome = outcome, FaultCode = fault, FinishedAt = now,
            };
            if (repo.HasComponent<OperatorOrderOutcome>(entity)) repo.SetComponent(entity, value);
            else repo.AddComponent(entity, value);
        }
    }
}
