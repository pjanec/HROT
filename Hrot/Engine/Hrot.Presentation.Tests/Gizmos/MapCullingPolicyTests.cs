using System;
using System.Linq;
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

        // ── CE-3120 (R-227) — a gizmo FAMILY's scope and per-unit PINS, through the same per-projector seam ──────────────

        private Entity Mover(float x, bool selected = false)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new SimTransform { Position = new Vector3(x, 0f, 0f) });
            var trace = new CarKinem.Core.PathTrace { TrajectoryId = 1, TotalLength = 10f, Count = 2 };
            trace.PointsRW()[0] = new CarKinem.Core.PathTracePoint { Position = new Vector3(x, 0, 0), S = 0f };
            trace.PointsRW()[1] = new CarKinem.Core.PathTracePoint { Position = new Vector3(x, 10, 0), S = 10f };
            _repo.AddComponent(e, trace);
            _repo.AddComponent(e, new CarKinem.Core.NavState { Mode = CarKinem.Core.KinematicsMode.CustomTrajectory, TrajectoryId = 1 });
            if (selected) _repo.AddComponent(e, new SelectionState { IsSelected = true });
            return e;
        }

        private void RegisterFamilyTypes()
        {
            _repo.RegisterComponent<CarKinem.Core.PathTrace>();
            _repo.RegisterComponent<CarKinem.Core.NavState>();
            _repo.RegisterComponent<SelectionState>();
            _repo.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.DebugState>();
            _repo.RegisterManagedEvent<Fdp.Toolkit.Behavior.Diagnostics.PatchDebugStateCommand>();
        }

        /// <summary>The path lines a frame drew, grouped by the mover's x (each mover's path runs along its own x).</summary>
        private static float[] PathXs(MapInteraction mi) => mi.Buffer.GetFrame().ToArray()
            .Where(p => p.DebugLayer == DebugTraceLayers.Paths && p.Shape == DebugPrimitiveShape.Line)
            .Select(p => p.LineStart.X).Distinct().OrderBy(x => x).ToArray();

        /// <summary>
        /// ⭐⭐ <c>CE-3120</c> — the Path family defaults to <i>selected or pinned</i>: the pack attaches the family policy to
        /// <c>PlannedPathGizmo</c>, so only the selected mover's path draws; PINNING the other (a <c>PatchDebugStateCommand</c> applied
        /// by <c>DebugStatePatchSystem</c>, exactly what the context-menu action publishes) makes it draw too; switching the family to
        /// <i>All</i> draws every mover.
        /// </summary>
        [Fact]
        public void CE3120_ThePathFamily_DrawsTheSelectedAndThePinned_UntilItsScopeIsAll()
        {
            RegisterFamilyTypes();
            var settings = new GizmoSettingsRegistry();
            Mover(0f, selected: true);
            var other = Mover(50f);
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _repo, StartEnabled = true, Settings = settings });

            mi.StatelessSystem.Execute(_repo, 0.016f);
            Assert.Equal(new[] { 0f }, PathXs(mi));

            Hrot.Common.Diagnostics.Gizmos.GizmoPins.Set(_repo, other, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path, true);
            _repo.Bus.SwapBuffers();
            new Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem().Execute(_repo, 0.016f);
            mi.Buffer.Clear();
            mi.StatelessSystem.Execute(_repo, 0.016f);
            Assert.Equal(new[] { 0f, 50f }, PathXs(mi));

            Hrot.Common.Diagnostics.Gizmos.GizmoPins.Set(_repo, other, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path, false);
            _repo.Bus.SwapBuffers();
            new Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem().Execute(_repo, 0.016f);
            GizmoFamilies.SetScope(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path, GizmoScope.All);
            mi.Buffer.Clear();
            mi.StatelessSystem.Execute(_repo, 0.016f);
            Assert.Equal(new[] { 0f, 50f }, PathXs(mi));   // unpinned, but the family now draws for every mover
        }

        /// <summary>
        /// ⭐ <c>CE-3120</c> — the pin actions reach every map host through the pack, and a pin of ONE family leaves the others alone.
        /// </summary>
        [Fact]
        public void CE3120_ThePackRegistersThePinActions_AndAPinTouchesOnlyItsFamily()
        {
            RegisterFamilyTypes();
            var unit = Mover(0f);
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _repo, StartEnabled = true });
            foreach (var f in GizmoFamilies.All)
                Assert.True(mi.Actions.TryGetHandler(Hrot.Common.Diagnostics.Gizmos.GizmoPins.ActionIdOf(f), out _), $"pin action for {f}");

            Hrot.Common.Diagnostics.Gizmos.GizmoPins.Toggle(_repo, unit, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory);
            _repo.Bus.SwapBuffers();
            new Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem().Execute(_repo, 0.016f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory,
                _repo.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(unit).Ai);

            // ⭐ the unit's right-click menu carries the "Pin gizmos" submenu with every family's action (the DTO serialises camelCase)
            string menu = Hrot.Common.Diagnostics.Gizmos.ContextMenuProjectorGizmo.MenuJsonFor(_repo, unit);
            Assert.Contains("Pin gizmos", menu);
            foreach (var f in GizmoFamilies.All)
                Assert.Contains($"\"id\":{Hrot.Common.Diagnostics.Gizmos.GizmoPins.ActionIdOf(f)},", menu);
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
