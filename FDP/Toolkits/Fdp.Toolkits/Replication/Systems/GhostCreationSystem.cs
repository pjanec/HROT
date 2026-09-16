using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostCreationSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        /// <summary>
        /// ⛔⛔⛔ <b>MEASURED <c>2026-09-11</c>: THIS PROPERTY IS WRITTEN BY THREE PRODUCTION SITES AND
        /// READ BY NONE. Setting it changes nothing.</b> 📄 Filed as <c>CE-259ap</c>.
        ///
        /// <para>⚠⚠ <b>Its previous summary described behaviour that does not exist:</b> *"When
        /// <c>true</c>, <see cref="CreateGhost"/> skips all lifecycle state assignments and network map
        /// registration, creating a bare entity shell only."* 📐 <see cref="CreateGhost"/> below does
        /// <b>not consult this flag</b> — it unconditionally sets <c>EntityLifecycle.Ghost</c> and
        /// registers the entity in the <c>NetworkEntityMap</c>. ⇒ the comment was a SPECIFICATION read as
        /// a description, which is the worst kind of doc rot: it reads as a guarantee.</para>
        ///
        /// <para>📐 <b>The write sites, so the intent is not lost:</b>
        /// <c>ReferenceReplayLoadHandler</c> takes a <c>bypass =&gt; …BypassLifecycle = bypass</c>
        /// callback and flips it <c>true</c> on <c>RunningReplay</c> / <c>false</c> on
        /// <c>RunningLive</c> (<c>CGF1-S0304</c>/<c>S0305</c>); <c>NodeBootstrapper.cs:219</c> supplies
        /// that callback. ⭐ The intent — *incoming network samples must not spawn ghosts whose ids
        /// collide with the recorded entities being replayed* — is real and is recorded in
        /// <c>docs/designs/replay-and-modules/DESIGN.md</c> §2.1.</para>
        ///
        /// <para>⛔ <b>NOT implemented here deliberately</b>: honouring it would CHANGE replay behaviour
        /// on every host, and nothing has yet measured what depends on ghost creation continuing during
        /// playback. ⚠ <b>And four rails assert this flag FLIPS</b> — <c>ReplayLoadClusterOpHandlerTests</c>,
        /// <c>LiveFromReplayTests</c>, <c>NodeBootstrapperReplayTests</c>, <c>FullBranchPipelineTests</c>,
        /// **12/12 green** — while none asserts it has an EFFECT. 📌 A green rail over a live defect
        /// (<c>R-142</c> ③): they test the setter, and the setter is all there is.</para>
        /// </summary>
        public bool BypassLifecycle { get; set; } = false;

        public GhostCreationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        // No-op: system is registered for pipeline consistency.
        public void Execute(ISimulationView view, float dt) { }
        /// Called by ingress translators on the Input phase main thread.
        /// The caller must supply a live <see cref="EntityRepository"/> from their view.
        ///
        /// Sets <see cref="EntityLifecycle.Ghost"/> so <c>GhostPromotionSystem</c> can query
        /// by lifecycle state.  Also attaches <see cref="GhostStateTracker"/> stamped with the
        /// current simulation tick so that promotion and timeout systems can measure age.
        /// </summary>
        /// <param name="repo">The live entity repository.</param>
        /// <param name="networkId">The network (DIS) entity ID.</param>
        /// <param name="tick">
        ///   Current simulation tick (frame number).  Pass <c>view.Tick</c> from the
        ///   calling translator.  Defaults to <c>0</c> for backward compatibility.
        /// </param>
        public Entity CreateGhost(EntityRepository repo, long networkId, uint tick = 0)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(networkId));
            repo.AddComponent(entity, new GhostStateTracker { FirstSeenFrame = tick });

            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            _entityMap.Register(networkId, entity);

            return entity;
        }
    }
}

