using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Orders;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-8303</c> (D-P3 DP3-B — our half, R-4286; 📄 docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §4 / §5) — a STOCHASTIC SQUAD
    /// about to split gets REAL posts: every frame, for each squad-echelon unit in <see cref="SplitStage.Planning"/> whose current order is a Defend,
    /// it steps SquadDefend's OWN plan (<see cref="DefencePlan.Step{TCrew}"/>) over a <see cref="SlotCrew"/> (the unit is the carrier and the asks'
    /// origin, its slot table the men, each on his fallback point) and, once the plan is done, writes <see cref="PlannedPosts"/> on the unit
    /// (Ready, entry k = the k-th live slot). ⭐ The manager (agg-backend) reads it; until its wait lands (DP3-B) the posts are written but NOT
    /// consumed. Serial, before the manager (CgfLogicPack), the owner only.
    /// <para>⭐ The plan's working state (the stages, the candidates, the outstanding ask) is per unit in this system — process state, never
    /// recorded; <see cref="PlannedPosts.Stage"/> / <see cref="PlannedPosts.PlannedAt"/> are recorded, so a seek back into a plan restarts its
    /// asks. A unit that leaves Planning drops its asks; one that went back to Stochastic, not splitting (a withdrawn trigger, a merge), has its
    /// posts cleared. ⛔ Steady state allocates nothing (R-2084): a new plan allocates its working state and parses the order once.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class UnitPostPlanningSystem : IEcsModuleSystem
    {
        /// <summary>The order's search radius when it names none (<c>SquadDefend</c>'s own default).</summary>
        public const float DefaultSearchRadius = 30f;

        private sealed class Plan
        {
            public DefencePosts Posts;
            public DefenceAsk Ask;
            public AuthoredPosts Authored;
            public Vector3 Position;
            public float Radius;
            public SimInstant StartedAt;
            public double LastStep;
            public bool Done;
        }

        private readonly bool _gateOnAuthority;
        private readonly Dictionary<Entity, Plan> _plans = new();
        private readonly List<Entity> _gone = new();
        private readonly Vector3[] _fallback = new Vector3[SlotCrew.MaxSlots];
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;

        public UnitPostPlanningSystem(bool gateOnAuthority = false) { _gateOnAuthority = gateOnAuthority; }

        /// <summary>Units with a plan in progress or done (a diagnostic, and the rails' handle).</summary>
        public int PlansHeld => _plans.Count;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>() || !repo.IsComponentTypeRegistered<UnitSlotTable>()
                || !repo.IsComponentTypeRegistered<PlannedPosts>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<UnitSlotTable>().With<SimTransform>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build();
                _queriesFor = repo;
                _plans.Clear();
            }
            foreach (var unit in _units!)
            {
                ref readonly var u = ref repo.GetComponentRO<AggregateUnit>(unit);
                if (!Plans(repo, unit, in u))
                {
                    if (_plans.Remove(unit, out var left)) DefencePlan.Abandon(repo, unit, ref left.Ask);
                    if (u.Effective == AggregateResolution.Stochastic && u.Splitting == SplitStage.NotSplitting && repo.HasComponent<PlannedPosts>(unit)
                        && repo.GetComponentRO<PlannedPosts>(unit).Stage != PlannedPosts.StageNone)
                        repo.SetComponent(unit, default(PlannedPosts));   // a withdrawn trigger / a merge: the posts are history
                    continue;
                }
                if (!_plans.TryGetValue(unit, out var plan) || Stale(repo, unit, plan, now))
                {
                    if (plan != null) DefencePlan.Abandon(repo, unit, ref plan.Ask);
                    plan = Begin(repo, unit, now);
                    _plans[unit] = plan;
                }
                plan.LastStep = now;
                if (plan.Done) continue;
                ref readonly var slots = ref repo.GetComponentRO<UnitSlotTable>(unit);
                var at = (Vector3)repo.GetComponentRO<SimTransform>(unit).Position;
                UnitSplits.FallbackPosts(in u, at, in slots, _fallback);
                var crew = new SlotCrew(repo, unit, now, in slots, _fallback);
                if (!DefencePlan.Step(ref plan.Posts, ref plan.Ask, in crew, in plan.Authored, plan.Position, plan.Radius)) continue;
                plan.Done = true;
                Publish(repo, unit, plan, in crew);
            }
            // a unit that is gone takes its plan with it
            if (_plans.Count == 0) return;
            _gone.Clear();
            foreach (var kv in _plans) if (!repo.IsAlive(kv.Key)) _gone.Add(kv.Key);
            for (int i = 0; i < _gone.Count; i++) _plans.Remove(_gone[i]);
        }

        /// <summary>Does <paramref name="unit"/> plan posts now: a stochastic squad in Planning holding a Defend order.</summary>
        public static bool Plans(EntityRepository repo, Entity unit, in AggregateUnit u)
            => u.Effective == AggregateResolution.Stochastic && u.Splitting == SplitStage.Planning && SplitsIntoMen(repo, unit)
               && CurrentOrders.Of(repo, unit).Kind == OrderKind.Defend;

        /// <summary>⭐ CE-8307 (R-8388; DESIGN_Split_One_Level.md §6 Q-g) — the unit's split creates MEN (a squad), not stochastic units (a platoon's
        /// squads plan their own posts once they exist); ⭐ review F11 — and its table holds a PERSON (a tank platoon's, an artillery battery's is
        /// empty: holdings only). ⛔ Not the echelon: an authored table never sets it (R-8372). No TKB ⇒ the echelon. ⭐ Review F7: the composite
        /// answer is CACHED per type (built serially, once) — <see cref="Plans"/> asks it every Planning frame (R-2084: 0 B).</summary>
        private static bool SplitsIntoMen(EntityRepository repo, Entity unit)
        {
            if (!repo.IsComponentTypeRegistered<UnitSlotTable>() || !repo.HasComponent<UnitSlotTable>(unit) || repo.GetComponentRO<UnitSlotTable>(unit).Authorized() == 0) return false;
            if (repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()
                && repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit) && repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db)
            {
                var type = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit).TkbType;
                var cache = s_splitsIntoUnits.GetValue(db, static _ => new System.Collections.Concurrent.ConcurrentDictionary<Fdp.Core.TkbTypeId, bool>());
                if (!cache.TryGetValue(type, out bool units))
                    cache[type] = units = Hrot.Core.Tkb.StochasticUnitFill.SplitsIntoUnits(db, type);
                return !units;
            }
            return repo.GetComponentRO<AggregateUnit>(unit).Echelon == 0;
        }

        // per TKB database (weakly held), type → its split creates units; thread-safe (rails run worlds in parallel); no allocation once filled
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Concurrent.ConcurrentDictionary<Fdp.Core.TkbTypeId, bool>> s_splitsIntoUnits = new();

        /// <summary>A seek moved the clock back past this plan's last step, or the recorded plan is not this one ⇒ start again.</summary>
        private static bool Stale(EntityRepository repo, Entity unit, Plan plan, double now)
        {
            if (now < plan.LastStep) return true;
            if (!repo.HasComponent<PlannedPosts>(unit)) return true;
            ref readonly var recorded = ref repo.GetComponentRO<PlannedPosts>(unit);
            return recorded.Stage == PlannedPosts.StageNone || recorded.PlannedAt != plan.StartedAt;
        }

        private static Plan Begin(EntityRepository repo, Entity unit, double now)
        {
            var plan = new Plan { StartedAt = (SimInstant)now, LastStep = now };
            unsafe
            {
                for (int i = 0; i < DefencePosts.Capacity; i++)   // what SquadDefendPlay.Start does
                {
                    plan.Posts.PostOf[i] = DefencePosts.None;
                    plan.Posts.MovedAt[i] = double.NegativeInfinity;
                    plan.Posts.DwellFrom[i] = double.NegativeInfinity;
                }
            }
            plan.Posts.Hunter = DefencePosts.None;
            OrderOf(repo, unit, out plan.Position, out plan.Radius);
            var planned = new PlannedPosts { Stage = PlannedPosts.StagePlanning, PlannedAt = plan.StartedAt };
            if (repo.HasComponent<PlannedPosts>(unit)) repo.SetComponent(unit, planned);
            else repo.AddComponent(unit, planned);
            return plan;
        }

        /// <summary>The Defend order's position and search radius, from the text it was started with (<see cref="BehaviorStartRecord"/>; parsed
        /// once per plan); a missing / zero field ⇒ where the unit stands / <see cref="DefaultSearchRadius"/> (the plays' own defaults).</summary>
        private static void OrderOf(EntityRepository repo, Entity unit, out Vector3 position, out float radius)
        {
            position = (Vector3)repo.GetComponentRO<SimTransform>(unit).Position;
            radius = DefaultSearchRadius;
            if (!repo.TryGetTable(typeof(BehaviorStartRecord), out _) || !repo.HasManagedComponent<BehaviorStartRecord>(unit)) return;
            var record = ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(unit);
            if (record == null || string.IsNullOrWhiteSpace(record.JsonParams)) return;
            try
            {
                var order = System.Text.Json.JsonSerializer.Deserialize<DefendOrder>(record.JsonParams, BehaviorParams.JsonOptions);
                var at = (Vector3)order.Position;
                if (at != Vector3.Zero) position = at;
                if (order.SearchRadius > 0f) radius = order.SearchRadius;
            }
            catch (System.Text.Json.JsonException) { }   // an order the planner cannot read plans around the unit, by default
        }

        /// <summary>The plan's posts per LIVE slot, in table order (the order <c>Materialise</c> walks): entry k = the k-th live slot.</summary>
        private static unsafe void Publish(EntityRepository repo, Entity unit, Plan plan, in SlotCrew crew)
        {
            var planned = new PlannedPosts { Stage = PlannedPosts.StageReady, Ready = 1, PlannedAt = plan.StartedAt };
            int k = 0;
            for (int i = 0; i < crew.Count && k < PlannedPosts.Capacity; i++)
            {
                if (!crew.Live(i)) continue;
                byte post = i < DefencePosts.Capacity ? plan.Posts.PostOf[i] : DefencePosts.None;
                if (post != DefencePosts.None && post < plan.Posts.Count)
                {
                    byte flags = PlannedPosts.PostedFlag;
                    if (plan.Posts.Window[post] != 0) flags |= PlannedPosts.WindowFlag;
                    if (plan.Posts.Roof[post] != 0) flags |= PlannedPosts.RoofFlag;
                    if (plan.Posts.Frame[post] != 0) flags |= PlannedPosts.FrameFlag;
                    if (plan.Posts.Authored[post] != 0) flags |= PlannedPosts.AuthoredFlag;
                    if (crew.Support(i)) flags |= PlannedPosts.GunFlag;
                    planned.Set(k, plan.Posts.Point(post), plan.Posts.Watch(post), flags);
                }
                k++;
            }
            planned.Count = (byte)k;
            repo.SetComponent(unit, planned);
        }
    }
}
