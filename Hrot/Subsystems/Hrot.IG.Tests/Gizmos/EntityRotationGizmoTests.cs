using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-ROT: Entity rotation gizmo unit tests (migrated to IStatelessGizmo).
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
        public void SC_GZ021_ROT_1_Draw_IdentityRotation_DrawsArrowEast()
        {
            var gizmo = new EntityRotationGizmo(_settings);
            var draw  = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            // Identity quaternion: yaw=0 -> facing east (+X).
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });

            gizmo.Draw(_repo, entity, draw);

            Assert.NotEmpty(draw.ArrowCalls);
            var (from, to, _) = draw.ArrowCalls[0];
            // Arrow tip must be east of the origin (tip.X > from.X, tip.Y == from.Y).
            Assert.True(to.X > from.X, "Arrow tip X should be greater than origin X (east).");
            Assert.Equal(from.Y, to.Y, precision: 3);
        }

        [Fact]
        public void SC_GZ021_ROT_2_Draw_EmitsDrawText_WithDegreeValue()
        {
            var gizmo = new EntityRotationGizmo(_settings);
            var draw  = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });

            gizmo.Draw(_repo, entity, draw);

            Assert.NotEmpty(draw.TextCalls);
            // The label must contain a degree value (non-empty text string).
            string label = draw.TextCalls[0].Text.ToString();
            Assert.False(string.IsNullOrWhiteSpace(label));
        }

        [Fact]
        public void SC_GZ021_ROT_3_RequiredComponents_ContainsSimTransform()
        {
            var attr = typeof(EntityRotationGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(SimTransform), attr!.RequiredComponents);
        }

        [Fact]
        public void SC_GZ021_ROT_4_GizmoRegistrar_RegistersEntityRotationArrowLengthSetting()
        {
            // Ensure all components required by any registered gizmo are in ComponentTypeRegistry.
            using var tempRepo = new EntityRepository();
            tempRepo.RegisterComponent<SimTransform>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Perception.Components.PerceptionReceptor>();
            tempRepo.RegisterComponent<Hrot.IG.Components.IgHealthState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            // GZ057-058: components required by the new stateless gizmos added in BATCH-21.
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            tempRepo.RegisterComponent<Hrot.IG.Components.CullingState>();
            tempRepo.RegisterComponent<Hrot.IG.Components.VisualEffectState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            tempRepo.RegisterComponent<Hrot.IG.Components.MapOverlayStyle>();
            // Components required by gizmos added after BATCH-21.
            tempRepo.RegisterComponent<Hrot.IG.Components.SelectionState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();

            var registry          = new GizmoRegistry();
            var statelessRegistry = new StatelessGizmoRegistry();
            var settings          = new GizmoSettingsRegistry();

            Hrot.IG.Gizmos.GizmoRegistrar.Register(registry, statelessRegistry, settings);

            var registeredKeys = new HashSet<string>();
            foreach (var (key, _, _) in settings.EnumerateAll())
                registeredKeys.Add(key);

            Assert.Contains(EntityRotationGizmoSettings.ArrowLength, registeredKeys);
        }
    }
}
