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
        /// ⭐ <c>QA-009</c> — the STABILITY(Flaky) trait and its order-dependence note are GONE: the
        /// sentinel no longer carries a <c>[ComponentId]</c>, so no prior test in the process can put it
        /// in the registry. See the type's own comment for why that is the invariant.
        [Fact]
        public void SC_GZ022_2_Register_UnregisteredType_Throws()
        {
            var registry  = new StatelessGizmoRegistry();
            var projector = new MockStatelessGizmo();

            // ⭐ State the precondition — see the sibling case in GizmosSystemTests for why.
            Assert.Equal(-1, ComponentTypeRegistry.GetId(typeof(UnregisteredComp)));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(projector, new[] { typeof(UnregisteredComp) }));
        }
    }

    // ⭐⭐⭐ QA-009 — a sentinel that CANNOT be registered, by construction.
    //
    // ⛔ This used to carry [ComponentId(249)], which made it registerable — and something in a full
    //    suite run registered it, after which GetId returned 249 and SC_GZ022_2 could not throw. That
    //    is exactly what the STABILITY(Flaky) note above described and never fixed.
    //
    // ⭐ ComponentTypeRegistry.GetOrRegisterManaged REQUIRES a [ComponentId] and throws without one, so
    //    an attribute-less struct can never enter the registry by ANY path. The ABSENCE of the
    //    attribute is the invariant — do not add one back.
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

        // ── CE-188: a rule registered AFTER construction ──────────────────────
        //
        // ⚠ Named MockGlobalStatelessGizmo: this assembly already has a MockGlobalGizmo, and it
        // implements a DIFFERENT interface (IEntityStatefulGizmo).


        /// <summary>
        /// <b>The live defect: this system threw once per frame in every editor run.</b>
        ///
        /// <para>The visibility caches are sized in the constructor from the registry's counts, and the
        /// constructor's own documentation states that everything must be registered first. Production
        /// violates that: the registry is mutable (<c>Register</c>/<c>RegisterGlobal</c> append) and
        /// projectors arrive later, including on every AI hot-reload. The second global-rules loop then
        /// indexed the stale cache without a bounds check and threw
        /// <c>IndexOutOfRangeException</c>.</para>
        ///
        /// <para>⛔ The cost was not the throw. <c>TogglablePostSimulationGroup.Execute</c> is a plain
        /// <c>foreach</c> with no <c>try</c>, so <b>every system after this one in the group was skipped,
        /// every frame</b> — and <c>ModuleHostKernel</c> swallowed the exception, so the node kept
        /// answering healthy.</para>
        ///
        /// <para>⚠ Guarding the loop would have stopped the throw and left late-registered gizmos
        /// silently never drawing. The caches are grown instead, so registration order stops mattering.</para>
        /// </summary>
        [Fact]
        public void CE188_AGlobalRuleRegisteredAfterConstruction_DrawsInsteadOfThrowing()
        {
            var (repo, buf, registry) = CreateContext();

            // Constructed while the registry is EMPTY — exactly the production sequence.
            var sys = new StatelessGizmoSystem(registry, buf);

            var late = new MockGlobalStatelessGizmo();
            registry.RegisterGlobal(late);

            sys.Execute(repo, 0f);

            Assert.Equal(1, late.DrawCount);
        }

        /// <summary>The same for entity-scoped rules — one cache, one defect, two arms.</summary>
        [Fact]
        public void CE188_AnEntityRuleRegisteredAfterConstruction_DrawsInsteadOfBeingSkipped()
        {
            var (repo, buf, registry) = CreateContext();

            var sys = new StatelessGizmoSystem(registry, buf);

            var late = new MockStatelessGizmo();
            registry.Register(late, new[] { typeof(GizmoTestCompA) });

            var e = repo.CreateEntity();
            repo.AddComponent(e, new GizmoTestCompA { Value = 7 });

            sys.Execute(repo, 0f);

            Assert.Equal(1, late.DrawCount);
        }

        /// <summary>And repeated growth keeps working — hot reload registers more than once.</summary>
        [Fact]
        public void CE188_RulesRegisteredAcrossSeveralFramesAllDraw()
        {
            var (repo, buf, registry) = CreateContext();
            var sys = new StatelessGizmoSystem(registry, buf);

            var first = new MockGlobalStatelessGizmo();
            registry.RegisterGlobal(first);
            sys.Execute(repo, 0f);

            var second = new MockGlobalStatelessGizmo();
            registry.RegisterGlobal(second);
            sys.Execute(repo, 0f);

            Assert.Equal(2, first.DrawCount);
            Assert.Equal(1, second.DrawCount);
        }
    }

    /// <summary>A global (entity-less) stateless projector double.</summary>
    internal sealed class MockGlobalStatelessGizmo : IGlobalStatelessGizmo
    {
        public int DrawCount;

        public void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder) => DrawCount++;
    }
}
