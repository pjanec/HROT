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
using Hrot.AI.Behaviors.Orders;

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
    /// <remarks>
    /// ⭐⭐⭐ <c>X-2</c> (agg-behaviors, R-8353 … R-8363; 📄 docs/designs/aggregate-units/DESIGN_X2_Order_Reresolution.md) — renamed from
    /// <c>UnitSopResolutionSystem</c> (a Roslyn rename) and grown into THE RESOLUTION-FIT RECONCILER: every frame, for each owned squad-echelon
    /// <see cref="AggregateUnit"/>, the play it runs must belong to its resolution (a stochastic play on a stochastic unit, any other on a
    /// physical one — <see cref="ResolutionRoutingTable.IsStochasticPlay"/>, compared by hash: 0 B). ⭐ ORDER arm (X2-A / X2-C / X2-E): the
    /// unit's ORDER (<see cref="CurrentOrders.Of"/> — running, or paused under a reaction) whose play does not fit is RE-ISSUED — the same kind,
    /// origin and request id, the play the routing table gives at the unit's resolution, the order's own params (projected onto the stochastic
    /// DTO on a merge) — as ONE <see cref="AssignBehaviorEvent"/> with <c>IsResume</c> (no second answer to a station); a physical squad only
    /// once every released man is in its roster (the switch's assigns land a frame later). An order with no play there is cleared and its
    /// commander hears TaskFailed (the pin's case — SV-14). ⭐ REACTION arm (X2-E, R-8364): a task that is no order (a reaction, the SOP's,
    /// the unit's own) whose play does not fit is ended (<see cref="BehaviorOrigin.Self"/>) — the new resolution's SOP re-judges.
    /// ⭐ Stateless (a seek re-derives it): a request already on the bus for the unit is waited for (the ingress applies it two frames later).
    /// </remarks>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class UnitResolutionFitSystem : IEcsModuleSystem
    {
        /// <summary>How often the check runs, seconds of sim time (the manager's own step).</summary>
        public const float StepSeconds = 1f;

        private readonly BehaviorRegistry _registry;
        private readonly bool _gateOnAuthority;
        private readonly Func<string, bool> _isBehaviour;
        private EntityQuery? _units, _orderUnits, _children;
        private EntityRepository? _queriesFor;
        private readonly int[] _stochasticPlays;
        private const string SquadDefendPlayName = "SquadDefend";

        /// <summary>How many orders the order arm re-issued (diagnostics, rails).</summary>
        public int Reissued { get; private set; }
        /// <summary>How many tasks the arms cleared (an order with no play at the new resolution, a stale reaction).</summary>
        public int Cleared { get; private set; }
        private SimInstant _last = (SimInstant)(-1.0);

        public UnitResolutionFitSystem(BehaviorRegistry registry, bool gateOnAuthority = false)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _gateOnAuthority = gateOnAuthority;
            _isBehaviour = n => _registry.TryGetId(n, out _);   // built once (the routing table's closure argument)
            var plays = ResolutionRoutingTable.StochasticPlays;
            _stochasticPlays = new int[plays.Count];
            for (int i = 0; i < plays.Count; i++) _stochasticPlays[i] = BehaviorHash.FromName(plays[i]);
        }

        /// <summary>The routing table's question, answered by this node's registry (<see cref="OrderReissue.BlockReason"/> for the manager's seam).</summary>
        public Func<string, bool> IsBehaviour => _isBehaviour;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>()) return;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.IsComponentTypeRegistered<SopState>()
                    ? repo.Query().With<AggregateUnit>().With<SopState>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build() : null;
                _orderUnits = repo.IsComponentTypeRegistered<BehaviorState>()
                    ? repo.Query().With<AggregateUnit>().With<BehaviorState>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build() : null;
                _children = repo.IsComponentTypeRegistered<AggregateChild>() ? repo.Query().With<AggregateChild>().Build() : null;
                _queriesFor = repo;
            }
            if (_orderUnits != null) OrderArm(repo);   // ⭐ X-2 — every frame
            if (_units == null || !repo.Bus.IsRegisteredManaged<AssignSopEvent>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            if ((double)now - (double)_last < StepSeconds - 1e-6) return;
            _last = now;
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

        // ── ⭐ X-2: the order and reaction arms ──────────────────────────────────────────────────────────────────────────────────

        private bool IsStochasticPlay(int hash)
        {
            for (int i = 0; i < _stochasticPlays.Length; i++) if (_stochasticPlays[i] == hash) return true;
            return false;
        }

        private void OrderArm(EntityRepository repo)
        {
            bool pausedTable = repo.TryGetTable(typeof(PausedTask), out _);
            foreach (var unit in _orderUnits!)
            {
                ref readonly var a = ref repo.GetComponentRO<AggregateUnit>(unit);
                // X2-I (R-8362) — squad echelon only (a platoon's split flattens to men, F-X2-1); mid-process the unit is still what it was
                if (a.Echelon != 0 || a.Splitting != SplitStage.NotSplitting) continue;
                ref readonly var bs = ref repo.GetComponentRO<BehaviorState>(unit);
                int run = bs.ActiveBehaviorHash;
                if (run == BehaviorIds.None) continue;
                bool stochastic = a.Effective == AggregateResolution.Stochastic;
                bool runFits = IsStochasticPlay(run) == stochastic;   // ⭐ the steady state: one hash compare, 0 B
                if (CurrentOrders.IsOrderOrigin(bs.Origin))
                {
                    if (!runFits) Reissue(repo, unit, stochastic);
                    continue;
                }
                if (bs.Origin == BehaviorOrigin.Reaction && pausedTable
                    && Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem.PausedTaskOf(repo, unit) is { } p
                    && p.Hash != BehaviorIds.None && CurrentOrders.IsOrderOrigin(p.Origin) && IsStochasticPlay(p.Hash) != stochastic)
                {
                    Reissue(repo, unit, stochastic);   // X2-E — the paused order is re-issued: it outranks the reaction, which ends
                    continue;
                }
                if (!runFits && bs.Origin is BehaviorOrigin.Reaction or BehaviorOrigin.Sop or BehaviorOrigin.Self)
                    ClearTask(repo, unit, run);        // X2-E (R-8364) — a task of the other resolution, no order behind it
            }
        }

        /// <summary>X2-A / X2-C — re-issue <paramref name="unit"/>'s order at its new resolution (once; a physical squad once its men are rostered).</summary>
        private void Reissue(EntityRepository repo, Entity unit, bool stochastic)
        {
            if (Pending(repo, unit)) return;
            var order = CurrentOrders.Of(repo, unit);
            if (!order.Exists) return;
            var target = stochastic ? AggregateResolution.Stochastic : AggregateResolution.Physical;
            string? want = OrderReissue.PlayFor(repo, unit, target, order.Kind, _isBehaviour);
            if (want == null)
            {
                // the pin's case (SV-14: the automatic manager waits — X2-D): the order cannot go on here — ended, its commander told
                repo.Bus.Publish(new ClearBehaviorEvent { Entity = unit, Origin = order.Origin, OnlyBehaviorHash = order.BehaviorHash });
                TacticalIntentResolutionSystem.ReportRefusal(repo, unit, order.Origin);
                Cleared++;
                Fdp.Core.Logging.FdpLog<UnitResolutionFitSystem>.Info("[ResolutionFit] unit #{0}: its order has no play at " + target + " — cleared", unit.Index);
                return;
            }
            if (!stochastic && !RosterComplete(repo, unit)) return;   // X2-A — the men first (the hierarchy's assigns land a frame after the handover)
            string json = OrderJson(repo, unit, order.Paused);
            json = OrderReissue.ParamsFor(want, json, stochastic);
            // ⭐ X2-F (R-8359; DP3-D ②) — a split squad's SquadDefend starts from its PLANNED POSTS (D-2f Posts): every man already stands on one ⇒ Set at once, no ask
            if (!stochastic && want == SquadDefendPlayName && repo.IsComponentTypeRegistered<PlannedPosts>() && repo.HasComponent<PlannedPosts>(unit))
                json = OrderReissue.WithPlannedPosts(json, in repo.GetComponentRO<PlannedPosts>(unit));
            repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = unit, BehaviorName = want, JsonParams = json, Origin = order.Origin, RequestId = order.RequestId, IsResume = true,
            });
            Reissued++;
            Fdp.Core.Logging.FdpLog<UnitResolutionFitSystem>.Info("[ResolutionFit] unit #{0} re-issues its " + order.Kind + " order as " + want, unit.Index);
        }

        /// <summary>X2-E — end a task that belongs to the other resolution (the unit's own clear: a paused task resumes, else the SOP re-judges).</summary>
        private void ClearTask(EntityRepository repo, Entity unit, int run)
        {
            if (Pending(repo, unit)) return;
            repo.Bus.Publish(new ClearBehaviorEvent { Entity = unit, Origin = BehaviorOrigin.Self, OnlyBehaviorHash = run });
            Cleared++;
        }

        /// <summary>The order's params: its start record's (running), else the paused task's; <c>"{}"</c> when neither holds them.</summary>
        private static string OrderJson(EntityRepository repo, Entity unit, bool paused)
        {
            if (paused) return Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem.PausedTaskOf(repo, unit)?.JsonParams ?? "{}";
            if (!repo.TryGetTable(typeof(BehaviorStartRecord), out _) || !repo.HasManagedComponent<BehaviorStartRecord>(unit)) return "{}";
            var record = ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(unit);
            return record != null && record.InstanceId == repo.GetComponentRO<BehaviorState>(unit).InstanceId ? record.JsonParams : "{}";
        }

        /// <summary>A request for <paramref name="unit"/> is already on the bus (published last frame; the ingress applies it next frame) — wait for it.</summary>
        private static bool Pending(EntityRepository repo, Entity unit)
        {
            if (repo.Bus.IsRegisteredManaged<AssignBehaviorEvent>())
            {
                var assigns = repo.Bus.ReadManaged<AssignBehaviorEvent>();
                for (int i = 0; i < assigns.Count; i++) if (assigns[i] != null && assigns[i].Entity.Equals(unit)) return true;
            }
            var clears = repo.Bus.Read<ClearBehaviorEvent>();
            for (int i = 0; i < clears.Length; i++) if (clears[i].Entity.Equals(unit)) return true;
            return false;
        }

        /// <summary>⭐ X2-A — every live RELEASED child of <paramref name="unit"/> is in its <see cref="Fdp.Core.CommandHierarchy.UnitRoster"/>.</summary>
        private unsafe bool RosterComplete(EntityRepository repo, Entity unit)
        {
            if (_children == null) return true;
            bool has = repo.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>() && repo.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(unit);
            foreach (var c in _children)
            {
                ref readonly var tag = ref repo.GetComponentRO<AggregateChild>(c);
                if (tag.Unit != unit || tag.Held != 0 || !UnitSplits.Live(repo, c)) continue;
                if (!has) return false;
                ref readonly var roster = ref repo.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(unit);
                bool found = false;
                for (int i = 0; i < roster.Count && !found; i++) found = roster.SubordinateEntities[i].Equals(c);
                if (!found) return false;
            }
            return true;
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
