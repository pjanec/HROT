using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;

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
    public sealed class TacticalIntentResolutionSystem : IEcsModuleSystem
    {
        private readonly TacticalIntentMapperRegistry _mapperRegistry;

        /// <summary>
        /// Creates the system with the supplied mapper registry.
        /// </summary>
        /// <param name="mapperRegistry">
        /// Registry of intent-to-behavior mappers.  May be empty (all intents fall
        /// through to the pass-through path).  Must not be <c>null</c>.
        /// </param>
        public TacticalIntentResolutionSystem(TacticalIntentMapperRegistry mapperRegistry)
        {
            _mapperRegistry = mapperRegistry
                ?? throw new ArgumentNullException(nameof(mapperRegistry));
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

            foreach (var evt in events)
            {
                if (evt == null) continue;

                // Authority gate (CQRS boundary): skip when this node does not own the
                // cognitive state for the entity.  HasAuthority<BehaviorState> also returns
                // false when the entity has been destroyed, handling the deleted-entity case.
                if (!repo.HasAuthority<BehaviorState>(evt.Entity))
                    continue;

                // Try mapper path first.
                AssignBehaviorEvent? behaviorEvent = null;

                if (_mapperRegistry.TryGetMapper(evt.IntentId, out var mapper))
                {
                    if (mapper.TryMap(evt.Entity, repo, evt.JsonParams, out var mapped))
                        behaviorEvent = mapped;
                }

                // Fallback: treat IntentId as a direct behavior name.
                // A new instance is always allocated (pooling/reuse is not permitted).
                if (behaviorEvent == null)
                {
                    behaviorEvent = new AssignBehaviorEvent
                    {
                        Entity       = evt.Entity,
                        BehaviorName = evt.IntentId,
                        JsonParams   = evt.JsonParams,
                    };
                }

                repo.Bus.PublishManaged(behaviorEvent);
            }
        }
    }
}
