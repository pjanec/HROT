using Fdp.Toolkit.Combat.Components;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Player.Gizmos;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
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
            _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
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

        /// <summary>⭐ CE-4582 (§6am D-7) — the heading is a row of the unit's CARD (row 81, Unit details), never free text at the unit.
        /// 🔴 Red-proof: the free <c>DrawText</c> back ⇒ the card row is missing and a free text appears.</summary>
        [Fact]
        public void SC_GZ021_ROT_2_Draw_TheHeadingIsARowOfHisCard_WithDegreeValue()
        {
            var gizmo = new EntityRotationGizmo(_settings);
            var draw  = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });
            _repo.AddComponent(entity, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1043 });

            gizmo.Draw(_repo, entity, draw);

            Assert.Empty(draw.TextCalls);   // no free text
            var row = Assert.Single(draw.RawCalls, p => p.Shape == DebugPrimitiveShape.Text);
            Assert.Equal(CoordinateSpace.EntityCard, row.Space);
            Assert.Equal(1043, row.AnchorIndex);
            Assert.Equal(EntityCardRows.Heading, row.ZIndex);
            Assert.Equal(DebugTraceLayers.UnitDetails, row.DebugLayer);
            Assert.Equal("90*", row.TextContent.ToString());   // ⭐ CE-4432 — facing east = compass 90, formatted on the stack (the value the old "{deg:F0}*" gave)
        }

        /// <summary>⭐ CE-4432 (R-2084) — the gizmo runs for every unit every frame: its label costs 0 B (it was an interpolated string per unit).
        /// 🔴 Red-proof: <c>new FixedString32($"{compassDeg:F0}*")</c> back ⇒ red.</summary>
        [Fact]
        public void SC_GZ021_ROT_2b_Draw_Allocates0B()
        {
            var gizmo = new EntityRotationGizmo(_settings);
            var draw  = new FullCapturingDrawBuilder();
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f) });
            _repo.AddComponent(entity, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1043 });   // ⭐ CE-4582 — the card row's path too
            for (int i = 0; i < 40; i++) gizmo.Draw(_repo, entity, draw);   // warm (the double's lists grow to 64)
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) gizmo.Draw(_repo, entity, draw);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a0);
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
            tempRepo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            // GZ057-058: components required by the new stateless gizmos added in BATCH-21.
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            tempRepo.RegisterComponent<Hrot.Map.Common.Components.CullingState>();
            tempRepo.RegisterComponent<Hrot.Map.Common.Components.VisualEffectState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            tempRepo.RegisterComponent<Hrot.Map.Common.Components.MapOverlayStyle>();

            var registry          = new GizmoRegistry();
            var statelessRegistry = new StatelessGizmoRegistry();
            var settings          = new GizmoSettingsRegistry();

            Hrot.Player.Gizmos.GizmoRegistrar.Register(registry, statelessRegistry, settings);

            var registeredKeys = new HashSet<string>();
            foreach (var (key, _, _) in settings.EnumerateAll())
                registeredKeys.Add(key);

            Assert.Contains(EntityRotationGizmoSettings.ArrowLength, registeredKeys);
        }
    }
}
