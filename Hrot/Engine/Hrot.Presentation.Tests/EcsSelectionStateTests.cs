using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Selection;
using Hrot.ScenarioEditor.Systems;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// <b><c>UXI-11</c> slice <c>S-1</c> — <see cref="EcsSelectionState"/>, the view over the one truth.</b>
///
/// <para>📄 Acceptance cases from <c>docs/UX/UX_Feature_Selection.md</c> §3: <b>11.1</b> (set through
/// the view is visible in the component — <em>the desync regression guard</em>), <b>11.2</b> (a
/// component change is visible through the view), <b>11.3</b> (exactly one primary after any
/// selecting operation), <b>11.9</b> (despawning the primary leaves no stale primary).</para>
///
/// <para>⭐⭐ <b>Why 11.1/11.2 are the load-bearing pair.</b> 🔴 Measured <c>2026-09-20</c>: the editor
/// and CGF each held a <c>DefaultSelectionState</c> hash set while <c>SelectionInteractionSystem</c>
/// wrote the component. Both directions of this pair were FALSE on those hosts, and nothing said so —
/// the divergence was carried as a comment in <c>ScenarioMissionView</c> rather than as a rail.</para>
/// </summary>
public sealed class EcsSelectionStateTests
{
    private readonly EntityRepository  _world;
    private readonly EcsSelectionState _view;
    private long _nextNetId = 9_100L;

