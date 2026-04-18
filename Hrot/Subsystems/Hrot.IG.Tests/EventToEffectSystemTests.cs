using System.Numerics;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="EventToEffectSystem"/> and <see cref="VisualEffectCleanupSystem"/>.
///
/// Validates:
/// <list type="bullet">
///   <item>A <see cref="DetonationNotification"/> spawns one explosion at the hit position.</item>
///   <item>A <see cref="WeaponFireNotification"/> with live shooter/target spawns one tracer.</item>
///   <item>A <see cref="WeaponFireNotification"/> with null or dead entities spawns no tracer.</item>
///   <item>No event → no effect entities.</item>
///   <item><see cref="VisualEffectCleanupSystem"/> increments <see cref="VisualEffectState.ElapsedTime"/>.</item>
///   <item><see cref="VisualEffectCleanupSystem"/> destroys entities whose
///   <see cref="VisualEffectState.IsExpired"/> is <c>true</c>.</item>
/// </list>
///
/// No DDS or Raylib window context required.
/// </summary>
public class EventToEffectSystemTests
{
    // ── Test constants ────────────────────────────────────────────────────────

    private const float ShooterX = 100f;
    private const float ShooterY = 200f;
    private const float TargetX  = 500f;
    private const float TargetY  = 600f;
    private const float HitX     = 450f;
    private const float HitY     = 550f;
    private const float TickDt   = 0.1f;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<VisualEffectState>();
        repo.RegisterComponent<TracerTarget>();
        repo.RegisterEvent<DetonationNotification>();
        repo.RegisterEvent<WeaponFireNotification>();
        return repo;
    }

    private static void RunSpawnSystem(EntityRepository repo, EventToEffectSystem system, float dt = TickDt)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, dt);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    private static void RunCleanupSystem(EntityRepository repo, VisualEffectCleanupSystem system, float dt)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, dt);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (int explosions, int tracers) CountEffects(EntityRepository repo)
    {
        var view      = (ISimulationView)repo;
        var query     = view.Query().With<VisualEffectState>().Build();
        int explosions = 0, tracers = 0;

        foreach (var entity in query)
        {
            ref readonly var state = ref view.GetComponentRO<VisualEffectState>(entity);
            if (state.Type == EffectType.Explosion) explosions++;
            else if (state.Type == EffectType.Tracer) tracers++;
        }

        return (explosions, tracers);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EventToEffectSystem — spawn correctness
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When no events are published, the system must not create any effect entities.
    /// </summary>
    [Fact]
    public void Execute_NoEvent_NoEffectEntitiesSpawned()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        RunSpawnSystem(repo, system); // no publish

        var (explosions, tracers) = CountEffects(repo);
        Assert.Equal(0, explosions);
        Assert.Equal(0, tracers);
    }

    /// <summary>
    /// A single <see cref="DetonationNotification"/> must spawn exactly one explosion entity.
    /// </summary>
    [Fact]
    public void Execute_DetonationNotification_SpawnsOneExplosionEntity()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        repo.Bus.Publish(new DetonationNotification { HitX = HitX, HitY = HitY });
        RunSpawnSystem(repo, system);

        var (explosions, _) = CountEffects(repo);
        Assert.Equal(1, explosions);
    }

    /// <summary>
    /// The explosion entity must be positioned at the event's hit coordinates.
    /// </summary>
    [Fact]
    public void Execute_DetonationNotification_ExplosionPositionedAtHitPoint()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();
        var view   = (ISimulationView)repo;

        repo.Bus.Publish(new DetonationNotification { HitX = HitX, HitY = HitY });
        RunSpawnSystem(repo, system);

        var query = view.Query().With<VisualEffectState>().Build();
        foreach (var entity in query)
        {
            ref readonly var state     = ref view.GetComponentRO<VisualEffectState>(entity);
            ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);

            if (state.Type == EffectType.Explosion)
            {
                Assert.Equal(HitX, transform.Position.X);
                Assert.Equal(HitY, transform.Position.Y);
                return;
            }
        }

        Assert.Fail("No explosion entity found after DetonationNotification.");
    }

    /// <summary>
    /// A <see cref="WeaponFireNotification"/> with live shooter and target entities
    /// must spawn exactly one tracer entity.
    /// </summary>
    [Fact]
    public void Execute_WeaponFireNotification_LiveEntities_SpawnsOneTracerEntity()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        var shooter = repo.CreateEntity();
        var target  = repo.CreateEntity();
        repo.AddComponent(shooter, new SimTransform { Position = new Vector3(ShooterX, ShooterY, 0f), Rotation = Quaternion.Identity });
        repo.AddComponent(target,  new SimTransform { Position = new Vector3(TargetX,  TargetY,  0f), Rotation = Quaternion.Identity });

        repo.Bus.Publish(new WeaponFireNotification { Shooter = shooter, Target = target });
        RunSpawnSystem(repo, system);

        var (_, tracers) = CountEffects(repo);
        Assert.Equal(1, tracers);
    }

    /// <summary>
    /// The tracer entity must be positioned at the shooter's world position.
    /// </summary>
    [Fact]
    public void Execute_WeaponFireNotification_TracerPositionedAtShooter()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();
        var view   = (ISimulationView)repo;

        var shooter = repo.CreateEntity();
        var target  = repo.CreateEntity();
        repo.AddComponent(shooter, new SimTransform { Position = new Vector3(ShooterX, ShooterY, 0f), Rotation = Quaternion.Identity });
        repo.AddComponent(target,  new SimTransform { Position = new Vector3(TargetX,  TargetY,  0f), Rotation = Quaternion.Identity });

        repo.Bus.Publish(new WeaponFireNotification { Shooter = shooter, Target = target });
        RunSpawnSystem(repo, system);

        var query = view.Query().With<VisualEffectState>().Build();
        foreach (var entity in query)
        {
            ref readonly var state     = ref view.GetComponentRO<VisualEffectState>(entity);
            ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);

            if (state.Type == EffectType.Tracer)
            {
                Assert.Equal(ShooterX, transform.Position.X);
                Assert.Equal(ShooterY, transform.Position.Y);
                return;
            }
        }

        Assert.Fail("No tracer entity found after WeaponFireNotification.");
    }

    /// <summary>
    /// The tracer entity must carry a <see cref="TracerTarget"/> pointing at the target's position.
    /// </summary>
    [Fact]
    public void Execute_WeaponFireNotification_TracerHasCorrectTracerTarget()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();
        var view   = (ISimulationView)repo;

        var shooter = repo.CreateEntity();
        var target  = repo.CreateEntity();
        repo.AddComponent(shooter, new SimTransform { Position = new Vector3(ShooterX, ShooterY, 0f), Rotation = Quaternion.Identity });
        repo.AddComponent(target,  new SimTransform { Position = new Vector3(TargetX,  TargetY,  0f), Rotation = Quaternion.Identity });

        repo.Bus.Publish(new WeaponFireNotification { Shooter = shooter, Target = target });
        RunSpawnSystem(repo, system);

        var query = view.Query().With<VisualEffectState>().With<TracerTarget>().Build();
        foreach (var entity in query)
        {
            ref readonly var tracerTarget = ref view.GetComponentRO<TracerTarget>(entity);
            Assert.Equal(TargetX, tracerTarget.EndX);
            Assert.Equal(TargetY, tracerTarget.EndY);
            return;
        }

        Assert.Fail("No tracer entity with TracerTarget found.");
    }

    /// <summary>
    /// A <see cref="WeaponFireNotification"/> with <see cref="Entity.Null"/> shooter
    /// must not spawn a tracer (null-entity guard).
    /// </summary>
    [Fact]
    public void Execute_WeaponFireNotification_NullShooter_NoTracerSpawned()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        var target = repo.CreateEntity();
        repo.AddComponent(target, new SimTransform { Position = new Vector3(TargetX, TargetY, 0f), Rotation = Quaternion.Identity });

        repo.Bus.Publish(new WeaponFireNotification { Shooter = Entity.Null, Target = target });
        RunSpawnSystem(repo, system);

        var (_, tracers) = CountEffects(repo);
        Assert.Equal(0, tracers);
    }

    /// <summary>
    /// Two separate <see cref="DetonationNotification"/> events in the same frame must
    /// each spawn one explosion (2 total).
    /// </summary>
    [Fact]
    public void Execute_TwoDetonationNotifications_SpawnsTwoExplosions()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        repo.Bus.Publish(new DetonationNotification { HitX = 100f, HitY = 100f });
        repo.Bus.Publish(new DetonationNotification { HitX = 200f, HitY = 200f });
        RunSpawnSystem(repo, system);

        var (explosions, _) = CountEffects(repo);
        Assert.Equal(2, explosions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VisualEffectCleanupSystem — lifecycle management
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Each tick must advance <see cref="VisualEffectState.ElapsedTime"/> by the
    /// supplied <c>deltaTime</c>.
    /// </summary>
    [Fact]
    public void Cleanup_Execute_AdvancesElapsedTime()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var cleanup = new VisualEffectCleanupSystem();
        const float duration = VisualEffectStateConstants.ExplosionDurationSeconds;

        repo.AddComponent(entity, new VisualEffectState
        {
            Type        = EffectType.Explosion,
            Duration    = duration,
            ElapsedTime = 0f,
        });

        RunCleanupSystem(repo, cleanup, dt: TickDt);

        var state = repo.GetComponent<VisualEffectState>(entity);
        Assert.Equal(TickDt, state.ElapsedTime, precision: 5);
    }

    /// <summary>
    /// An entity whose elapsed time exceeds its duration must be destroyed after
    /// a single cleanup tick.
    /// </summary>
    [Fact]
    public void Cleanup_ExpiredEffect_EntityDestroyed()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var cleanup = new VisualEffectCleanupSystem();

        repo.AddComponent(entity, new VisualEffectState
        {
            Type        = EffectType.Explosion,
            Duration    = 0.5f,
            ElapsedTime = 0f,
        });

        // Run with dt greater than the duration — entity should be destroyed.
        RunCleanupSystem(repo, cleanup, dt: 1.0f);

        Assert.False(((ISimulationView)repo).IsAlive(entity));
    }

    /// <summary>
    /// An entity that is not yet expired must survive a cleanup tick.
    /// </summary>
    [Fact]
    public void Cleanup_NonExpiredEffect_EntitySurvives()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var cleanup = new VisualEffectCleanupSystem();

        repo.AddComponent(entity, new VisualEffectState
        {
            Type        = EffectType.Explosion,
            Duration    = VisualEffectStateConstants.ExplosionDurationSeconds,
            ElapsedTime = 0f,
        });

        // One small tick — far from expiry.
        RunCleanupSystem(repo, cleanup, dt: TickDt);

        Assert.True(((ISimulationView)repo).IsAlive(entity));
    }

    /// <summary>
    /// An entity whose elapsed time exactly equals its duration must be destroyed
    /// (boundary condition: <see cref="VisualEffectState.IsExpired"/> uses &gt;=).
    /// </summary>
    [Fact]
    public void Cleanup_ElapsedEqualsExactDuration_EntityDestroyed()
    {
        var repo    = CreateRepo();
        var entity  = repo.CreateEntity();
        var cleanup = new VisualEffectCleanupSystem();
        const float duration = 1.0f;

        repo.AddComponent(entity, new VisualEffectState
        {
            Type        = EffectType.Explosion,
            Duration    = duration,
            ElapsedTime = 0f,
        });

        RunCleanupSystem(repo, cleanup, dt: duration);

        Assert.False(((ISimulationView)repo).IsAlive(entity));
    }
}
