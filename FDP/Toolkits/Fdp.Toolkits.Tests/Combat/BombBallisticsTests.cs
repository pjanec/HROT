using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §8, A3) — <see cref="BombBallistics"/> is the ONE predictor of
    /// where a bomb lands: it steps what flies the bomb (gravity on the velocity in <see cref="BallisticsSystem"/>, then the move), so the
    /// impact it predicts is where the real flight bursts; and its cue says "now" once per pass, when that impact reaches the target.
    /// </summary>
    public sealed class BombBallisticsTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _w = new();
        private readonly BallisticsSystem _ballistics = new();

        public BombBallisticsTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<BallisticProjectile>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterEvent<RaycastRequestEvent>();
            _w.RegisterEvent<DetonationNotification>();
            _w.SetSingleton(new GlobalTime { FrameNumber = 1, TimeScale = 1f });
        }

        public void Dispose() => _w.Dispose();

        /// <summary>⭐ A3 — a bomb dropped at 150 m/s from 500 m: the predicted impact is the flown one (the burst stands off a few
        /// centimetres back along the flight, AreaEffect.BurstStandOffMetres), and the fall takes the predicted time to a frame (⭐ CE-2203: + the contact-hit wait before the burst).</summary>
        [Theory]
        [InlineData(500f, 150f)]
        [InlineData(120f, 220f)]
        public void A3_ThePredictedImpact_IsTheFlownImpact(float height, float speed)
        {
            var from = new Vector3(0f, 0f, height);
            var v0 = BombBallistics.Launch(new Vector3(speed, 0f, 0f));
            var (predicted, seconds) = BombBallistics.Impact(from, v0, ground: null, Dt);

            var bomb = _w.CreateEntity();
            _w.AddComponent(bomb, new SimTransform { Position = from, Rotation = Quaternion.Identity });
            _w.AddComponent(bomb, new SimVelocity { Linear = v0 });
            _w.AddComponent(bomb, new BallisticProjectile
            {
                PreviousPosition = from, Muzzle = from, SpawnTick = 1, TerrainFlags = 1, Ammo = 6022,
                Warhead = WarheadRound.Area | WarheadRound.Arc,
            });

            // the order the SimHost runs them in (BallisticsSystem → LinearKinematicsSystem)
            var bursts = new List<(uint Tick, DetonationNotification D)>();
            for (uint tick = 2; tick < 60 * 60 && bursts.Count == 0; tick++)
            {
                _w.SetSingleton(new GlobalTime { FrameNumber = tick, TimeScale = 1f });
                _ballistics.Execute(_w, Dt);
                foreach (var e in _w.Query().With<SimTransform>().With<SimVelocity>().Build())
                {
                    ref var tf = ref _w.GetComponentRW<SimTransform>(e);
                    tf.Position += _w.GetComponentRO<SimVelocity>(e).Linear * Dt;
                }
                ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
                _w.Bus.SwapBuffers();
                foreach (var d in _w.Bus.Read<DetonationNotification>()) bursts.Add((tick, d));
            }

            var (burstTick, burst) = Assert.Single(bursts);
            float miss = Vector2.Distance(new Vector2(burst.HitX, burst.HitY), new Vector2(predicted.X, predicted.Y));
            Assert.True(miss <= AreaEffect.BurstStandOffMetres + 0.01f, $"predicted {predicted}, burst at ({burst.HitX}, {burst.HitY}, {burst.HitZ}) — {miss:F3} m");
            // detected on the frame after it crossed — ⭐ CE-2203: and it bursts the contact-hit wait later (a late hit on it wins)
            Assert.InRange((burstTick - 1 - CombatConstants.ContactHitWaitTicks) * Dt, seconds, seconds + 2f * Dt);
            Assert.InRange(predicted.X, speed * seconds - 1f, speed * seconds + 1f);   // no drag: the carrier's speed all the way
        }

        /// <summary>⭐ A6 (the cue) — on a level pass the cue first says "now" where the impact a bomb dropped then would make reaches the
        /// target (before it the target is still ahead, seconds > 0), and stops saying it half a second later (the pass is missed). A
        /// target off to the side by more than the tolerance never says now. One bomb per pass is the action's round count.</summary>
        [Fact]
        public void A6_TheCue_SaysNow_WhenTheImpactReachesTheTarget()
        {
            var v = new Vector3(150f, 0f, 0f);
            var (impact, _) = BombBallistics.Impact(new Vector3(0f, 0f, 500f), BombBallistics.Launch(v), null, Dt);
            var target = new Vector3(3000f, 0f, 0f);
            float releaseX = target.X - impact.X;   // where the carrier must be

            int nows = 0; float firstNowX = float.NaN;
            for (float x = 0f; x < 3000f; x += v.X * Dt)
            {
                if (!BombBallistics.ReleaseNow(new Vector3(x, 0f, 500f), v, target, null, Dt)) continue;
                if (nows++ == 0) firstNowX = x;
            }
            Assert.InRange(nows, 1, (int)MathF.Ceiling(BombBallistics.MissedPassSeconds / Dt) + 1);
            Assert.InRange(firstNowX, releaseX - v.X * Dt, releaseX + v.X * Dt);
            var (along, cross, seconds) = BombBallistics.Cue(new Vector3(releaseX - 300f, 0f, 500f), v, target, null, Dt);
            Assert.True(seconds > 1.9f && along > 290f && MathF.Abs(cross) < 0.01f, $"300 m early: {along} m ahead, {seconds} s");

            Assert.False(BombBallistics.ReleaseNow(new Vector3(releaseX, 0f, 500f), v, target + new Vector3(0f, 15f, 0f), null, Dt));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2180</c> (§8a) — the release cue runs on the BRAIN; the intent reaches the SimHost frames later (measured live: the
        /// Mk-82 burst 20–27 m long at 150 m/s ⇒ 0.14–0.18 s). An intent READ N frames after it was RAISED starts the bomb from the carrier
        /// as it was at the raise, flown forward N frames — so the burst does not move with N. N = 0 is one world (the brain's frame is
        /// the fire frame's predecessor), exactly as before; and the burst is where <see cref="BombBallistics.Impact"/> said at the raise.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(11)]
        [InlineData(40)]
        public void CE2180_AnIntentReadNFramesLate_LandsWhereTheReleaseSaid(int framesLate)
        {
            const long PlaneType = 9405, BombType = 9022;
            const uint RaiseTick = 10;
            _w.RegisterComponent<WeaponState>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireIntent>();
            _w.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireNotification>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var plane = new Fdp.Interfaces.TkbTemplate("plane", PlaneType);
            plane.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = BombType, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(plane);
            var bomb = new Fdp.Interfaces.TkbTemplate("bomb", BombType);
            bomb.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto
                { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive, Indirect = true, BlastInjuryRadiusM = 20f, BlastLethalRadiusM = 8f, BlastLethalDamage = 1000f });
            db.Register(bomb);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var carrierV = new Vector3(150f, 20f, 0f);
            var aircraft = _w.CreateEntity();
            _w.AddComponent(aircraft, new SimTransform { Position = new Vector3(0f, 0f, 300f), Rotation = Quaternion.Identity });
            _w.AddComponent(aircraft, new SimVelocity { Linear = carrierV });
            _w.AddComponent(aircraft, new WeaponState { MuzzleVelocity = 0f, Ammo = 4 });
            _w.AddComponent(aircraft, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing });
            _w.AddComponent(aircraft, new TkbIdentity { TkbType = PlaneType });

            var fire = new FireProcessingSystem();
            Vector3 atRaise = default; Vector3 predicted = default;
            var bursts = new List<DetonationNotification>();
            for (uint tick = 1; tick < 60 * 60 && bursts.Count == 0; tick++)
            {
                // a frame as the SimHost runs it: the clock, Input (FireProcessingSystem), Ballistics, then the move
                _w.SetSingleton(new GlobalTime { FrameNumber = tick, TotalTime = tick * (double)Dt, DeltaTime = Dt, TimeScale = 1f });
                fire.Execute(_w, Dt);
                _ballistics.Execute(_w, Dt);
                foreach (var e in _w.Query().With<SimTransform>().With<SimVelocity>().Build())
                {
                    ref var tf = ref _w.GetComponentRW<SimTransform>(e);
                    tf.Position += _w.GetComponentRO<SimVelocity>(e).Linear * Dt;
                }
                ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
                if (tick == RaiseTick)
                {
                    // the brain's cue, at the end of the raise frame: where a bomb dropped now lands
                    atRaise = _w.GetComponentRO<SimTransform>(aircraft).Position;
                    (predicted, _) = BombBallistics.Impact(atRaise, BombBallistics.Launch(carrierV), null, Dt);
                }
                // …and the intent reaches the fire path framesLate frames after the frame that would read it in one world
                if (tick == RaiseTick + (uint)framesLate)
                    _w.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireIntent
                    {
                        Shooter = aircraft, Target = Entity.Null, AtPoint = true, TargetPoint = predicted,
                        RaisedAt = RaiseTick * (double)Dt,
                    });
                _w.Bus.SwapBuffers();
                foreach (var d in _w.Bus.Read<DetonationNotification>()) bursts.Add(d);
            }

            var burst = Assert.Single(bursts);
            float miss = Vector2.Distance(new Vector2(burst.HitX, burst.HitY), new Vector2(predicted.X, predicted.Y));
            Assert.True(miss <= AreaEffect.BurstStandOffMetres + 0.05f,
                $"{framesLate} frames late: predicted at the raise {predicted}, burst at ({burst.HitX}, {burst.HitY}) — {miss:F2} m "
                + $"(uncorrected it would be ≈ {carrierV.Length() * framesLate * Dt:F1} m long)");
            var shot = Assert.Single(ShotLog.For(_w).Recent(1));
            Assert.Equal(framesLate * Dt, shot.CaughtUpSeconds, 3);
            Assert.Equal(RaiseTick * (double)Dt, shot.RaisedAt, 6);
        }
    }
}
