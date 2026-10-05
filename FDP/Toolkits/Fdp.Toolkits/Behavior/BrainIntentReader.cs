using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>Which slots <see cref="BrainIntentReader.Read"/> reports.</summary>
    public enum BrainIntentScope
    {
        /// <summary>The SCENARIO's rule (R-192): only what an ORDER set — a template default, a mission phase and the SOP's
        /// idle choice are left out, because a reload re-derives them.</summary>
        Ordered,

        /// <summary>The HAND-OVER's rule (<c>CE-3048</c>): everything that RUNS, template defaults and mission phases included,
        /// because the node gaining the Brain must end up running the same thing, whatever its replica held.</summary>
        Running,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3048</c> — THE ONE READER of a unit's AI as <c>{Name, Params, Origin}</c> per slot (<see cref="InitialBrainIntent"/>),
    /// shared by the scenario save (<see cref="BrainIntentScope.Ordered"/>, <c>CE-3042</c>) and the Brain hand-over
    /// (<see cref="BrainIntentScope.Running"/>), so the two cannot drift apart. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c>
    /// §7.5, §7.7.
    /// <list type="bullet">
    /// <item>task: a running reaction reports the task it PAUSED (a reaction answers this node's senses); a behaviour stamped
    ///   directly (origin Unmarked) reports as Superior.</item>
    /// <item>SOP: from its start record.</item>
    /// <item>ROE: as it is.</item>
    /// </list>
    /// </summary>
    public static class BrainIntentReader
    {
        /// <summary>The unit's AI in <paramref name="scope"/>; a slot is null when there is nothing to report.</summary>
        public static InitialBrainIntent Read(EntityRepository repo, Entity entity, BehaviorRegistry registry, BrainIntentScope scope)
            => new()
            {
                Behavior = TaskOf(repo, entity, registry, scope),
                Sop      = SopOf(repo, entity, scope),
                Roe      = RoeOf(repo, entity, scope),
            };

        /// <summary>The task slot (see the type's notes).</summary>
        public static SavedBrainSlot? TaskOf(EntityRepository repo, Entity entity, BehaviorRegistry registry, BrainIntentScope scope)
        {
            if (!repo.IsComponentTypeRegistered<BehaviorState>() || !repo.HasComponent<BehaviorState>(entity)) return null;
            ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);
            if (scope == BrainIntentScope.Ordered && DrivenByMissionPlan(repo, entity)) return null;

            if (state.Origin == BehaviorOrigin.Reaction)                  // the reaction is transient — keep what it paused
                return BehaviorIngressSystem.PausedTaskOf(repo, entity) is { } paused
                    ? new SavedBrainSlot { Name = paused.BehaviorName, Params = Json(paused.JsonParams), Origin = paused.Origin }
                    : null;
            if (state.ActiveBehaviorHash == BehaviorIds.None) return null;
            if (scope == BrainIntentScope.Ordered && state.Origin == BehaviorOrigin.Sop) return null;

            string? name = null, json = null;
            if (repo.HasManagedComponent<BehaviorStartRecord>(entity)
                && ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(entity) is { } record
                && record.InstanceId == state.InstanceId)
            {
                name = record.BehaviorName;
                json = record.JsonParams;
            }
            if (name == null && !registry.TryGetName(state.ActiveBehaviorHash, out name)) return null;
            return new SavedBrainSlot
            {
                Name   = name!,
                Params = Json(json),
                Origin = state.Origin == BehaviorOrigin.Unmarked ? BehaviorOrigin.Superior : state.Origin,
            };
        }

        /// <summary>The SOP slot.</summary>
        public static SavedBrainSlot? SopOf(EntityRepository repo, Entity entity, BrainIntentScope scope)
        {
            if (!repo.IsComponentTypeRegistered<SopState>() || !repo.HasComponent<SopState>(entity)) return null;
            ref readonly var sop = ref repo.GetComponentRO<SopState>(entity);
            if (sop.SopHash == BehaviorIds.None) return null;
            if (scope == BrainIntentScope.Ordered && (sop.SopOrigin == BehaviorOrigin.Sop || sop.SopOrigin == BehaviorOrigin.Unmarked))
                return null;
            if (!repo.HasManagedComponent<SopStartRecord>(entity)) return null;
            var record = ((ISimulationView)repo).GetManagedComponentRO<SopStartRecord>(entity);
            if (record == null || string.IsNullOrEmpty(record.BehaviorName)) return null;
            return new SavedBrainSlot
            {
                Name   = record.BehaviorName,
                Params = Json(record.JsonParams),
                Origin = sop.SopOrigin == BehaviorOrigin.Unmarked ? BehaviorOrigin.Sop : sop.SopOrigin,
            };
        }

        /// <summary>The ROE.</summary>
        public static SavedRoe? RoeOf(EntityRepository repo, Entity entity, BrainIntentScope scope)
        {
            if (!repo.IsComponentTypeRegistered<Roe>() || !repo.HasComponent<Roe>(entity)) return null;
            ref readonly var roe = ref repo.GetComponentRO<Roe>(entity);
            if (scope == BrainIntentScope.Ordered && roe.SetBy == BehaviorOrigin.Unmarked) return null;   // the template's default
            return new SavedRoe { Fire = roe.Fire, Reactions = roe.Reactions, SetBy = roe.SetBy,
                                  ReturnFireWindowSeconds = roe.ReturnFireWindowSeconds };   // CE-2095
        }

        /// <summary>A mission plan with phases left drives the task slot — the plan is saved, not the phase's behaviour.</summary>
        public static bool DrivenByMissionPlan(EntityRepository repo, Entity entity)
        {
            if (!repo.IsComponentTypeRegistered<MissionPlanQueue>() || !repo.HasComponent<MissionPlanQueue>(entity)) return false;
            ref readonly var queue = ref repo.GetComponentRO<MissionPlanQueue>(entity);
            return queue.PhaseCount > 0 && queue.CurrentPhase < queue.PhaseCount;
        }

        /// <summary>Two slots run the same thing: same name, same params, same origin.</summary>
        public static bool Same(SavedBrainSlot? a, SavedBrainSlot? b)
            => a is null ? b is null
             : b is not null && a.Name == b.Name && a.Origin == b.Origin && Json(a.Params).Trim() == Json(b.Params).Trim();

        private static string Json(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json!;
    }
}
