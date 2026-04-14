using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.ScenarioEditor.Tools;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration test for Task IG.4.5 â€” verifies that all four IG Phase 4 systems
/// and tools operate correctly in concert using a shared <see cref="EntityRepository"/>.
///
/// Scenario:
/// <list type="number">
///   <item>
///     <b>History trail</b>: An entity with ShowTrail = true accumulates samples
///     over multiple <see cref="HistoryRecordingSystem"/> ticks.
///   </item>
///   <item>
///     <b>Visual effects</b>: A <see cref="FireInteractionEvent"/> causes
///     <see cref="EventToEffectSystem"/> to spawn explosion and tracer entities;
///     <see cref="VisualEffectCleanupSystem"/> destroys them once their lifetime
///     expires.
///   </item>
///   <item>
///     <b>Context menu</b>: <see cref="ContextMenuSystem"/> opens a menu for an
///     entity and reflects the state in <see cref="ContextMenuState"/>.
///   </item>
///   <item>
///     <b>Edit tool</b>: <see cref="EditTool"/> loads polyline vertices, accepts
///     a drag, and fires <see cref="EditTool.OnPolylineCommitted"/> on right-click.
///   </item>
/// </list>
///
/// All four subsystems share one repo to confirm that their component registrations
/// are compatible and that no system corrupts another's data.
///
/// No DDS or Raylib window context required.
/// </summary>
public class AdvancedFeaturesIntegrationTests
{
    // â”€â”€ Test constants (Â§CODE-STANDARDS Â§1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private const float TrailEntityX   = 1000f;
    private const float TrailEntityY   = 2000f;
    private const float ShooterX       = 0f;
    private const float ShooterY       = 0f;
    private const float TargetX        = 500f;
    private const float TargetY        = 500f;
    private const float MenuScreenX    = 400f;
    private const float MenuScreenY    = 300f;
    private const float PolyVertex0X   = 100f;
    private const float PolyVertex0Y   = 100f;
    private const float PolyVertex1X   = 200f;
    private const float PolyVertex1Y   = 200f;
    private const float DragTargetX    = 250f;
    private const float DragTargetY    = 300f;

    // â”€â”€ Shared world factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Registers all components and events needed by all four Phase-4 subsystems.
    /// </summary>
    private static EntityRepository CreateFullRepo()
    {
        var repo = new EntityRepository();

        // --- Unmanaged components ---
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<HistoryTrail>();
        repo.RegisterComponent<VisualEffectState>();
        repo.RegisterComponent<TracerTarget>();

        // --- Managed components ---
        repo.RegisterManagedComponent<ContextMenuState>();
        repo.RegisterManagedComponent<EditablePolyline>();

        // --- Events ---
        repo.RegisterEvent<Hrot.Map.Common.Events.FireInteractionEvent>();

        return repo;
    }

