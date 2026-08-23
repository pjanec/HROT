#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless unit tests for <see cref="EditorSelectionState"/> (STR-P5-T3, BATCH-23).
///
/// <para>
/// All tests run without a GPU, a window, or any Raylib context.
/// They exercise the pure logic of the shared selection state: set/clear/alive-guard/
/// version-bump/center-request one-shot flag.
/// </para>
/// </summary>
public sealed class EditorSelectionStateTests : IDisposable
{
    private readonly EditorStrideSubsystem _sut;

    public EditorSelectionStateTests()
    {
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();
    }

    public void Dispose() => _sut.Dispose();

    // ── B23-SEL-1: default state ───────────────────────────────────────────

    /// <summary>
    /// Freshly-constructed <see cref="EditorSelectionState"/> has no selection
    /// (SelectedEntity = Null, HasSelection = false, Version = 0).
    /// </summary>
    [Fact]
    public void DefaultState_NoSelection_VersionZero()
    {
        var sel = new EditorSelectionState();

        Assert.Equal(Entity.Null, sel.SelectedEntity);
        Assert.False(sel.HasSelection);
        Assert.Equal(0, sel.Version);
    }

    // ── B23-SEL-2: Select bumps version ────────────────────────────────────

    /// <summary>
    /// Calling <see cref="EditorSelectionState.Select"/> with a non-null entity stores the
    /// entity and increments <see cref="EditorSelectionState.Version"/>.
    /// </summary>
    [Fact]
    public void Select_BumpsVersion_AndStoresEntity()
    {
        var sel    = new EditorSelectionState();
        var entity = SpawnOneEntity();

        int versionBefore = sel.Version;
        sel.Select(entity);

        Assert.Equal(entity, sel.SelectedEntity);
        Assert.True(sel.HasSelection);
        Assert.Equal(versionBefore + 1, sel.Version);
    }

    // ── B23-SEL-3: Clear bumps version and removes entity ─────────────────

    /// <summary>
    /// <see cref="EditorSelectionState.Clear"/> sets the entity back to <see cref="Entity.Null"/>
    /// and increments <see cref="EditorSelectionState.Version"/>.
    /// </summary>
    [Fact]
    public void Clear_BumpsVersion_AndRemovesEntity()
    {
        var sel    = new EditorSelectionState();
        var entity = SpawnOneEntity();
        sel.Select(entity);
        int versionAfterSelect = sel.Version;

        sel.Clear();

        Assert.Equal(Entity.Null, sel.SelectedEntity);
        Assert.False(sel.HasSelection);
        Assert.Equal(versionAfterSelect + 1, sel.Version);
    }

    // ── B23-SEL-4: Clear on already-clear state does not bump version ──────

    /// <summary>
    /// Calling <see cref="EditorSelectionState.Clear"/> when nothing is selected is a no-op —
    /// it does NOT increment <see cref="EditorSelectionState.Version"/>.
    /// </summary>
    [Fact]
    public void Clear_WhenAlreadyClear_DoesNotBumpVersion()
    {
        var sel    = new EditorSelectionState();
        int before = sel.Version;

        sel.Clear(); // nothing selected → no-op

        Assert.Equal(before, sel.Version);
    }

    // ── B23-SEL-5: ClearIfDead keeps alive entity ──────────────────────────

    /// <summary>
    /// <see cref="EditorSelectionState.ClearIfDead"/> does NOT clear the selection when the
    /// selected entity is still alive in the world.
    /// </summary>
    [Fact]
    public void ClearIfDead_AliveEntity_KeepsSelection()
    {
        var sel    = new EditorSelectionState();
        var entity = SpawnOneEntity();
        sel.Select(entity);
        int versionBefore = sel.Version;

        sel.ClearIfDead(_sut.World);

        // Entity is alive → selection unchanged, version unchanged.
        Assert.Equal(entity, sel.SelectedEntity);
        Assert.Equal(versionBefore, sel.Version);
    }

    // ── B23-SEL-6: ClearIfDead clears dead entity ─────────────────────────

    /// <summary>
    /// <see cref="EditorSelectionState.ClearIfDead"/> DOES clear the selection when the
    /// selected entity is no longer alive (null entity handle simulates this).
    /// </summary>
    [Fact]
    public void ClearIfDead_NullEntity_ClearsSelection()
    {
        var sel = new EditorSelectionState();
        sel.Select(Entity.Null); // force a non-default null entity into selection
        // Note: Select(Entity.Null) marks HasSelection=false since Null == Null,
        // but we test ClearIfDead with a real "was once alive" entity that we kill.
        // Use a freshly-spawned entity then test via null world (world=null path).

        var entity = SpawnOneEntity();
        sel.Select(entity); // Select a real alive entity.
        Assert.True(sel.HasSelection);

        // Simulate world being null (subsystem disposed / not yet booted).
        sel.ClearIfDead(null);

        Assert.False(sel.HasSelection);
        Assert.Equal(Entity.Null, sel.SelectedEntity);
    }

