using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4624 MS-T2 (docs/designs/minefields/DESIGN_Minefields.md MR-5, lane decision R-5049) — a driven unit's own track near a minefield: the struck unit backs out along it.
    /// </summary>
    public sealed class MineBreadcrumbTests
    {
        private static (EntityRepository W, Entity Unit, Entity Far) Rig()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<Minefield>(); w.RegisterComponent<EntityGuid>(); w.RegisterComponent<MineBreadcrumbs>(); w.RegisterComponent<BehaviorState>();
            w.RegisterManagedComponent<TestAreaOutline>();
            var field = w.CreateEntity();
            w.AddComponent(field, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(field, new Minefield { Type = MineType.AntiPersonnel, SpacingM = 5f });
            w.SetManagedComponent(field, new TestAreaOutline { Points = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100) } });
            Entity Unit(Vector2 at)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
                w.AddComponent(e, new BehaviorState());
                return e;
            }
            return (w, Unit(new Vector2(-30, 50)), Unit(new Vector2(-500, 50)));
        }

        private static void MoveTo(EntityRepository w, Entity e, Vector2 p) { var t = w.GetComponentRO<SimTransform>(e); t.Position = new Vector3(p, 0f); w.SetComponent(e, t); }

        /// <summary>
        /// ⭐ MR-5 — walking into a field leaves a point every 2 m (from 15 m outside it); the trail backs out NEWEST first, ending outside the field; a unit far from every field
        /// gets none; clear of every field for 30 s the trail is dropped. 🔴 Red-proof: a step test of 0 ⇒ a point every frame (more than the walk's 2 m spacing).
        /// </summary>
        [Fact]
        public void AUnitNearAField_LeavesABackOutTrail_EveryTwoMetres_AndDropsItOnceClear()
        {
            var (w, unit, far) = Rig();
            var sys = new MineBreadcrumbSystem();
            for (int i = 0; i <= 60; i++) { MoveTo(w, unit, new Vector2(-30 + i * 1f, 50)); sys.Execute(w, 0.1f); }   // from x = −30 to x = 30, 1 m a frame
            Assert.False(w.HasComponent<MineBreadcrumbs>(far));
            var crumbs = w.GetComponentRO<MineBreadcrumbs>(unit);
            Assert.InRange(crumbs.Count, 22, 24);                                  // from x = −15 (the margin) to 30, every 2 m
            Span<Vector2> back = stackalloc Vector2[MineBreadcrumbs.Capacity];
            int n = crumbs.CopyBack(back);
            Assert.True(back[0].X > back[n - 1].X, "newest first");
            Assert.True(back[n - 1].X < 0f, "the trail reaches back outside the field");
            for (int k = 1; k < n; k++) Assert.InRange(Vector2.Distance(back[k - 1], back[k]), 1.9f, 2.1f);

            MoveTo(w, unit, new Vector2(-300, 50));
            for (int i = 0; i < 310; i++) sys.Execute(w, 0.1f);                   // 31 s clear of every field
            Assert.False(w.HasComponent<MineBreadcrumbs>(unit));
        }

        /// <summary>⭐ R-2084 — a steady frame (units standing near a field) allocates nothing.</summary>
        [Fact]
        public void ASteadyFrame_AllocatesNothing()
        {
            var (w, unit, _) = Rig();
            var sys = new MineBreadcrumbSystem();
            MoveTo(w, unit, new Vector2(10, 50));
            for (int i = 0; i < 20; i++) sys.Execute(w, 0.1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) sys.Execute(w, 0.1f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
