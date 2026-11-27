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

        /// <summary>⭐ CE-2394 — one pass over the entities collects every rule's matches; the draw order is unchanged (rule by rule, each in
        /// ascending entity order), a dead slot is skipped, and a steady frame allocates nothing. ✅ Red-proof: append matches without
        /// resetting the per-rule counts ⇒ the second frame draws twice.</summary>
        [Fact]
        public void CE2394_OnePass_KeepsTheOrder_AndAllocatesNothingInASteadyFrame()
        {
            var (repo, buf, registry) = CreateContext();
            var order = new List<string>(64);
            var pa = new OrderGizmo("A", order); var pb = new OrderGizmo("B", order);
            registry.Register(pa, new[] { typeof(GizmoTestCompA) });
            registry.Register(pb, new[] { typeof(GizmoTestCompB) });
            var sys = new StatelessGizmoSystem(registry, buf);
            var e1 = repo.CreateEntity(); repo.AddComponent(e1, new GizmoTestCompA()); repo.AddComponent(e1, new GizmoTestCompB());
            var dead = repo.CreateEntity(); repo.AddComponent(dead, new GizmoTestCompA());
            var e3 = repo.CreateEntity(); repo.AddComponent(e3, new GizmoTestCompB());
            var e4 = repo.CreateEntity(); repo.AddComponent(e4, new GizmoTestCompA());
            repo.DestroyEntity(dead);

            sys.Execute(repo, 0f);
            Assert.Equal(new[] { $"A{e1.Index}", $"A{e4.Index}", $"B{e1.Index}", $"B{e3.Index}" }, order);

            order.Clear(); sys.Execute(repo, 0f); order.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            sys.Execute(repo, 0f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(4, order.Count);
            Assert.Equal(0, allocated);
        }

        /// <summary>
        /// ⭐ CE-2402 (DESIGN_Evaluation_Cost_And_Cadence §10) — with more matched entities than <c>FreshEntitiesPerFrame</c>, a rule draws a SLICE
        /// fresh per frame and re-emits the rest from their held pictures: every frame still holds EXACTLY one picture per entity in ascending
        /// entity order, no picture is older than the period, a new entity is drawn at once and a destroyed one is gone at once, a steady frame
        /// allocates nothing, and with the default (0) every entity is drawn every frame.
        /// 🔴 Red-proof: the held branch skipping the re-emit ⇒ the frame holds only the slice and the per-frame count fails.
        /// </summary>
        [Fact]
        public void CE2402_ASliceIsDrawnFresh_TheRestIsHeld_EveryFrameShowsEveryEntityOnce()
        {
            var (repo, _, registry) = CreateContext();
            var buf = new DebugPrimitiveBuffer(4096);
            var projector = new LineGizmo();
            registry.Register(projector, new[] { typeof(GizmoTestCompA) });
            var sys = new StatelessGizmoSystem(registry, buf) { FreshEntitiesPerFrame = 10, MaxHeldFrames = 3 };   // 40 entities ⇒ period 4
            var ents = new List<Entity>();
            for (int i = 0; i < 40; i++) { var e = repo.CreateEntity(); repo.AddComponent(e, new GizmoTestCompA()); ents.Add(e); }

            var lastDrawn = new Dictionary<int, int>();
            int[] Frame(int f)
            {
                buf.EndFrame(0f); projector.Drawn.Clear();
                sys.Execute(repo, 0f);
                foreach (var i in projector.Drawn) lastDrawn[i] = f;
                var frame = buf.GetFrame(); var xs = new int[frame.Length];
                for (int k = 0; k < frame.Length; k++) xs[k] = (int)frame[k].LineStart.X;
                return xs;
            }
            int[] Expected() { var l = new List<int>(); foreach (var e in ents) if (repo.IsAlive(e)) l.Add(e.Index); return l.ToArray(); }

            Assert.Equal(Expected(), Frame(0));
            Assert.Equal(40, projector.Drawn.Count);                                     // no picture yet: all fresh
            for (int f = 1; f <= 12; f++)
            {
                Assert.Equal(Expected(), Frame(f));                                      // one picture per entity, in order
                Assert.InRange(projector.Drawn.Count, 9, 11);                            // a quarter drawn fresh
                foreach (var e in ents) Assert.True(f - lastDrawn[e.Index] < 4, $"entity {e.Index} held {f - lastDrawn[e.Index]} frames");
            }

            var born = repo.CreateEntity(); repo.AddComponent(born, new GizmoTestCompA()); ents.Add(born);
            repo.DestroyEntity(ents[3]);
            Assert.Equal(Expected(), Frame(13));
            Assert.Contains(born.Index, projector.Drawn);                                // new ⇒ drawn at once

            Frame(14); Frame(15);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 16; f < 20; f++) { buf.EndFrame(0f); projector.Drawn.Clear(); sys.Execute(repo, 0f); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a0);

            sys.FreshEntitiesPerFrame = 0;                                               // off ⇒ every entity every frame
            Frame(20);
            Assert.Equal(Expected().Length, projector.Drawn.Count);
        }

        private sealed class LineGizmo : IStatelessGizmo
        {
            public readonly List<int> Drawn = new(128);
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder b)
            {
                Drawn.Add(entity.Index);
                b.DrawLine(new System.Numerics.Vector3(entity.Index, 0, 0), new System.Numerics.Vector3(entity.Index, 1, 0), default);
            }
        }

        private sealed class OrderGizmo : IStatelessGizmo
        {
            private readonly string _tag; private readonly List<string> _order;
            private readonly string[] _names = new string[64];
            public OrderGizmo(string tag, List<string> order) { _tag = tag; _order = order; for (int i = 0; i < 64; i++) _names[i] = tag + i; }
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => _order.Add(_names[entity.Index]);
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

        // ── ⭐ CE-4388 — the map's view filter (docs/DESIGN_Map_View_Filter.md, R-1049) ───────────────────────────────────────────────

        private sealed class FakeView : IGizmoViewFilter
        {
            public readonly HashSet<int> Visible = new();
            public bool Active { get; set; } = true;
            public float MetresPerPixel { get; set; }
            public int Tests;
            public bool InView(ISimulationView view, Entity entity) { Tests++; return Visible.Contains(entity.Index); }
            public bool Is3D => false;
            public bool Overlaps(System.Numerics.Vector2 min, System.Numerics.Vector2 max) => true;
        }

        [GizmoProjector(typeof(GizmoTestCompA))]
        private sealed class PlainGizmo : IStatelessGizmo
        {
            public readonly List<int> Drawn = new(16);
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => Drawn.Add(entity.Index);
        }

        [GizmoProjector(typeof(GizmoTestCompA), DrawsOffScreen = true)]
        private sealed class OffScreenGizmo : IStatelessGizmo
        {
            public readonly List<int> Drawn = new(16);
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => Drawn.Add(entity.Index);
        }

        [GizmoProjector(typeof(GizmoTestCompA), MaxMetresPerPixel = 1f)]
        private sealed class CloseOnlyGizmo : IStatelessGizmo
        {
            public readonly List<int> Drawn = new(16);
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => Drawn.Add(entity.Index);
        }

        [GizmoProjector(typeof(GizmoTestCompA), MinMetresPerPixel = 2f, MaxMetresPerPixel = 6f)]
        private sealed class BandGizmo : IStatelessGizmo
        {
            public readonly List<int> Drawn = new(16);
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => Drawn.Add(entity.Index);
        }

        /// <summary>⭐ CE-4388 (D-1, D-4, D-7) — an entity OUT of view reaches no rule — tested once however many rules match it — except a
        /// <c>DrawsOffScreen</c> one, which sees every match as before; a filter that is not active changes nothing. 🔴 Red-proof (compiles): the view
        /// test skipped in the match pass ⇒ the plain rule draws the out-of-view entity.</summary>
        [Fact]
        public void CE4388_AnEntityOutOfView_ReachesNoRule_ButAnOffScreenOne_AndIsTestedOnce()
        {
            var (repo, buf, registry) = CreateContext();
            var plain = new PlainGizmo(); var plain2 = new PlainGizmo(); var off = new OffScreenGizmo();
            registry.Register(plain, new[] { typeof(GizmoTestCompA) });
            registry.Register(plain2, new[] { typeof(GizmoTestCompA) });
            registry.Register(off, new[] { typeof(GizmoTestCompA) });
            var view = new FakeView();
            var sys = new StatelessGizmoSystem(registry, buf) { ViewFilter = view };
            var seen = repo.CreateEntity(); repo.AddComponent(seen, new GizmoTestCompA());
            var away = repo.CreateEntity(); repo.AddComponent(away, new GizmoTestCompA());
            view.Visible.Add(seen.Index);

            sys.Execute(repo, 0f);
            Assert.Equal(new[] { seen.Index }, plain.Drawn);
            Assert.Equal(new[] { seen.Index }, plain2.Drawn);
            Assert.Equal(new[] { seen.Index, away.Index }, off.Drawn);
            Assert.Equal(2, view.Tests);                 // once per entity, not once per rule
            Assert.Equal(1, sys.EntitiesOutOfView);

            view.Active = false; plain.Drawn.Clear(); off.Drawn.Clear();
            sys.Execute(repo, 0f);
            Assert.Equal(new[] { seen.Index, away.Index }, plain.Drawn);   // no view ⇒ everything, as before
        }

        /// <summary>⭐ CE-4388 (D-3) — a rule coarser than its <c>MaxMetresPerPixel</c> is skipped for the frame; closer in it draws; an unknown scale (0)
        /// gates nothing. 🔴 Red-proof (compiles): the zoom gate removed ⇒ the close-only rule draws at 5 m/px.</summary>
        [Fact]
        public void CE4388_ARuleCoarserThanItsZoomLimit_IsSkipped()
        {
            var (repo, buf, registry) = CreateContext();
            var close = new CloseOnlyGizmo(); var plain = new PlainGizmo();
            registry.Register(close, new[] { typeof(GizmoTestCompA) });
            registry.Register(plain, new[] { typeof(GizmoTestCompA) });
            var view = new FakeView { MetresPerPixel = 5f };
            var sys = new StatelessGizmoSystem(registry, buf) { ViewFilter = view };
            var e = repo.CreateEntity(); repo.AddComponent(e, new GizmoTestCompA());
            view.Visible.Add(e.Index);

            sys.Execute(repo, 0f);
            Assert.Empty(close.Drawn);
            Assert.Single(plain.Drawn);
            Assert.Equal(1, sys.RulesSkippedForZoom);

            view.MetresPerPixel = 0.5f; sys.Execute(repo, 0f);
            Assert.Single(close.Drawn);
            view.MetresPerPixel = 0f; sys.Execute(repo, 0f);
            Assert.Equal(2, close.Drawn.Count);
        }

        /// <summary>⭐ CE-8610 (docs/UX/DESIGN_Unit_Symbols.md P-3) — a rule with a zoom BAND draws only inside it: finer than its <c>MinMetresPerPixel</c> it is
        /// skipped like one coarser than its <c>Max</c>; an unknown scale (0) gates nothing. 🔴 Red-proof (compiles): the lower bound dropped ⇒ it draws at 1 m/px.</summary>
        [Fact]
        public void CE8610_ARuleWithAZoomBand_DrawsOnlyInsideIt()
        {
            var (repo, buf, registry) = CreateContext();
            var band = new BandGizmo();
            registry.Register(band, new[] { typeof(GizmoTestCompA) });
            var view = new FakeView { MetresPerPixel = 1f };
            var sys = new StatelessGizmoSystem(registry, buf) { ViewFilter = view };
            var e = repo.CreateEntity(); repo.AddComponent(e, new GizmoTestCompA());
            view.Visible.Add(e.Index);

            sys.Execute(repo, 0f);
            Assert.Empty(band.Drawn);                       // finer than the band
            Assert.Equal(1, sys.RulesSkippedForZoom);
            view.MetresPerPixel = 4f; sys.Execute(repo, 0f);
            Assert.Single(band.Drawn);                      // inside
            view.MetresPerPixel = 10f; sys.Execute(repo, 0f);
            Assert.Single(band.Drawn);                      // coarser
            view.MetresPerPixel = 0f; sys.Execute(repo, 0f);
            Assert.Equal(2, band.Drawn.Count);              // unknown scale: no gate
        }

        /// <summary>⭐ CE-4388 (R-2084) — a steady frame with the view filter on allocates nothing.</summary>
        [Fact]
        public void CE4388_ASteadyFilteredFrame_AllocatesNothing()
        {
            var (repo, buf, registry) = CreateContext();
            var plain = new PlainGizmo();
            registry.Register(plain, new[] { typeof(GizmoTestCompA) });
            var view = new FakeView { MetresPerPixel = 2f };
            var sys = new StatelessGizmoSystem(registry, buf) { ViewFilter = view };
            for (int k = 0; k < 20; k++) { var e = repo.CreateEntity(); repo.AddComponent(e, new GizmoTestCompA()); if (k % 2 == 0) view.Visible.Add(e.Index); }
            for (int k = 0; k < 3; k++) { plain.Drawn.Clear(); sys.Execute(repo, 0f); }
            plain.Drawn.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            sys.Execute(repo, 0f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(10, plain.Drawn.Count);
        }
    }

    /// <summary>
    /// ⭐⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-41 / R2-10 / R3-13) — a <c>[GizmoProjector(Hud = true)]</c>
    /// rule: the SYSTEM opens a <c>HudScope</c> for each entity's network id around its draw (an entity with no network id draws nothing); the HUD
    /// family's policy is fixed in the registry — the projector's own visibility policy beats the host's, no zoom band, drawn off screen; and it is
    /// never cut by the frame budget nor served from a held picture. 🔴 Red-proof: <c>DrawHud</c>'s <c>BeginScope</c> removed ⇒ no scope, the first rail fails.
    /// </summary>
    public class StatelessGizmoSystemHudTests
    {
        [GizmoProjector(typeof(GizmoTestCompA), Hud = true, MaxMetresPerPixel = 0.001f)]
        private sealed class HudDouble : IStatelessGizmo, IGizmoVisibilityPolicy
        {
            public readonly List<int> Drawn = new(16);
            public bool Visible = true;
            public bool IsGloballyEnabled(ISimulationView view) => true;
            public bool IsEntityVisible(ISimulationView view, Entity entity) => Visible;
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder b)
            {
                Drawn.Add(entity.Index);
                b.DrawLine(new System.Numerics.Vector3(entity.Index, 0, 0), new System.Numerics.Vector3(entity.Index, 1, 0), default, layer: DebugTraceLayers.DeviceHud);
            }
        }

        private sealed class SlowGizmo : IStatelessGizmo
        {
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder b) => System.Threading.Thread.Sleep(3);
        }

        private static Entity Device(EntityRepository repo, int netId)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new GizmoTestCompA());
            if (netId > 0) repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = netId });
            return e;
        }

        [Fact]
        public void S1d2_AHudRule_IsScopedPerEntityByItsNetworkId_AndAnEntityWithNoNetworkIdDrawsNothing()
        {
            using var repo = GizmoTestRepo.Create();
            var buf = new DebugPrimitiveBuffer(512);
            var registry = new StatelessGizmoRegistry();
            var hud = new HudDouble();
            registry.Register(hud, new[] { typeof(GizmoTestCompA) });
            var sys = new StatelessGizmoSystem(registry, buf);
            Device(repo, 41); Device(repo, 42); var bare = Device(repo, 0);

            sys.Execute(repo, 0f);
            var frame = buf.GetFrame();
            Assert.Equal(4, frame.Length);                                   // two scopes, each [scope, line]; the bare one drew nothing
            Assert.True(HudScopes.IsScope(in frame[0]) && frame[0].HudScopeSource == 41 && frame[0].HudScopeCount == 1);
            Assert.True(HudScopes.IsScope(in frame[2]) && frame[2].HudScopeSource == 42 && frame[2].HudScopeCount == 1);
            Assert.DoesNotContain(bare.Index, hud.Drawn);
            Assert.Equal(2, sys.HudRunsDrawn);
            DebugPrimitive[] scratch = Array.Empty<DebugPrimitive>();
            Assert.Equal(1, HudScopes.Keep(frame, 41, ref scratch).Length);   // a view bound to 41 keeps its line only
            Assert.Equal(0, HudScopes.Keep(frame, HudScopes.None, ref scratch).Length);
        }

        [Fact]
        public void S1d2_TheHudFamilyPolicy_IsFixedInTheRegistry_ItsOwnPolicyBeatsTheHosts_NoZoomBand_DrawnOffScreen()
        {
            using var repo = GizmoTestRepo.Create();
            var buf = new DebugPrimitiveBuffer(512);
            var registry = new StatelessGizmoRegistry();
            var hud = new HudDouble { Visible = false };
            registry.Register(hud, new[] { typeof(GizmoTestCompA) }, AlwaysVisiblePolicy.Instance);   // the host offers "always"
            var rule = registry.Rules[0];
            Assert.True(rule.Hud);
            Assert.Same(hud, rule.VisibilityPolicy);
            Assert.Equal(0f, rule.MaxMetresPerPixel);
            Assert.True(rule.DrawsOffScreen);
            var sys = new StatelessGizmoSystem(registry, buf);
            Device(repo, 41);
            sys.Execute(repo, 0f);
            Assert.Empty(hud.Drawn);                                          // the projector's own "no" wins
        }

        [Fact]
        public void S1d2_AHudRule_IsNeverCutByTheFrameBudget_NorHeld()
        {
            using var repo = GizmoTestRepo.Create();
            var buf = new DebugPrimitiveBuffer(4096);
            var registry = new StatelessGizmoRegistry();
            registry.Register(new SlowGizmo(), new[] { typeof(GizmoTestCompB) });   // registered FIRST: it eats the budget
            var hud = new HudDouble();
            registry.Register(hud, new[] { typeof(GizmoTestCompA) });
            var sys = new StatelessGizmoSystem(registry, buf) { MaxGizmoFrameMs = 1f, FreshEntitiesPerFrame = 1, MaxHeldFrames = 3 };
            for (int i = 0; i < 3; i++) { var e = repo.CreateEntity(); repo.AddComponent(e, new GizmoTestCompB()); }
            for (int i = 0; i < 4; i++) Device(repo, 100 + i);
            for (int f = 0; f < 3; f++)
            {
                hud.Drawn.Clear(); buf.EndFrame(0f);
                sys.Execute(repo, 0f);
                Assert.Equal(4, hud.Drawn.Count);                             // every device drawn fresh every frame, past the budget, never held
                Assert.Equal(4, sys.HudRunsDrawn);
            }
        }
    }

    /// <summary>A global (entity-less) stateless projector double.</summary>
    internal sealed class MockGlobalStatelessGizmo : IGlobalStatelessGizmo
    {
        public int DrawCount;

        public void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder) => DrawCount++;
    }
}
