using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐ <c>CE-3170</c> (<c>docs/DESIGN_Entity_Control.md</c> §7 S2, C-5) — the enemy an ATTACK order named: <c>CombatPosture</c>
    /// with a <c>target</c>. It is valid only for the run that wrote it (<see cref="InstanceId"/> = that run's
    /// <see cref="BehaviorState.InstanceId"/>), so a later behaviour never inherits it and nothing has to remove it.
    /// <para>⭐ ONE reader: <c>Fdp.Toolkit.Utility.SquadAssignment.TargetOf</c> — the order's target comes BEFORE the squad's fire
    /// distribution there, so the threat ranking (<c>IsAssignedTarget</c>), the posture's top threat and the fire-from-cover lock
    /// all follow it. Brain node only.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DesignatedTarget)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DesignatedTarget
    {
        /// <summary>The target (<see cref="Entity.PackedValue"/>; 0 = none).</summary>
        public long TargetPacked;

        /// <summary>The run that designated it.</summary>
        public uint InstanceId;

        /// <summary>Designates <paramref name="target"/> for <paramref name="entity"/>'s running behaviour (registers the type on
        /// first use). No-op when the unit runs no behaviour.</summary>
        public static void Set(EntityRepository repo, Entity entity, Entity target)
        {
            if (!repo.IsComponentTypeRegistered<BehaviorState>() || !repo.HasComponent<BehaviorState>(entity)) return;
            if (!repo.TryGetTable(typeof(DesignatedTarget), out _)) repo.RegisterComponent<DesignatedTarget>();
            var value = new DesignatedTarget
            {
                TargetPacked = (long)target.PackedValue,
                InstanceId = repo.GetComponentRO<BehaviorState>(entity).InstanceId,
            };
            if (repo.HasComponent<DesignatedTarget>(entity)) repo.SetComponent(entity, value);
            else repo.AddComponent(entity, value);
        }

        /// <summary>The target the RUNNING behaviour of <paramref name="entity"/> designated; false when there is none (no
        /// designation, or it belongs to an earlier run).</summary>
        public static bool Of(EntityRepository repo, Entity entity, out Entity target)
        {
            target = Entity.Null;
            if (!repo.IsComponentTypeRegistered<DesignatedTarget>() || !repo.HasComponent<DesignatedTarget>(entity)) return false;
            if (!repo.HasComponent<BehaviorState>(entity)) return false;
            ref readonly var d = ref repo.GetComponentRO<DesignatedTarget>(entity);
            if (d.TargetPacked == 0 || d.InstanceId != repo.GetComponentRO<BehaviorState>(entity).InstanceId) return false;
            target = new Entity((ulong)d.TargetPacked);
            return true;
        }
    }
}
