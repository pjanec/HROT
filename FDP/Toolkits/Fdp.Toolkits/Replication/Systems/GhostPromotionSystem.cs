using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// Promotes ghost entities to <see cref="EntityLifecycle.Constructing"/> once all their
    /// mandatory ECS components (as defined by the <see cref="TkbTemplate"/>) have physically
    /// arrived in memory.
    ///
    /// <para>The system operates as a pure ECS state machine: the promotion query naturally
    /// filters on <c>EntityLifecycle.Ghost + TkbIdentity</c>, so once an entity's lifecycle
    /// advances to <c>Constructing</c> it falls out of the query on the next frame without
    /// any explicit "trigger removal" step.</para>
    ///
    /// <para>Checks are O(1) bitmask operations against the entity's
    /// <see cref="EntityHeader.ComponentMask"/> — no network concepts involved.</para>
    /// </summary>
    /// <remarks>
    /// ⭐⭐⭐ <b><c>P2</c> (<c>2026-09-11</c>) — THE TWO ATTRIBUTES BELOW EXIST BECAUSE THE REGISTRAR MOVED.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.7 · §6 step <c>0a</c>. Registration left
    /// <c>NedReplicationModule.RegisterSystems</c> — <b>one</b> network implementation — for
    /// <c>EntityCreationPack</c>, which every ECS host builds. ⭐ Ghost CREATION is a network concern and
    /// stays in the replication module; ghost PROMOTION consumes the TKB and the translator list and is a
    /// SIMULATION concern. ⇒ the BDC gap closes as a side effect: <c>BdcReplicationModule</c> creates
    /// ghosts and registered no promotion, so its ghosts were never promoted.
    ///
    /// <para>⭐⭐ <see cref="UpdateAfterAttribute"/> — <b>the ordering was true BY REGISTRATION ORDER and is
    /// now true BY CONSTRUCTION.</b> 📐 Measured: with no declared edge, <c>SystemScheduler</c> orders a
    /// phase by insertion order *(Kahn's algorithm over nodes added in registration order,
    /// <c>SystemScheduler.cs:233</c>/<c>:280</c>)*. ⛔ While both systems were registered by the SAME
    /// module that was free; across two registrars it would depend on which the host wires first — and a
    /// promotion running before creation costs a frame of latency silently. ⚠ The edge is PHASE-SCOPED
    /// *(<c>SystemScheduler.cs:250</c> adds it only when the target is in the same phase)*, and both are
    /// <see cref="SystemPhase.BeforeSync"/>; on a host with no replication module at all *(the editor's
    /// <c>NullReplicationModule</c>, the integration harness)* there is no <c>GhostCreationSystem</c> to
    /// order against and the edge is correctly skipped.</para>
    ///
    /// <para>⭐⭐ <see cref="SingleInstanceAttribute"/> — <b>this is what makes step <c>0a</c>'s gate
    /// structural instead of a rail.</b> The gate is *"a node built from the pack registers promotion
    /// exactly once"*; ⛔ the failure mode of a MOVE is landing the add without the remove, which would
    /// promote twice per frame. 📌 <c>CE-165</c> put this attribute in the scheduler for exactly that
    /// class of defect, and it recurses into groups, so a second registration now throws at
    /// <c>BeginRun()</c> rather than being measured later.</para>
    ///
    /// <para>⚠⚠ <b>WHAT THE MOVE DELIBERATELY DID NOT CHANGE — the replay gate.</b> 📐 Measured
    /// <c>2026-09-11</c>: <c>NetworkLifecycleSystemGroup</c>'s own summary claims it groups
    /// <i>"LifecycleSystem, GhostPromotionSystem and NetworkGatewaySystem"</i> so that *"no … ghost
    /// promotions occur during playback"*, but <b>no production site has ever put this system in it</b> —
    /// every one passes <c>GhostCreationSystem</c> alone, and promotion was registered standalone at
    /// <c>NedReplicationModule.cs:417</c>, i.e. OUTSIDE the gate. ⇒ the pack registers it standalone too,
    /// preserving today's behaviour exactly. ⛔ Whether promotion SHOULD be gated during replay is a
    /// separate question and is filed, not answered here — a relocation may not change behaviour.</para>
    /// </remarks>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    [UpdateAfter(typeof(GhostCreationSystem))]
    [SingleInstance]
    public class GhostPromotionSystem : IEcsModuleSystem
    {
        private readonly ITkbDatabase _tkbDatabase;
        private readonly EntityLifecycleModule _lifecycleModule;
        private readonly IReadOnlyList<ITkbEntityTranslator>? _explicitTranslators;

        /// <summary>
        /// ⭐ The node's TKB→ECS projection list. An explicit list wins; otherwise the ONE list the
        /// node's <see cref="EntityLifecycleModule"/> already holds is used — §6.3's
        /// <i>"identical for all three systems within the same node"</i>, satisfied by SHARING the
        /// instance rather than by a second argument nobody passes.
        ///
        /// <para>📌 <c>CE-155</c>. ⚠ <b>Corrected scope, <c>2026-09-01</c>:</b> an earlier version of this
        /// comment said the list was <c>Array.Empty</c> on <i>every</i> node. 📐 It is empty on the
        /// <b>FACTORY path</b> only — <c>NedNetworkFactory.CreateReplicationModule()</c> omits
        /// <c>tkbEntityTranslators</c>, which is how <b>CGF</b> builds its module. Hosts on the
        /// <b>BUILDER path</b> could pass one, via
        /// <c>HrotNodeBuilderReplicationExtensions.Build()</c> forwarding <c>.WithTranslators(...)</c>.
        /// ⚠⚠ <b>UPDATED <c>2026-09-03</c>: NO PRODUCTION HOST CALLS IT ANY MORE.</b> SimHost dropped it
        /// at <c>CE-140</c> step 3 and IG — the last caller — at <c>CE-141</c>, under the ruling that
        /// every ECS node uses the same TKB projection through the same shared code. ⇒ ⭐ <b>this
        /// fallback is now THE path, not a factory-path convenience</b>, and it is what makes
        /// <c>tkb-1/DESIGN.md</c> §6.3's <i>"identical for all three systems within the same node"</i>
        /// true by SHARING the instance. Resolved lazily because composition roots call
        /// <see cref="EntityLifecycleModule.SetTranslators"/> after the module is constructed.</para>
        /// </summary>
        private IReadOnlyList<ITkbEntityTranslator> Translators
            => _explicitTranslators ?? _lifecycleModule.Translators;

        private readonly Queue<Entity> _promotionQueue = new();
        private readonly HashSet<Entity> _inQueue = new();
        private readonly Stopwatch _stopwatch = new();
        private static readonly long PROMOTION_BUDGET_TICKS =
            (long)(0.002 * Stopwatch.Frequency);

        private EntityRepository? _world;
        private EntityQuery? _readyGhostQuery;

        public GhostPromotionSystem(
            ITkbDatabase tkbDatabase,
            EntityLifecycleModule lifecycleModule,
            IReadOnlyList<ITkbEntityTranslator>? translators = null)
        {
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
            _lifecycleModule = lifecycleModule ?? throw new ArgumentNullException(nameof(lifecycleModule));
            _explicitTranslators = translators;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The replay gate</b> — asked once per <see cref="Execute"/>; <c>true</c> makes this system
        /// do nothing. 📄 <c>mgmt-1/DESIGN.md</c> §8.10: during replay <i>"the ELM pipeline is never
        /// invoked"</i>, and §8.10 names this system among the three that must not run.
        ///
        /// <para>🔴 <b>Why it matters HERE specifically.</b> Lifecycle IS recorded — the entity index's cold
        /// chunk carries <c>EntityMetadataCold.LifecycleState</c> — so an entity recorded while it was a
        /// GHOST <b>comes back as a ghost</b>, matches this system's
        /// <c>With&lt;TkbIdentity&gt;().WithLifecycle(Ghost)</c> query, and would be promoted: mutating
        /// entities the LOG owns, and calling <c>BeginConstruction</c> on them. 📄 <c>CE-259ap</c>.</para>
        ///
        /// <para>⛔ Gated IN PLACE rather than relocated into <c>NetworkLifecycleSystemGroup</c> as §8.10
        /// prescribes — that group's <c>ExecuteGroup</c> has exactly ONE caller, so it never ticks on the
        /// editor or on BDC nodes, and this system is <c>[SingleInstance]</c> and already scheduler-
        /// registered by <c>EntityCreationPack</c>. Authorised deviation; 📄 <c>DESIGN.md</c> §2.1m.</para>
        ///
        /// <para>⚠ Unset (the default) means "never replaying", so a host that does not wire it behaves
        /// exactly as before.</para>
        /// </summary>
        public System.Func<bool>? IsReplayActive { get; set; }

        public void Execute(ISimulationView view, float dt)
        {
            // ⛔ The log owns the world during playback — a restored ghost is the log's, not ours to promote.
            if (IsReplayActive != null && IsReplayActive()) return;

            _world = view as EntityRepository;
            if (_world == null) return;

            EnsureQueriesInitialized(_world);

            EnqueueReadyGhosts();

            if (_promotionQueue.Count == 0) return;

            _stopwatch.Restart();
            var cmdBuffer = view.GetCommandBuffer();
            var tick = view.Tick;

            while (_promotionQueue.Count > 0)
            {
                if (_stopwatch.ElapsedTicks > PROMOTION_BUDGET_TICKS) break;

                var entity = _promotionQueue.Dequeue();
                _inQueue.Remove(entity);

                if (!_world.IsAlive(entity)) continue;
                if (!_world.HasComponent<TkbIdentity>(entity)) continue;

                PromoteGhost(entity, cmdBuffer, tick);
            }
            _stopwatch.Stop();
        }

        private void EnqueueReadyGhosts()
        {
            foreach (var entity in _readyGhostQuery!)
            {
                if (_inQueue.Contains(entity)) continue;

                _promotionQueue.Enqueue(entity);
                _inQueue.Add(entity);
            }
        }

        private void PromoteGhost(Entity entity, IEntityCommandBuffer cmdBuffer, uint tick)
        {
            var tkbIdentity = _world!.GetComponent<TkbIdentity>(entity);

            // Fetch component mask for O(1) bitmask checks.
            ref var compGP = ref _world.GetComponentMask(entity.Index);

            // Read ghost age for soft-timeout evaluation.
            var tracker = _world.GetComponent<GhostStateTracker>(entity);

            // Evaluate mandatory components defined by the template.
            if (_tkbDatabase.TryGetByType(tkbIdentity.TkbType, out var template))
            {
                foreach (var req in template.MandatoryComponents)
                {
                    bool hasComponent = compGP.IsSet(req.ComponentTypeId);

                    if (!hasComponent)
                    {
                        if (req.IsHard)
                            return; // Abort — hard requirement not yet satisfied.

                        // Soft requirement: wait until timeout expires.
                        if (tick - tracker.FirstSeenFrame <= req.SoftTimeoutFrames)
                            return;
                        // Timeout elapsed — proceed without this optional component.
                    }
                }

                // All requirements satisfied: apply blueprint defaults.
                foreach (var t in Translators)
                    t.Inject(_world!, entity, template);
            }

            // Promote: Ghost → Constructing.
            // The entity naturally falls out of _readyGhostQuery next frame because
            // the query requires EntityLifecycle.Ghost.
            _world!.SetLifecycleState(entity, EntityLifecycle.Constructing);

            // Remove the transient tracker now that the ghost has been promoted.
            _world!.RemoveComponent<GhostStateTracker>(entity);

            _lifecycleModule.BeginConstruction(entity, tkbIdentity.TkbType, tick, cmdBuffer);
        }

        private void EnsureQueriesInitialized(EntityRepository repo)
        {
            if (_readyGhostQuery != null) return;

            _readyGhostQuery = repo.Query()
                .With<TkbIdentity>()
                .WithLifecycle(EntityLifecycle.Ghost)
                .Build();
        }
    }
}

