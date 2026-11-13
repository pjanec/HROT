using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;

namespace Hrot.Editor.Review;

/// <summary>What the editor found for an exercise it is asked to open: the group the inventory knows, which node each file is, and whether THIS node recorded a part of it.</summary>
/// <param name="Parts">(node id, file) per participant, by node id.</param>
/// <param name="HasOwnPart">This node's own file of the exercise exists — today's AAR (every node replays its own file, W5).</param>
public sealed record ReviewOpening(RecordingGroup Group, IReadOnlyList<(int Node, string Path)> Parts, bool HasOwnPart)
{
    /// <summary>A participant group with NO part of this node — what <see cref="ReviewSource"/> opens.</summary>
    public bool OpensMerged => !HasOwnPart && Parts.Count > 0;
}

/// <summary>What the Review source shows now.</summary>
public enum ReviewSourceKind { None, Merged, Part }

/// <summary>
/// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D6 / D7 / D9, §4 V6) — <b>the source of the editor's ONE world while a participant GROUP with no part of this node is open.</b>
/// Today that case opens an EMPTY world: <c>EcsRecordReplayController.PrepareReplayAsync</c> installs a <c>ReplayModule</c> over THIS node's file, a node with no file becomes a LISTENER
/// (<c>"NO recording — listener"</c>) that waits for other nodes over the wire, and the all-in-one editor has none. This class fills the world instead, from the group's files:
/// <list type="bullet">
/// <item><b>Merged</b> (the default, Q7): <see cref="FederatedReplayManager"/> (the Replay Browser's hidden per-node sandboxes) + <see cref="BinaryMerger"/> (⭐ G-7 S1: a binary copy of each node's owned
/// components) into the editor's world, which is the merge's ONE persistent target (D8) — cleared only at a world boundary, never per step; a step costs a full pass (S4 makes it incremental) ⇒ step / scrub only.
/// ⛔ SUPERSEDED: the JSON merge (<see cref="TransientMasterBuilder.BuildInto"/>) into a world cleared every step — it lives on as the rails' oracle.</item>
/// <item><b>a part</b> (<see cref="SelectPart"/>): that node's file replays into the editor's world by the recording's own binary playback (every component exact) — and plays, following the
/// toolbar's ▶ and rate (the transport's facade), as an own-part AAR does.</item>
/// </list>
/// ⭐ The cluster still enters AAR through the ONE door (the master plans the replay); this node's replay module is then an idle LISTENER that cleared the world ONCE on entry
/// (<c>PlaybackTickSystem.Listen</c>) — so the source fills the world only once the node listens (<c>ready</c>), after which nothing of the replay writes it. ⛔ The recorder is untouched.
/// </summary>
public sealed class ReviewSource : IDisposable, IReviewHost
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<TransientMasterBuilder?> _builder;
    private readonly Action _clearWorld;
    private readonly Func<string> _stagingRoot;
    private readonly int _thisNode;
    private readonly Func<bool> _ready;
    private readonly Func<bool> _inReplay;
    private readonly Func<int?> _selection;
    private readonly Func<int, bool> _select;
    private readonly Func<int, string> _nodeName;
    private readonly Func<Hrot.UI.Common.Facades.ITimeTransportFacade?> _facade;

    private FederatedReplayManager? _manager;
    private PlaybackController? _part;
    private int _partNode;
    private ReviewOpening? _pending;
    private ReviewOpening? _open;
    private bool _readyOnce;
    private double _partClockTicks;
    private bool _filling;
    // ⭐ G-7 / CE-4354 S1 — the ONE merge and the world-boundary flag: the world is cleared (and the merger's map forgotten) ONLY when something other than the merge
    //   wrote it (a group opened, the source was a part, the group closed) — never per step (D8: the editor's world IS the merge target).
    private readonly BinaryMerger _merger = new(new OwnershipSource(Hrot.Network.Replication.NedOwnershipGroupBinding.ReviewMap));   // ⭐ S2 — owners from the recorded ownership components
    private bool _worldDirty = true;
    private string? _statusCache;
    private int _statusKey = -1;

    /// <param name="world">The editor's ONE world.</param>
    /// <param name="builder">⭐ G-7: no longer CALLED (the merge is <see cref="BinaryMerger"/>, which needs no serializer; the old JSON builder is the rails' oracle) — returns null until the editor has bootstrapped, which is the gate that holds a merge back until then.</param>
    /// <param name="clearWorld">The editor's world boundary (<c>ScenarioFileService.NewScenario</c>: the reset observers, <c>SoftClear</c>, the epoch) — the clear a scenario load starts from.</param>
    /// <param name="stagingRoot">Where the nodes record (<c>OrchestrationConstants.ResolveStagingRoot</c>).</param>
    /// <param name="thisNode">This node's id (a part of it is its own file under its own staging root).</param>
    /// <param name="ready">The replay is up and this node LISTENS (the cluster is in AAR and the listener cleared the world once).</param>
    /// <param name="inReplay">The cluster is in AAR (false ⇒ an open group closes).</param>
    /// <param name="selection">The selected entity's network id (kept across a refill) — or null.</param>
    /// <param name="select">Selects an entity by network id.</param>
    /// <param name="nodeName">A participant's name for the status bar; default <c>NodeIdRanges.NameOf</c> — the kind from the id's range (the sidecar carries no role).</param>
    /// <param name="facade">The toolbar's time control, read: a part plays while it plays.</param>
    public ReviewSource(Func<EntityRepository?> world, Func<TransientMasterBuilder?> builder, Action clearWorld, Func<string> stagingRoot, int thisNode,
                        Func<bool> ready, Func<bool> inReplay, Func<int?>? selection = null, Func<int, bool>? select = null, Func<int, string>? nodeName = null,
                        Func<Hrot.UI.Common.Facades.ITimeTransportFacade?>? facade = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _clearWorld = clearWorld ?? throw new ArgumentNullException(nameof(clearWorld));
        _stagingRoot = stagingRoot ?? throw new ArgumentNullException(nameof(stagingRoot));
        _thisNode = thisNode;
        _ready = ready ?? throw new ArgumentNullException(nameof(ready));
        _inReplay = inReplay ?? throw new ArgumentNullException(nameof(inReplay));
        _selection = selection ?? (() => null);
        _select = select ?? (_ => false);
        _nodeName = nodeName ?? Hrot.Orchestrator.NodeIdRanges.NameOf;   // ⭐ the role from the id's range (R-4175), so a recording's rows read "CGF-1" with its role icon
        _facade = facade ?? (() => null);
        Transport = new ReviewSourceTransport(this);
    }

    // ── ⭐ CE-4496 R-2 (docs/DESIGN_Threaded_Review_Source.md §1.1) — the source as an IReviewHost: the review UI reads the model the ONE builder makes from these facts, and
    //    commands through a channel (InlineReviewChannel with the switch off; the threaded one in mode 3). ⛔ No UI type holds this source or its manager.

    private int _factsKey;
    private bool _playing;   // what the last Update saw of ▶ (the facts are the source's own state: no read of the UI's facade per model read)

    /// <summary>⭐ The source's facts for the read model — every value cached here, so reading them is 0 B.</summary>
    public ReviewHostFacts Facts => new(
        Kind switch { ReviewSourceKind.Merged => ReviewKind.Merged, ReviewSourceKind.Part => ReviewKind.Part, _ => ReviewKind.None },
        IsOpen, IsPending, Failure, ExerciseId, _partNode, IsOpen ? CurrentFrame : -1, IsOpen ? TotalFrames : 0,
        IsOpen && _playing, StatusText(), FaceLabel, IsOpen ? FaceIconKey : null, _factsKey);

    /// <summary>The Federation slider: a node's manual offset (re-seeks every node; the merge follows through <c>OnTimeChanged</c>).</summary>
    public void SetNodeOffsetTicks(int node, long offsetTicks) => _manager?.SetNodeOffset(node, offsetTicks);

    /// <summary>The group's base wall time (a seek of every node).</summary>
    public void SetBaseWallTicks(long ticks) => _manager?.SetBaseWallTicks(ticks);

    /// <summary>The <i>Local entities shown from</i> checkbox.</summary>
    public void SetLocalEntitiesShown(int node, bool shown) => _manager?.SetLocalEntitiesShown(node, shown);

    /// <summary>The primary (clock / timeline) node.</summary>
    public void SetLocalEntitiesProvider(int node) { try { _manager?.SetLocalEntitiesProvider(node); } catch (ArgumentOutOfRangeException) { } }

    // ── what is open ─────────────────────────────────────────────────────────────────────────

    /// <summary>A participant group is open (loaded) — Merged or a part.</summary>
    public bool IsOpen => _open is not null;

    /// <summary>A group was asked for and the replay has not come up yet (it fills when it does).</summary>
    public bool IsPending => _pending is not null;

    /// <summary>What the world shows now.</summary>
    public ReviewSourceKind Kind { get; private set; }

    /// <summary>The node of the open part (valid while <see cref="Kind"/> is <see cref="ReviewSourceKind.Part"/>).</summary>
    public int PartNode => _partNode;

    /// <summary>The participants of the open group, by node id.</summary>
    public IReadOnlyList<(int Node, string Path)> Participants => _open?.Parts ?? Array.Empty<(int, string)>();

    /// <summary>The reason the last open failed (a group the manager refused, a file it could not read); null otherwise.</summary>
    public string? Failure { get; private set; }

    /// <summary>The group's exercise.</summary>
    public Guid? ExerciseId => (_open ?? _pending)?.Group.ExerciseId.Value;

    /// <summary>The manager the Recording info / JSON export windows read (the hidden sandboxes); null when none is open.</summary>
    public FederatedReplayManager? Manager => _manager;

    /// <summary>⭐ G-7 S5 — the manager while the world shows the MERGE (null for a part / nothing open): what Details ▸ By node, the <i>Replica differs</i> lane and the search read.</summary>
    public FederatedReplayManager? MergedManager => Kind == ReviewSourceKind.Merged ? _manager : null;

    private Dictionary<int, string>? _localNotes;

    /// <summary>⭐ G-7 S5 (I7, M-6) — <c>"local to SimHost-1"</c> for a LOCAL (no network id) entity of the merge — the node it came from (<see cref="MergeTarget.TryGetLocalNode"/>); null for any other entity. Cached: 0 B per frame.</summary>
    public string? LocalOriginNote(Entity entity)
    {
        if (Kind != ReviewSourceKind.Merged || !_merger.Target.TryGetLocalNode(entity, out int node)) return null;
        _localNotes ??= new Dictionary<int, string>();
        if (!_localNotes.TryGetValue(node, out var note)) _localNotes[node] = note = "local to " + _nodeName(node);
        return note;
    }

    /// <summary>⭐ The ONE AAR transport's implementation over this source (the editor's segment draws it while a group is open).</summary>
    public ReviewSourceTransport Transport { get; }

    /// <summary>Fired when the open group, the kind or the part changes (the status bar and the combo re-read their text).</summary>
    public event Action? Changed;

    private void Raise() { _statusKey = -1; _factsKey++; Changed?.Invoke(); }

    /// <summary>The file the review windows (search, diff) read: the primary participant's (Merged) or the part's.</summary>
    public string[] RecordingFiles()
    {
        if (_open is null) return Array.Empty<string>();
        int node = Kind == ReviewSourceKind.Part ? _partNode : (_manager?.LocalEntitiesProviderNodeId ?? _open.Parts[0].Node);
        string? path = _open.Parts.Where(p => p.Node == node).Select(p => p.Path).FirstOrDefault();
        return path is null ? Array.Empty<string>() : new[] { path };
    }

    // ── the branch: what a recording is when it opens ───────────────────────────────────────

    /// <summary>
    /// ⭐ THE BRANCH (D6): classifies an exercise the editor is asked to open — <see cref="RecordingGroupInventory"/> (the Replay Browser's own group loader) for its participant files, and
    /// whether this node's own file is among them (the file <c>PrepareReplay</c> would replay). Null when the inventory does not know the exercise.
    /// </summary>
    public ReviewOpening? Classify(Guid exerciseId)
    {
        var group = RecordingGroupInventory.Scan(_stagingRoot()).FirstOrDefault(g => g.ExerciseId.Value == exerciseId);
        return group is null ? null : Classify(group);
    }

    /// <summary><see cref="Classify(Guid)"/> for a group the caller already scanned (the picker scans once for all its rows).</summary>
    public ReviewOpening Classify(RecordingGroup group)
    {
        string root = _stagingRoot();
        Guid exerciseId = group.ExerciseId.Value;
        var parts = new List<(int, string)>();
        foreach (var path in group.Paths)
        {
            var meta = ReadSidecar(path);
            if (meta is not null) parts.Add(((int)meta.NodeId, path));
        }
        parts.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        string own = OrchestrationConstants.GetExerciseRecordingFilePath(OrchestrationConstants.GetNodeStagingRoot(root, _thisNode), exerciseId, _thisNode);
        return new ReviewOpening(group, parts, File.Exists(own));
    }

    /// <summary>
    /// ⭐ THE ONE BRANCH where a recording opens (called by the editor's session as the open goes out): this node has a part ⇒ today's AAR, <b>unchanged</b> (any group open is closed);
    /// a group with no part here ⇒ this source takes the world (<see cref="IsPending"/> until the replay is up). Returns whether the source took it.
    /// </summary>
    public bool OnRecordingOpened(Guid exerciseId)
    {
        Close();
        var found = Classify(exerciseId);
        if (found is null || !found.OpensMerged) { _pending = null; return false; }
        _pending = found;
        Failure = null;
        Raise();
        return true;
    }

    /// <summary>What the picker's row says about an exercise: <c>"4 participants — opens MERGED"</c>, <c>"this editor's run"</c>, or null when the inventory does not know it.</summary>
    public string? PickerNote(ReviewOpening? found) => found switch
    {
        null => null,
        { HasOwnPart: true } => "this editor's run",
        { Parts.Count: 1 } => "1 participant — opens MERGED",
        _ => found.Parts.Count + " participants — opens MERGED",
    };

    // ── the per-frame step ───────────────────────────────────────────────────────────────────

    /// <summary>How many times <see cref="Update"/> ran (a rail's observation of the ONE call site, CE-4497).</summary>
    public int Updates { get; private set; }

    /// <summary>
    /// Once a frame: loads a pending group the moment the replay is up and this node listens; closes an open group when the cluster has left AAR; advances a playing part.
    /// </summary>
    /// <param name="dtSeconds">Wall seconds since the last call.</param>
    public void Update(double dtSeconds)
    {
        Updates++;
        bool inReplay = _inReplay();
        _playing = _facade() is { IsPaused: false };
        if (_open is not null && !inReplay && _readyOnce) { Close(); return; }
        if (_pending is not null && inReplay && _ready()) { var p = _pending; _pending = null; _readyOnce = true; Load(p); }
        if (_open is not null && Kind == ReviewSourceKind.Part) AdvancePart(dtSeconds);
        else if (_open is not null && Kind == ReviewSourceKind.Merged) AdvanceMerged(dtSeconds);
        else _mergedWasPlaying = false;
    }

    /// <summary>Loads <paramref name="opening"/> now: the manager over its files, the switch, and the world — Merged.</summary>
    public void Load(ReviewOpening opening)
    {
        Close();
        Failure = null;
        try
        {
            _manager = FederatedReplayManager.LoadGroup(opening.Parts.Select(p => p.Path).ToArray());
        }
        catch (Exception ex) when (ex is LoadGroupException or IOException or InvalidDataException)
        {
            Failure = ex.Message;
            Raise();
            return;
        }
        _open = opening;
        _worldDirty = true;   // a group opened: a world boundary — the first merge clears the world and starts a fresh map
        if (_world() is { } w) RepositoryPriming.RegisterDiscoveredComponents(w);   // every recorded component has a table in the resident world (idempotent: the same call a fresh Replay Browser repository gets)
        // ⭐ CE-4496 R-2 — no panel here: the review UI's ONE Federation panel reads the model (ReviewUi); the group opens MERGED.
        _manager.OnTimeChanged += OnManagerTime;
        Kind = ReviewSourceKind.Merged;
        var primary = Primary();
        if (primary?.Playback is { TotalFrames: > 0 } pb)
            _manager.SetBaseWallTicks(pb.GetFrameMetadata(0).WallClockTicks);   // → OnManagerTime → the first merge into the world
        else RefillMerged();
        Raise();
    }

    /// <summary>Leaves the group: the sandboxes are disposed. The world keeps what it shows until the cluster's own replay ends (it is cleared by the world boundary then).</summary>
    public void Close()
    {
        _pending = null;
        if (_open is null && _manager is null) return;
        if (_manager is not null) _manager.OnTimeChanged -= OnManagerTime;
        _part?.Dispose(); _part = null;
        _manager?.Dispose(); _manager = null;
        _open = null; _readyOnce = false;
        _worldDirty = true; _merger.Reset();   // the world keeps what it shows, but the merger's map no longer describes a group
        Kind = ReviewSourceKind.None;
        Raise();
    }

    public void Dispose() => Close();

    // ── the Source switch ────────────────────────────────────────────────────────────────────

    /// <summary>The Source switch ▸ Merged: the world is the merge of every participant (the default).</summary>
    public void SelectMerged()
    {
        if (_open is null || _manager is null) return;
        if (Kind == ReviewSourceKind.Merged) return;
        int atFrame = Transport.CurrentFrame;
        _part?.Dispose(); _part = null;
        _worldDirty = true;   // the world held a part: a world boundary
        Kind = ReviewSourceKind.Merged;
        if (Primary()?.Playback is { TotalFrames: > 0 } pb)
            _manager.SetBaseWallTicks(pb.GetFrameMetadata(Math.Clamp(atFrame, 0, pb.TotalFrames - 1)).WallClockTicks);
        else RefillMerged();
        Raise();
    }

    /// <summary>The Source switch ▸ a node's part: the world is THAT node's recording, applied by the recording's own playback (exact); ▶ plays it.</summary>
    public void SelectPart(int node)
    {
        if (_open is null || _manager is null) return;
        var part = _open.Parts.FirstOrDefault(p => p.Node == node);
        if (part.Path is null) return;
        if (Kind == ReviewSourceKind.Part && _partNode == node) return;
        long wall = CurrentWallTicks();
        _part?.Dispose();
        _partNode = node;
        _part = new PlaybackController(part.Path);
        Kind = ReviewSourceKind.Part;
        try { _manager.SetLocalEntitiesProvider(node); } catch (ArgumentOutOfRangeException) { }
        _partClockTicks = wall;
        FillPartAt(FrameAtOrBefore(_part, wall));
        Raise();
    }

    // ── filling the ONE world ────────────────────────────────────────────────────────────────

    private ReplayBrowserContext? Primary()
        => _manager is not null && _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var c) ? c : null;

    private void OnManagerTime()
    {
        if (Kind == ReviewSourceKind.Merged) RefillMerged();
    }

    /// <summary>
    /// D7 / ⭐ G-7 S1 — the merge, copied straight into the editor's world: <see cref="BinaryMerger.Merge"/> keeps ONE target entity per network id across steps (stable handles),
    /// so the world is NOT cleared per step — only at a world boundary (<c>_worldDirty</c>). The selection follows its unit by network id.
    /// ⛔ SUPERSEDED: <c>_clearWorld()</c> + <c>TransientMasterBuilder.BuildInto</c> (the JSON merge) every step.
    /// </summary>
    private void RefillMerged()
    {
        if (_filling || _manager is null || _world() is not { } world || _builder() is null) return;
        _filling = true;
        try
        {
            int? selected = _selection();
            if (_worldDirty) { _clearWorld(); _merger.Reset(); _worldDirty = false; }
            _merger.Merge(_manager, world);
            if (selected is { } id) _select(id);
        }
        finally { _filling = false; }
    }

    private void FillPartAt(int frame)
    {
        if (_part is null || _world() is not { } world || _part.TotalFrames <= 0) return;
        int? selected = _selection();
        _clearWorld();
        _worldDirty = true;   // the world now holds a part's recording, not the merge
        _part.SeekToFrame(world, Math.Clamp(frame, 0, _part.TotalFrames - 1));
        _partClockTicks = _part.GetFrameMetadata(_part.CurrentFrame).WallClockTicks;
        if (selected is { } id) _select(id);
    }

    private long CurrentWallTicks()
    {
        if (Kind == ReviewSourceKind.Part && _part is { CurrentFrame: >= 0 } p) return p.GetFrameMetadata(p.CurrentFrame).WallClockTicks;
        return Primary()?.Playback is { CurrentFrame: >= 0 } pb ? pb.GetFrameMetadata(pb.CurrentFrame).WallClockTicks : 0;
    }

    private static int FrameAtOrBefore(PlaybackController pb, long wall)
    {
        int lo = 0, hi = pb.TotalFrames - 1, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (pb.GetFrameMetadata(mid).WallClockTicks <= wall) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return best;
    }

    /// <summary>A playing part: the recording's own clock advances with the toolbar's ▶ and rate (as an own-part AAR follows the cluster clock), frames applied by the playback's step.</summary>
    private void AdvancePart(double dt)
    {
        if (_part is null || _world() is not { } world || _facade() is not { IsPaused: false } f || _part.TotalFrames <= 0) return;
        _partClockTicks += dt * Math.Max(0.01f, f.TimeScale) * TimeSpan.TicksPerSecond;
        int target = FrameAtOrBefore(_part, (long)_partClockTicks);
        int gap = target - _part.CurrentFrame;
        if (gap <= 0) { if (_part.IsAtEnd) _partClockTicks = _part.GetFrameMetadata(_part.TotalFrames - 1).WallClockTicks; return; }
        if (gap > 30) { FillPartAt(target); Raise(); return; }
        for (int i = 0; i < gap && !_part.IsAtEnd; i++) _part.StepForward(world);
    }

    private double _mergedClockTicks;
    private bool _mergedWasPlaying;

    /// <summary>
    /// ⭐ G-7 S4 (M-7) — ▶ in MERGED: the toolbar's ▶ and rate advance the group's clock; every participant steps frame by frame (<see cref="FederatedReplayManager.StepForwardAll"/> — the path the
    /// ▶| step takes, so each step merges through <c>OnManagerTime</c> → <see cref="RefillMerged"/>, incrementally); a jump of more than 30 frames is one seek (one merge), as a part does.
    /// While paused the clock follows the primary's frame, so a step or a scrub is where ▶ resumes from.
    /// </summary>
    private void AdvanceMerged(double dt)
    {
        if (_manager is null || Primary()?.Playback is not { TotalFrames: > 0 } pb) return;
        if (_facade() is not { IsPaused: false } f) { _mergedWasPlaying = false; return; }
        if (!_mergedWasPlaying) { _mergedClockTicks = CurrentWallTicks(); _mergedWasPlaying = true; }
        _mergedClockTicks += dt * Math.Max(0.01f, f.TimeScale) * TimeSpan.TicksPerSecond;
        int target = FrameAtOrBefore(pb, (long)_mergedClockTicks);
        int gap = target - pb.CurrentFrame;
        if (gap <= 0) { if (pb.IsAtEnd) _mergedClockTicks = pb.GetFrameMetadata(pb.TotalFrames - 1).WallClockTicks; return; }
        if (gap > 30) { _manager.SetBaseWallTicks(pb.GetFrameMetadata(target).WallClockTicks); return; }   // a seek: one merge
        for (int i = 0; i < gap && !pb.IsAtEnd; i++) _manager.StepForwardAll();                            // a step each: one merge each
    }

    // ── the transport's operations (ReviewSourceTransport calls these) ───────────────────────

    internal bool HasRecording => (Kind == ReviewSourceKind.Part ? _part : Primary()?.Playback) is { TotalFrames: > 0 };
    internal int CurrentFrame => Kind == ReviewSourceKind.Part ? _part?.CurrentFrame ?? -1 : Primary()?.CurrentFrame ?? -1;
    internal int TotalFrames => (Kind == ReviewSourceKind.Part ? _part : Primary()?.Playback)?.TotalFrames ?? 0;
    internal PlaybackController? ActivePlayback => Kind == ReviewSourceKind.Part ? _part : Primary()?.Playback;
    internal Hrot.UI.Common.Facades.ITimeTransportFacade? Facade => _facade();

    public void SeekFrame(int frame)
    {
        var pb = ActivePlayback;
        if (pb is null || pb.TotalFrames <= 0) return;
        frame = Math.Clamp(frame, 0, pb.TotalFrames - 1);
        if (Kind == ReviewSourceKind.Part) { FillPartAt(frame); return; }
        _manager?.SetBaseWallTicks(pb.GetFrameMetadata(frame).WallClockTicks);   // → OnManagerTime → the merge
    }

    public void StepFrame(int delta)
    {
        var pb = ActivePlayback;
        if (pb is null || delta == 0) return;
        if (Kind == ReviewSourceKind.Part)
        {
            if (_world() is not { } world) return;
            if (delta > 0 && !_part!.IsAtEnd) _part.StepForward(world);
            else if (delta < 0 && _part!.CurrentFrame > 0) FillPartAt(_part.CurrentFrame - 1);   // a step back is a seek: the playback restores from the keyframe
            _partClockTicks = _part!.CurrentFrame >= 0 ? _part.GetFrameMetadata(_part.CurrentFrame).WallClockTicks : _partClockTicks;
            return;
        }
        if (_manager is null) return;
        if (delta > 0) { if (pb.CurrentFrame + 1 < pb.TotalFrames) _manager.StepForwardAll(); }
        else if (pb.CurrentFrame > 0) _manager.StepBackwardAll();
    }

    // ── the status bar's words (D9) ──────────────────────────────────────────────────────────

    /// <summary>The Merged text's lead — the design's <c>⧉</c> is not in the UI font; the plain word is.</summary>
    public const string MergedLead = "Merged";

    /// <summary>
    /// ⭐ D9 — what the status bar's source segment says: <i>"Merged · 4 participants (Node 1, Node 2, …) — step / scrub"</i>; a part <i>"Part: Node 2"</i>;
    /// <see langword="null"/> for an ordinary AAR (nothing extra). A failed open says why. Rebuilt only when the group or the kind changes (0 B steady).
    /// </summary>
    public string? StatusText()
    {
        int key = _open is null ? (Failure is not null ? 1 : _pending is not null ? 4 : 0) : 2 + (int)Kind * 1000 + _partNode;
        if (key == _statusKey) return _statusCache;
        _statusKey = key;
        if (_open is null) return _statusCache = Failure is null ? (_pending is null ? null : "Review: opening the group…") : "Review: could not open the group — " + Failure;
        if (Kind == ReviewSourceKind.Part) return _statusCache = "Part: " + _nodeName(_partNode);
        var names = string.Join(", ", _open.Parts.Select(p => _nodeName(p.Node)));
        int n = _open.Parts.Count;
        return _statusCache = $"{MergedLead} · {n} participant{(n == 1 ? "" : "s")} ({names}) — step / scrub";
    }

    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<int, string> _partLabels = new();
    private string? _faceLabel;
    private ReviewSourceKind _faceKind = ReviewSourceKind.None;
    private int _faceNode = int.MinValue, _faceCount = -1;

    /// <summary>The label of a participant in the Source switch. Cached per node (a name is stable for a group): the menu and the face draw it every frame, 0 B.</summary>
    public string NameOf(int node)
    {
        if (!_names.TryGetValue(node, out var name)) _names[node] = name = _nodeName(node);
        return name;
    }

    /// <summary>⭐ Mockup ③ — a participant's menu row: <c>"CGF-1's part"</c>. Cached.</summary>
    public string PartLabel(int node)
    {
        if (!_partLabels.TryGetValue(node, out var label)) _partLabels[node] = label = NameOf(node) + "'s part";
        return label;
    }

    /// <summary>⭐ Mockup ③ — the NodeEditor.Core.Widgets.Icons key of a participant's ROLE icon (colour and glyph belong to the key, R-4139): CGF → <c>node/cgf</c> (robot, blue) · SimHost → <c>node/simhost</c> (cube, amber) · IG / Map →
    /// <c>node/ig</c> · Player → <c>node/player</c> · any other <c>node/other</c>. 📐 A recording's sidecar carries no role, so the role is read from the participant's NAME (what the host calls it).</summary>
    public string RoleKeyOf(int node)
    {
        string n = NameOf(node);
        if (n.StartsWith("CGF", StringComparison.OrdinalIgnoreCase)) return "node/cgf";
        if (n.StartsWith("SimHost", StringComparison.OrdinalIgnoreCase)) return "node/simhost";
        if (n.StartsWith("IG", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Map", StringComparison.OrdinalIgnoreCase)) return "node/ig";
        if (n.StartsWith("Player", StringComparison.OrdinalIgnoreCase)) return "node/player";
        return "node/other";
    }

    /// <summary>⭐ Mockup ③ — what the Source FACE says (status-bar segment, toolbar combo): <c>"Merged · 4 participants"</c> or the part's <c>"CGF-1's part"</c>; null while no group is open. Cached.</summary>
    public string? FaceLabel
    {
        get
        {
            if (_open is null) return null;
            int count = _open.Parts.Count;
            if (_faceLabel is null || _faceKind != Kind || _faceNode != _partNode || _faceCount != count)
            {
                _faceKind = Kind; _faceNode = _partNode; _faceCount = count;
                _faceLabel = Kind == ReviewSourceKind.Part ? PartLabel(_partNode) : $"{MergedLead} · {count} participant{(count == 1 ? "" : "s")}";
            }
            return _faceLabel;
        }
    }

    /// <summary>The Icons key of the face's glyph: the merged sitemap (teal) or the open part's role.</summary>
    public string FaceIconKey => Kind == ReviewSourceKind.Part ? RoleKeyOf(_partNode) : "review/merged";

    internal static RecordingMetadata? ReadSidecar(string fdpPath)
    {
        var metaPath = fdpPath + ".meta.json";
        if (!File.Exists(metaPath)) return null;
        try { return MetadataSerializer.Deserialize(File.ReadAllText(metaPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }
}
