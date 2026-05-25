using System;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only component for ExternalHitTag tests (ComponentId 220) ---------

/// <summary>Test component used in ExternalHitTag compound-predicate tests.</summary>
[ComponentId(220)]
internal struct HealthComponent { public int Value; }

// =============================================================================
// ExternalHitTagPredicateDto / OnExternalHit integration tests
// =============================================================================

[Collection("ComponentRegistry")]
public sealed class ExternalHitTagTests
{
    private readonly DataBreakpointManager _manager;
    private readonly EntityRepository _liveRepo;
    private readonly MockDebugTimeController _tc;

    public ExternalHitTagTests()
    {
        ComponentTypeRegistry.Clear();

        var preTickSnapshot  = new EntityRepository();
        _liveRepo            = new EntityRepository();
        _tc                  = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var predicateCompiler    = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventScannerCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        _manager = new DataBreakpointManager(
            _liveRepo, preTickSnapshot, snapshotProvider, _tc,
            predicateCompiler, eventScannerCompiler);
    }

    // --- Helpers ---

    private Entity SpawnWithHealth(int value)
    {
        _liveRepo.RegisterComponent<HealthComponent>();
        var entity = _liveRepo.CreateEntity();
        _liveRepo.AddComponent(entity, new HealthComponent { Value = value });
        return entity;
    }

    // -------------------------------------------------------------------------
    // 1. Standalone ExternalHitTagPredicateDto fires on matching tag
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalHitTag_Standalone_TriggersOnTagMatch()
    {
        Breakpoint? firedBp = null;
        _manager.OnBreakpointHit += (bp, _) => firedBp = bp;

        _manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "myTag" });

        var entity = _liveRepo.CreateEntity();
        _manager.OnExternalHit("myTag", entity);

        Assert.NotNull(firedBp);
    }

    // -------------------------------------------------------------------------
    // 2. Wrong tag does not fire
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalHitTag_WrongTag_DoesNotFire()
    {
        Breakpoint? firedBp = null;
        _manager.OnBreakpointHit += (bp, _) => firedBp = bp;

        _manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "myTag" });

        var entity = _liveRepo.CreateEntity();
        _manager.OnExternalHit("otherTag", entity);

        Assert.Null(firedBp);
    }

    // -------------------------------------------------------------------------
    // 3. Compound AND with ExternalHitTag + PropertyMatch fires when property matches
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalHitTag_InCompoundAnd_ValueZero_Fires()
    {
        // Fresh manager to avoid hit-count interference
        var (mgr, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<HealthComponent>();

        Breakpoint? firedBp = null;
        mgr.OnBreakpointHit += (bp, _) => firedBp = bp;

        var predicate = new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new System.Collections.Generic.List<SearchPredicateDto>
            {
                new ExternalHitTagPredicateDto { Tag = "dmgTag" },
                new PropertyMatchDto
                {
                    ComponentType = typeof(HealthComponent),
                    PropertyPath  = nameof(HealthComponent.Value),
                    Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
                },
            },
        };
        mgr.AddBreakpoint(predicate);

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new HealthComponent { Value = 0 });

        mgr.OnExternalHit("dmgTag", entity);

        Assert.NotNull(firedBp);
    }

    // -------------------------------------------------------------------------
    // 4. Compound AND with ExternalHitTag + PropertyMatch does NOT fire when
    //    the property does not match
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalHitTag_InCompoundAnd_ValueNonZero_DoesNotFire()
    {
        var (mgr, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<HealthComponent>();

        Breakpoint? firedBp = null;
        mgr.OnBreakpointHit += (bp, _) => firedBp = bp;

        var predicate = new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new System.Collections.Generic.List<SearchPredicateDto>
            {
                new ExternalHitTagPredicateDto { Tag = "dmgTag" },
                new PropertyMatchDto
                {
                    ComponentType = typeof(HealthComponent),
                    PropertyPath  = nameof(HealthComponent.Value),
                    Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
                },
            },
        };
        mgr.AddBreakpoint(predicate);

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new HealthComponent { Value = 5 });

        mgr.OnExternalHit("dmgTag", entity);

        Assert.Null(firedBp);
    }

    // -------------------------------------------------------------------------
    // 5. Disabled breakpoint does not fire
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalHitTag_DisabledBreakpoint_DoesNotFire()
    {
        Breakpoint? firedBp = null;
        _manager.OnBreakpointHit += (bp, _) => firedBp = bp;

        var bpId = _manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "myTag" });
        _manager.SetEnabled(bpId, false);

        var entity = _liveRepo.CreateEntity();
        _manager.OnExternalHit("myTag", entity);

        Assert.Null(firedBp);
    }

    // -------------------------------------------------------------------------
    // 6. No matching tag registered: no pause (fallback removed by P11T6)
    // -------------------------------------------------------------------------

    [Fact]
    public void OnExternalHit_NoTagMatch_DoesNotPause()
    {
        // No breakpoint registered at all.
        int pauseChangedCount = 0;
        _manager.OnPauseStateChanged += _ => pauseChangedCount++;

        var entity = _liveRepo.CreateEntity();
        _manager.OnExternalHit("nonexistent-tag", entity);

        Assert.False(_manager.IsPaused);
        Assert.Equal(0, pauseChangedCount);
    }

    // -------------------------------------------------------------------------
    // 6b. P11T6: Tag match still pauses and rewinds after fallback removal
    // -------------------------------------------------------------------------

    [Fact]
    public void OnExternalHit_TagMatch_StillPausesAndRewinds()
    {
        int pauseChangedCount = 0;
        _manager.OnPauseStateChanged += fired => { if (fired) pauseChangedCount++; };

        _manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "hit-me" });

        _manager.OnExternalHit("hit-me", Fdp.Core.Entity.Null);

        Assert.True(_manager.IsPaused);
        Assert.Equal(1, pauseChangedCount);
    }

    // -------------------------------------------------------------------------
    // 7. PredicateCompiler compiles ExternalHitTagPredicateDto as always-false
    // -------------------------------------------------------------------------

    [Fact]
    public void ExternalHitTag_Compiler_ReturnsAlwaysFalse()
    {
        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var predicate = new ExternalHitTagPredicateDto { Tag = "anyTag" };

        var del = compiler.CompileComponentPredicate(predicate);

        var repo   = new EntityRepository();
        var entity = repo.CreateEntity();

        // Must return false regardless of entity state
        Assert.False(del(repo, entity));
    }
}
