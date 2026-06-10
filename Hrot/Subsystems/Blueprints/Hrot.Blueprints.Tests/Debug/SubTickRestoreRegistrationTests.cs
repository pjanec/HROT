using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Regression test for BF-01: scratch repo seeded with SyncFrom (no includeTransient)
/// fails to register recordable-but-not-snapshotable component types, causing
/// PlaybackSystem.ApplyChunkData to throw when a keyframe containing those types
/// is restored.
///
/// <para>The fix: BlueprintDebugSession.RestorePointerToScratch() now calls
/// SyncFrom(_liveRepo, includeTransient: true) so the scratch repo registers ALL
/// component types, which is a superset of the recordable types in the keyframe.</para>
///
/// ComponentId values 504..505 are reserved for this test file only.
/// </summary>
public sealed class SubTickRestoreRegistrationTests
{
    // ── Test component types ────────────────────────────────────────────────

    /// <summary>
    /// A recordable-but-NOT-snapshotable component (mirrors the real-world type 162 crash).
    /// DataPolicy.NoSnapshot excludes it from the snapshotable mask used by SyncFrom's
    /// default seeding, but FlightRecorder keyframes still capture it because it is recordable.
    /// </summary>
    [ComponentId(504)]
    [DataPolicy(DataPolicy.NoSnapshot)]
    private struct NoSnapshotProbe { public int V; }

    /// <summary>Normal int component — both recordable and snapshotable (default policy).</summary>
    [ComponentId(505)]
    private struct NormalInt { public int Value; }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a fresh EntityRepository with both NoSnapshotProbe and NormalInt registered.
    /// </summary>
    private static EntityRepository MakeRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NoSnapshotProbe>();
        repo.RegisterComponent<NormalInt>();
        return repo;
    }

    // ── Test: reproduce the recordable-but-not-snapshotable registration mismatch ──

    /// <summary>
    /// BF-01: scratch repo seeded WITHOUT includeTransient fails to register
    /// a [DataPolicy(DataPolicy.NoSnapshot)] component, causing RestoreTo to throw
    /// when the keyframe contains that component.
    ///
    /// <para>Steps:
    /// 1. Create repo with NoSnapshotProbe + NormalInt, create entity, set both values.
    /// 2. Record a node entry via SubTickSnapshotRecorder (keyframe contains both types).
    /// 3. BAD path: scratch seeded with SyncFrom(repo) — prove RestoreTo throws OR
    ///    prove the mask difference that causes the failure.
    /// 4. GOOD path: scratch seeded with SyncFrom(repo, includeTransient: true) —
    ///    RestoreTo must NOT throw, and the restored NormalInt value must be correct.
    /// </para>
    /// </summary>
    [Fact]
    public void RestoreTo_IncludeTransient_RegistersRecordableNonSnapshotableTypes()
    {
        // ── Arrange ────────────────────────────────────────────────────────────

        using var repo = MakeRepo();
        repo.Tick(); // advance to a real frame
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new NoSnapshotProbe { V = 42 });
        repo.AddComponent(entity, new NormalInt { Value = 99 });

        // Record a node entry — the keyframe captures both types (both are recordable).
        var sut = new SubTickSnapshotRecorder(capacity: 8);
        sut.BeginTick(repo);
        sut.RecordNodeEntry(repo, "n0");

        Assert.Equal(1, sut.Count);

        // ── Step 4: Reproduce the bug — mask-level proof ──────────────────────

        // The snapshotable mask (includeTransient: false) must NOT contain NoSnapshotProbe
        // because [DataPolicy(DataPolicy.NoSnapshot)] marks it non-snapshotable.
        // This is the root cause: SyncFrom(repo) uses this mask and skips the type.
        int noSnapshotId = ComponentType<NoSnapshotProbe>.ID;
        var maskExcludingTransient = repo.GetSnapshotableMask(includeTransient: false);
        Assert.False(maskExcludingTransient.IsSet(noSnapshotId),
            $"NoSnapshotProbe (id={noSnapshotId}) must NOT be in the snapshotable mask " +
            "with includeTransient:false — this is why SyncFrom(repo) skips it.");

        // ── Step 4 alt: Reproduce the bug — runtime proof ─────────────────────

        // When SyncFrom(repo) is called without includeTransient, the scratch repo
        // does NOT register NoSnapshotProbe. The keyframe contains NoSnapshotProbe data,
        // so RestoreTo → ApplyFrame → ApplyChunkData throws InvalidOperationException.
        using var bad = new EntityRepository();
        bad.SyncFrom(repo); // OLD seeding — omits NoSnapshotProbe
        Assert.Throws<InvalidOperationException>(() => sut.RestoreTo(0, bad));

        // ── Step 5: Prove the fix ─────────────────────────────────────────────

        // With includeTransient: true, SyncFrom uses GetSnapshotableMask(true) =
        // ComponentTypeRegistry.GetAllIds() = ALL registered types. This is a superset
        // of the recordable types in the keyframe, so all types are registered.
        using var good = new EntityRepository();
        good.SyncFrom(repo, includeTransient: true); // NEW seeding — includes ALL types

        // RestoreTo must NOT throw.
        sut.RestoreTo(0, good);

        // The restored entity must exist and the NormalInt value must be correct.
        var restoredEntity = new Entity(entity.Index, entity.Generation);
        Assert.True(good.IsAlive(restoredEntity),
            "Restored entity must be alive after includeTransient:true SyncFrom.");
        Assert.Equal(99, good.GetComponent<NormalInt>(restoredEntity).Value);

        // The NoSnapshotProbe component must also be present (registered + restored).
        Assert.True(good.HasComponent<NoSnapshotProbe>(restoredEntity),
            "NoSnapshotProbe must be present in the restored scratch when includeTransient:true is used.");
        Assert.Equal(42, good.GetComponent<NoSnapshotProbe>(restoredEntity).V);
    }
}
