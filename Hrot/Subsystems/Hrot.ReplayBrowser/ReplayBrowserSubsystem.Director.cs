using System;
using System.IO;
using Fdp.Core;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.ReplayBrowser;

/// <summary>
/// ⭐ <c>CE-1303</c> P2a (docs/DESIGN_Director_Timeline_Editor.md §11m) — the ONE film director in the Replay Browser: the same
/// <see cref="DirectorPack"/> the editor attaches (CR-2, R-4068), over <see cref="ReplayDirectorHost"/> (CR-3). ⛔ This host builds no runner,
/// session or window of its own — it hands the pack its host, its Event Timeline, its window manager and layers, and ticks it.
/// </summary>
public sealed partial class ReplayBrowserSubsystem : IProvidesDirector
{
    private DirectorPack? _directorPack;

    /// <summary>The director — ⭐ CE-1303 P3: the process's debug HTTP host attaches it (<c>/director/*</c>, <c>/map/layers</c>).</summary>
    public DirectorPack? Director => _directorPack;

    /// <summary>This host's Event Timeline panel, which the pack gives the open take's tracks (test seam).</summary>
    internal Fdp.Presentation.Panels.Timeline.TimelinePanel EventTimeline => _eventTimeline;

    /// <summary>Attached once, at <see cref="Initialize"/>, on a windowed and a headless host alike.</summary>
    private void AttachDirector()
    {
        // ⭐ D8 (TE-1) — the takes of a recording live BESIDE it, keyed by the scenario it names; the bake's scenario hash is the recording's
        var takes = new TakeStore(() => DirectorTakesRoot, TakeStore.FindFilmsDirectory,
                                  scenario => PrimaryMetadata is { } m && m.ScenarioName == scenario ? m.ScenarioHash : null);
        var host = new ReplayDirectorHost(
            world:         () => _activeRepo,
            open:          () => ReplayState == Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay,
            playing:       () => IsPlaying,
            pause:         () => IsPlaying = false,
            simSeconds:    () => DirectorSimSeconds,
            moveTo:        MoveDirectorPlayhead,
            behaviourName: hash => _behaviorRegistry.TryGetName(hash, out var n) ? n : null,
            takes:         takes);
        _directorPack = DirectorPack.Attach(new DirectorPackHost(
            Host:          host,
            Wm:            () => _windowManager,
            Layers:        () => _gizmoBuffer is { } b ? Hrot.ScenarioEditor.Map.MapInteractionPack.LayerControlFor(b) : null,
            Headless:      _headless,
            Pilot:         () => PilotCamera.FromMap(_canvas?.Camera, _activeRepo, _inspectorState?.SelectedEntity),
            Timeline:      _eventTimeline,
            EventAt:       DirectorEventAt,     // ⭐ D10 — CR-6 over the shown recording's index
            TitleBarColor: TitleBarColor,
            Perspective:   "ReplayBrowser"));   // ⭐ D11
    }

    /// <summary>The primary node's recording meta (its scenario's name and hash), or null.</summary>
    private Fdp.Core.FlightRecorder.Metadata.RecordingMetadata? PrimaryMetadata
        => _manager != null && _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx) ? ctx.Playback?.Metadata : null;

    /// <summary>
    /// ⭐ D8 — the open recording's folder (its takes: <c>&lt;folder&gt;/&lt;scenario&gt;/takes/</c>); none open: an empty store. ⭐ <c>CE-1303</c> P2b (§11o S13,
    /// TE-1) — a KEPT exercise's folder first: <c>{shared}/exercises/&lt;id&gt;/</c> when it exists. ⚠ Measured: while a node still has its own (temp) copy
    /// of the recording, that copy is what opens (<c>RecordingGroupInventory</c>: "the local copy wins"), so "beside the recording" alone would put the
    /// takes in the temp folder that the kept exercise outlives.
    /// </summary>
    internal string DirectorTakesRoot
        => KeptExerciseFolder
           ?? (PrimaryRecordingPath is { } path && Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir
               ? dir : Path.Combine(Path.GetTempPath(), "hrot-replay-no-recording"));

    /// <summary>The kept (archived) folder of the open recording's exercise, when there is one (<see cref="RecordingsRoot"/>'s shared root).</summary>
    private string? KeptExerciseFolder
        => PrimaryMetadata is { } meta && meta.ExerciseId != Guid.Empty
           && Path.Combine(Fdp.Toolkit.Orchestration.OrchestrationConstants.GetSharedRoot(RecordingsRoot),
                           Fdp.Toolkit.Orchestration.OrchestrationConstants.ExercisesDirectoryName, meta.ExerciseId.ToString()) is var kept
           && Directory.Exists(kept) ? kept : null;

    /// <summary>⭐ D1 — the recording's SIM second at the playhead (a v6 recording, which has no sim axis: wall seconds after the replay's zero).</summary>
    internal double DirectorSimSeconds
    {
        get
        {
            if (_manager == null) return 0d;
            var map = CachedTimeMap;
            return map.HasSimTime ? map.SimAt(_manager.BaseWallTicks) : ReplaySeconds ?? 0d;
        }
    }

    /// <summary>
    /// ⭐ D1 — the director's step and seek: the playhead to <paramref name="sim"/> (clamped to the recording), through the call every playhead
    /// move of this host ends in (<c>SetBaseWallTicks</c> — the Event Timeline's cursor, the viewer's loop wrap). ⛔ No history: the director is
    /// playing, not navigating (the same reason <see cref="TickLocalLoop"/> keeps none).
    /// </summary>
    internal void MoveDirectorPlayhead(double sim)
    {
        if (_manager == null || ReplayZeroTicks is not { } zero || ReplayEndTicks is not { } end) return;
        var map = CachedTimeMap;
        long target = map.HasSimTime ? map.WallAt(sim) : zero + (long)Math.Round(sim * TimeSpan.TicksPerSecond);
        _manager.SetBaseWallTicks(Math.Clamp(target, zero, end));
    }

    /// <summary>⭐ D10 (CR-6) — K's anchor: the nearest combat event at or before <paramref name="sim"/> in the index this host's Event Timeline shows.</summary>
    private EventAnchor? DirectorEventAt(double sim)
        => _timelineSource?.Index is { } index ? EventAnchors.NearestBefore(index, sim, DirectorUnitName) : null;

    private string? DirectorUnitName(long networkId)
    {
        if (_activeRepo is not { } world) return null;
        var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, networkId);
        if (e.IsNull || !world.IsAlive(e) || !world.HasComponent<EntityInfo>(e)) return null;
        var name = world.GetComponentRO<EntityInfo>(e).Name.ToString();
        return name.Length > 0 ? name : null;
    }
}
