using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

[ComponentId(231)]
file struct WatchTestComponent { public int Value; }

[Collection("ComponentRegistry")]
public sealed class WatchPersistenceTests
{
    // ── P9T3 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Watches_PersistAcrossRestart_StructureCompatible()
    {
        ComponentTypeRegistry.Clear();

        var path = Path.Combine(Path.GetTempPath(), $"watches_test_{Guid.NewGuid():N}.json");
        try
        {
            // Arrange: manager1 with 3 watch-flagged breakpoints.
            var (mgr1, _, _, _) = ManagerFactory.Create();
            for (int i = 0; i < 3; i++)
            {
                var id = mgr1.AddBreakpoint(
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(WatchTestComponent),
                        PropertyPath  = "Value",
                        Predicate     = new NumericPredicateDto { MinValue = i, MaxValue = i + 10 },
                    },
                    displayName: $"Watch{i}");
                // Mark as watch.
                mgr1.MarkAsWatch(id, true);
            }
            Assert.Equal(3, mgr1.AllBreakpoints.Count(b => b.IsWatch));

            // Act: save watches from mgr1, restore into mgr2 (simulates restart).
            mgr1.SaveWatches(path);

            var (mgr2, _, _, _) = ManagerFactory.Create();
            mgr2.LoadWatches(path);

            // Assert: 3 watches restored, none broken.
            var watches = mgr2.AllBreakpoints.Where(b => b.IsWatch).ToList();
            Assert.Equal(3, watches.Count);
            Assert.All(watches, bp => Assert.False(bp.IsBroken));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Watches_Restore_FailsGracefullyOnDriftedSchema()
    {
        ComponentTypeRegistry.Clear();

        var path = Path.Combine(Path.GetTempPath(), $"watches_drift_{Guid.NewGuid():N}.json");
        try
        {
            // Arrange: save a watch pointing to WatchTestComponent.
            var (mgr1, _, _, _) = ManagerFactory.Create();
            var id = mgr1.AddBreakpoint(
                new PropertyMatchDto
                {
                    ComponentType = typeof(WatchTestComponent),
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 100 },
                },
                displayName: "DriftedWatch");
            mgr1.MarkAsWatch(id, true);
            mgr1.SaveWatches(path);

            // Simulate schema drift: clear registry (component no longer registered).
            ComponentTypeRegistry.Clear();

            // Act: load into a fresh manager with cleared registry (compilation will fail).
            var liveRepo         = new EntityRepository();
            var preTickSnapshot  = new EntityRepository();
            var tc               = new MockDebugTimeController();
            var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
            var failingCompiler  = new AlwaysThrowCompiler();
            var mgr2 = new DataBreakpointManager(
                liveRepo, preTickSnapshot, snapshotProvider, tc,
                predicateCompiler: failingCompiler);

            // Should not throw.
            var ex = Record.Exception(() => mgr2.LoadWatches(path));
            Assert.Null(ex);

            // Assert: watch is present but marked broken (not silently discarded).
            var watches = mgr2.AllBreakpoints.Where(b => b.IsWatch).ToList();
            Assert.Single(watches);
            Assert.True(watches[0].IsBroken);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

// ---- Test helpers -----------------------------------------------------------

file sealed class AlwaysThrowCompiler : IPredicateCompiler
{
    public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto dto)
        => throw new InvalidOperationException("Schema drifted -- component not registered.");

    public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto dto) =>
        Array.Empty<Type>();
}
