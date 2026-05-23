using System;
using Fdp.Core;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Boot-level integration tests for Editor subsystems using the headless
/// <see cref="EditorHarness"/> (no CycloneDDS, no display/raylib).
///
/// <para>Coverage goals:</para>
/// <list type="bullet">
///   <item>Verify the ECS-512 hot/cold entity index initialises correctly.</item>
///   <item>Verify entity lifecycle (create, read mask, destroy, re-create) through
///     the full subsystem stack.</item>
///   <item>Verify the Kernel simulation pipeline remains stable across many frames.</item>
/// </list>
/// </summary>
public sealed class EditorSubsystemBootTests : IDisposable
{
    private readonly EditorHarness _h;

    public EditorSubsystemBootTests()
    {
        _h = new EditorHarness();
    }

    public void Dispose() => _h.Dispose();

    // ── Boot assertions ──────────────────────────────────────────────────────

    /// <summary>
    /// The harness exposes a live Repo, an event Bus, and a Kernel.
    /// Verified here so that every subsequent test can rely on these properties.
    /// </summary>
    [Fact]
    public void Harness_CoreProperties_AreNotNull()
    {
        Assert.NotNull(_h.Repo);
        Assert.NotNull(_h.Bus);
        Assert.NotNull(_h.Kernel);
        Assert.NotNull(_h.Editor);
        Assert.NotNull(_h.FileService);
    }

    /// <summary>
    /// The Repo starts with zero live entities; no ghost entities from
    /// subsystem initialisation should be left in the hot-mask table.
    /// </summary>
    [Fact]
    public void Repo_StartsEmpty_AfterHarnessInit()
    {
        Assert.Equal(0, _h.Repo.EntityCount);
    }

    /// <summary>
    /// The Kernel can advance 60 frames without throwing.
    /// This exercises every registered system in the simulation pipeline,
    /// including all systems added by the ECS-512 rewrite.
    /// </summary>
    [Fact]
    public void Kernel_PumpSixtyFrames_NoException()
    {
        _h.PumpFrames(60);
    }

    // ── ECS-512 hot/cold index ───────────────────────────────────────────────

    /// <summary>
    /// Entities freshly created in the repository expose an empty 512-bit
    /// component mask via the hot table.
    /// </summary>
    [Fact]
    public void CreateEntities_HotComponentMask_IsInitiallyEmpty()
    {
        var e0 = _h.Repo.CreateEntity();
        var e1 = _h.Repo.CreateEntity();

        Assert.Equal(2, _h.Repo.EntityCount);

        EntityIndex idx = _h.Repo.GetEntityIndex();
        ref BitMask512 mask0 = ref idx.GetComponentMask(e0.Index);
        ref BitMask512 mask1 = ref idx.GetComponentMask(e1.Index);

        Assert.True(mask0.IsEmpty(), "Newly created entity should have an empty component mask.");
        Assert.True(mask1.IsEmpty(), "Newly created entity should have an empty component mask.");
    }

    /// <summary>
    /// The cold metadata table records the same generation value as the
    /// Entity handle returned by CreateEntity.
    /// </summary>
    [Fact]
    public void EntityIndex_ColdMetadata_Generation_MatchesEntityHandle()
    {
        var e = _h.Repo.CreateEntity();
        EntityIndex idx = _h.Repo.GetEntityIndex();
        ref EntityMetadataCold meta = ref idx.GetMetadata(e.Index);

        Assert.Equal(e.Generation, meta.Generation);
    }

    // ── Entity lifecycle ─────────────────────────────────────────────────────

    /// <summary>
    /// Destroying an entity decrements EntityCount and makes the handle
    /// invalid according to IsAlive.
    /// </summary>
    [Fact]
    public void DestroyEntity_DecreasesCount_AndInvalidatesHandle()
    {
        var e = _h.Repo.CreateEntity();
        Assert.Equal(1, _h.Repo.EntityCount);

        _h.Repo.DestroyEntity(e);

        Assert.Equal(0, _h.Repo.EntityCount);
        Assert.False(_h.Repo.IsAlive(e), "Destroyed entity handle must be invalid.");
    }

    /// <summary>
    /// Recreating an entity slot produces a new, strictly-greater generation,
    /// invalidating the old handle while the new one is alive.
    /// </summary>
    [Fact]
    public void RecreateEntity_NewGeneration_OldHandleInvalid()
    {
        var e1 = _h.Repo.CreateEntity();
        _h.Repo.DestroyEntity(e1);
        var e2 = _h.Repo.CreateEntity();

        // Same slot should be reused.
        Assert.Equal(e1.Index, e2.Index);
        // Generation must have advanced.
        Assert.True(e2.Generation > e1.Generation,
            "Re-created entity must have a higher generation to invalidate old handles.");

        Assert.False(_h.Repo.IsAlive(e1), "Old handle must be dead after slot re-use.");
        Assert.True(_h.Repo.IsAlive(e2),  "New handle must be alive.");
    }

    // ── BitMask512 mask properties ───────────────────────────────────────────

    /// <summary>
    /// GetSnapshotableMask returns a non-empty BitMask512 after all component
    /// registries have run — confirming the 512-wide mask is populated.
    /// </summary>
    [Fact]
    public void GetSnapshotableMask_ReturnsBitMask512_NonEmpty()
    {
        BitMask512 mask = _h.Repo.GetSnapshotableMask();
        Assert.False(mask.IsEmpty(),
            "Snapshotable mask must be non-empty after HrotSharedComponentRegistry.RegisterAll.");
    }

    /// <summary>
    /// GetRecordableMask returns a non-empty BitMask512 after all component
    /// registries have run.
    /// </summary>
    [Fact]
    public void GetRecordableMask_ReturnsBitMask512_NonEmpty()
    {
        BitMask512 mask = _h.Repo.GetRecordableMask();
        Assert.False(mask.IsEmpty(),
            "Recordable mask must be non-empty after component registration.");
    }

    // ── Frame stability with entities present ────────────────────────────────

    /// <summary>
    /// Creating entities before pumping does not destabilise the subsystem
    /// pipeline. Exercises that all systems safely iterate an entity set.
    /// </summary>
    [Fact]
    public void CreateEntities_ThenPumpFrames_NoException()
    {
        for (int i = 0; i < 20; i++)
            _h.Repo.CreateEntity();

        _h.PumpFrames(10);

        Assert.Equal(20, _h.Repo.EntityCount);
    }
}
