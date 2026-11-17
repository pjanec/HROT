using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-8301</c> (agg-behaviors ST-5, F-5, R-8327) — a unit that CHANGES resolution keeps the SOP that fits it. <see cref="StochasticSopRouting"/> picks the
    /// SOP when the template's default is ASSIGNED; a merge (physical → stochastic) or a split (back to physical) happens LATER, in agg-backend's
    /// <see cref="ResolutionManagerSystem"/>, which — by design (§16b: the manager owns the resolution, the tactics never see it) — knows nothing of SOPs.
    /// ⭐ So this is ONE behaviors-side owner, STATELESS: once a second, for each unit whose SOP is still the TEMPLATE'S default (<see cref="BehaviorOrigin.Sop"/> — an
    /// author's or a commander's explicit SOP is never touched) it compares the SOP it runs with the one its CURRENT resolution wants (stochastic ⇒ the routing
    /// table's <c>Sop</c> row, <see cref="TacticalIntentResolutionSystem.SopFor"/>; physical ⇒ the template's <c>DefaultSop</c>) and, when they differ, publishes
    /// the same <see cref="AssignSopEvent"/> the template does (the ingress substitutes the routed one for a stochastic unit). ⭐ No change-detection state
    /// (nothing to record or replay): the desired SOP is a pure function of the unit's resolution, so a seek or a replay re-derives it. ⛔ Steady state allocates
    /// nothing (the check is a hash compare; an event only on a real change).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class UnitSopResolutionSystem : IEcsModuleSystem
    {
        /// <summary>How often the check runs, seconds of sim time (the manager's own step).</summary>
        public const float StepSeconds = 1f;

        private readonly BehaviorRegistry _registry;
        private readonly bool _gateOnAuthority;
        private readonly Func<string, bool> _isBehaviour;
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;
        private SimInstant _last = (SimInstant)(-1.0);

        public UnitSopResolutionSystem(BehaviorRegistry registry, bool gateOnAuthority = false)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _gateOnAuthority = gateOnAuthority;
            _isBehaviour = n => _registry.TryGetId(n, out _);   // built once (the routing table's closure argument)
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>() || !repo.IsComponentTypeRegistered<SopState>()
                || !repo.Bus.IsRegisteredManaged<AssignSopEvent>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            if ((double)now - (double)_last < StepSeconds - 1e-6) return;
            _last = now;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<SopState>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build();
                _queriesFor = repo;
            }
            foreach (var unit in _units!)
            {
                ref readonly var sop = ref repo.GetComponentRO<SopState>(unit);
                if (sop.SopHash == 0 || sop.SopOrigin != BehaviorOrigin.Sop) continue;   // no SOP, or one somebody chose
                bool stochastic = AggregateUnits.IsStochastic(repo, unit);
                string? want; string json = "{}";
                if (stochastic) want = TacticalIntentResolutionSystem.SopFor(repo, unit, _isBehaviour);
                else if (!TemplateSop(repo, unit, out want, out json)) continue;
                if (string.IsNullOrEmpty(want) || !_registry.TryGetId(want, out int id) || id == sop.SopHash) continue;
                repo.Bus.PublishManaged(new AssignSopEvent { Entity = unit, BehaviorName = want, JsonParams = json, Origin = BehaviorOrigin.Sop });
            }
        }

        /// <summary>The unit's TEMPLATE default SOP (its <see cref="BehaviorProfileDto.DefaultSop"/>), what a physical unit runs.</summary>
        private static bool TemplateSop(EntityRepository repo, Entity unit, out string? name, out string json)
        {
            name = null; json = "{}";
            if (!repo.HasSingletonManaged<ITkbDatabase>() || !repo.HasComponent<TkbIdentity>(unit)) return false;
            if (repo.GetSingletonManaged<ITkbDatabase>() is not { } db) return false;
            if (!db.TryGetByType(repo.GetComponentRO<TkbIdentity>(unit).TkbType, out var t) || t == null
                || t.GetDescriptor<BehaviorProfileDto>() is not { } dto || string.IsNullOrEmpty(dto.DefaultSop)) return false;
            name = dto.DefaultSop; json = dto.DefaultSopParamsJson ?? "{}";
            return true;
        }
    }
}
