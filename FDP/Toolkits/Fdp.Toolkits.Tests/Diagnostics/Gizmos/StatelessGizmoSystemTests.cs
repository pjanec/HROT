using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ==========================================================================
    // Mock stateless gizmo for testing
    // ==========================================================================

    internal sealed class MockStatelessGizmo : IStatelessGizmo
    {
        public int DrawCount;
        public List<Entity> DrawnEntities = new List<Entity>();

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder)
        {
            DrawCount++;
            DrawnEntities.Add(entity);
        }
    }

    // ==========================================================================
    // SC-GZ022: StatelessGizmoRegistry tests
    // ==========================================================================

    public class StatelessGizmoRegistryTests
    {
        // SC-GZ022-1: Register with GizmoTestCompA → mask has GizmoTestCompA bit set.
        [Fact]
        public void SC_GZ022_1_Register_SetsComponentBitInMask()
        {
            using var repo = GizmoTestRepo.Create();

            var registry  = new StatelessGizmoRegistry();
            var projector = new MockStatelessGizmo();

            registry.Register(projector, new[] { typeof(GizmoTestCompA) });

            Assert.Equal(1, registry.Rules.Count);
            int idA = ComponentTypeRegistry.GetId(typeof(GizmoTestCompA));
            Assert.NotEqual(-1, idA);
            Assert.True(registry.Rules[0].RequiredMask.IsSet(idA));
        }

        // SC-GZ022-2: Register with an unregistered component type → InvalidOperationException.
        // STABILITY(Flaky): Order-dependent — passes in isolation but fails in full suite when static ComponentTypeRegistry has UnregisteredComp[249] registered by a prior test in the process
        [Trait("Stability", "Flaky")]
        [Fact]
        public void SC_GZ022_2_Register_UnregisteredType_Throws()
        {
            var registry  = new StatelessGizmoRegistry();
            var projector = new MockStatelessGizmo();

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(projector, new[] { typeof(UnregisteredComp) }));
        }
    }

    // Sentinel type never registered with any EntityRepository.
    [ComponentId(249)]
    internal struct UnregisteredComp { }

    // ==========================================================================
    // SC-GZ022: StatelessGizmoSystem tests
    // ==========================================================================

    public class StatelessGizmoSystemTests
    {
        private static (EntityRepository repo, DebugPrimitiveBuffer buf, StatelessGizmoRegistry registry)
            CreateContext()
        {
            var repo     = GizmoTestRepo.Create();
            var buf      = new DebugPrimitiveBuffer(512);
            var registry = new StatelessGizmoRegistry();
            return (repo, buf, registry);
        }

        // SC-GZ022-3: Execute → Draw called for every matching entity.
        [Fact]
        public void SC_GZ022_3_Execute_CallsDraw_ForEveryMatchingEntity()
        {
            var (repo, buf, registry) = CreateContext();
            var projector = new MockStatelessGizmo();
            registry.Register(projector, new[] { typeof(GizmoTestCompA) });

            var sys = new StatelessGizmoSystem(registry, buf);

            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new GizmoTestCompA { Value = 1 });
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new GizmoTestCompA { Value = 2 });

            sys.Execute(repo, 0f);

            Assert.Equal(2, projector.DrawCount);
            Assert.Contains(e1, projector.DrawnEntities);
            Assert.Contains(e2, projector.DrawnEntities);
        }

        // SC-GZ022-4: Entity that does not have the required component → no Draw call.
        [Fact]
        public void SC_GZ022_4_Execute_NoDraw_ForNonMatchingEntity()
        {
            var (repo, buf, registry) = CreateContext();
            var projector = new MockStatelessGizmo();
            registry.Register(projector, new[] { typeof(GizmoTestCompA) });

            var sys = new StatelessGizmoSystem(registry, buf);

            // Only add GizmoTestCompB (rule requires GizmoTestCompA).
            var e = repo.CreateEntity();
            repo.AddComponent(e, new GizmoTestCompB { Value = 99 });

            sys.Execute(repo, 0f);

            Assert.Equal(0, projector.DrawCount);
        }

        // SC-GZ022-5: isSelectedPredicate returns false for all entities → no Draw calls.
        [Fact]
        public void SC_GZ022_5_Execute_Predicate_ReturnsFalse_NoDraw()
        {
            var (repo, buf, registry) = CreateContext();
            var projector = new MockStatelessGizmo();
            registry.Register(projector, new[] { typeof(GizmoTestCompA) });

            // Predicate always rejects.
            var sys = new StatelessGizmoSystem(registry, buf, (_, _) => false);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new GizmoTestCompA { Value = 1 });

            sys.Execute(repo, 0f);

            Assert.Equal(0, projector.DrawCount);
        }

        // SC-GZ022-6: isSelectedPredicate is null → all matching entities drawn.
        [Fact]
        public void SC_GZ022_6_Execute_NullPredicate_DrawsAll()
        {
            var (repo, buf, registry) = CreateContext();
            var projector = new MockStatelessGizmo();
            registry.Register(projector, new[] { typeof(GizmoTestCompA) });

            var sys = new StatelessGizmoSystem(registry, buf, isSelectedPredicate: null);

            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new GizmoTestCompA());
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new GizmoTestCompA());

            sys.Execute(repo, 0f);

            Assert.Equal(2, projector.DrawCount);
        }

        // SC-GZ022-7: IsGloballyEnabled is evaluated once per rule per frame,
        // regardless of how many entities match.
        [Fact]
        public void SC_GZ022_7_Execute_GlobalVisibilityPolicy_EvaluatedOncePerRule()
        {
            var (repo, buf, registry) = CreateContext();
            var projector = new MockStatelessGizmo();
            var policy    = new MockVisibilityPolicy { GloballyEnabled = true, EntityVisible = true };
            registry.Register(projector, new[] { typeof(GizmoTestCompA) }, policy);

            var sys = new StatelessGizmoSystem(registry, buf);

            // Create 5 matching entities.
            for (int i = 0; i < 5; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new GizmoTestCompA());
            }

            sys.Execute(repo, 0f);

            // IsGloballyEnabled must be called exactly once (1 rule × 1 frame), not 5 times.
            Assert.Equal(1, policy.IsGloballyEnabledCallCount);
            Assert.Equal(5, projector.DrawCount);
        }

        // SC-GZ022-8: NeverVisiblePolicy → no Draw calls at all.
        [Fact]
        public void SC_GZ022_8_Execute_NeverVisiblePolicy_NoDraw()
        {
            var (repo, buf, registry) = CreateContext();
            var projector = new MockStatelessGizmo();
            registry.Register(projector, new[] { typeof(GizmoTestCompA) }, NeverVisiblePolicy.Instance);

            var sys = new StatelessGizmoSystem(registry, buf);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new GizmoTestCompA());

            sys.Execute(repo, 0f);

            Assert.Equal(0, projector.DrawCount);
        }
    }
}
