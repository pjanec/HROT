using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
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
    // SC-GZ021-HB: Health bar gizmo unit tests.
    // ========================================================================

    public sealed class HealthBarGizmoTests : System.IDisposable
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly EntityRepository      _repo;

        public HealthBarGizmoTests()
        {
            _settings = new GizmoSettingsRegistry();
            _settings.RegisterSetting(HealthBarGizmoSettings.BarHeightKey, HealthBarGizmoSettings.DefaultBarHeight);
            _settings.RegisterSetting(HealthBarGizmoSettings.BarWidthKey,  HealthBarGizmoSettings.DefaultBarWidth);

            _repo = new EntityRepository();
            _repo.RegisterComponent<IgHealthState>();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void SC_GZ021_HB_1_RequiredComponents_ContainsIgHealthState()
        {
            var def = new HealthBarGizmoDefinition(_settings);

            Assert.Contains(typeof(IgHealthState), def.RequiredComponents);
        }

        [Fact]
        public void SC_GZ021_HB_2_VisibilityPolicy_IsAlwaysVisiblePolicy()
        {
            var def = new HealthBarGizmoDefinition(_settings);

            Assert.Same(AlwaysVisiblePolicy.Instance, def.VisibilityPolicy);
        }

        [Fact]
        public void SC_GZ021_HB_3_UpdateAndDraw_FullHealth_CallsDrawEntityBadge()
        {
            var def      = new HealthBarGizmoDefinition(_settings);
            var instance = def.CreateInstance();
            var draw     = new CapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new IgHealthState { Damage = 0f });

            instance.UpdateAndDraw(_repo, entity, 0f, draw);

            Assert.NotEmpty(draw.BadgeCalls);
            Assert.Equal(entity, draw.BadgeCalls[0].Target);
        }

        [Fact]
        public void SC_GZ021_HB_4_OnInitialize_And_OnTeardown_DoNotThrow()
        {
            var def      = new HealthBarGizmoDefinition(_settings);
            var instance = def.CreateInstance();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new IgHealthState { Damage = 50f });

            var exInit     = Record.Exception(() => instance.OnInitialize(_repo, entity));
            var exTeardown = Record.Exception(() => instance.OnTeardown());

            Assert.Null(exInit);
            Assert.Null(exTeardown);
        }

        [Fact]
        public void SC_GZ021_HB_5_GizmoRegistrar_RegistersSettingsForBothKeys()
        {
            var registry = new GizmoRegistry();
            var settings = new GizmoSettingsRegistry();

            GizmoRegistrar.Register(registry, settings);

            // Verify both keys are registered via EnumerateAll (IsRegistered is internal).
            var registeredKeys = new HashSet<string>();
            foreach (var (key, _, _) in settings.EnumerateAll())
                registeredKeys.Add(key);

            Assert.Contains(HealthBarGizmoSettings.BarHeightKey, registeredKeys);
            Assert.Contains(HealthBarGizmoSettings.BarWidthKey,  registeredKeys);
        }
    }
}
