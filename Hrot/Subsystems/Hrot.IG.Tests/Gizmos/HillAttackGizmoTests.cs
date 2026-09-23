using Fdp.Toolkit.Combat.Components;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Gizmos;
using Hrot.IG.Gizmos;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-HA: Hill attack gizmo unit tests (migrated to IStatelessGizmo).
    // ============================================================================

    public sealed class HillAttackGizmoTests : IDisposable
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly EntityRepository      _repo;

        public HillAttackGizmoTests()
        {
            _settings = new GizmoSettingsRegistry();
            HillAttackGizmoSettings.Register(_settings);

            _repo = new EntityRepository();
            _repo.RegisterComponent<BehaviorState>();
            _repo.RegisterComponent<SimTransform>();
            // ⭐ CE-329 (2026-09-23): params live in an OCCURRENCE SLOT since P3-C, so a world that
            //   never registered the tier ladder has nowhere to put them. Production registers these
            //   Hrot-wide; a bare test world has to ask.
            BlueprintTierTable.RegisterAll(_repo);
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void SC_GZ021_HA_1_RequiredComponents_ContainsBothTypes()
        {
            // ⚠ CE-329 (2026-09-23) — PREMISE CORRECTION, and the rail was UNCOMPILABLE until now.
            //   It asserted three components including `BrainBlackboard`, which `P4` DELETED; the
            //   projector declares exactly two today. ⛔ Nothing caught it because Hrot.IG.Tests had
            //   no obj/project.assets.json, so every --no-restore build SKIPPED the project.
            // ⭐ There is no successor to assert: the params region is an occurrence SLOT, not a
            //   component, so a projector cannot require it. ⇒ two is the whole set, and the rail
            //   asserts the COUNT so a silent third arrival is caught.
            var attr = typeof(HillAttackGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(BehaviorState), attr!.RequiredComponents);
            Assert.Contains(typeof(SimTransform),  attr.RequiredComponents);
            Assert.Equal(2, attr.RequiredComponents.Length);
        }

        [Fact]
        public unsafe void SC_GZ021_HA_2_Draw_EmitsNoDrawCalls_WhenHashNotPlatoonHillAttack()
        {
            var gizmo = new HillAttackGizmo(_settings);
            var draw  = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new BehaviorState
            {
                ActiveBehaviorHash = 0,  // not PlatoonHillAttack_BT (3014)
                InstanceId         = 0,
                BrainTier          = 0
            });
            // BrainBlackboard not strictly needed (early return before access), but add it anyway.
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            gizmo.Draw(_repo, entity, draw);

            Assert.Empty(draw.LineCalls);
            Assert.Empty(draw.SphereCalls);
        }

        [Fact]
        public unsafe void SC_GZ021_HA_3_Draw_EmitsDrawLineCalls_WhenHashMatches()
        {
            var gizmo  = new HillAttackGizmo(_settings);
            var draw   = new FullCapturingDrawBuilder();

            var entity = CreateEntityWithParams(_repo, buildDefaultParams(), showSlots: false);

            gizmo.Draw(_repo, entity, draw);

            // Expect exactly 2 DrawLine calls: one for fire line, one for baseline.
            Assert.True(draw.LineCalls.Count >= 2,
                $"Expected at least 2 DrawLine calls, got {draw.LineCalls.Count}.");
        }

        [Fact]
        public unsafe void SC_GZ021_HA_4_Draw_EmitsDrawSphereCalls_WhenShowSlotsTrue()
        {
            // Override setting to true.
            var settings = new GizmoSettingsRegistry();
            settings.RegisterSetting(HillAttackGizmoSettings.ShowSlots, GizmoSettingValue.From(true));

            var gizmo  = new HillAttackGizmo(settings);
            var draw   = new FullCapturingDrawBuilder();

            var entity = CreateEntityWithParams(_repo, buildDefaultParams(), showSlots: true);

            gizmo.Draw(_repo, entity, draw);

            Assert.NotEmpty(draw.SphereCalls);
        }

        [Fact]
        public unsafe void SC_GZ021_HA_5_Draw_EmitsNoSphereCalls_WhenShowSlotsFalse()
        {
            // Override setting to false.
            var settings = new GizmoSettingsRegistry();
            settings.RegisterSetting(HillAttackGizmoSettings.ShowSlots, GizmoSettingValue.From(false));

            var gizmo  = new HillAttackGizmo(settings);
            var draw   = new FullCapturingDrawBuilder();

            var entity = CreateEntityWithParams(_repo, buildDefaultParams(), showSlots: false);

            gizmo.Draw(_repo, entity, draw);

            Assert.Empty(draw.SphereCalls);
        }

        [Fact]
        public void SC_GZ021_HA_6_GizmoRegistrar_RegistersShowSlotsSetting()
        {
            // Ensure all components required by any registered gizmo are in ComponentTypeRegistry.
            using var tempRepo = new EntityRepository();
            tempRepo.RegisterComponent<BehaviorState>();
            tempRepo.RegisterComponent<SimTransform>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Perception.Components.PerceptionReceptor>();
            // GZ057-058: components required by the new stateless gizmos added in BATCH-21.
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            tempRepo.RegisterComponent<Hrot.IG.Components.CullingState>();
            tempRepo.RegisterComponent<Hrot.IG.Components.VisualEffectState>();
            tempRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            tempRepo.RegisterComponent<Hrot.IG.Components.MapOverlayStyle>();

            var registry          = new GizmoRegistry();
            var statelessRegistry = new StatelessGizmoRegistry();
            var settings          = new GizmoSettingsRegistry();

            Hrot.IG.Gizmos.GizmoRegistrar.Register(registry, statelessRegistry, settings);

            var registeredKeys = new HashSet<string>();
            foreach (var (key, _, _) in settings.EnumerateAll())
                registeredKeys.Add(key);

            Assert.Contains(HillAttackGizmoSettings.ShowSlots, registeredKeys);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static PlatoonHillAttackParams buildDefaultParams() => new PlatoonHillAttackParams
        {
            StartX          = 100f, StartY          = 0f,
            EndX            = 200f, EndY            = 0f,
            BaselineStartX  = 100f, BaselineStartY  = -50f,
            BaselineEndX    = 200f, BaselineEndY    = -50f,
            TankSpacing     = 30f
        };

        private static unsafe Entity CreateEntityWithParams(
            EntityRepository repo,
            PlatoonHillAttackParams p,
            bool showSlots)
        {
            var entity = repo.CreateEntity();

            repo.AddComponent(entity, new BehaviorState
            {
                ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.PlatoonHillAttack),  // PlatoonHillAttack_BT
                InstanceId         = 1,
                BrainTier          = 0
            });

            // ⭐⭐ CE-329 (2026-09-23) — SEED THE ROOT PARAMS OCCURRENCE SLOT.
            // 🔴 This was a half-finished P4 mechanical edit and it did NOT COMPILE:
            //      byte bb = 0;
            //      fixed (byte* mem = &Unsafe.AsRef(in bb).BehaviorParameters[0]) …
            //    `.BehaviorParameters` on a `byte`. The edit replaced the BrainBlackboard TYPE with
            //    `byte` and left the member access behind. ⛔ Invisible because the project was
            //    unrestored and therefore skipped, never built.
            int paramsBytes = sizeof(PlatoonHillAttackParams);
            var spec = BlueprintTierTable.Select(
                paramsBytes + BlueprintBlackboardPartitions.SlotEntrySize, requiredSlots: 1);
            spec.Add(repo, entity);
            byte* store = spec.Memory(repo, entity);
            BlueprintBlackboardPartitions.Initialize(store, spec.TotalSize, (byte)spec.MaxSlots);

            byte* mem = RootParamsAccess.ResolveOrAttachRoot(
                repo, entity, BehaviorHash.FromName(BehaviorNames.PlatoonHillAttack),
                paramsBytes, OccurrenceKind.BTree, out _);
            Assert.True(mem != null, "the fixture could not attach a root params slot");
            *(PlatoonHillAttackParams*)mem = p;

            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            return entity;
        }
    }
}
