using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for NGS-1.2 / NGS-1.3 — SubTickSnapshotRecorder: capture ring + restore.
///
/// All assertions operate on REAL runtime values: restored component values,
/// per-entity results, managed-component values, SimulationTick frozen check,
/// and ring overflow signal. No string-presence or "object exists" assertions.
///
/// ComponentId values 501..503 are reserved for this test file only.
/// </summary>
public sealed class SubTickSnapshotRecorderTests
{
    // ── Test component types ────────────────────────────────────────────────

    /// <summary>Unmanaged integer component for basic counter/attribution tests.</summary>
    [ComponentId(501)]
    private struct SnapshotInt { public int Value; }

    /// <summary>Second unmanaged component for multi-entity tests.</summary>
    [ComponentId(502)]
    private struct SnapshotInt2 { public int Value; }

    /// <summary>Managed component for managed-capture test.</summary>
    [ComponentId(503)]
    private sealed class SnapshotManagedString
    {
        public string Value = string.Empty;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Create a fresh EntityRepository with SnapshotInt registered.</summary>
    private static EntityRepository MakeRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SnapshotInt>();
        return repo;
    }

    /// <summary>Create a fresh EntityRepository with both SnapshotInt and SnapshotInt2 registered.</summary>
    private static EntityRepository MakeRepoTwo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SnapshotInt>();
        repo.RegisterComponent<SnapshotInt2>();
        return repo;
    }

    /// <summary>Create a fresh EntityRepository with SnapshotInt and SnapshotManagedString registered.</summary>
    private static EntityRepository MakeRepoManaged()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SnapshotInt>();
        repo.RegisterManagedComponent<SnapshotManagedString>();
        return repo;
    }

    // ── Test 1: Counter semantics (the pin / 5-6-7) ─────────────────────────

    /// <summary>
    /// NGS-1.3: counter semantics.
    /// Setup: value=5; BeginTick; RecordNodeEntry("n0"); mutate→6; RecordNodeEntry("n1");
    ///        mutate→7; RecordNodeEntry("n2").
    /// Assert: restore(0)=5, restore(1)=6, restore(2)=7.
    ///
    /// This is the canonical off-by-one guard: each restore shows state AS-OF entering that node.
    /// </summary>
    [Fact]
    public void RestoreTo_CounterSemantics_567()
    {
        using var repo = MakeRepo();
        repo.Tick(); // advance to a real frame
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SnapshotInt { Value = 5 });

        var sut = new SubTickSnapshotRecorder(capacity: 8);

        // Snapshot the baseline state (value=5).
        sut.BeginTick(repo);

        // n0 entry (before n0 writes anything)
        sut.RecordNodeEntry(repo, "n0");
        // n0 runs and mutates value → 6
        repo.SetComponent(entity, new SnapshotInt { Value = 6 });

        // n1 entry (before n1 writes anything)
        sut.RecordNodeEntry(repo, "n1");
        // n1 runs and mutates value → 7
        repo.SetComponent(entity, new SnapshotInt { Value = 7 });

        // n2 entry (before n2 writes anything)
        sut.RecordNodeEntry(repo, "n2");

        Assert.Equal(3, sut.Count);
        Assert.Equal("n0", sut.NodeIdAt(0));
        Assert.Equal("n1", sut.NodeIdAt(1));
        Assert.Equal("n2", sut.NodeIdAt(2));

        // Restore index 0: state before n0 ran = 5
        using var scratch0 = MakeRepo();
        sut.RestoreTo(0, scratch0);
        Assert.Equal(5, scratch0.GetComponent<SnapshotInt>(entity).Value);

        // Restore index 1: state before n1 ran = 6
        using var scratch1 = MakeRepo();
        sut.RestoreTo(1, scratch1);
        Assert.Equal(6, scratch1.GetComponent<SnapshotInt>(entity).Value);

        // Restore index 2: state before n2 ran = 7
        using var scratch2 = MakeRepo();
        sut.RestoreTo(2, scratch2);
        Assert.Equal(7, scratch2.GetComponent<SnapshotInt>(entity).Value);
    }

    // ── Test 2: Attribution guard (off-by-one) ───────────────────────────────

    /// <summary>
    /// NGS-1.3: attribution correctness.
    /// A mutation performed AFTER RecordNodeEntry("nK") and BEFORE RecordNodeEntry("nK+1")
    /// must appear when restoring nK+1 but NOT when restoring nK.
    /// </summary>
    [Fact]
    public void RestoreTo_Attribution_MutationAfterNKAppearsAtNKPlus1Only()
    {
        using var repo = MakeRepo();
        repo.Tick();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SnapshotInt { Value = 100 });

        var sut = new SubTickSnapshotRecorder(capacity: 8);
        sut.BeginTick(repo);

        // n0 entry
        sut.RecordNodeEntry(repo, "n0");
        // Mutation between n0 and n1 (attributed to n0's execution)
        repo.SetComponent(entity, new SnapshotInt { Value = 200 });

        // n1 entry
        sut.RecordNodeEntry(repo, "n1");

        // Restore to n0: must NOT see value 200 (mutation happened AFTER n0's entry point)
        using var scratch0 = MakeRepo();
        sut.RestoreTo(0, scratch0);
        Assert.Equal(100, scratch0.GetComponent<SnapshotInt>(entity).Value);

        // Restore to n1: MUST see value 200 (mutation was captured in delta for n1)
        using var scratch1 = MakeRepo();
        sut.RestoreTo(1, scratch1);
        Assert.Equal(200, scratch1.GetComponent<SnapshotInt>(entity).Value);
    }

    // ── Test 3: Whole-repo / multi-entity ───────────────────────────────────

    /// <summary>
    /// NGS-1.3: whole-repo capture.
    /// Two entities; mutate entity A at one node and entity B at the next.
    /// Restoring each index shows correct per-entity values for BOTH entities.
    /// </summary>
    [Fact]
    public void RestoreTo_MultiEntity_WholeRepoCapture()
    {
        using var repo = MakeRepoTwo();
        repo.Tick();

        var entityA = repo.CreateEntity();
        repo.AddComponent(entityA, new SnapshotInt  { Value = 10 });
        repo.AddComponent(entityA, new SnapshotInt2 { Value = 0  });

        var entityB = repo.CreateEntity();
        repo.AddComponent(entityB, new SnapshotInt  { Value = 0  });
        repo.AddComponent(entityB, new SnapshotInt2 { Value = 20 });

        var sut = new SubTickSnapshotRecorder(capacity: 8);
        sut.BeginTick(repo);

        // n0 entry — before any mutations
        sut.RecordNodeEntry(repo, "n0");
        // Node n0 mutates entity A's SnapshotInt (A changes, B unchanged)
        repo.SetComponent(entityA, new SnapshotInt { Value = 11 });

        // n1 entry — entity A has changed; entity B still 0
        sut.RecordNodeEntry(repo, "n1");
        // Node n1 mutates entity B's SnapshotInt2 (B changes, A unchanged)
        repo.SetComponent(entityB, new SnapshotInt2 { Value = 21 });

        // n2 entry — both entities mutated
        sut.RecordNodeEntry(repo, "n2");

        // Restore to n0: both entities at initial values
        using var scratch0 = MakeRepoTwo();
        sut.RestoreTo(0, scratch0);
        Assert.Equal(10, scratch0.GetComponent<SnapshotInt>(entityA).Value);
        Assert.Equal(0,  scratch0.GetComponent<SnapshotInt2>(entityA).Value);
        Assert.Equal(0,  scratch0.GetComponent<SnapshotInt>(entityB).Value);
        Assert.Equal(20, scratch0.GetComponent<SnapshotInt2>(entityB).Value);

        // Restore to n1: entity A mutated (11), entity B unchanged (20)
        using var scratch1 = MakeRepoTwo();
        sut.RestoreTo(1, scratch1);
        Assert.Equal(11, scratch1.GetComponent<SnapshotInt>(entityA).Value);
        Assert.Equal(0,  scratch1.GetComponent<SnapshotInt2>(entityA).Value);
        Assert.Equal(0,  scratch1.GetComponent<SnapshotInt>(entityB).Value);
        Assert.Equal(20, scratch1.GetComponent<SnapshotInt2>(entityB).Value);

        // Restore to n2: entity A (11) and entity B's SnapshotInt2 (21) both updated
        using var scratch2 = MakeRepoTwo();
        sut.RestoreTo(2, scratch2);
        Assert.Equal(11, scratch2.GetComponent<SnapshotInt>(entityA).Value);
        Assert.Equal(0,  scratch2.GetComponent<SnapshotInt2>(entityA).Value);
        Assert.Equal(0,  scratch2.GetComponent<SnapshotInt>(entityB).Value);
        Assert.Equal(21, scratch2.GetComponent<SnapshotInt2>(entityB).Value);
    }

    // ── Test 4: Managed component ────────────────────────────────────────────

    /// <summary>
    /// NGS-1.3: managed component capture.
    /// A managed string component mutated across two nodes restores correctly.
    /// Proves that managed-chunk capture (with per-node allocation) works end-to-end.
    /// </summary>
    [Fact]
    public void RestoreTo_ManagedComponent_RestoresCorrectly()
    {
        using var repo = MakeRepoManaged();
        repo.Tick();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SnapshotManagedString { Value = "alpha" });

        var sut = new SubTickSnapshotRecorder(capacity: 8);
        sut.BeginTick(repo);

        // n0 entry (before n0 writes)
        sut.RecordNodeEntry(repo, "n0");
        // n0 mutates managed component → "beta"
        repo.SetComponent(entity, new SnapshotManagedString { Value = "beta" });

        // n1 entry (before n1 writes)
        sut.RecordNodeEntry(repo, "n1");
        // n1 mutates managed component → "gamma"
        repo.SetComponent(entity, new SnapshotManagedString { Value = "gamma" });

        // n2 entry
        sut.RecordNodeEntry(repo, "n2");

        // Restore to n0: managed value = "alpha"
        using var scratch0 = MakeRepoManaged();
        sut.RestoreTo(0, scratch0);
        Assert.Equal("alpha", scratch0.GetComponent<SnapshotManagedString>(entity).Value);

        // Restore to n1: managed value = "beta"
        using var scratch1 = MakeRepoManaged();
        sut.RestoreTo(1, scratch1);
        Assert.Equal("beta", scratch1.GetComponent<SnapshotManagedString>(entity).Value);

        // Restore to n2: managed value = "gamma"
        using var scratch2 = MakeRepoManaged();
        sut.RestoreTo(2, scratch2);
        Assert.Equal("gamma", scratch2.GetComponent<SnapshotManagedString>(entity).Value);
    }

    // ── Test 5: SimulationTick frozen ────────────────────────────────────────

    /// <summary>
    /// NGS-1.2: SimulationTick must stay frozen across all RecordNodeEntry calls.
    /// GlobalVersion must advance by exactly (number of RecordNodeEntry calls) since
    /// each call bumps BumpMemoryVersion() once.
    /// </summary>
    [Fact]
    public void RecordNodeEntry_SimulationTickFrozen_GlobalVersionAdvances()
    {
        using var repo = MakeRepo();
        repo.Tick(); // GV=2, ST=2
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SnapshotInt { Value = 1 });

        uint stBefore = repo.SimulationTick;
        uint gvBefore = repo.GlobalVersion;

        var sut = new SubTickSnapshotRecorder(capacity: 8);
        sut.BeginTick(repo); // records keyframe; GV should be unchanged by BeginTick

        const int nodeCount = 5;
        for (int i = 0; i < nodeCount; i++)
        {
            sut.RecordNodeEntry(repo, $"n{i}");
            // Each call to RecordNodeEntry bumps once → GV advances by 1 per call
        }

        // SimulationTick must be unchanged (frozen during sub-tick debug)
        Assert.Equal(stBefore, repo.SimulationTick);

        // GlobalVersion must have advanced by exactly nodeCount bumps
        // (BeginTick doesn't bump; each RecordNodeEntry bumps exactly once)
        Assert.Equal(gvBefore + (uint)nodeCount, repo.GlobalVersion);
    }

    // ── Test 6: Ring overflow ────────────────────────────────────────────────

    /// <summary>
    /// NGS-1.2: exceeding ring capacity drops oldest entries, increments DroppedFrameCount,
    /// and does NOT throw. The most recent entries are retained in insertion order.
    /// </summary>
    [Fact]
    public void RecordNodeEntry_RingOverflow_DropsOldestAndSignals()
    {
        const int cap = 4;
        using var repo = MakeRepo();
        repo.Tick();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SnapshotInt { Value = 0 });

        var sut = new SubTickSnapshotRecorder(capacity: cap);
        sut.BeginTick(repo);

        // Fill the ring exactly (no overflow yet)
        for (int i = 0; i < cap; i++)
        {
            sut.RecordNodeEntry(repo, $"n{i}");
            repo.SetComponent(entity, new SnapshotInt { Value = i + 1 });
        }
        Assert.Equal(cap, sut.Count);
        Assert.Equal(0, sut.DroppedFrameCount);

        // Record one more — this should overflow, drop n0, and signal
        sut.RecordNodeEntry(repo, "nOverflow");
        repo.SetComponent(entity, new SnapshotInt { Value = 99 });

        // Count stays at cap (oldest was dropped)
        Assert.Equal(cap, sut.Count);
        // DroppedFrameCount must be exactly 1 (the overflow signal)
        Assert.Equal(1, sut.DroppedFrameCount);
        // Oldest entry is now n1 (n0 was dropped)
        Assert.Equal("n1", sut.NodeIdAt(0));
        // Most recent entry is nOverflow
        Assert.Equal("nOverflow", sut.NodeIdAt(cap - 1));
    }

    // ── Test 7: BeginTick resets ring state ──────────────────────────────────

    /// <summary>
    /// Calling BeginTick a second time resets the recorder (count=0, dropped=0)
    /// so it is safe to reuse across ticks.
    /// </summary>
    [Fact]
    public void BeginTick_Reset_ClearsRingAndDroppedCount()
    {
        using var repo = MakeRepo();
        repo.Tick();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SnapshotInt { Value = 42 });

        var sut = new SubTickSnapshotRecorder(capacity: 4);
        sut.BeginTick(repo);
        sut.RecordNodeEntry(repo, "a");
        sut.RecordNodeEntry(repo, "b");
        sut.RecordNodeEntry(repo, "c");
        sut.RecordNodeEntry(repo, "d");
        sut.RecordNodeEntry(repo, "overflow"); // triggers a drop

        Assert.Equal(1, sut.DroppedFrameCount);
        Assert.Equal(4, sut.Count);

        // Advance the real frame clock and begin a new tick
        repo.Tick();
        sut.BeginTick(repo);

        // All state must be reset
        Assert.Equal(0, sut.Count);
        Assert.Equal(0, sut.DroppedFrameCount);
    }
}
