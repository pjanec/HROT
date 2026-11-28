using System;
using NLog;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Translates <see cref="AssignTacticalIntentEvent"/>s into
    /// <see cref="AssignBehaviorEvent"/>s by consulting the
    /// <see cref="TacticalIntentMapperRegistry"/>.
    ///
    /// <para><b>Frame behaviour:</b></para>
    /// <list type="number">
    ///   <item>
    ///     Read all <see cref="AssignTacticalIntentEvent"/>s from the managed bus
    ///     read buffer (populated by <c>SwapBuffers</c> at the end of the previous frame).
    ///   </item>
    ///   <item>
    ///     For each event, evaluate <c>repo.HasAuthority&lt;BehaviorState&gt;(evt.Entity)</c>.
    ///     If <c>false</c>, the cognitive state is owned by a remote node (or the entity
    ///     no longer exists) — skip silently without publishing anything.
    ///   </item>
    ///   <item>
    ///     Look up <c>evt.IntentId</c> in the <see cref="TacticalIntentMapperRegistry"/>.
    ///   </item>
    ///   <item>
    ///     <b>Mapper found and <c>TryMap</c> succeeds:</b> publish the returned
    ///     <see cref="AssignBehaviorEvent"/>.
    ///   </item>
    ///   <item>
    ///     <b>No mapper or <c>TryMap</c> returns <c>false</c>:</b> treat the intent ID as a
    ///     direct behavior name and publish
    ///     <c>new AssignBehaviorEvent { Entity, BehaviorName = evt.IntentId, JsonParams }</c>.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// This system must NOT mutate <c>BehaviorState</c>, <c>BrainBTreeState</c>, or
    /// <c>BrainBlackboard</c> directly — all cognitive state transitions are handled by
    /// <c>BehaviorIngressSystem</c> (Input phase), which consumes the published
    /// <see cref="AssignBehaviorEvent"/> on the next frame.
    /// </para>
    ///
    /// <para>Registered in the Simulation phase of <c>CgfLogicPack</c>
    /// immediately after <c>MissionAdapterSystem</c>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // ⭐⭐ CE-3494 (R-3114, DESIGN_Unit_Intent.md §1 R1) — nothing the AI does takes effect on a paused frame: this runs only when time
    //   advances and reads every request since its last run, IN ORDER (the step window) — so stepped and continuous runs agree.
    [TickMode(TickPolicy.WhenTimeAdvances)]
    [Fdp.Core.NotState("a system/module, classified on its own")]
    public sealed class TacticalIntentResolutionSystem : IEcsModuleSystem
    {
        [Fdp.Core.NotState("static NLog logger")]
        private static readonly Logger s_aiLog = LogManager.GetLogger("AI.Behavior.TacticalIntent");

        private readonly TacticalIntentMapperRegistry _mapperRegistry;
        private readonly BehaviorRegistry _behaviorRegistry;
        private readonly IntentClearRegistry? _clearRegistry;
        private readonly EntityClassTable _classes;

        /// <summary>⭐ <c>CE-3167</c> — the providers a <c>ClearsPriorIntent</c> order runs (null when built without any; a
        /// production host passes <c>CgfLogicPack</c>'s registry — <c>CgfLogicPackIntentClearTests</c> rails it).</summary>
        public IntentClearRegistry? ClearRegistry => _clearRegistry;

        /// <summary>
        /// Creates the system with the supplied mapper registry.
        /// </summary>
        /// <param name="mapperRegistry">
        /// Registry of intent-to-behavior mappers.  May be empty (all intents fall
        /// through to the pass-through path).  Must not be <c>null</c>.
        /// </param>
        /// <param name="classes">⭐ <c>CE-2142</c> — the entity classes an order resolves through; null = the engine's one table.</param>
        /// <param name="clearRegistry">⭐ <c>CE-3167</c> — the providers an order with
        /// <see cref="AssignTacticalIntentEvent.ClearsPriorIntent"/> runs before it starts; null clears nothing.</param>
        public TacticalIntentResolutionSystem(
            TacticalIntentMapperRegistry mapperRegistry,
            BehaviorRegistry behaviorRegistry,
            IntentClearRegistry? clearRegistry = null,
            EntityClassTable? classes = null)
        {
            _clearRegistry = clearRegistry;
            _classes = classes ?? EntityClassTable.Default;   // ⭐ CE-2142 — THE class table; a test may pass its own
            _mapperRegistry = mapperRegistry
                ?? throw new ArgumentNullException(nameof(mapperRegistry));
            _behaviorRegistry = behaviorRegistry
                ?? throw new ArgumentNullException(nameof(behaviorRegistry));
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(TacticalIntentResolutionSystem)} requires direct " +
                    $"EntityRepository access and cannot run on a read-only snapshot " +
                    $"({view.GetType().Name}).");

            // ⭐⭐ CE-3494 §10 P5 (R-3119) — a PERSON's task waits in the unit's intent (a paused frame keeps it, a checkpoint keeps it): take it
            //   here, FIRST (intent before the step's requests, R-3117), as the order event it replaces — same step, same rules (per-class
            //   resolution, plan hold, the station's answer) — and strip the slot. The step's own requests follow in order.
            var window = repo.ReadManagedEvents<AssignTacticalIntentEvent>();
            System.Collections.Generic.IReadOnlyList<AssignTacticalIntentEvent> events = window;
            if (TakePersonsTasks(repo) > 0)
            {
                for (int k = 0; k < window.Count; k++) _taken.Add(window[k]);
                events = _taken;
            }

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                if (evt == null) continue;

                // Authority gate (CQRS boundary): skip when this node does not own the
                // cognitive state for the entity.  HasAuthority<BehaviorState> also returns
                // false when the entity has been destroyed, handling the deleted-entity case.
                if (!repo.HasAuthority<BehaviorState>(evt.Entity))
                    continue;

                // ⭐ CE-3167 (Entity Commanding §2.5b-d) — the order cancels the unit's prior plans FIRST, synchronously, in
                //   this one call: an abort and an assign arriving as two events would run the clear LAST in the ingress and
                //   leave the unit brain-dead with the order discarded. Providers run on the owner only (after the gate).
                // ⭐⭐ CE-3494 (R-3115, DESIGN_Unit_Intent.md §4) — the order HOLDS the plan, and only once it has STARTED: an assign carries the
                //   flag to the ingress, which runs the providers after a successful start in the same call. ⛔ SUPERSEDED: clearing here, before
                //   the order was even tried (a refused order left the unit with no plan). A CLEAR order has nothing to start — it holds here.
                if (evt.ClearsPriorIntent && evt.Kind == TacticalIntentKind.Clear) _clearRegistry?.ClearFor(evt.Entity, repo);

                // ⭐ CE-3167 — a Clear ends the running behaviour at the intent's origin (the gate still applies: a lower origin
                //   cannot end a higher one); the SOP picks next. The one wire path for assign AND clear (Sensors §7.7).
                if (evt.Kind == TacticalIntentKind.Clear)
                {
                    // ⭐ CE-2141 (D2-D) — a clear followed by an order to the same entity in this frame is SUPERSEDED: the ingress applies
                    //   a frame's assigns before its clears (two buses), so passing both on would end the new order. The last word wins.
                    if (AssignedLater(events, i, evt.Entity)) continue;
                    // ⭐ CE-2141 (D2-C) — a clear that NAMES a behaviour ends only that one (a commander releasing its ended drill's
                    //   order); a name this node does not know cannot be running here.
                    int only = 0;
                    if (!string.IsNullOrEmpty(evt.IntentId) && !_behaviorRegistry.TryGetId(evt.IntentId, out only)) continue;
                    repo.Bus.Publish(new ClearBehaviorEvent { Entity = evt.Entity, Origin = evt.Origin, OnlyBehaviorHash = only });
                    continue;
                }

                // ⭐⭐ CE-2142 (D3, R-273) — an ORDER (`Order.<name>`): the receiver picks <Class><Order> by its OWN class (its DIS
                //   type through the one class table), most specific first. No mapper, no soldier fallback: an order no class of
                //   this entity fulfils starts nothing (a body-less unit given a soldier's behaviour would stand still for ever).
                AssignBehaviorEvent? behaviorEvent = null;
                if (evt.IntentId.StartsWith(OrderAttribute.IntentPrefix, StringComparison.Ordinal))
                {
                    string order = evt.IntentId.Substring(OrderAttribute.IntentPrefix.Length);
                    // ⭐⭐ CE-8000 (SV-2, B-1) — the unit's RESOLUTION is an input: physical (and every entity with no AggregateUnit) is
                    //   OrderResolver's rule untouched; a stochastic unit resolves through the (class, resolution, order) table or is refused.
                    var r = ResolutionRoutingTable.Default.Resolve(_classes, repo.GetDisType(evt.Entity), ResolutionOf(repo, evt.Entity), order,
                                                                   n => _behaviorRegistry.TryGetId(n, out _));
                    if (r.Outcome != OrderResolution.Resolved)
                    {
                        if (r.Outcome == OrderResolution.Tie) s_aiLog.Error("Entity:[{EntityId}] Node:[TacticalIntentResolutionSystem] | {Detail}", evt.Entity.Index, r.Detail);
                        else s_aiLog.Warn("Entity:[{EntityId}] Node:[TacticalIntentResolutionSystem] | refused: {Detail}", evt.Entity.Index, r.Detail);
                        Fdp.Toolkit.Replication.Edits.InteractionOutcomes.Refused(repo, Fdp.Toolkit.Behavior.RequestIds.IsMinted(evt.RequestId) ? System.Guid.Empty : evt.RequestId, evt.Entity, r.Detail);   // ⭐ CE-4612 (R-2090): a station's request only   // ⭐ CE-4306 (F5): the station that asked is told why
                        ReportRefusal(repo, evt);   // ⭐⭐ CE-8002 (B-8, RV-6) — and the COMMANDER that gave it hears TaskFailed
                        continue;
                    }
                    behaviorEvent = new AssignBehaviorEvent { Entity = evt.Entity, BehaviorName = r.Behaviour, JsonParams = evt.JsonParams };
                }

                // Try mapper path first.

                if (behaviorEvent == null && _mapperRegistry.TryGetMapper(evt.IntentId, out var mapper))
                {
                    if (mapper.TryMap(evt.Entity, repo, evt.JsonParams, out var mapped))
                        behaviorEvent = mapped;
                }

                // Fallback: treat IntentId as a direct behavior name.
                // A new instance is always allocated (pooling/reuse is not permitted).
                if (behaviorEvent == null)
                {
                    if (!_behaviorRegistry.TryGetId(evt.IntentId, out _) && s_aiLog.IsWarnEnabled)
                    {
                        int behaviorHash = repo.HasComponent<BehaviorState>(evt.Entity)
                            ? repo.GetComponent<BehaviorState>(evt.Entity).ActiveBehaviorHash : 0;
                        s_aiLog.Warn(
                            "Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[TacticalIntentResolutionSystem] | Intent [{IntentId}] not found in mapper registry AND not found in behavior registry. Check for authoring typos.",
                            evt.Entity.Index, behaviorHash, evt.IntentId);
                    }

                    behaviorEvent = new AssignBehaviorEvent
                    {
                        Entity       = evt.Entity,
                        BehaviorName = evt.IntentId,
                        JsonParams   = evt.JsonParams,
                    };
                }

                // ⭐ CE-3034 — the behaviour carries the intent's origin, whichever path built the assignment.
                behaviorEvent.Origin = evt.Origin;
                behaviorEvent.RequestId = evt.RequestId;   // ⭐ CE-4306 (F5): the gate answers under the order's id
                behaviorEvent.HoldsPlan = evt.ClearsPriorIntent;   // ⭐ CE-3494 — the ingress holds the plan once this starts
                repo.Bus.PublishManaged(behaviorEvent);
            }
        }

        /// <summary>
        /// ⭐⭐ agg-behaviors <c>ST-2</c> (RV-7, R-8311) — the SOP of a unit at its RESOLUTION, from the same table that resolves its orders: the <c>"Sop"</c> row
        /// of <see cref="ResolutionRoutingTable.Default"/> for the unit's most specific class (<c>StochasticUnitSop</c> for a stochastic one); null = the table has no
        /// row, so the template's own SOP stands. ⭐ This is what <c>CgfLogicPack</c> hands the ingress as <c>StochasticSopRouting.Resolver</c>.
        /// </summary>
        public static string? SopFor(EntityRepository repo, Entity e, Func<string, bool> isBehaviour)
        {
            var r = ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, repo.GetDisType(e), ResolutionOf(repo, e), ResolutionRoutingTable.SopOrder, isBehaviour);
            return r.Outcome == OrderResolution.Resolved ? r.Behaviour : null;
        }

        /// <summary>⭐ <c>CE-8000</c> — the resolution an order is resolved at: the unit's <c>AggregateUnit</c>, else physical.</summary>
        internal static Fdp.Toolkit.Aggregate.AggregateResolution ResolutionOf(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<Fdp.Toolkit.Aggregate.AggregateUnit>() && repo.HasComponent<Fdp.Toolkit.Aggregate.AggregateUnit>(e)
                ? repo.GetComponentRO<Fdp.Toolkit.Aggregate.AggregateUnit>(e).Effective
                : Fdp.Toolkit.Aggregate.AggregateResolution.Physical;

        /// <summary>
        /// ⭐⭐ <c>CE-8002</c> (B-8, RV-6; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §19) — an order from a SUPERIOR
        /// that the receiver cannot fulfil reports PROGRESS TaskFailed to that superior, through the receiver's own outbox — the SAME queue and
        /// delay a play's failure takes (<c>StaffComposerSystem</c>, CC-5). ⛔ Before, a commander's minted request was refused to
        /// <c>Guid.Empty</c> and the commander waited to its deadline. Generic: every unit, physical or not. A station's / operator's order is
        /// answered by <c>InteractionOutcomes.Refused</c> only (it has no commander to report to).
        /// </summary>
        internal static void ReportRefusal(EntityRepository repo, AssignTacticalIntentEvent evt) => ReportRefusal(repo, evt.Entity, evt.Origin);

        /// <summary>⭐ <c>X-2</c> X2-D — the same report for an order that cannot go on (the resolution-fit reconciler: no play at the new resolution).</summary>
        internal static void ReportRefusal(EntityRepository repo, Entity entity, BehaviorOrigin origin)
        {
            var evt = (Entity: entity, Origin: origin);
            if (evt.Origin != BehaviorOrigin.Superior) return;
            if (!repo.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>()
                || !repo.HasComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>(evt.Entity)) return;
            var up = repo.GetComponentRO<Fdp.Core.CommandHierarchy.UnitSubordinate>(evt.Entity).Commander;
            if (up.IsNull || !repo.IsAlive(up)) return;
            var now = Fdp.Toolkit.Squad.Command.CommandPictureOps.Now(repo);
            var at = repo.HasComponent<SimTransform>(evt.Entity) ? repo.GetComponentRO<SimTransform>(evt.Entity).Position : default;
            Fdp.Toolkit.Squad.Command.ReportQueue.Enqueue(repo, evt.Entity, new Fdp.Toolkit.Squad.Command.CommandReport
            {
                Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)Fdp.Toolkit.Squad.Command.ProgressKind.TaskFailed,
                At = at, ObservedAt = now,
            }, now + Fdp.Toolkit.Squad.Command.StaffComposerSystem.ReportDelaySeconds);
        }

        [Fdp.Core.FrameScratch("a person's tasks taken from the intent this step, then the step's events — cleared at the top of each take")]
        private readonly System.Collections.Generic.List<AssignTacticalIntentEvent> _taken = new();
        [Fdp.Core.FrameScratch("the units whose intent holds a person's task this step")]
        private readonly System.Collections.Generic.List<Entity> _takeUnits = new();
        private EntityQuery? _intentQuery;
        private EntityRepository? _intentQueryOf;

        /// <summary>
        /// ⭐ CE-3494 §10 — moves every pending PERSON's task (origin Operator) out of the units' <see cref="UnitIntent"/> into <see cref="_taken"/> as
        /// the order event it stands for; the slot is removed (the record too when nothing else is pending). Returns how many. No allocation when none
        /// (the query is built once per world). ⛔ Not during a replay (R11).
        /// </summary>
        private int TakePersonsTasks(EntityRepository repo)
        {
            _taken.Clear();
            if (!repo.TryGetTable(typeof(UnitIntent), out _) || Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) return 0;
            if (!ReferenceEquals(_intentQueryOf, repo)) { _intentQuery = repo.Query().WithManaged<UnitIntent>().Build(); _intentQueryOf = repo; }
            _takeUnits.Clear();
            foreach (var e in _intentQuery!) _takeUnits.Add(e);
            for (int i = 0; i < _takeUnits.Count; i++)
            {
                var entity = _takeUnits[i];
                var intent = ((ISimulationView)repo).GetManagedComponentRO<UnitIntent>(entity);
                if (intent?.Behavior is not { Origin: BehaviorOrigin.Operator } task || !repo.HasAuthority<BehaviorState>(entity)) continue;
                _taken.Add(new AssignTacticalIntentEvent
                {
                    Entity = entity, IntentId = task.Clear ? string.Empty : task.Name, JsonParams = task.Params, Origin = task.Origin,
                    Kind = task.Clear ? TacticalIntentKind.Clear : TacticalIntentKind.Assign, ClearsPriorIntent = true, RequestId = task.RequestId,
                });
                var rest = intent with { Behavior = null };
                if (rest.Sop is null && rest.Roe is null) repo.RemoveComponent<UnitIntent>(entity);
                else repo.SetManagedComponent(entity, rest);
            }
            return _taken.Count;
        }

        /// <summary>⭐ <c>CE-2141</c> — an ASSIGN to <paramref name="entity"/> follows event <paramref name="index"/> in this frame.</summary>
        private static bool AssignedLater(System.Collections.Generic.IReadOnlyList<AssignTacticalIntentEvent> events, int index, Entity entity)
        {
            for (int j = index + 1; j < events.Count; j++)
                if (events[j] is { Kind: TacticalIntentKind.Assign } later && later.Entity.Equals(entity)) return true;
            return false;
        }
    }
}
