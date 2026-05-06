using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-ROT: Entity rotation gizmo unit tests.
    // ============================================================================

    public sealed class EntityRotationGizmoTests : IDisposable
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly EntityRepository      _repo;

        public EntityRotationGizmoTests()
        {
            _settings = new GizmoSettingsRegistry();
            EntityRotationGizmoSettings.Register(_settings);

            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void SC_GZ021_ROT_1_UpdateAndDraw_IdentityRotation_DrawsArrowEast()
        {
            var def      = new EntityRotationGizmoDefinition(_settings);
            var instance = def.CreateInstance();
            var draw     = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            // Identity quaternion: yaw=0 -> facing east (+X).
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });

            instance.UpdateAndDraw(_repo, entity, 0f, draw);

            Assert.NotEmpty(draw.ArrowCalls);
            var (from, to, _) = draw.ArrowCalls[0];
            // Arrow tip must be east of the origin (tip.X > from.X, tip.Y == from.Y).
            Assert.True(to.X > from.X, "Arrow tip X should be greater than origin X (east).");
            Assert.Equal(from.Y, to.Y, precision: 3);
        }

        [Fact]
        public void SC_GZ021_ROT_2_UpdateAndDraw_EmitsDrawText_WithDegreeValue()
        {
            var def      = new EntityRotationGizmoDefinition(_settings);
            var instance = def.CreateInstance();
            var draw     = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });

            instance.UpdateAndDraw(_repo, entity, 0f, draw);

            Assert.NotEmpty(draw.TextCalls);
            // The label must contain a degree value (non-empty text string).
            string label = draw.TextCalls[0].Text.ToString();
            Assert.False(string.IsNullOrWhiteSpace(label));
        }

        [Fact]
        public void SC_GZ021_ROT_3_RequiredComponents_ContainsSimTransform()
        {
            var def = new EntityRotationGizmoDefinition(_settings);

            Assert.Contains(typeof(SimTransform), def.RequiredComponents);
        }

        [Fact]
        public void SC_GZ021_ROT_4_GizmoRegistrar_RegistersEntityRotationArrowLengthSetting()
        {
            var registry = new GizmoRegistry();
            var settings = new GizmoSettingsRegistry();

            GizmoRegistrar.Register(registry, settings);

            var registeredKeys = new HashSet<string>();
            foreach (var (key, _, _) in settings.EnumerateAll())
                registeredKeys.Add(key);

            Assert.Contains(EntityRotationGizmoSettings.ArrowLength, registeredKeys);
        }
    }
}
