using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
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

        public void DrawEntityBadge(Entity target, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All)
        {
            BadgeCalls.Add((target, richText));
        }

        // Stubs for unused interface members.
        public void DrawLine(Vector3 start, Vector3 end, Rgba32 color, float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels, PipelineTarget target = PipelineTarget.All, byte layer = 0) { }
        public void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels, PipelineTarget target = PipelineTarget.All, byte layer = 0) { }
        public void DrawSphere(Vector3 center, float radius, Rgba32 color,
            PipelineTarget target = PipelineTarget.All, byte layer = 0) { }
        public void DrawArrow(Vector3 from, Vector3 to, Rgba32 color, float headSize = 1f, byte layer = 0) { }
        public void DrawText(float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0) { }
        public void DrawTextLong(float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0) { }
        public void DrawEntityLocal(Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0) { }
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
            _repo.RegisterComponent<IgHealthState>();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void SC_GZ021_HB_1_RequiredComponents_ContainsIgHealthState()
        {
            var attr = typeof(HealthBarGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(IgHealthState), attr!.RequiredComponents);
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

        [Fact]
        public void SC_GZ021_HB_3_Draw_FullHealth_CallsDrawEntityBadge()
        {
            var gizmo = new HealthBarGizmo(_settings);
            var draw  = new CapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new IgHealthState { Damage = 0f });

            gizmo.Draw(_repo, entity, draw);

            Assert.NotEmpty(draw.BadgeCalls);
            Assert.Equal(entity, draw.BadgeCalls[0].Target);
        }

        [Fact]
        public void SC_GZ021_HB_4_Draw_DoesNotThrow()
        {
            var gizmo = new HealthBarGizmo(_settings);
            var draw  = new CapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new IgHealthState { Damage = 50f });

            var ex = Record.Exception(() => gizmo.Draw(_repo, entity, draw));

            Assert.Null(ex);
        }

        [Fact]
        public void SC_GZ021_HB_5_GizmoRegistrar_RegistersSettingsForBothKeys()
        {
            // Ensure all components required by any registered gizmo are in ComponentTypeRegistry.
            using var tempRepo = new EntityRepository();
            tempRepo.RegisterComponent<IgHealthState>();
            tempRepo.RegisterComponent<SimTransform>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Perception.Components.PerceptionReceptor>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();

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
