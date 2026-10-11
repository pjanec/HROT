using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ WL-2a (docs/DESIGN_Weapon_Layers.md §5c, W-8, W-11 / R-267) — the weapon muscle keeps a turret on its task: it slews at the
    /// TKB rates, stays inside the limits (a limited arc never swings through its dead zone, a full circle turns the short way),
    /// solves in the HULL frame (a turned or tilted hull), reports Slewing → OnTarget, and fails OutOfArc / NoTarget / NoTurret.
    /// </summary>
    public sealed class WeaponTaskDispatcherTests : IDisposable
    {
        private const float Deg = MathF.PI / 180f;
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _world = new();
        private readonly WeaponTaskDispatcherSystem _muscle = new();

        public WeaponTaskDispatcherTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterComponent<TurretPose>();
            _world.RegisterComponent<WeaponTask>();
            _world.RegisterComponent<WeaponTaskStatus>();
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            db.Register(Type(1, new TurretDto { PivotZ = 2f, TraverseRateDegPerSec = 24f, ElevationRateDegPerSec = 6f,
                                                ElevationMinDeg = -6f, ElevationMaxDeg = 14f }));
            db.Register(Type(2, new TurretDto { PivotZ = 2f, TraverseMinDeg = -30f, TraverseMaxDeg = 30f, TraverseRateDegPerSec = 30f }));
            db.Register(new TkbTemplate("NoTurret", 3));
            _world.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _world.Dispose();

        private static TkbTemplate Type(long id, TurretDto turret)
        {
            var t = new TkbTemplate($"T{id}", id);
            t.AddDescriptor(new TurretSuiteDto { Turrets = { turret } });
            t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.TankGun, TurretIndex = 0 } } });
            return t;
        }

        private (Entity Hull, Entity Turret) Tank(long type, Quaternion rotation = default)
        {
            var hull = _world.CreateEntity();
            _world.AddComponent(hull, new SimTransform { Position = Vector3.Zero, Rotation = rotation == default ? Quaternion.Identity : rotation });
            _world.AddComponent(hull, new TkbIdentity { TkbType = type });
            return (hull, TurretParts.Ensure(_world, hull, 0));
        }

        private void Order(Entity turret, Vector3 point, uint epoch = 1, Entity target = default)
            => _world.AddOrSet(turret, new WeaponTask { Mode = WeaponTaskMode.Track, Point = point, Target = target, Epoch = epoch });

        private void Run(int frames) { for (int i = 0; i < frames; i++) _muscle.Execute(_world, Dt); }

        private TurretPose Pose(Entity turret) => _world.GetComponentRO<TurretPose>(turret);
        private WeaponTaskStatus Status(Entity turret) => _world.GetComponentRO<WeaponTaskStatus>(turret);

        [Fact]
        public void WL2_Track_SlewsAtTheTkbRate_ThenReportsOnTarget()
        {
            var (_, turret) = Tank(1);
            Order(turret, new Vector3(0, 100, 2));   // 90° left, level with the pivot

            Run(60);   // one second at 24°/s
            Assert.Equal(24f * Deg, Pose(turret).Azimuth, 3);
            Assert.Equal(WeaponTaskState.Slewing, Status(turret).State);
            Assert.Equal(1u, Status(turret).Epoch);

            Run(60 * 3);
            Assert.Equal(90f * Deg, Pose(turret).Azimuth, 3);
            Assert.Equal(WeaponTaskState.OnTarget, Status(turret).State);
        }

        [Fact]
        public void WL2_TheAim_IsInTheHullFrame_TurnedAndTilted()
        {
            var (_, turned) = Tank(1, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 90f * Deg));
            Assert.Equal((0f, 0f), Round(BallisticAim.Solve(_world, _world.GetComponent<PartMetadata>(turned).ParentEntity,
                CombatTkb.TurretOf(_world, _world.GetComponent<PartMetadata>(turned).ParentEntity, 0)!, null, new Vector3(0, 500, 2))));

            // nose up 10° (a hull climbing a slope): a level target far ahead sits 10° BELOW the turret plane
            var (tiltedHull, _) = Tank(1, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -10f * Deg));
            var pivot = BallisticAim.PivotOf(_world.GetComponent<SimTransform>(tiltedHull), CombatTkb.TurretOf(_world, tiltedHull, 0)!);
            var (az, el) = BallisticAim.Solve(_world, tiltedHull, CombatTkb.TurretOf(_world, tiltedHull, 0)!, null, pivot + new Vector3(1000, 0, 0));
            Assert.Equal(0f, az, 3);
            Assert.Equal(-10f * Deg, el, 3);
        }

        [Fact]
        public void WL2_OutsideTheLimits_FailsOutOfArc_AndThePoseHolds()
        {
            var (_, turret) = Tank(1);
            Order(turret, new Vector3(10, 0, 50));   // 78° up: past the +14° gun limit
            Run(30);
            Assert.Equal(WeaponTaskState.Failed, Status(turret).State);
            Assert.Equal(WeaponTaskFailure.OutOfArc, Status(turret).Reason);
            Assert.Equal(default, Pose(turret));
        }

        [Fact]
        public void WL2_ALimitedArc_NeverSwingsThroughItsDeadZone_AFullCircle_TurnsTheShortWay()
        {
            var (_, limited) = Tank(2);
            Order(limited, new Vector3(-100, 5, 2));   // ~177°: behind, outside −30…30
            Run(10);
            Assert.Equal(WeaponTaskFailure.OutOfArc, Status(limited).Reason);
            Order(limited, new Vector3(100, 40, 2), epoch: 2);   // ~21.8° left, inside
            Run(120);
            Assert.Equal(WeaponTaskState.OnTarget, Status(limited).State);

            var (_, full) = Tank(1);
            _world.SetComponent(full, new TurretPose { Azimuth = 170f * Deg });
            Order(full, new Vector3(-100, -17.6f, 2));   // ≈ −170°: 20° away the short way, 340° the long way
            Run(30);   // half a second at 24°/s = 12° — through ±180
            Assert.True(MathF.Abs(TurretSlew.Wrap(Pose(full).Azimuth - 182f * Deg)) < 0.5f * Deg, $"azimuth {Pose(full).Azimuth / Deg}°");
        }

        [Fact]
        public void WL2_AMovingTarget_IsFollowed_AndAGoneTargetFails()
        {
            var (_, turret) = Tank(1);
            var target = _world.CreateEntity();
            _world.AddComponent(target, new SimTransform { Position = new Vector3(400, 0, 0), Rotation = Quaternion.Identity });
            Order(turret, default, target: target);
            for (int i = 0; i < 600; i++)
            {
                var at = new Vector3(400, i * 0.2f, 0);   // crossing at 12 m/s ⇒ ~1.7°/s, well inside 24°/s
                _world.SetComponent(target, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Run(1);
            }
            Assert.Equal(WeaponTaskState.OnTarget, Status(turret).State);
            Assert.Equal(MathF.Atan2(119.8f, 400f), Pose(turret).Azimuth, 2);

            _world.DestroyEntity(target);
            Run(1);
            Assert.Equal(WeaponTaskFailure.NoTarget, Status(turret).Reason);
        }

        [Fact]
        public void WL2_ATypeWithoutThatTurret_FailsNoTurret_AndIdleHolds()
        {
            var (hull, _) = Tank(3);
            var orphan = _world.CreateEntity();   // a turret part whose hull's type declares no turret
            _world.AddComponent(orphan, new PartMetadata { ParentEntity = hull, InstanceId = TurretParts.FirstPartId });
            _world.AddComponent(orphan, default(TurretPose));
            Order(orphan, new Vector3(100, 0, 0));
            Run(1);
            Assert.Equal(WeaponTaskFailure.NoTurret, Status(orphan).Reason);

            var (_, idle) = Tank(1);
            _world.SetComponent(idle, new TurretPose { Azimuth = 0.3f });
            _world.AddOrSet(idle, new WeaponTask { Mode = WeaponTaskMode.Idle, Epoch = 7 });
            Run(60);
            Assert.Equal(0.3f, Pose(idle).Azimuth);
            Assert.Equal(new WeaponTaskStatus { Epoch = 7 }, Status(idle));
        }

        private static (float, float) Round((float A, float E) v) => (MathF.Round(v.A, 3) + 0f, MathF.Round(v.E, 3) + 0f);
    }

    internal static class RepoTestExtensions
    {
        public static void AddOrSet<T>(this EntityRepository repo, Entity e, T value) where T : unmanaged
        {
            if (repo.HasComponent<T>(e)) repo.SetComponent(e, value); else repo.AddComponent(e, value);
        }
    }
}
