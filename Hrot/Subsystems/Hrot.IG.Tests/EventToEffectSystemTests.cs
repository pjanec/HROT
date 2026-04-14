using System.Numerics;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.4.2: <see cref="EventToEffectSystem"/> and
/// <see cref="VisualEffectCleanupSystem"/>.
///
/// Validates:
/// <list type="bullet">
///   <item>A <see cref="FireInteractionEvent"/> spawns exactly two effect entities
///   (one explosion + one tracer).</item>
///   <item>Spawned entities carry correct <see cref="VisualEffectState"/> values.</item>
///   <item>The tracer entity also carries a <see cref="TracerTarget"/>.</item>
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
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const float ShooterX = 100f;
    private const float ShooterY = 200f;
    private const float TargetX  = 500f;
    private const float TargetY  = 600f;
    private const float TickDt   = 0.1f;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<VisualEffectState>();
        repo.RegisterComponent<TracerTarget>();
        repo.RegisterEvent<Hrot.Map.Common.Events.FireInteractionEvent>();
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

    /// <summary>Publishes a FireInteractionEvent to the repo's bus (before SwapBuffers).</summary>
    private static void PublishFire(EntityRepository repo,
        float sx = ShooterX, float sy = ShooterY,
        float tx = TargetX,  float ty = TargetY)
    {
        repo.Bus.Publish(new Hrot.Map.Common.Events.FireInteractionEvent
        {
            ShooterX = sx, ShooterY = sy,
            TargetX  = tx, TargetY  = ty,
        });
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
    /// When no <see cref="FireInteractionEvent"/> is published, the system must not
    /// create any effect entities.
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
    /// A single <see cref="FireInteractionEvent"/> must spawn exactly one explosion entity.
    /// </summary>
    [Fact]
    public void Execute_SingleFireEvent_SpawnsOneExplosionEntity()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        PublishFire(repo);
        RunSpawnSystem(repo, system);

        var (explosions, _) = CountEffects(repo);
        Assert.Equal(1, explosions);
    }

    /// <summary>
    /// A single <see cref="FireInteractionEvent"/> must spawn exactly one tracer entity.
    /// </summary>
    [Fact]
    public void Execute_SingleFireEvent_SpawnsOneTracerEntity()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        PublishFire(repo);
        RunSpawnSystem(repo, system);

        var (_, tracers) = CountEffects(repo);
        Assert.Equal(1, tracers);
    }

    /// <summary>
    /// The explosion entity must be positioned at the event's target coordinates.
    /// </summary>
    [Fact]
    public void Execute_FireEvent_ExplosionPositionedAtTarget()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();
        var view   = (ISimulationView)repo;

        PublishFire(repo);
        RunSpawnSystem(repo, system);

        var query = view.Query().With<VisualEffectState>().Build();
        foreach (var entity in query)
        {
            ref readonly var state     = ref view.GetComponentRO<VisualEffectState>(entity);
            ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);

            if (state.Type == EffectType.Explosion)
            {
                Assert.Equal(TargetX,  transform.Position.X);
                Assert.Equal(TargetY,  transform.Position.Y);
                return;
            }
        }

        Assert.Fail("No explosion entity found after FireInteractionEvent.");
    }

    /// <summary>
    /// The tracer entity must be positioned at the event's shooter coordinates.
    /// </summary>
    [Fact]
    public void Execute_FireEvent_TracerPositionedAtShooter()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();
        var view   = (ISimulationView)repo;

        PublishFire(repo);
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

        Assert.Fail("No tracer entity found after FireInteractionEvent.");
    }

    /// <summary>
    /// The tracer entity must carry a <see cref="TracerTarget"/> pointing at the
    /// event's target coordinates.
    /// </summary>
    [Fact]
    public void Execute_FireEvent_TracerHasCorrectTracerTarget()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();
        var view   = (ISimulationView)repo;

        PublishFire(repo);
        RunSpawnSystem(repo, system);

        var query = view.Query().With<VisualEffectState>().With<TracerTarget>().Build();
        foreach (var entity in query)
        {
            ref readonly var target = ref view.GetComponentRO<TracerTarget>(entity);
            Assert.Equal(TargetX, target.EndX);
            Assert.Equal(TargetY, target.EndY);
            return;
        }

        Assert.Fail("No tracer entity with TracerTarget found.");
    }

    /// <summary>
    /// Two separate events in the same frame must each spawn their own pair of
    /// effect entities (4 total: 2 explosions + 2 tracers).
    /// </summary>
    [Fact]
    public void Execute_TwoFireEvents_SpawnsFourEffectEntities()
    {
        var repo   = CreateRepo();
        var system = new EventToEffectSystem();

        repo.Bus.Publish(new Hrot.Map.Common.Events.FireInteractionEvent { ShooterX = 0f, ShooterY = 0f, TargetX = 100f, TargetY = 100f });
        repo.Bus.Publish(new Hrot.Map.Common.Events.FireInteractionEvent { ShooterX = 200f, ShooterY = 200f, TargetX = 300f, TargetY = 300f });
        RunSpawnSystem(repo, system);

        var (explosions, tracers) = CountEffects(repo);
        Assert.Equal(2, explosions);
        Assert.Equal(2, tracers);
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
