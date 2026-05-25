using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---------------------------------------------------------------------------
// ReentrancyTests  (UBP-P11T4)
// ---------------------------------------------------------------------------

/// <summary>
/// Tests for the re-entrancy guard added to <see cref="DataBreakpointManager.OnHit"/>.
/// When the manager is already paused, subsequent <c>OnHit</c> calls in the same tick
/// must be dropped silently (first hit wins).
/// </summary>
[Collection("ComponentRegistry")]
public sealed class ReentrancyTests
{
    // ------------------------------------------------------------------ //
    // Test 1: second OnHit in same tick does not overwrite PostTickSnapshot
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The re-entrancy guard must prevent a second call to <c>OnHit</c> from
    /// overwriting the <c>PostTickSnapshot</c> captured on the first hit.
    /// </summary>
    [Fact]
    public void OnHit_SecondHitInSameTick_DoesNotOverwritePostTickSnapshot()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, tc) = ManagerFactory.Create();

        liveRepo.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 100 });

        // Register two breakpoints.
        var idA = manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "tagA" },
                                        displayName: "bpA");
        var idB = manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "tagB" },
                                        displayName: "bpB");
        var bpA = manager.AllBreakpoints.First(b => b.Id == idA);
        var bpB = manager.AllBreakpoints.First(b => b.Id == idB);

        // First hit: pauses and captures PostTickSnapshot.
        manager.OnHit(bpA, entity);
        Assert.True(manager.IsPaused);

        // Record the snapshot version after the first hit.
        uint versionAfterFirst = manager.PostTickSnapshot.GlobalVersion;

        // Mutate the live repo so a second SyncFrom would yield a different version.
        liveRepo.Tick();

        // Second hit: must be dropped by the re-entrancy guard.
        manager.OnHit(bpB, Fdp.Core.Entity.Null);

        // PostTickSnapshot version must be unchanged (second hit was dropped).
        Assert.Equal(versionAfterFirst, manager.PostTickSnapshot.GlobalVersion);
        Assert.True(manager.IsPaused);
        // Time controller must have been paused only once.
        Assert.Equal(1, tc.PauseRequestCount);
    }

    // ------------------------------------------------------------------ //
    // Test 2: EvaluateStatefulBreakpoints with multiple hits pauses only once
    // ------------------------------------------------------------------ //

    /// <summary>
    /// When <c>EvaluateStatefulBreakpoints</c> produces multiple hits in one call
    /// (e.g. 3 structural BPs all fire), the re-entrancy guard must ensure that
    /// only the first hit actually pauses — subsequent <c>OnHit</c> calls are no-ops.
    /// </summary>
    [Fact]
    public void EvaluateStatefulBreakpoints_MultipleHits_PausesOnce()
    {
        ComponentTypeRegistry.Clear();
        var repo     = new EntityRepository();
        var preTick  = new EntityRepository();
        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTick);
        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var mgr      = new DataBreakpointManager(repo, preTick, provider, tc, compiler);

        repo.RegisterComponent<TestHealth>();

        // Register 3 structural breakpoints all watching TestHealth Added.
        for (int i = 0; i < 3; i++)
        {
            mgr.AddBreakpoint(new StructuralPredicateDto
            {
                ComponentType        = typeof(TestHealth),
                ModificationType     = StructuralModification.Added,
                AuthorityRequirement = AuthorityRequirement.AnyAuthority
            }, displayName: $"StructBP{i}");
        }

        // Add an entity so all 3 structural trackers will detect a new component.
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new TestHealth { Current = 50 });

        // Track OnPauseStateChanged events.
        int pauseChangedCount = 0;
        mgr.OnPauseStateChanged += _ => pauseChangedCount++;

        // EvaluateStatefulBreakpoints fires all 3 trackers; only the first OnHit should pause.
        mgr.EvaluateStatefulBreakpoints(repo);

        Assert.True(mgr.IsPaused);
        Assert.Equal(1, tc.PauseRequestCount);
        Assert.Equal(1, pauseChangedCount);
    }
}
