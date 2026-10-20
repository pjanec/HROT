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

        /// <summary>
        /// ⭐ <c>CE-1109</c> (DESIGN_Indirect_Fire FM-4) — <see cref="TurretSlew.SecondsTo"/> is <see cref="TurretSlew.Step"/> in closed form:
        /// a full circle the short way round, a limited arc the long way inside it (never through the dead zone), the slower axis
        /// deciding, an axis of rate 0 never arriving — and stepping <see cref="TurretSlew.Step"/> reaches OnTarget at that time.
        /// 🔴 Red-proof: the short way on a limited arc (|Wrap(Δ)|) ⇒ the dead-zone case reads 40° / 30 °/s ⇒ red.
        /// </summary>
        [Fact]
        public void CE1109_SecondsTo_IsStepsOwnArithmetic_InClosedForm()
        {
            static float S(float deg, float rate) => (deg - 0.5f) / rate;   // OnTarget is within 0.5°
            static void Near(float want, float got) => Assert.True(MathF.Abs(want - got) < 1e-3f, $"want {want}, got {got}");
            var full = new TurretDto { TraverseRateDegPerSec = 24f, ElevationRateDegPerSec = 6f, ElevationMinDeg = -6f, ElevationMaxDeg = 30f };
            var arc = new TurretDto { TraverseMinDeg = -170f, TraverseMaxDeg = 170f, TraverseRateDegPerSec = 30f, ElevationRateDegPerSec = 30f,
                                      ElevationMaxDeg = 10f };

            // a full circle: 170° → −170° is 20° the short way through ±180
            Near(S(20f, 24f), TurretSlew.SecondsTo(new TurretPose { Azimuth = 170f * Deg }, -170f * Deg, 0f, full));
            // a limited arc: 160° → −160° goes 320° the long way inside it, never 40° through its dead zone
            Near(S(320f, 30f), TurretSlew.SecondsTo(new TurretPose { Azimuth = 160f * Deg }, -160f * Deg, 0f, arc));
            // the slower axis decides: 12° of traverse (0.5 s) and 12° of elevation (2 s)
            Near(S(12f, 6f), TurretSlew.SecondsTo(default, 12f * Deg, 12f * Deg, full));
            // an elevation past the limit is laid to the limit, as Step lays it
            Near(S(30f, 6f), TurretSlew.SecondsTo(default, 0f, 80f * Deg, full));
            // a rate of 0: never there — unless that axis is already on
            var frozen = new TurretDto { TraverseRateDegPerSec = 0f, ElevationRateDegPerSec = 5f, ElevationMaxDeg = 45f };
            Assert.True(float.IsPositiveInfinity(TurretSlew.SecondsTo(default, 10f * Deg, 0f, frozen)));
            Near(S(10f, 5f), TurretSlew.SecondsTo(default, 0f, 10f * Deg, frozen));
            Assert.Equal(0f, TurretSlew.SecondsTo(new TurretPose { Azimuth = 0.1f * Deg }, 0f, 0f, full));

            // and Step agrees: stepping at 60 Hz reaches OnTarget within a frame of SecondsTo
            foreach (var (t, from, az, el) in new[] { (full, 170f, -170f, 20f), (arc, 160f, -160f, 5f), (full, -90f, 45f, -4f) })
            {
                var pose = new TurretPose { Azimuth = from * Deg };
                float want = TurretSlew.SecondsTo(pose, az * Deg, el * Deg, t);
                int frames = 0;
                while (!TurretSlew.OnTarget(pose, az * Deg, el * Deg) && frames < 60 * 60) { pose = TurretSlew.Step(pose, az * Deg, el * Deg, t, Dt); frames++; }
                Assert.InRange(frames * Dt, want - Dt - 1e-3f, want + Dt + 1e-3f);
            }
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

        /// <summary>⭐ WL-3 — Engage fires only when on target, at most the granted rounds, spaced by the task's cooldown, through
        /// an ordinary <see cref="Fdp.Toolkit.Combat.Events.WeaponFireIntent"/> marked remote (never forwarded again); then Done.</summary>
        [Fact]
        public void WL3_Engage_FiresTheGrantedRounds_OnTarget_SpacedByTheCooldown_EachGapItsOwn_ThenDone()
        {
            _world.RegisterComponent<WeaponMuscleState>();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireIntent>();
            var (hull, turret) = Tank(1);
            _world.AddOrSet(turret, new WeaponTask { Mode = WeaponTaskMode.Engage, Point = new Vector3(0, 100, 2), RoundsGranted = 3,
                                                     CooldownSeconds = 0.5f, Epoch = 1 });
            var frames = new System.Collections.Generic.List<int>();
            for (int f = 0; f < 60 * 8; f++)
            {
                _muscle.Execute(_world, Dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<Fdp.Toolkit.Combat.Events.WeaponFireIntent>())
                {
                    Assert.Equal(hull, shot.Shooter);
                    Assert.True(shot.AtPoint && shot.IsRemote);
                    frames.Add(f);
                }
            }
            Assert.Equal(3, frames.Count);
            Assert.True(frames[0] >= 225 - 2, $"first round at frame {frames[0]}: not before the 90° slew at 24°/s ends (3.75 s)");
            // ⭐ CE-2323 (R-2063) — each gap is the task's cooldown scaled by this turret's own draw for that round, and the draws differ
            for (int r = 0; r < 2; r++)
            {
                float gap = Fdp.Toolkit.Combat.FirePacing.Gap(0.5f, turret, r, 1);
                Assert.InRange(frames[r + 1] - frames[r], (int)(gap * 60f) - 1, (int)(gap * 60f) + 2);
            }
            Assert.NotEqual(frames[1] - frames[0], frames[2] - frames[1]);
            Assert.Equal(WeaponTaskState.Done, Status(turret).State);
            Assert.Equal(3, Status(turret).RoundsFired);
        }

        /// <summary>⭐ M8 — the TURRET path carries the loaded round: the round the weapon brain wrote on the task
        /// (<see cref="WeaponTask.AmmoGuid"/>, riding <c>EntityWeaponTask</c>) is the round on the fire intent the muscle publishes.</summary>
        [Fact]
        public void M8_TheLoadedRound_ReachesTheMuscle_OnTheTurretPath()
        {
            _world.RegisterComponent<WeaponMuscleState>();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireIntent>();
            var (_, turret) = Tank(1);
            _world.AddOrSet(turret, new WeaponTask { Mode = WeaponTaskMode.Engage, Point = new Vector3(100, 0, 2), RoundsGranted = 1,
                                                     CooldownSeconds = 0.5f, Epoch = 1, AmmoGuid = 9003 });
            long fired = 0;
            for (int f = 0; f < 60 * 4 && fired == 0; f++)
            {
                _muscle.Execute(_world, Dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<Fdp.Toolkit.Combat.Events.WeaponFireIntent>()) fired = shot.AmmoGuid;
            }
            Assert.Equal(9003, fired);
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
