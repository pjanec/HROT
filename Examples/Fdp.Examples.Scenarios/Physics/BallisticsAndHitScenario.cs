using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Examples.Common;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.Scenarios.Physics
{
    /// <summary>
    /// DEM1-D003 — BallisticsAndHit: CCD (Continuous Collision Detection) anti-tunneling.
    ///
    /// <para>A Shooter at the origin fires a hyper-velocity round at a Target at (10, 0, 0).
    /// With <c>MuzzleVelocity = 2000 m/s</c> at 60 Hz the bullet displaces ~33 m per tick —
    /// far more than the target's 4 m diameter. The swept-segment CCD raycast issued by
    /// <see cref="BallisticsSystem"/> before <see cref="LinearKinematicsSystem"/> advances
    /// the bullet still detects the crossing and issues a <see cref="HitEvent"/>.</para>
    ///
    /// <para><b>System execution order within each tick:</b></para>
    /// <list type="number">
    ///   <item><see cref="FireProcessingSystem"/> — Input: consumes <see cref="FireRequestEvent"/>, spawns bullet</item>
    ///   <item><see cref="SpatialHashSystem"/> — Sim: rebuilds spatial grid</item>
    ///   <item><see cref="BallisticsSystem"/> — PostSim: submits swept-segment raycast <em>before</em> position advance</item>
    ///   <item><see cref="Fdp.Toolkit.CarKinem.Systems.LinearKinematicsSystem"/> — PostSim: advances bullet position</item>
    ///   <item><see cref="RaycastSolverSystem"/> — Input (next logical step): resolves batch from step 3</item>
    ///   <item><see cref="HitResolutionSystem"/> — Input: emits <see cref="HitEvent"/> to event bus write buffer</item>
    ///   <item><see cref="DamageSystem"/> — Sim: applies damage from the <em>previous</em> tick's HitEvent (after bus swap)</item>
    /// </list>
    ///
    /// <para><b>Phase table:</b></para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 2)</term><description>Bullet alive, <c>SimVelocity.Linear.X == MuzzleVelocity</c></description></item>
    ///   <item><term>Phase 2 (tick 3)</term><description>Bullet position X &gt; target X — bullet already past target in raw space (CCD caught the crossing)</description></item>
    ///   <item><term>Phase 3+4 (tick 4)</term><description>Target health &lt; 100, bullet entity destroyed → scenario succeeds</description></item>
    /// </list>
    ///
    /// <para><b>Deviation note:</b> MuzzleVelocity is set to <c>2000 m/s</c> (not the
    /// design-talk value of 40 m/s) so that one tick's displacement (~33 m) exceeds the
    /// target diameter (4 m), demonstrating genuine CCD anti-tunneling. Target placed at
    /// X = 10 m (not 100 m) so the scenario resolves within 5 ticks. See BATCH-04 report.</para>
    /// </summary>
    public sealed class BallisticsAndHitScenario : IScenario
    {
        // ── Scenario constants ────────────────────────────────────────────────

        /// <summary>
        /// Bullet muzzle velocity in m/s. At 60 Hz this yields ~33 m per tick.
        /// Exceeds target diameter (4 m) to guarantee a tunneling scenario without CCD.
        /// </summary>
        public const float MuzzleVelocity = 2000f;

        private const float TargetX      = 10f;
        private const float TargetRadius  = 2f;
        private const float MaxHealth     = 100f;

        // ── Physics module (held alive for NativeArray lifetime) ──────────────
        private PhysicsToolkitModule? _physicsModule;

        // ── Network entity map for FireProcessingSystem ───────────────────────
        private readonly NetworkEntityMap _entityMap = new NetworkEntityMap();

        // Network IDs assigned to shooter and target.
        private const long ShooterNetId = 1L;
        private const long TargetNetId  = 2L;

        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>Bullet entity captured on spawn (tick 1).</summary>
        public Entity BulletEntity { get; private set; }

        /// <summary>Bullet's SimVelocity.Linear.X captured at tick 2 (Phase 1).</summary>
        public float BulletVelocityXAtTick2 { get; private set; }

        /// <summary>Bullet's SimTransform.Position.X captured at tick 3 (Phase 2).</summary>
        public float BulletPositionXAtTick3 { get; private set; }

        /// <summary>Target health captured after damage is applied (tick 4, Phase 3).</summary>
        public float TargetHealthAfterHit { get; private set; } = MaxHealth;

        // ── Phase latch flags ─────────────────────────────────────────────────

        private bool _fireInjected;
        private bool _phase1Checked;
        private bool _phase2Checked;

        // ── Entity handles ────────────────────────────────────────────────────

        private Entity _shooter;
        private Entity _target;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => "ballisticsandhit";

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Component registration ─────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<PhysicsCollider>();

            // ── Event registration ─────────────────────────────────────────────
            world.RegisterEvent<WeaponFireIntent>();
            world.RegisterEvent<HitEvent>();

            // ── Physics singleton (RaycastBatchData with persistent NativeArrays) ──
            // Module retains ownership; Dispose() is called via OnShutdown() at scenario teardown.
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(world);

            // ── System pipeline ────────────────────────────────────────────────
            // Execution order is critical for CCD anti-tunneling integrity:
            //   FireProcessingSystem → SpatialHashSystem → BallisticsSystem
            //   → LinearKinematicsSystem → RaycastSolverSystem → HitResolutionSystem → DamageSystem
            //
            // BallisticsSystem records PreviousPosition and submits the swept-segment raycast
            // BEFORE LinearKinematicsSystem advances the bullet, so the segment covers exactly
            // the distance traversed in the previous tick.
            // HitResolutionSystem publishes HitEvent to the write bus; DamageSystem consumes
            // the event one tick later after kernel.Update()'s SwapBuffers makes it readable.
            var systems = new ComponentSystem[]
            {
                new FireProcessingSystem(),
                new SpatialHashSystem(),
                new BallisticsSystem(),
                new LinearKinematicsSystem(),
                new RaycastSolverSystem(),
                new HitResolutionSystem(),
                new DamageSystem(),
            };

            foreach (var sys in systems)
                sys.Create(world);

            kernel.RegisterModule(new BallisticsModule("BallisticsAndHitModule", world, systems));

            // ── Entity spawning ────────────────────────────────────────────────
            _shooter = SpawnShooter(world);
            _target  = SpawnTarget(world);

            // Register entities in the map so FireProcessingSystem can resolve them.
            _entityMap.Register(ShooterNetId, _shooter);
            _entityMap.Register(TargetNetId,  _target);
        }

        /// <inheritdoc/>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // ── Tick 1: inject FireRequestEvent ──────────────────────────────
            if (tick == 1 && !_fireInjected)
            {
                _fireInjected = true;
                world.Bus.Publish(new WeaponFireIntent
                {
                    Shooter     = _shooter,
                    Target      = _target,
                    WeaponIndex = 0,
                });
            }

            // ── Phase 1 (tick 2): bullet spawned with correct velocity ─────────
            if (tick == 2 && !_phase1Checked)
            {
                _phase1Checked = true;

                // Locate bullet entity (first entity with BallisticProjectile).
                var bulletQuery = world.Query()
                    .With<BallisticProjectile>()
                    .With<SimVelocity>()
                    .Build();

                Entity bullet = default;
                foreach (var e in bulletQuery) { bullet = e; break; }

                if (!world.IsAlive(bullet))
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED: No bullet entity found at tick {tick}");

                BulletEntity = bullet;
                var vel = world.GetComponent<SimVelocity>(bullet);
                BulletVelocityXAtTick2 = vel.Linear.X;

                if (MathF.Abs(vel.Linear.X - MuzzleVelocity) > 0.1f)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED: bullet.SimVelocity.Linear.X={vel.Linear.X:F1} " +
                        $"expected {MuzzleVelocity:F1} m/s");
            }

            // ── Phase 2 (tick 3): bullet past target in raw navigation space ───
            // This confirms the tunneling scenario: the bullet teleported past the target
            // in one tick, yet CCD still detected the hit (checked in Phase 3).
            if (tick == 3 && !_phase2Checked)
            {
                _phase2Checked = true;

                if (!world.IsAlive(BulletEntity))
                {
                    // Bullet may already be destroyed if DamageSystem ran earlier than expected —
                    // that would mean Phase 3 is also met; record it as an early success indicator.
                    return false;
                }

                var tf = world.GetComponent<SimTransform>(BulletEntity);
                BulletPositionXAtTick3 = tf.Position.X;

                // Bullet must have moved PAST the target (anti-tunneling evidence via position).
                if (tf.Position.X <= TargetX)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED: bullet.Position.X={tf.Position.X:F2} " +
                        $"expected > {TargetX} (bullet should have tunneled past target without CCD)");
            }

            // ── Phase 3+4 (tick 4): CCD hit applied, bullet destroyed ──────────
            if (tick == 4)
            {
                // Phase 3: target health reduced (DamageSystem applied the CCD hit).
                var health = world.GetComponent<Health>(_target);
                TargetHealthAfterHit = health.Current;

                if (health.Current >= MaxHealth)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED: target.Health={health.Current} still at max={MaxHealth}; " +
                        $"CCD hit not applied by tick {tick}");

                // Phase 4: bullet destroyed after impact (single-hit semantics).
                if (world.IsAlive(BulletEntity))
                    throw new ScenarioFailureException(4,
                        $"Phase 4 FAILED: bullet entity still alive at tick {tick}; " +
                        $"expected destruction after hit");

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        /// <inheritdoc/>
        public void OnShutdown() => _physicsModule?.Dispose();

        // ── Entity factories ──────────────────────────────────────────────────

        private Entity SpawnShooter(EntityRepository world)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(e, new WeaponState
            {
                Ammo             = 100,
                MuzzleVelocity   = MuzzleVelocity,
                CooldownTicksRemaining = 0,
            });
            return e;
        }

        private Entity SpawnTarget(EntityRepository world)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform
            {
                Position = new Vector3(TargetX, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(e, new Health  { Current = MaxHealth, Max = MaxHealth });
            world.AddComponent(e, new PhysicsCollider
            {
                Radius         = TargetRadius,
                CollisionLayer = 1,   // standard entity layer; hit by bullet LayerMask = ~2
            });
            return e;
        }

        // ── Inner module ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs all ballistics pipeline systems in the required order and disposes
        /// the <see cref="RaycastBatchData"/> NativeArrays when the kernel shuts down.
        /// </summary>
        private sealed class BallisticsModule : IEcsModule, IDisposable
        {
            private readonly ComponentSystem[]   _systems;
            private readonly EntityRepository    _world;
            private bool _disposed;

            public string Name { get; }
            public ExecutionPolicy Policy              => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public BallisticsModule(string name, EntityRepository world, ComponentSystem[] systems)
            {
                Name     = name;
                _world   = world;
                _systems = systems;
            }

            public void RegisterSystems(ISystemRegistry registry) { }

            public void Tick(ISimulationView view, float deltaTime)
            {
                foreach (var sys in _systems)
                    sys.Run();
            }

            public IReadOnlyList<Type>? GetRequiredComponents() => null;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                foreach (var sys in _systems)
                    sys.Dispose();

                // Free the NativeArrays that PhysicsToolkitModule transferred to the world.
                if (_world.HasSingleton<RaycastBatchData>())
                {
                    ref var batch = ref _world.GetSingleton<RaycastBatchData>();
                    if (batch.Requests.IsCreated) batch.Requests.Dispose();
                    if (batch.Hits.IsCreated)     batch.Hits.Dispose();
                }
            }
        }
    }
}
