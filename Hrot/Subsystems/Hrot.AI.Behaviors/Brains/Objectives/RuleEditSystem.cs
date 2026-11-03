using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules ② (📄 docs/DESIGN_Scenario_Rules.md §00.11a, R2 / R3) — applies a rule's LIVE EDIT: an owned rule entity whose <see cref="RuleEdit"/> carries a <c>Seq</c> not yet
    /// <c>Done</c> has its params re-parsed and its state kept by <see cref="BehaviorIngressSystem.Reparameterise"/> (the ONE place — the same parse as an assign, the kind's keeper,
    /// no restart), ONCE per Seq. A refusal puts <c>"edit refused: …"</c> on the status <c>Reason</c> (the journal entry is the ingress's). ⭐ <b>R3 — a trigger that is
    /// <c>Acting</c> WAITS</b>: its Seq is not consumed until the run ends, then the edit applies whole (the running <c>do</c> list in <see cref="Fdp.Toolkit.ScenarioRules.Actions.ActionArgs"/> is
    /// never swapped under it). Registered by <c>CgfLogicPack</c> BEFORE the brain tick. A replica holds no <see cref="RuleEdit"/> authority and does nothing.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class RuleEditSystem : IEcsModuleSystem
    {
        private sealed class Log { }
        private readonly BehaviorIngressSystem _ingress;
        private readonly List<Entity> _pending = new();

        public RuleEditSystem(BehaviorIngressSystem ingress) => _ingress = ingress;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.TryGetTable(typeof(RuleEdit), out _) || !repo.IsComponentTypeRegistered<ObjectiveStatus>()) return;
            _pending.Clear();
            foreach (var e in repo.Query().With<ObjectiveStatus>().Build())   // collected first: applying an edit is structural
                if (repo.HasManagedComponent<RuleEdit>(e) && repo.HasAuthority(e, ManagedComponentType<RuleEdit>.ID)) _pending.Add(e);
            foreach (var e in _pending)
            {
                var edit = ((ISimulationView)repo).GetManagedComponentRO<RuleEdit>(e);
                if (edit.Seq == edit.Done) continue;
                ref readonly var status = ref repo.GetComponentRO<ObjectiveStatus>(e);
                if (status.Role == ObjectiveRole.Trigger && status.TriggerState == TriggerState.Acting) continue;   // R3 — waits, the Seq stays unconsumed
                string? why = _ingress.Reparameterise(repo, e, edit.Json, (BehaviorOrigin)(byte)edit.Origin);
                repo.SetManagedComponent(e, edit with { Done = edit.Seq });
                ref var s = ref repo.GetComponentRW<ObjectiveStatus>(e);
                if (why != null)
                {
                    string text = "edit refused: " + why;
                    s.Reason = new FixedString64(text.Length > 55 ? text.Substring(0, 55) : text);
                }
                Fdp.Core.Logging.FdpLog<Log>.Info($"rule '{s.Title}': edited by {(BehaviorOrigin)(byte)edit.Origin}" + (why != null ? $" — refused: {why}" : ""));
            }
        }
    }
}
