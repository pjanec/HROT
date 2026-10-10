using Fdp.Toolkit.Combat.Components;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.IG.Components;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // ========================================================================
    // Capturing draw builder stub for testing IStatefulGizmo.UpdateAndDraw.
    // ========================================================================

    internal sealed class CapturingDrawBuilder : IDebugDrawBuilder
    {
        public readonly List<(Entity Target, FixedString32 Text)> BadgeCalls = new();
        public readonly List<DebugPrimitive> Raw = new();

        // ⭐ CE-1033 S5b — the card sugar (EntityCards) emits ordinary primitives through EmitRaw.
        public void EmitRaw(in DebugPrimitive prim) => Raw.Add(prim);

        public void DrawEntityBadge(Entity target, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All)
        {
            BadgeCalls.Add((target, richText));
        }

        // Stubs for unused interface members.
        public void DrawLine(Vector3 start, Vector3 end, Rgba32 color, float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels, PipelineTarget target = PipelineTarget.All, byte layer = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels, PipelineTarget target = PipelineTarget.All, byte layer = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawSphere(Vector3 center, float radius, Rgba32 color,
            float thickness = 0f, SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All, byte layer = 0,
            Rgba32 fillColor = default, LineStyle style = LineStyle.Solid) { }
        public void DrawArrow(Vector3 from, Vector3 to, Rgba32 color, float headSize = 1f, byte layer = 0) { }
        public void DrawText(float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) { }
        public void DrawTextLong(float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) { }
        public void DrawEntityLocal(long anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0) { }
        public void DrawEntityLocalInteractive(long anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, ushort subElementId, float thickness = 1f, byte layer = 0) { }
    }

    // ========================================================================
    // SC-GZ021-HB: Health bar gizmo unit tests (migrated to IStatelessGizmo).
    // ========================================================================

    public sealed class HealthBarGizmoTests : System.IDisposable
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly EntityRepository      _repo;

        public HealthBarGizmoTests()
        {
            _settings = new GizmoSettingsRegistry();
            HealthBarGizmoSettings.Register(_settings);

            _repo = new EntityRepository();
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        }

        private Entity Unit(float current, float max, long net = 1043)
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new Health { Current = current, Max = max });
            _repo.AddComponent(entity, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = net });
            return entity;
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void SC_GZ021_HB_1_RequiredComponents_ContainsHealth()
        {
            var attr = typeof(HealthBarGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(Health), attr!.RequiredComponents);
        }

        [Fact]
        public void SC_GZ021_HB_2_DefaultVisibilityPolicy_IsAlwaysVisible()
        {
            // StatelessGizmoRegistry.Register uses AlwaysVisiblePolicy.Instance by default.
            var statelessRegistry = new StatelessGizmoRegistry();
            var gizmo = new HealthBarGizmo(_settings);
            var attr  = typeof(HealthBarGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            statelessRegistry.Register(gizmo, attr.RequiredComponents);

            Assert.Same(AlwaysVisiblePolicy.Instance, statelessRegistry.Rules[0].VisibilityPolicy);
        }

        // ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6) — the bar is ROW 10 of the entity's CARD now, not a text badge: a dark
        //   back the card's width and a fill Current/Max of it, both fractions of the card, on the Labels layer.
        [Fact]
        public void SC_GZ021_HB_3_Draw_EmitsABarRowIntoTheEntitysCard()
        {
            var gizmo = new HealthBarGizmo(_settings);
            var draw  = new CapturingDrawBuilder();

            gizmo.Draw(_repo, Unit(25f, 100f), draw);

            Assert.Empty(draw.BadgeCalls);
            Assert.Equal(2, draw.Raw.Count);
            Assert.All(draw.Raw, p =>
            {
                Assert.Equal(CoordinateSpace.EntityCard, p.Space);
                Assert.Equal(1043, p.AnchorIndex);
                Assert.Equal(HealthBarGizmo.Row, p.ZIndex);
                Assert.Equal(DebugTraceLayers.Labels, p.DebugLayer);
                Assert.Equal(DebugPrimitiveShape.Box2D, p.Shape);
                Assert.Equal(SizeMode.ScreenPercent, p.SizeMode);
            });
            Assert.Equal(1f, draw.Raw[0].BoxExtentX * 2f, 3);       // the back: the whole card
            Assert.Equal(0.25f, draw.Raw[1].BoxExtentX * 2f, 3);    // the fill: a quarter of it
            Assert.Equal(Rgba32.Red, draw.Raw[1].FillColor);        // < 33 % is critical
        }

        [Fact]
        public void SC_GZ021_HB_3b_Draw_NoNetworkId_NoCard()
        {
            var draw = new CapturingDrawBuilder();
            new HealthBarGizmo(_settings).Draw(_repo, Unit(50f, 100f, net: 0), draw);
            Assert.Empty(draw.Raw);
        }

        [Fact]
        public void SC_GZ021_HB_4_Draw_DoesNotThrow()
        {
            var gizmo = new HealthBarGizmo(_settings);
            var draw  = new CapturingDrawBuilder();

            var ex = Record.Exception(() => gizmo.Draw(_repo, Unit(50f, 100f), draw));

            Assert.Null(ex);
        }

        [Fact]
        public void SC_GZ021_HB_5_GizmoRegistrar_RegistersSettingsForBothKeys()
        {
            // Ensure all components required by any registered gizmo are in ComponentTypeRegistry.
            using var tempRepo = new EntityRepository();
            tempRepo.RegisterComponent<Health>();
            tempRepo.RegisterComponent<SimTransform>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Perception.Components.PerceptionReceptor>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            // GZ057-058: components required by the new stateless gizmos added in BATCH-21.
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            tempRepo.RegisterComponent<CullingState>();
            tempRepo.RegisterComponent<VisualEffectState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            tempRepo.RegisterComponent<MapOverlayStyle>();

            var registry          = new GizmoRegistry();
            var statelessRegistry = new StatelessGizmoRegistry();
            var settings          = new GizmoSettingsRegistry();

            Hrot.IG.Gizmos.GizmoRegistrar.Register(registry, statelessRegistry, settings);
            var registeredKeys = new HashSet<string>();
            foreach (var (key, _, _) in settings.EnumerateAll())
                registeredKeys.Add(key);

            Assert.Contains(HealthBarGizmoSettings.BarHeightKey, registeredKeys);
            Assert.Contains(HealthBarGizmoSettings.BarWidthKey,  registeredKeys);
        }
    }
}