    public EcsSelectionStateTests()
    {
        _world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_world);
        _world.RegisterComponent<SelectionState>();
        _view = new EcsSelectionState(_world);
    }

    private Entity NewEntity(bool withSelectionComponent = true)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, default(SimTransform));
        _world.AddComponent(e, new NetworkIdentity { Value = _nextNetId++ });
        if (withSelectionComponent) _world.AddComponent(e, new SelectionState());
        return e;
    }

    private SelectionState ComponentOf(Entity e) => _world.GetComponent<SelectionState>(e);

    // ── 11.1 — through the view, into the component ──────────────────────────

    /// <summary>⭐⭐⭐ <b>11.1</b> — the guard against the two-store split coming back.</summary>
    [Fact]
    public void PrimarySetThroughTheViewIsVisibleInTheComponent()
    {
        var e = NewEntity();

        _view.PrimarySelected = e;

        Assert.True(ComponentOf(e).IsSelected);
        Assert.True(ComponentOf(e).IsPrimarySelection);
    }

    // ── 11.2 — through the component, into the view ──────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>11.2</b> — and this is the direction a hash set could never satisfy: the write goes
    /// straight to the world, bypassing the view entirely, exactly as a map click does.
    /// </summary>
    [Fact]
    public void AComponentChangeIsVisibleThroughTheView()
    {
        var e = NewEntity();

        _world.SetComponent(e, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        Assert.True(_view.IsSelected(e));
        Assert.Equal(e, _view.PrimarySelected);
        Assert.Equal(new[] { e }, _view.SelectedEntities.ToArray());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The store-collapse gate, stated as behaviour.</b> A map click reaches
    /// <see cref="SelectionInteractionSystem"/>, which writes the component — and the host's view must
    /// see it. 🔴 On <c>DefaultSelectionState</c> this was false, which is precisely why the Mission
    /// Editor did not follow the map.
    /// </summary>
    [Fact]
    public void WhatTheInteractionSystemSelectsIsWhatTheViewReports()
    {
        var system = new SelectionInteractionSystem(_world, _world.Bus);
        var e      = NewEntity();

        system.ClearAllSelections();
        Assert.Empty(_view.SelectedEntities);

        _world.SetComponent(e, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        Assert.Equal(e, _view.PrimarySelected);
    }

    // ── 11.3 — exactly one primary ───────────────────────────────────────────

    [Fact]
    public void SetMultipleLeavesExactlyOnePrimary()
    {
        var a = NewEntity();
        var b = NewEntity();
        var c = NewEntity();

        _view.SetMultiple(new[] { a, b, c });

        Assert.Equal(3, _view.SelectedEntities.Count);
        Assert.Single(new[] { a, b, c }.Where(e => ComponentOf(e).IsPrimarySelection));
        Assert.Equal(a, _view.PrimarySelected);
    }

    [Fact]
    public void AddPromotesTheNewEntityAndDemotesTheIncumbentPrimary()
    {
        var a = NewEntity();
        var b = NewEntity();

        _view.PrimarySelected = a;
        _view.Add(b);

        Assert.Equal(2, _view.SelectedEntities.Count);
        Assert.True(ComponentOf(a).IsSelected);
        Assert.False(ComponentOf(a).IsPrimarySelection);
        Assert.True(ComponentOf(b).IsPrimarySelection);
        Assert.Equal(b, _view.PrimarySelected);
    }

    /// <summary>
    /// ⚠ Removing the primary must leave a primary behind while anything is still selected — a
    /// selection with none violates 11.3 just as much as one with two.
    /// </summary>
    [Fact]
    public void RemovingThePrimaryPromotesAnotherSelectedEntity()
    {
        var a = NewEntity();
        var b = NewEntity();
        _view.SetMultiple(new[] { a, b });

        _view.Remove(_view.PrimarySelected!.Value);

        Assert.Single(_view.SelectedEntities);
        Assert.NotNull(_view.PrimarySelected);
        Assert.True(ComponentOf(_view.PrimarySelected!.Value).IsPrimarySelection);
    }

    [Fact]
    public void RemovingTheLastSelectedEntityLeavesNoPrimary()
    {
        var a = NewEntity();
        _view.PrimarySelected = a;

        _view.Remove(a);

        Assert.Empty(_view.SelectedEntities);
        Assert.Null(_view.PrimarySelected);
    }

    /// <summary>⚠ The long-standing contract: assigning primary REPLACES the selection.</summary>
    [Fact]
    public void AssigningPrimaryReplacesTheWholeSelection()
    {
        var a = NewEntity();
        var b = NewEntity();
        var c = NewEntity();
        _view.SetMultiple(new[] { a, b });

        _view.PrimarySelected = c;

        Assert.Equal(new[] { c }, _view.SelectedEntities.ToArray());
        Assert.False(ComponentOf(a).IsSelected);
        Assert.False(ComponentOf(b).IsSelected);
    }

    [Fact]
    public void ClearEmptiesTheSelectionAndThePrimary()
    {
        _view.SetMultiple(new[] { NewEntity(), NewEntity() });

        _view.Clear();

        Assert.Empty(_view.SelectedEntities);
        Assert.Null(_view.PrimarySelected);
    }

    /// <summary>⭐ The component is added on demand — an entity need not carry it to be selectable.</summary>
    [Fact]
    public void SelectingAnEntityWithoutTheComponentAddsIt()
    {
        var e = NewEntity(withSelectionComponent: false);
        Assert.False(_world.HasComponent<SelectionState>(e));

        _view.PrimarySelected = e;

        Assert.True(_world.HasComponent<SelectionState>(e));
        Assert.True(_view.IsSelected(e));
    }

    // ── 11.9 — no stale primary after a despawn ──────────────────────────────

    [Fact]
    public void DespawningThePrimaryLeavesNoStalePrimary()
    {
        var a = NewEntity();
        var b = NewEntity();
        _view.SetMultiple(new[] { a, b });
        var primary = _view.PrimarySelected!.Value;

        _world.DestroyEntity(primary);

        Assert.DoesNotContain(primary, _view.SelectedEntities);
        Assert.NotEqual(primary, _view.PrimarySelected);
        Assert.False(_view.IsSelected(primary));
    }

    // ── Version — the change token the 2-D↔3-D sync polls ────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The win S-1 pays out for free.</b> <c>EditorStrideSubsystem.SyncSelection2D3D</c> polls
    /// <c>Selection2DVersion</c>. 🔴 On the hash set it bumped only when editor code assigned through
    /// the view ⇒ a MAP click never reached the 3-D view. Deriving it from the observed ECS truth
    /// makes that work without touching the sync.
    /// </summary>
    [Fact]
    public void VersionBumpsForAChangeWrittenStraightToTheComponent()
    {
        var e   = NewEntity();
        int was = _view.Version;

        _world.SetComponent(e, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        Assert.NotEqual(was, _view.Version);
    }

    /// <summary>⚠ And it must be STABLE when nothing changed, or the sync bounces every frame.</summary>
    [Fact]
    public void VersionIsStableAcrossRepeatedReadsWithNoChange()
    {
        _view.PrimarySelected = NewEntity();
        int v = _view.Version;

        Assert.Equal(v, _view.Version);
        Assert.Equal(v, _view.Version);
    }

    /// <summary>
    /// ⭐⭐ Two views over one world can never disagree — the property that makes it safe for the host
    /// and <see cref="SelectionInteractionSystem"/> to hold separate instances. ⛔ It is the property
    /// the two hash sets did not have.
    /// </summary>
    [Fact]
    public void TwoViewsOverOneWorldReportTheSameSelection()
    {
        var other = new EcsSelectionState(_world);
        var e     = NewEntity();

        _view.PrimarySelected = e;

        Assert.Equal(e, other.PrimarySelected);
        Assert.True(other.IsSelected(e));
        Assert.Equal(_view.SelectedEntities.ToArray(), other.SelectedEntities.ToArray());
    }

    /// <summary>
    /// ⚠ The query is built once in the constructor. It must still see entities created afterwards —
    /// <c>EntityEnumerator</c> snapshots <c>MaxIssuedIndex</c> per <c>foreach</c>, not at
    /// <c>Build()</c>. ⛔ If that ever changes, every selection made after startup vanishes from the
    /// view, silently.
    /// </summary>
    [Fact]
    public void TheCachedQuerySeesEntitiesCreatedAfterTheViewWasBuilt()
    {
        var late = NewEntity();

        _view.PrimarySelected = late;

        Assert.Equal(new[] { late }, _view.SelectedEntities.ToArray());
    }

    /// <summary>⭐ Hover is view-local on purpose: no component backs it.</summary>
    [Fact]
    public void HoveredEntityIsViewLocalAndDoesNotTouchTheComponent()
    {
        var e = NewEntity();

        _view.HoveredEntity = e;

        Assert.Equal(e, _view.HoveredEntity);
        Assert.False(ComponentOf(e).IsSelected);
        Assert.Empty(_view.SelectedEntities);
    }

    /// <summary>⭐ The interface is what consumers hold — every mutator must be reachable through it.</summary>
    [Fact]
    public void TheMutatorsAreReachableThroughTheInterface()
    {
        ISelectionState view = _view;
        var a = NewEntity();
        var b = NewEntity();

        view.Add(a);
        view.Add(b);
        view.Remove(a);
        Assert.Equal(new[] { b }, view.SelectedEntities.ToArray());

        view.SetMultiple(new List<Entity> { a, b });
        Assert.Equal(2, view.SelectedEntities.Count);

        view.Clear();
        Assert.Empty(view.SelectedEntities);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4049</c> (the coordinator's replay-UI-state handoff ①) — SELECTION IS UI STATE, NOT SIMULATION STATE: a recording carries
    /// no selection, and what the viewer selects during playback SURVIVES a seek (a keyframe clears the component tables; the store, a world
    /// singleton keyed by network id, refills the copy on the next frame). 🔒 User, 2026-10-25: <i>"selection is not the simulation state of an
    /// entity, it is a UI state having nothing to do with simulation and recording"</i>.
    /// 🔴 Red-proofs: <c>SelectionState</c> back to <c>NoScenario</c> only ⇒ the recorded A shows selected in playback; <c>Sync</c> not
    /// rewriting from the store ⇒ B's selection is lost at the seek.
    /// </summary>
    [Fact]
    public void CE4049_ARecordingCarriesNoSelection_AndTheViewersSelectionSurvivesASeek()
    {
        string path = System.IO.Path.GetTempFileName() + ".fdp";
        try
        {
            var a = NewEntity();
            var b = NewEntity();
            long netA = _world.GetComponentRO<NetworkIdentity>(a).Value, netB = _world.GetComponentRO<NetworkIdentity>(b).Value;
            _view.PrimarySelected = a;                                                                  // selected while recording
            Assert.Equal(netA, SelectionStore.Peek(_world)!.Primary);
            using (var recorder = new Fdp.Core.FlightRecorder.AsyncRecorder(path))
            {
                _world.Tick();
                recorder.CaptureKeyframe(_world, 100_000L, blocking: true, eventBus: _world.Bus);
                uint prev = _world.GlobalVersion - 1;
                _world.Bus.SwapBuffers();
                _world.Tick();
                recorder.CaptureFrame(_world, prev, 200_000L, blocking: true, eventBus: _world.Bus);
            }

            using var sandbox = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(sandbox);
            sandbox.RegisterComponent<SelectionState>();
            var viewer = new EcsSelectionState(sandbox);
            using var playback = new Fdp.Core.FlightRecorder.PlaybackController(path);
            Assert.True(playback.StepForward(sandbox));                                                // frame 0 (a keyframe)
            EcsSelectionState.Sync(sandbox);
            Assert.Empty(viewer.SelectedEntities);                                                     // the recording holds no selection

            var bInPlayback = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(sandbox, netB);
            viewer.PrimarySelected = bInPlayback;                                                      // the viewer selects B
            Assert.True(playback.StepForward(sandbox));                                                // frame 1
            playback.SeekToFrame(sandbox, 0);                                                          // a seek: a keyframe clears the tables
            EcsSelectionState.Sync(sandbox);                                                           // the next frame (SelectionRequestSystem)
            var afterSeek = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(sandbox, netB);
            Assert.Equal(afterSeek, viewer.PrimarySelected);
            Assert.True(sandbox.GetComponentRO<SelectionState>(afterSeek).IsPrimarySelection);
            var aInPlayback = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(sandbox, netA);
            Assert.False(viewer.IsSelected(aInPlayback));                                              // and never the recorded A
        }
        finally
        {
            try { System.IO.File.Delete(path); System.IO.File.Delete(path + ".meta.json"); } catch { }
        }
    }

    /// <summary>⭐ <c>CE-4049</c> — no selection or menu type is recorded: the per-entity copy is <c>NoReplay</c>, the store and the IG's
    /// right-click menu are <c>Transient</c>.</summary>
    [Fact]
    public void CE4049_NoSelectionOrMenuType_IsRecorded()
    {
        static Fdp.Core.DataPolicy PolicyOf(System.Type t) => ((Fdp.Core.DataPolicyAttribute)System.Attribute.GetCustomAttribute(t, typeof(Fdp.Core.DataPolicyAttribute))!).Policy;
        Assert.True((PolicyOf(typeof(SelectionState)) & Fdp.Core.DataPolicy.NoReplay) != 0);
        Assert.Equal(Fdp.Core.DataPolicy.Transient, PolicyOf(typeof(SelectionStore)));
    }

    /// <summary>
    /// ⭐ The coordinator's replay-UI-state handoff ② — the UI COMMANDS are not in a recording: select, centre, rename, tool, shortcut and
    /// world reset, recorded in a frame and played back WITH an event bus (the worst case: every player that passes one would re-fire them),
    /// arrive as nothing. 📐 Measured before the change (this rail, red): all six were recorded and re-injected. In production none re-fired
    /// yet — the cluster replay passes no bus (<c>ReplayModule</c>), the Replay Browser injects into its context's own bus, not the world's
    /// (<c>ReplayBrowserContext.SandboxBus</c>), a dry-run exit restores no events (<c>RestoreOptions.DryRunExit</c>) — but every recording carried them.
    /// </summary>
    [Fact]
    public void ReplayUiState2_TheUiCommands_AreNotInARecording()
    {
        string path = System.IO.Path.GetTempFileName() + ".fdp";
        try
        {
            var a = NewEntity();
            using (var recorder = new Fdp.Core.FlightRecorder.AsyncRecorder(path))
            {
                _world.Tick();
                recorder.CaptureKeyframe(_world, 100_000L, blocking: true, eventBus: _world.Bus);
                uint prev = _world.GlobalVersion - 1;
                _world.Bus.SwapBuffers();
                _world.Bus.Publish(new Hrot.Common.Events.SelectEntityCommand { NetworkId = 9_100L });
                _world.Bus.Publish(new Hrot.Common.Events.CenterOnEntityCommand { NetworkId = 9_100L });
                _world.Bus.Publish(new Hrot.Common.Events.OpenRenameDialogCommand { NetworkId = 9_100L });
                _world.Bus.Publish(new Hrot.Common.Events.ActivateEditorToolEvent(default));
                _world.Bus.Publish(default(Hrot.Common.Events.WorldResetEvent));
                _world.Bus.Publish(default(Hrot.Common.Interactions.MapShortcutKeyEvent));
                _world.Tick();
                recorder.CaptureFrame(_world, prev, 200_000L, blocking: true, eventBus: _world.Bus);
            }
            using var sandbox = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(sandbox);
            var bus = new FdpEventBus();
            using var playback = new Fdp.Core.FlightRecorder.PlaybackController(path) { EventBus = bus };
            Assert.True(playback.StepForward(sandbox));
            Assert.True(playback.StepForward(sandbox));
            Assert.Equal((0, 0, 0, 0, 0, 0),
                (bus.Read<Hrot.Common.Events.SelectEntityCommand>().Length, bus.Read<Hrot.Common.Events.CenterOnEntityCommand>().Length,
                 bus.Read<Hrot.Common.Events.OpenRenameDialogCommand>().Length, bus.Read<Hrot.Common.Events.ActivateEditorToolEvent>().Length,
                 bus.Read<Hrot.Common.Events.WorldResetEvent>().Length, bus.Read<Hrot.Common.Interactions.MapShortcutKeyEvent>().Length));
        }
        finally
        {
            try { System.IO.File.Delete(path); System.IO.File.Delete(path + ".meta.json"); } catch { }
        }
    }

    /// <summary>⭐ CE-3327 RB-7 — a view over the world a host SHOWS (the Replay Browser switches it under one map): it reads and writes
    /// whichever world the provider names now, never the one it was built over.</summary>
    [Fact]
    public void RB7_AViewOverAWorldProvider_FollowsTheShownWorld()
    {
        using var other = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(other);
        other.RegisterComponent<SelectionState>();
        EntityRepository shown = _world;
        var view = new EcsSelectionState(() => shown);

        var inFirst = NewEntity();
        view.PrimarySelected = inFirst;
        Assert.True(ComponentOf(inFirst).IsPrimarySelection);

        shown = other;
        Assert.Null(view.PrimarySelected);                                    // the shown world has no selection yet
        var inOther = other.CreateEntity();
        other.AddComponent(inOther, new NetworkIdentity { Value = 9_900L });
        other.AddComponent(inOther, new SelectionState());
        view.PrimarySelected = inOther;
        Assert.True(other.GetComponentRO<SelectionState>(inOther).IsPrimarySelection);
        Assert.Equal(inOther, view.PrimarySelected);

        shown = _world;
        Assert.Equal(inFirst, view.PrimarySelected);                          // and back
    }
}
