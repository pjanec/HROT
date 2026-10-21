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
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class TacticalIntentResolutionSystem : IEcsModuleSystem
    {
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

            var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();

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
                if (evt.ClearsPriorIntent) _clearRegistry?.ClearFor(evt.Entity, repo);

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
                    var r = OrderResolver.Resolve(_classes, repo.GetDisType(evt.Entity), order, n => _behaviorRegistry.TryGetId(n, out _));
                    if (r.Outcome != OrderResolution.Resolved)
                    {
                        if (r.Outcome == OrderResolution.Tie) s_aiLog.Error("Entity:[{EntityId}] Node:[TacticalIntentResolutionSystem] | {Detail}", evt.Entity.Index, r.Detail);
                        else s_aiLog.Warn("Entity:[{EntityId}] Node:[TacticalIntentResolutionSystem] | refused: {Detail}", evt.Entity.Index, r.Detail);
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
                repo.Bus.PublishManaged(behaviorEvent);
            }
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