    private static void RunSystem(EntityRepository repo, IEcsModuleSystem system, float dt = 0f)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, dt);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // IG.4.5 â€” End-to-end integration scenario
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// End-to-end integration test exercising all four Phase-4 subsystems in a
    /// single shared repo:
    /// <list type="bullet">
    ///   <item>HistoryRecordingSystem accumulates trail samples.</item>
    ///   <item>EventToEffectSystem spawns effects; VisualEffectCleanupSystem destroys them.</item>
    ///   <item>ContextMenuSystem opens and reflects the menu state.</item>
    ///   <item>EditTool loads, drags, and commits the polyline.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Phase4_AllSubsystems_WorkTogetherInSharedRepo()
    {
        var repo = CreateFullRepo();
        var view = (ISimulationView)repo;

        // â”€â”€ Step 1: History trail â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        var trailEntity = repo.CreateEntity();
        repo.AddComponent(trailEntity, new SimTransform
        {
            Position = new Vector3(TrailEntityX, TrailEntityY, 0f),
            Rotation = Quaternion.Identity,
        });
        var style = ResolvedStyle.CreateDefault();
        style.ShowTrail = true;
        repo.AddComponent(trailEntity, style);
        repo.AddComponent(trailEntity, HistoryTrail.Create(
            sampleInterval: HistoryTrailConstants.DefaultSampleIntervalSeconds));

        var historySystem = new HistoryRecordingSystem();

        // Three ticks at the default sample interval â†’ three trail points.
        const float sampleDt = HistoryTrailConstants.DefaultSampleIntervalSeconds;
        RunSystem(repo, historySystem, dt: sampleDt);
        RunSystem(repo, historySystem, dt: sampleDt);
        RunSystem(repo, historySystem, dt: sampleDt);

        var trail = repo.GetComponent<HistoryTrail>(trailEntity);
        Assert.Equal(3, trail.Count);

        for (int i = 0; i < trail.Count; i++)
        {
            var (x, y) = trail.GetPoint(i);
            Assert.Equal(TrailEntityX, x);
            Assert.Equal(TrailEntityY, y);
        }

        // â”€â”€ Step 2: Visual effects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        var spawnSystem   = new EventToEffectSystem();
        var cleanupSystem = new VisualEffectCleanupSystem();

        // Publish the event and run the spawn system.
        repo.Bus.Publish(new Hrot.Map.Common.Events.FireInteractionEvent
        {
            ShooterX = ShooterX, ShooterY = ShooterY,
            TargetX  = TargetX,  TargetY  = TargetY,
        });
        RunSystem(repo, spawnSystem, dt: 0f);

        // Count spawned effect entities.
        var effectQuery = view.Query().With<VisualEffectState>().Build();
        int explosions  = 0, tracers = 0;
        Entity explosionEntity = Entity.Null;

        foreach (var e in effectQuery)
        {
            ref readonly var state = ref view.GetComponentRO<VisualEffectState>(e);
            if (state.Type == EffectType.Explosion) { explosions++; explosionEntity = e; }
            else if (state.Type == EffectType.Tracer) tracers++;
        }

        Assert.Equal(1, explosions);
        Assert.Equal(1, tracers);

        // Advance the cleanup system past the explosion's lifetime.
        const float longDt = VisualEffectStateConstants.ExplosionDurationSeconds + 1f;
        RunSystem(repo, cleanupSystem, dt: longDt);

        // All effect entities must be gone.
        var afterCleanup = view.Query().With<VisualEffectState>().Build();
        int remaining = 0;
        foreach (var _ in afterCleanup) remaining++;
        Assert.Equal(0, remaining);

        // â”€â”€ Step 3: Context menu â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        var menuEntity = repo.CreateEntity();
        var menuSystem = new ContextMenuSystem();

        menuSystem.TestHook_TriggerContextMenu(menuEntity, MenuScreenX, MenuScreenY);
        RunSystem(repo, menuSystem);

        Assert.Equal(menuEntity, menuSystem.ActiveMenuEntity);

        bool hasMenu = view.HasManagedComponent<ContextMenuState>(menuEntity);
        Assert.True(hasMenu, "Entity must have ContextMenuState after trigger.");

        var menuState = view.GetManagedComponentRO<ContextMenuState>(menuEntity);
        Assert.True(menuState.IsOpen);

        menuSystem.TestHook_CloseContextMenu(menuEntity);
        RunSystem(repo, menuSystem);
        Assert.Equal(Entity.Null, menuSystem.ActiveMenuEntity);

        // â”€â”€ Step 4: Edit tool â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        var polyEntity = repo.CreateEntity();
        repo.SetManagedComponent(polyEntity, new EditablePolyline
        {
            Points = new List<Vector2>
            {
                new Vector2(PolyVertex0X, PolyVertex0Y),
                new Vector2(PolyVertex1X, PolyVertex1Y),
            },
        });

        var editTool = new EditTool(polyEntity, view);
        editTool.OnEnter(null!);

        Assert.Equal(2, editTool.GhostPoints.Count);

        // Click near vertex 1 to select it.
        var nearV1 = new Vector2(
            PolyVertex1X + EditToolConstants.VertexPickRadiusWorldUnits * 0.4f,
            PolyVertex1Y);
        editTool.HandleClick(nearV1, MouseButton.Left);
        Assert.Equal(1, editTool.SelectedVertexIndex);

        // Drag vertex 1 to a new position.
        var dragTarget = new Vector2(DragTargetX, DragTargetY);
        editTool.HandleDrag(dragTarget, Vector2.Zero);
        Assert.Equal(dragTarget, editTool.GhostPoints[1]);

        // Right-click commits.
        List<Vector2>? committed = null;
        editTool.OnPolylineCommitted += (_, pts) => committed = pts;
        editTool.HandleClick(Vector2.Zero, MouseButton.Right);

        Assert.NotNull(committed);
        Assert.Equal(2, committed!.Count);
        Assert.Equal(dragTarget, committed[1]);

        // â”€â”€ Step 5: History-trail entity is still alive and unaffected â”€â”€â”€â”€â”€â”€â”€â”€

        Assert.True(view.IsAlive(trailEntity));
        var finalTrail = repo.GetComponent<HistoryTrail>(trailEntity);
        Assert.Equal(3, finalTrail.Count); // No additional samples since last tick.
    }

    /// <summary>
    /// Multiple events in a single frame each spawn their own effect entities â€”
    /// verifies the spawn loop iterates all events, not just the first.
    /// </summary>
    [Fact]
    public void Phase4_TwoFireEvents_BothSpawnEffects()
    {
        var repo        = CreateFullRepo();
        var spawnSystem = new EventToEffectSystem();
        var view        = (ISimulationView)repo;

        repo.Bus.Publish(new Hrot.Map.Common.Events.FireInteractionEvent { ShooterX = 0f, ShooterY = 0f, TargetX = 100f, TargetY = 100f });
        repo.Bus.Publish(new Hrot.Map.Common.Events.FireInteractionEvent { ShooterX = 200f, ShooterY = 0f, TargetX = 300f, TargetY = 0f });

        RunSystem(repo, spawnSystem, dt: 0f);

        var query       = view.Query().With<VisualEffectState>().Build();
        int explosions  = 0, tracers = 0;
        foreach (var e in query)
        {
            ref readonly var state = ref view.GetComponentRO<VisualEffectState>(e);
            if (state.Type == EffectType.Explosion) explosions++;
            else tracers++;
        }

        Assert.Equal(2, explosions);
        Assert.Equal(2, tracers);
    }

    /// <summary>
    /// HistoryRecordingSystem must not sample entities whose ShowTrail flag is false,
    /// even when other trail-enabled entities exist in the same repo.
    /// </summary>
    [Fact]
    public void Phase4_TrailSystem_OnlySamplesEntitiesWithShowTrailTrue()
    {
        var repo   = CreateFullRepo();
        var system = new HistoryRecordingSystem();

        // Entity A â€” trail enabled.
        var entityA = repo.CreateEntity();
        repo.AddComponent(entityA, new SimTransform { Position = new Vector3(10f, 20f, 0f), Rotation = Quaternion.Identity });
        var styleA = ResolvedStyle.CreateDefault();
        styleA.ShowTrail = true;
        repo.AddComponent(entityA, styleA);
        repo.AddComponent(entityA, HistoryTrail.Create());

        // Entity B â€” trail disabled.
        var entityB = repo.CreateEntity();
        repo.AddComponent(entityB, new SimTransform { Position = new Vector3(30f, 40f, 0f), Rotation = Quaternion.Identity });
        var styleB = ResolvedStyle.CreateDefault();
        styleB.ShowTrail = false;
        repo.AddComponent(entityB, styleB);
        repo.AddComponent(entityB, HistoryTrail.Create());

        RunSystem(repo, system, dt: HistoryTrailConstants.DefaultSampleIntervalSeconds);

        Assert.Equal(1, repo.GetComponent<HistoryTrail>(entityA).Count);
        Assert.Equal(0, repo.GetComponent<HistoryTrail>(entityB).Count);
    }
}
