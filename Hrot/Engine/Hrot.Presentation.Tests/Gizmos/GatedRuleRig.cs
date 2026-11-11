using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos
{
    /// <summary>
    /// ⭐ CE-1480 G5 — the gate-on == gate-off equivalence rail for ONE real gizmo rule that declares <c>Inputs</c>. The same rule runs in two
    /// <c>StatelessGizmoSystem</c>s over ONE world: the gated one with the declaration, the reference through a wrapper that hides it (so it is drawn every
    /// frame). Every frame the two primitive lists must be byte-identical. <c>Standard</c> scripts: still · pan · each rule-specific change · seek.
    /// </summary>
    internal sealed class GatedRuleRig
    {
        private sealed class Hide : IStatelessGizmo
        {
            private readonly IStatelessGizmo _inner;
            public Hide(IStatelessGizmo inner) => _inner = inner;
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder b) => _inner.Draw(view, entity, b);
        }

        public sealed class Rect : IGizmoViewFilter
        {
            public float MaxX = 1_000_000; public long Ver = 1;
            public bool Active => true; public float MetresPerPixel => 1f; public bool Is3D => false; public long Version => Ver;
            public bool InView(ISimulationView view, Entity entity) => !view.HasComponent<SimTransform>(entity) || view.GetComponentRO<SimTransform>(entity).Position.X <= MaxX;
            public bool Overlaps(Vector2 min, Vector2 max) => true;
            public void Set(float maxX) { if (maxX != MaxX) { MaxX = maxX; Ver++; } }
        }

        public readonly EntityRepository Repo;
        public readonly List<Entity> Ents = new();
        public readonly DebugPrimitiveBuffer GatedBuf = new(1 << 16), PlainBuf = new(1 << 16);
        public readonly StatelessGizmoSystem Gated, Plain;
        public readonly Rect View = new();
        public readonly IStatelessGizmo Rule;

        public GatedRuleRig(IStatelessGizmo rule, Action<EntityRepository> register, Func<EntityRepository, int, Entity> spawn, int count = 24)
        {
            Assert.NotNull(rule.Inputs);   // the rail is about a DECLARED rule
            Rule = rule;
            Repo = new EntityRepository();
            register(Repo);
            var types = rule.GetType().GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents;
            var rg = new StatelessGizmoRegistry(); rg.Register(rule, types);
            var rp = new StatelessGizmoRegistry(); rp.Register(new Hide(rule), types);
            Gated = new StatelessGizmoSystem(rg, GatedBuf) { ViewFilter = View };
            Plain = new StatelessGizmoSystem(rp, PlainBuf) { ViewFilter = View };
            for (int i = 0; i < count; i++) Ents.Add(spawn(Repo, i));
        }

        public void Frame()
        {
            Repo.Tick(); GatedBuf.EndFrame(0f); PlainBuf.EndFrame(0f);
            Gated.Execute(Repo, 0f); Plain.Execute(Repo, 0f);
            var g = MemoryMarshal.AsBytes(GatedBuf.GetFrame()); var p = MemoryMarshal.AsBytes(PlainBuf.GetFrame());
            Assert.True(g.SequenceEqual(p), $"the gated picture differs from the ungated one ({GatedBuf.GetFrame().Length} vs {PlainBuf.GetFrame().Length} primitives)");
        }

        public void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

        /// <summary>still · pan · each change (followed within a few frames, equal to the reference EVERY frame) · seek.</summary>
        public void Standard(params Action<GatedRuleRig>[] changes)
        {
            Frames(8);
            Assert.True(GatedBuf.GetFrame().Length > 0, "the rule drew nothing — the rail would prove nothing");
            int reused = Gated.RulePicturesReused;
            Frames(4);
            Assert.True(Gated.RulePicturesReused > reused, "a still world must re-append the declared rule's picture");
            View.Set(10f); Frames(6); View.Set(1_000_000f); Frames(6);
            foreach (var change in changes) { change(this); Frames(8); }
            Repo.ResetGlobalVersion(2); Frames(60);
        }
    }
}
