using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    /// <summary>
    /// ⭐⭐ CE-1480 G5 (docs/DESIGN_Change_Gated_Systems.md §4 G5) — the gizmo pass skips what did not change, and the picture is IDENTICAL to the
    /// ungated one: a rule that declares <c>Inputs</c> is re-appended while they are still, the match pass is skipped while the entity set / the view /
    /// the active rules are still. The ungated reference is the SAME projector without the declaration, on its own buffer, over the same world.
    /// </summary>
    public sealed class ChangeGatedGizmoTests
    {
        /// <summary>A pure projector: one line per entity from its component value; optionally declares its inputs.</summary>
        private sealed class PureGizmo : IStatelessGizmo
        {
            public int Draws;
            public GizmoInputs? Inputs { get; }
            public PureGizmo(bool declare, bool dependsOnView = false)
                => Inputs = declare ? new GizmoInputs(dependsOnView, null, typeof(GizmoTestCompA)) : null;
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder b)
            {
                Draws++;
                int v = view.GetComponentRO<GizmoTestCompA>(entity).Value;
                b.DrawLine(new Vector3(entity.Index, v, 0), new Vector3(entity.Index, v + 1, 0), default);
            }
        }

        /// <summary>A view filter with a rectangle on the entity index, and a Version that moves when the rectangle does.</summary>
        private sealed class Rect : IGizmoViewFilter
        {
            public int MaxX = 1000; public long Ver = 1;
            public bool Active => true;
            public float MetresPerPixel => 1f;
            public bool Is3D => false;
            public long Version => Ver;
            public bool InView(ISimulationView view, Entity entity) => entity.Index <= MaxX;
            public bool Overlaps(Vector2 min, Vector2 max) => true;
            public void Set(int maxX) { if (maxX != MaxX) { MaxX = maxX; Ver++; } }
        }

        private sealed class Rig
        {
            public readonly EntityRepository Repo = GizmoTestRepo.Create();
            public readonly DebugPrimitiveBuffer GatedBuf = new(4096), PlainBuf = new(4096);
            public readonly PureGizmo GatedGizmo, PlainGizmo;
            public readonly StatelessGizmoSystem Gated, Plain;
            public readonly Rect View = new();
            public readonly List<Entity> Ents = new();

            public Rig(bool viewDependent = false, bool withView = false)
            {
                GatedGizmo = new PureGizmo(true, viewDependent); PlainGizmo = new PureGizmo(false);
                var rg = new StatelessGizmoRegistry(); rg.Register(GatedGizmo, new[] { typeof(GizmoTestCompA) });
                var rp = new StatelessGizmoRegistry(); rp.Register(PlainGizmo, new[] { typeof(GizmoTestCompA) });
                Gated = new StatelessGizmoSystem(rg, GatedBuf); Plain = new StatelessGizmoSystem(rp, PlainBuf);
                if (withView) { Gated.ViewFilter = View; Plain.ViewFilter = View; }
                for (int i = 0; i < 30; i++) Ents.Add(Spawn(i));
            }

            public Entity Spawn(int v) { var e = Repo.CreateEntity(); Repo.AddComponent(e, new GizmoTestCompA { Value = v }); return e; }

            /// <summary>One kernel frame; both systems run; the two pictures must be the same primitives in the same order.</summary>
            public void Frame()
            {
                Repo.Tick(); GatedBuf.EndFrame(0f); PlainBuf.EndFrame(0f);
                Gated.Execute(Repo, 0f); Plain.Execute(Repo, 0f);
                var g = GatedBuf.GetFrame(); var p = PlainBuf.GetFrame();
                Assert.Equal(p.Length, g.Length);
                for (int i = 0; i < g.Length; i++) Assert.True(g[i].LineStart == p[i].LineStart && g[i].LineEnd == p[i].LineEnd, $"primitive {i} differs");
            }
        }

        [Fact]
        public void AStillWorld_ReAppendsThePicture_AndSkipsTheMatchPass_AndEqualsTheUngatedFrame()
        {
            var rig = new Rig();
            for (int i = 0; i < 6; i++) rig.Frame();
            int draws = rig.GatedGizmo.Draws, reused = rig.Gated.RulePicturesReused, skipped = rig.Gated.MatchPassesSkipped;
            for (int i = 0; i < 5; i++) rig.Frame();
            Assert.Equal(draws, rig.GatedGizmo.Draws);                      // the rule was not drawn at all
            Assert.Equal(reused + 5, rig.Gated.RulePicturesReused);         // its picture was re-appended
            Assert.Equal(skipped + 5, rig.Gated.MatchPassesSkipped);        // and the walk over the entities did not run
            Assert.True(rig.PlainGizmo.Draws > draws);                      // (the ungated reference drew every frame)
        }

        [Fact]
        public void EveryInputChange_IsFollowedOnTheNextFrame_AndTheFrameEqualsTheUngatedOne()
        {
            var rig = new Rig();
            for (int i = 0; i < 6; i++) rig.Frame();

            rig.Repo.SetComponent(rig.Ents[4], new GizmoTestCompA { Value = 99 });   // a declared input's VALUE
            int draws = rig.GatedGizmo.Draws;
            rig.Frame();
            Assert.True(rig.GatedGizmo.Draws > draws);
            for (int i = 0; i < 4; i++) rig.Frame();

            rig.Ents.Add(rig.Spawn(7)); rig.Frame();                                  // an entity born
            for (int i = 0; i < 4; i++) rig.Frame();
            rig.Repo.DestroyEntity(rig.Ents[2]); rig.Frame();                         // an entity gone
            for (int i = 0; i < 4; i++) rig.Frame();

            rig.Repo.ResetGlobalVersion(2); rig.Frame();                               // a seek: the clock goes back (the chunks keep their old, higher
            for (int i = 0; i < 40; i++) rig.Frame();                                  // versions, so the gate reads "changed" until the clock passes them)
            int reused = rig.Gated.RulePicturesReused;
            for (int i = 0; i < 3; i++) rig.Frame();
            Assert.True(rig.Gated.RulePicturesReused > reused, "settled again after the changes");
        }

        [Fact]
        public void AViewDependentRule_FollowsTheView_AndIsNeverHeldWithoutAViewVersion()
        {
            var rig = new Rig(viewDependent: true, withView: true);
            for (int i = 0; i < 6; i++) rig.Frame();
            int reused = rig.Gated.RulePicturesReused;
            for (int i = 0; i < 3; i++) rig.Frame();
            Assert.Equal(reused + 3, rig.Gated.RulePicturesReused);                   // still view ⇒ held

            rig.View.Set(10);                                                          // the camera pans: fewer entities in view
            rig.Frame();
            int inView = 0; foreach (var e in rig.Ents) if (e.Index <= 10) inView++;
            Assert.Equal(inView, rig.GatedBuf.GetFrame().Length);                      // (the reference system skips its match pass by the same rule — so the expectation is explicit)
            for (int i = 0; i < 4; i++) rig.Frame();
            rig.View.Set(1000); rig.Frame();                                           // and back
            for (int i = 0; i < 4; i++) rig.Frame();

            // a filter that publishes no version (-1): a view-dependent rule is drawn every frame, never held
            var rig2 = new Rig(viewDependent: true, withView: false);
            rig2.Gated.ViewFilter = new NoVersion(); rig2.Plain.ViewFilter = new NoVersion();
            for (int i = 0; i < 8; i++) rig2.Frame();
            Assert.Equal(0, rig2.Gated.RulePicturesReused);
        }

        private sealed class NoVersion : IGizmoViewFilter
        {
            public bool Active => true; public float MetresPerPixel => 1f; public bool Is3D => false;
            public bool InView(ISimulationView view, Entity entity) => true;
            public bool Overlaps(Vector2 min, Vector2 max) => true;
        }

        [Fact]
        public void WithTheHoldOn_AStillWorldSettlesOnAFullyFreshPicture_NeverAFrozenOlderOne()
        {
            var rig = new Rig();
            rig.Gated.FreshEntitiesPerFrame = 3; rig.Gated.MaxHeldFrames = 5;   // 30 entities ⇒ period 6: a held picture can be 5 frames old
            // the world changes EVERY frame (the hold is alive: slices drawn fresh, the rest held, lagging), then goes still
            for (int f = 0; f < 10; f++)
            {
                for (int i = 0; i < rig.Ents.Count; i++) rig.Repo.SetComponent(rig.Ents[i], new GizmoTestCompA { Value = 100 * f + i });
                rig.Repo.Tick(); rig.GatedBuf.EndFrame(0f); rig.Gated.Execute(rig.Repo, 0f);
            }
            for (int f = 0; f < 14; f++) { rig.Repo.Tick(); rig.GatedBuf.EndFrame(0f); rig.Gated.Execute(rig.Repo, 0f); }
            var g = rig.GatedBuf.GetFrame();
            Assert.Equal(rig.Ents.Count, g.Length);
            for (int i = 0; i < g.Length; i++) Assert.Equal(900f + i, g[i].LineStart.Y);   // every entity at its CURRENT value, none frozen at an older one
            Assert.True(rig.Gated.RulePicturesReused > 0);
        }

        private sealed class Toggle : IGizmoVisibilityPolicy
        {
            public bool On;
            public bool IsGloballyEnabled(ISimulationView view) => On;
            public bool IsEntityVisible(ISimulationView view, Entity entity) => true;
        }

        [Fact]
        public void ARuleSwitchedOn_WithNothingElseChanged_IsMatchedAndDrawn()
        {
            var repo = GizmoTestRepo.Create();
            var buf = new DebugPrimitiveBuffer(4096);
            var policy = new Toggle();
            var registry = new StatelessGizmoRegistry();
            registry.Register(new PureGizmo(true), new[] { typeof(GizmoTestCompA) }, policy);
            var sys = new StatelessGizmoSystem(registry, buf);
            for (int i = 0; i < 30; i++) { var e = repo.CreateEntity(); repo.AddComponent(e, new GizmoTestCompA { Value = i }); }
            for (int f = 0; f < 6; f++) { repo.Tick(); buf.EndFrame(0f); sys.Execute(repo, 0f); }
            Assert.Equal(0, buf.GetFrame().Length);                                    // switched off: nothing drawn, nothing matched
            policy.On = true;
            repo.Tick(); buf.EndFrame(0f); sys.Execute(repo, 0f);
            Assert.Equal(30, buf.GetFrame().Length);                                   // on again: the match pass ran though no entity changed
        }

        [Fact]
        public void ARuleThatDeclaresNothing_IsDrawnEveryFrame_AsBefore()
        {
            var rig = new Rig();
            for (int i = 0; i < 10; i++) rig.Frame();
            Assert.Equal(10 * 30, rig.PlainGizmo.Draws);
        }
    }
}
