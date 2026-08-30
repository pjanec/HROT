using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Map;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S4</c> — culling as a POLICY.</b>
    /// 📄 Design: <c>UX_Feature_Entity_Symbology.md</c> §3.4 (the target line) ·
    /// <c>UX_Feature_Map_Parity.md</c> §3.2f (why it could not be built until now).
    ///
    /// <para>🔴 <b>The rails here would ALL have been vacuous before <c>S4</c>.</b>
    /// <c>StatelessGizmoSystem</c> never called <c>IsEntityVisible</c>, so a per-entity policy was stored
    /// and silently ignored — the seam law's second failure mode. These rails exist to keep the consumer
    /// half honoured.</para>
    /// </summary>
    public sealed class MapCullingPolicyTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public MapCullingPolicyTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<CullingState>();
        }

        public void Dispose() => _repo.Dispose();

        private Entity Spawn(long id, bool? visible = null)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            _repo.AddComponent(e, new NetworkIdentity(id));
            if (visible is bool v) _repo.AddComponent(e, new CullingState { IsVisible = v });
            return e;
        }

        private static GizmoSettingsRegistry CullingOn()
        {
            var s = new GizmoSettingsRegistry();
            EntityPresentationGizmoSettings.Register(s);
            s.Write(GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.CullOffscreen),
                    GizmoSettingValue.From(true));
            return s;
        }

        // ── the consumer half: StatelessGizmoSystem must honour IsEntityVisible ─────────────────

        /// <summary>
        /// 🔴🔴 <b>The rail that makes §3.4's design possible at all.</b> Before <c>S4</c> this was red by
        /// construction: the system consulted only <c>IsGloballyEnabled</c>, so a per-entity policy could
        /// never suppress anything.
        /// </summary>
        [Fact]
        public void TheStatelessSystem_HonoursAPerEntityPolicy()
        {
            var entity = Spawn(1L);

            // ⚠⚠ NOT NeverVisiblePolicy. That returns false from IsGloballyEnabled too, which the PRE-S4
            // system already honoured — so a rail using it passes with or without the per-entity call, and
            // proves nothing about the half S4 added. Its own red-proof caught that. This policy is
            // globally enabled and entity-invisible, so only the per-entity call can suppress it.
            var registry = new StatelessGizmoRegistry();
            var probe = new CountingProbe();
            registry.Register(probe, new[] { typeof(SimTransform) }, new EntityOnlyInvisiblePolicy());

            new StatelessGizmoSystem(registry, new DebugPrimitiveBuffer()).Execute(_repo, 0.016f);

            Assert.Equal(0, probe.DrawCount);
            Assert.True(entity.Index >= 0);
        }

        /// <summary>⭐ And the default policy still draws — the fast path must not suppress anything.</summary>
        [Fact]
        public void TheStatelessSystem_StillDrawsUnderTheDefaultPolicy()
        {
            Spawn(2L);

            var registry = new StatelessGizmoRegistry();
            var probe = new CountingProbe();
            registry.Register(probe, new[] { typeof(SimTransform) });   // defaults to AlwaysVisiblePolicy

            new StatelessGizmoSystem(registry, new DebugPrimitiveBuffer()).Execute(_repo, 0.016f);

            Assert.True(probe.DrawCount > 0);
        }

        // ── the policy itself ───────────────────────────────────────────────────────────────────

        /// <summary>⭐ Culling ON + hidden entity ⇒ suppressed. This is IG's capability, preserved.</summary>
        [Fact]
        public void ThePolicy_HidesAnOffScreenEntityWhenTheHostAsked()
        {
            var entity = Spawn(3L, visible: false);
            var policy = new CullingStateVisibilityPolicy(CullingOn());

            Assert.False(policy.IsEntityVisible(_repo, entity));
        }

        /// <summary>⭐ Culling ON + visible entity ⇒ drawn. A gate, not an off switch.</summary>
        [Fact]
        public void ThePolicy_ShowsAnOnScreenEntityWhenTheHostAsked()
        {
            var entity = Spawn(4L, visible: true);
            var policy = new CullingStateVisibilityPolicy(CullingOn());

            Assert.True(policy.IsEntityVisible(_repo, entity));
        }

        /// <summary>
        /// 🔴 <b>Default OFF — the measured default (<c>CE-131</c>).</b> A host that carries
        /// <c>CullingState</c> but does not maintain it must still get a map; enabling culling by default
        /// blanked the IG perspective in a live run.
        /// </summary>
        [Fact]
        public void ThePolicy_IgnoresCullingStateUnlessTheHostAsked()
        {
            var entity = Spawn(5L, visible: false);
            var policy = new CullingStateVisibilityPolicy(new GizmoSettingsRegistry());

            Assert.True(policy.IsEntityVisible(_repo, entity),
                "Culling must be OPT-IN. CE-131: IG's MapCullingSystem marks every entity invisible, so a "
              + "default-on policy blanks its map.");
        }

        /// <summary>⭐ No CullingState at all ⇒ visible, even with culling on. Absence means DRAW.</summary>
        [Fact]
        public void ThePolicy_ShowsAnEntityThatCarriesNoCullingState()
        {
            var entity = Spawn(6L);                       // no CullingState
            var policy = new CullingStateVisibilityPolicy(CullingOn());

            Assert.True(policy.IsEntityVisible(_repo, entity));
        }

        /// <summary>⭐ It never suppresses the whole projector — culling is a per-entity question.</summary>
        [Fact]
        public void ThePolicy_IsAlwaysGloballyEnabled()
            => Assert.True(new CullingStateVisibilityPolicy(CullingOn()).IsGloballyEnabled(_repo));

        // ── end to end, through the pack ────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <b>The whole route, as a host would get it.</b> The pack's default resolver attaches the
        /// culling policy to the entity projector; with culling on and every entity off-screen, the map
        /// emits no entity shapes — through the seam, with no filtering inside the projector.
        /// </summary>
        [Fact]
        public void ThePack_WiresCullingThroughTheSeam()
        {
            for (int i = 0; i < 4; i++) Spawn(10 + i, visible: false);

            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _repo,
                Settings = CullingOn(),
                StartEnabled = true,
            });

            mi.StatelessSystem.Execute(_repo, 0.016f);

            foreach (var p in mi.Buffer.GetFrame().ToArray())
                Assert.NotEqual(DebugPrimitiveShape.SemanticShape, p.Shape);
        }

        /// <summary>⭐ The same pack, culling not asked for ⇒ the map draws. The default is unchanged.</summary>
        [Fact]
        public void ThePack_DrawsWhenCullingWasNotAskedFor()
        {
            for (int i = 0; i < 4; i++) Spawn(20 + i, visible: false);

            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _repo,
                StartEnabled = true,
            });

            mi.StatelessSystem.Execute(_repo, 0.016f);

            int shapes = 0;
            foreach (var p in mi.Buffer.GetFrame().ToArray())
                if (p.Shape == DebugPrimitiveShape.SemanticShape) shapes++;

            Assert.True(shapes > 0);
        }

        /// <summary>⭐⭐ A host may override the resolver entirely — the <c>R-137</c> "gained" row.</summary>
        [Fact]
        public void AHostCanAttachItsOwnPolicyToAnyProjector()
        {
            Spawn(30L);

            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _repo,
                StartEnabled = true,
                VisibilityPolicyResolver = type =>
                    type == typeof(EntityPresentationGizmo) ? NeverVisiblePolicy.Instance : null,
            });

            mi.StatelessSystem.Execute(_repo, 0.016f);

            foreach (var p in mi.Buffer.GetFrame().ToArray())
                Assert.NotEqual(DebugPrimitiveShape.SemanticShape, p.Shape);
        }

        /// <summary>
        /// ⭐ Globally enabled, per-entity invisible — the ONLY shape that can distinguish the per-entity
        /// call from the global one. <c>NeverVisiblePolicy</c> cannot: it fails the global check first.
        /// </summary>
        private sealed class EntityOnlyInvisiblePolicy : IGizmoVisibilityPolicy
        {
            public bool IsGloballyEnabled(Fdp.ModuleHost.Abstractions.ISimulationView view) => true;
            public bool IsEntityVisible(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity entity) => false;
        }

        private sealed class CountingProbe : IStatelessGizmo
        {
            public int DrawCount;
            public void Draw(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity entity,
                             IDebugDrawBuilder drawBuilder) => DrawCount++;
        }
    }
}
