using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only components for stateful tests ----------------------------

/// <summary>Test component for structural and authority tests (ID 210).</summary>
[ComponentId(210)]
internal struct WeaponState { public int Ammo; }

/// <summary>Test component carrying a 2D position for spatial tests (ID 211).</summary>
[ComponentId(211)]
internal struct Position2D { public float X; public float Y; }

/// <summary>Test managed name component for lifecycle NameSubstring tests (ID 212).</summary>
[ComponentId(212)]
internal sealed class EntityLabel
{
    public string? Name;
}

// ---------------------------------------------------------------------------
// DataBreakpointSystemStatefulTests  (UBP-P2T3 -- stateful trackers)
// ---------------------------------------------------------------------------

/// <summary>
/// Integration tests for structural, spatial, and lifecycle breakpoints evaluated
/// by <see cref="DataBreakpointSystem"/> via <see cref="DataBreakpointManager.EvaluateStatefulBreakpoints"/>.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class DataBreakpointSystemStatefulTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var system        = new DataBreakpointSystem(manager);
        return (manager, system, repo);
    }

    // ------------------------------------------------------------------ //
    // UBP-P2T3 test 1: StructuralPredicate fires when component is added  //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A structural breakpoint with ModificationType = Added must fire on the tick
    /// the component is first attached to an entity.
    /// </summary>
    [Fact]
    public void StructuralPredicate_FiresOnComponentAdded()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<WeaponState>();
        var entity = repo.CreateEntity();

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "StructAdd",
            Condition           = new StructuralPredicateDto
            {
                ComponentType       = typeof(WeaponState),
                ModificationType    = StructuralModification.Added,
                AuthorityRequirement = AuthorityRequirement.AnyAuthority
            }
        });

        // Tick with no component: breakpoint must not fire.
        system.Execute(repo, 0f);
        Assert.False(manager.IsPaused);

        // Add component, then tick: breakpoint must fire.
        repo.AddComponent(entity, new WeaponState { Ammo = 5 });
        system.Execute(repo, 0f);
        Assert.True(manager.IsPaused);
    }

    // ------------------------------------------------------------------ //
    // UBP-P2T3 test 2: StructuralPredicate does not fire on dwelling      //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// After the initial Added event fires, subsequent ticks with the component
    /// still present must NOT fire the breakpoint again.
    /// </summary>
    [Fact]
    public void StructuralPredicate_DoesNotFireOnDwelling()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<WeaponState>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new WeaponState { Ammo = 5 });

        int hitCount = 0;
        manager.OnBreakpointHit += (_, _) => hitCount++;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "StructAdd",
            Condition           = new StructuralPredicateDto
            {
                ComponentType       = typeof(WeaponState),
                ModificationType    = StructuralModification.Added,
                AuthorityRequirement = AuthorityRequirement.AnyAuthority
            }
        });

        // First tick: Added event fires.
        system.Execute(repo, 0f);
        // Reset pause so subsequent Execute calls are not blocked.
        manager.RequestContinue();
        int countAfterFirst = hitCount;

        // Three more ticks: component still present, no additional fires.
        system.Execute(repo, 0f);
        system.Execute(repo, 0f);
        system.Execute(repo, 0f);

        Assert.Equal(countAfterFirst, hitCount);
    }

    // ------------------------------------------------------------------ //
    // UBP-P2T3 test 3: SpatialPredicate fires on Entry, not on dwelling   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A spatial breakpoint with TriggerEvent = Entry must fire exactly once when
    /// an entity moves into the bounding box, and must NOT fire again while the
    /// entity remains inside (dwelling).
    /// </summary>
    [Fact]
    public void SpatialPredicate_FiresOnEntry_NotOnDwelling()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<Position2D>();
        var entity = repo.CreateEntity();
        // Start outside the box.
        repo.AddComponent(entity, new Position2D { X = -10f, Y = -10f });

        int hitCount = 0;
        manager.OnBreakpointHit += (_, _) => hitCount++;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "SpatialEntry",
            Condition           = new SpatialBoundingPredicateDto
            {
                PositionComponentType = typeof(Position2D),
                PositionXPath         = "X",
                PositionYPath         = "Y",
                TriggerEvent          = BoundaryEvent.Entry,
                Bounds                = new BoundingBox2D
                {
                    Min = new Vector2(0f, 0f),
                    Max = new Vector2(10f, 10f)
                }
            }
        });

        // Tick 1: entity outside -> no hit.
        system.Execute(repo, 0f);
        Assert.Equal(0, hitCount);

        // Move entity inside the box.
        unsafe
        {
            void* ptr = repo.GetComponentPointer(entity, ComponentTypeRegistry.GetId(typeof(Position2D)));
            if (ptr != null)
            {
                var pos = new Position2D { X = 5f, Y = 5f };
                System.Runtime.CompilerServices.Unsafe.Copy(ptr, ref pos);
            }
        }

        // Tick 2: entity enters box -> hit.
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);

        // Resume and tick again: entity still inside -> no additional hit.
        manager.RequestContinue();
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);
    }

    // ------------------------------------------------------------------ //
    // UBP-P2T3 test 4: LifecyclePredicate fires on birth and on death     //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A lifecycle breakpoint with IdentifierType = EcsHandle fires once when the
    /// target entity first appears and once when it is destroyed.
    /// </summary>
    [Fact]
    public void LifecyclePredicate_FiresOnBirth_AndOnDeath_ByHandle()
    {
        var (manager, system, repo) = Setup();

        var entity = repo.CreateEntity();

        int hitCount = 0;
        manager.OnBreakpointHit += (_, _) => hitCount++;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "Lifecycle",
            Condition           = new LifecyclePredicateDto
            {
                IdentifierType = EntityIdentifierType.EcsHandle,
                TargetValue    = entity.Index.ToString()
            }
        });

        // Tick 1: entity is alive and seen for the first time -> birth hit.
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);

        // Resume so the system keeps running.
        manager.RequestContinue();

        // Tick 2: entity still alive, no additional birth hit.
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);

        // Destroy the entity and tick -> death hit.
        repo.DestroyEntity(entity);
        system.Execute(repo, 0f);
        Assert.Equal(2, hitCount);
    }

    // ------------------------------------------------------------------ //
    // UBP-P2T3 test 4b: LifecyclePredicate fires via NameSubstring path   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A lifecycle breakpoint with IdentifierType = NameSubstring fires once when the
    /// matching entity first appears and once when it is destroyed.
    /// A decoy entity whose name does not match must never trigger the breakpoint.
    /// </summary>
    [Fact]
    public void LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterManagedComponent<EntityLabel>();

        // Create entity and attach the label.
        var entity = repo.CreateEntity();
        var ecb1 = new EntityCommandBuffer();
        ecb1.AddManagedComponent(entity, new EntityLabel { Name = "EnemyTank" });
        ecb1.Playback(repo);

        // Create a decoy entity whose name does NOT match; it must NOT trigger the breakpoint.
        var decoy = repo.CreateEntity();
        var ecb2 = new EntityCommandBuffer();
        ecb2.AddManagedComponent(decoy, new EntityLabel { Name = "AlliedTank" });
        ecb2.Playback(repo);

        int hitCount = 0;
        Entity? lastHitEntity = null;
        manager.OnBreakpointHit += (_, e) => { hitCount++; lastHitEntity = e; };

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "LifecycleName",
            Condition           = new LifecyclePredicateDto
            {
                IdentifierType    = EntityIdentifierType.NameSubstring,
                TargetValue       = "EnemyTank",
                NameComponentType = typeof(EntityLabel),
                NamePropertyPath  = "Name"
            }
        });

        // Tick 1: entity first seen -> birth hit. Decoy must NOT fire.
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);
        Assert.Equal(entity, lastHitEntity);

        manager.RequestContinue();

        // Tick 2: both entities still alive -> no new birth hits.
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);

        // Destroy the EnemyTank entity -> death hit.
        repo.DestroyEntity(entity);
        system.Execute(repo, 0f);
        Assert.Equal(2, hitCount);

        manager.RequestContinue();

        // Destroy the decoy -> must NOT fire (name is "AlliedTank", not "EnemyTank").
        repo.DestroyEntity(decoy);
        system.Execute(repo, 0f);
        Assert.Equal(2, hitCount);
    }

    // ------------------------------------------------------------------ //
    // UBP-P2T3 test 5: RequireAuthority filters ghost-only mutations      //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A structural breakpoint with AuthorityRequirement = RequireAuthority must NOT
    /// fire when the component is added as a ghost (no authority), and MUST fire only
    /// after authority is explicitly granted.
    /// </summary>
    [Fact]
    public void AuthorityRequirement_RequireAuthority_FiltersGhostMutations()
    {
        var (manager, system, repo) = Setup();

        repo.RegisterComponent<WeaponState>();
        var entity = repo.CreateEntity();

        int hitCount = 0;
        manager.OnBreakpointHit += (_, _) => hitCount++;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "AuthOnly",
            Condition           = new StructuralPredicateDto
            {
                ComponentType       = typeof(WeaponState),
                ModificationType    = StructuralModification.Added,
                AuthorityRequirement = AuthorityRequirement.RequireAuthority
            }
        });

        // Add component without authority (ghost scenario).
        repo.AddComponent(entity, new WeaponState { Ammo = 1 });

        // Tick: component present but not authority -> must NOT fire.
        system.Execute(repo, 0f);
        Assert.Equal(0, hitCount);

        // Grant authority and tick -> must fire.
        repo.SetAuthority<WeaponState>(entity, true);
        system.Execute(repo, 0f);
        Assert.Equal(1, hitCount);
    }
}