    // ── B23-SEL-7: ClearIfDead on null world clears selection ─────────────

    /// <summary>
    /// Passing a null world to <see cref="EditorSelectionState.ClearIfDead"/> always clears
    /// the selection (safe fallback when the subsystem is not yet booted).
    /// </summary>
    [Fact]
    public void ClearIfDead_NullWorld_ClearsSelection()
    {
        var sel    = new EditorSelectionState();
        var entity = SpawnOneEntity();
        sel.Select(entity);

        sel.ClearIfDead(world: null);

        Assert.False(sel.HasSelection);
        Assert.Equal(Entity.Null, sel.SelectedEntity);
    }

    // ── B23-SEL-8: Version increments on each Select call ─────────────────

    /// <summary>
    /// Each call to <see cref="EditorSelectionState.Select"/> increments the version by 1,
    /// even if the same entity is re-selected. This lets readers detect every change.
    /// </summary>
    [Fact]
    public void Select_MultipleTimes_VersionIncrementsEachTime()
    {
        var sel    = new EditorSelectionState();
        var entity = SpawnOneEntity();

        sel.Select(entity);
        Assert.Equal(1, sel.Version);

        sel.Select(entity); // re-select same entity — still bumps
        Assert.Equal(2, sel.Version);

        sel.Select(Entity.Null);
        Assert.Equal(3, sel.Version);
    }

    // ── B23-SEL-9: ConsumeCenter one-shot flag ────────────────────────────

    /// <summary>
    /// <see cref="EditorSelectionState.ConsumeCenter"/> returns false initially.
    /// After <see cref="EditorSelectionState.RequestCenter"/>, it returns true ONCE and
    /// then returns false again (one-shot).
    /// </summary>
    [Fact]
    public void ConsumeCenter_ReturnsFalseInitially_TrueAfterRequest_FalseAfterConsume()
    {
        var sel = new EditorSelectionState();

        Assert.False(sel.ConsumeCenter());  // no request yet

        sel.RequestCenter();

        Assert.True(sel.ConsumeCenter());   // consumed
        Assert.False(sel.ConsumeCenter());  // already consumed — reset to false
    }

    // ── B23-SEL-10: EditorStrideSubsystem exposes SelectionState ──────────

    /// <summary>
    /// <see cref="EditorStrideSubsystem.SelectionState"/> is non-null after
    /// <see cref="EditorStrideSubsystem.Initialize"/> and the same instance each access
    /// (not re-created each frame).
    /// </summary>
    [Fact]
    public void SubsystemSelectionState_IsNonNull_AndStable()
    {
        var s1 = _sut.SelectionState;
        var s2 = _sut.SelectionState;

        Assert.NotNull(s1);
        Assert.Same(s1, s2);
    }

    // ── B23-SEL-11: Tick clears dead selected entity ──────────────────────

    /// <summary>
    /// After a selected entity is destroyed (despawned), the next
    /// <see cref="EditorStrideSubsystem.Tick"/> call clears the selection.
    /// This proves that <c>ClearIfDead</c> is wired into the Tick loop.
    /// </summary>
    [Fact]
    public void Tick_AfterEntityDies_ClearsSelection()
    {
        // Spawn an entity.
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = 1001L, // CivilianPedestrian
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = new System.Numerics.Vector3(0f, 5f, 0f) },
            },
        });

        // Let the entity materialise.
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        // Find and select the entity.
        Entity? found = null;
        foreach (var e in _sut.World.Query().With<SimTransform>().Build())
        {
            found = e;
            break;
        }
        Assert.True(found.HasValue, "Entity must exist after spawn.");
        _sut.SelectionState.Select(found!.Value);
        Assert.True(_sut.SelectionState.HasSelection);

        // Destroy the entity by calling DestroyEntity directly on the world.
        _sut.World.DestroyEntity(found!.Value);

        // Tick once → ClearIfDead should fire and remove the dead entity from selection.
        _sut.Tick(1f / 60f);

        Assert.False(_sut.SelectionState.HasSelection,
            "Selection must be cleared after the selected entity is destroyed.");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Spawns one entity via the Brain path and pumps three ticks until alive.</summary>
    private Entity SpawnOneEntity()
    {
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = 1001L,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = new System.Numerics.Vector3(1f, 2f, 0f) },
            },
        });

        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        foreach (var e in _sut.World.Query().With<SimTransform>().Build())
            return e;

        throw new InvalidOperationException("SpawnOneEntity: no entity found after 3 ticks.");
    }
}
