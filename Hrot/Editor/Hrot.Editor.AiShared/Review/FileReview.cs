using System;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using StructEdit.Core;
using StructEdit.Reflection;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §3 D4) — ONE throwaway reader over a recording FILE, kept while the path is the same: what the analysis (search, frame diff)
/// reads, never the shown world. The editor's HTTP API and the review windows share it (it replaces the API's own <c>_scanned</c> pair).
/// </summary>
public sealed class RecordingFileReader : IDisposable
{
    private (string Path, ReplayBrowserContext Context)? _held;

    /// <summary>The reader over <paramref name="path"/> (loaded on first use, re-loaded when the path changes); null when there is no such file or it holds no frames.</summary>
    public ReplayBrowserContext? Of(string? path)
    {
        if (path is null || !System.IO.File.Exists(path)) return null;
        if (_held is not { } held || held.Path != path)
        {
            _held?.Context.Dispose();
            var ctx = new ReplayBrowserContext();
            ctx.LoadRecording(path);
            if (ctx.Playback is null) { ctx.Dispose(); _held = null; return null; }
            _held = (path, ctx);
        }
        return _held.Value.Context;
    }

    public void Dispose() { _held?.Context.Dispose(); _held = null; }
}

/// <summary>What <see cref="FileReview"/> reads and drives — D3's host members plus the services the analysis compiles with.</summary>
/// <param name="RecordingFiles">The recording file(s) the analysis reads (D4); empty outside AAR. ⚠ Return a CACHED array — it is read every frame.</param>
/// <param name="CurrentFrame">The shown replay frame (-1 when unknown).</param>
/// <param name="Seek">The ONE seek of the host (the cluster's, on the main thread).</param>
/// <param name="Selection">The selected entity's network id, or null.</param>
/// <param name="Select">Selects an entity by network id; false when the shown world does not hold it (yet).</param>
public sealed record ReviewFileServices(
    Func<string[]> RecordingFiles,
    Func<long> CurrentFrame,
    Action<long> Seek,
    Func<int?> Selection,
    Func<int, bool> Select,
    ScenarioSerializer Serializer,
    BehaviorRegistry? Behaviors,
    BlueprintRegistry? Blueprints,
    RecordingFileReader Reader,
    Func<Fdp.Toolkit.ReplayBrowser.PlaybackHistoryTracker?>? History = null);   // R-6 / V7 — the transport's waypoints: a hit is a JUMP, ◀ returns to where you were

/// <summary>
/// ⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §3 D3 / D4, §4 V2) — the review windows' PANELS over a host's own-part recording FILE: the Replay search panel (a search reads
/// the file, a hit SEEKS the host and SELECTS its entity), the Frame diff panel (the selected unit at the shown frame vs the frame before, both read from the file) and the
/// timeline panel Recording info / JSON export draw (no federation here, so they say no recording is loaded). Outside AAR (no file) the search and diff panels say so.
/// ⭐ <see cref="Update"/> is the per-frame step the host calls: 0 B when nothing changed.
/// </summary>
public sealed class FileReview
{
    public const string NoRecording = "Open an exercise recording (AAR) to search / diff.";

    private readonly ReviewFileServices _s;
    private readonly ComponentDiffService _diffService = new();

    private string? _lastKey;               // the diff's last (path, frame, unit) — compared field by field below, never concatenated per frame
    private string? _diffPath;
    private long _diffFrame = long.MinValue;
    private int _diffUnit = int.MinValue;
    private long _diffAt;
    private int _pendingSelect = -1, _pendingTtl;
    private volatile int _scanFound = int.MinValue;   // a seek-to-change result handed back from its worker
    private bool _scanning;

    public FileReview(ReviewFileServices services)
    {
        _s = services ?? throw new ArgumentNullException(nameof(services));
        DiffPanel = new ComponentDiffPanel { UnavailableReason = Unavailable };
        DiffPanel.HasRecording = () => FilePath is not null;
        DiffPanel.OnEntityLinkClicked = SelectReaderEntity;
        DiffPanel.OnSeekToChangeRequested = SeekToChange;

        var editService = ReviewSearchServices.BuildEditService();
        var search = new RecordingSearchService(new PredicateCompiler(editService, _s.Behaviors, _s.Blueprints), new EventScannerCompiler(editService));
        SearchPanel = new ReplaySearchPanel(editService, search,
            onSeekRequested: f => _s.Seek(f),
            onEntitySelected: SelectReaderEntity,
            onMatchSelected: SeekToHit,
            _s.Behaviors, getSelectedEntity: null, getSelectedNetworkId: _s.Selection)
        { UnavailableReason = Unavailable };

        // ⭐ Recording info / JSON export draw the federation's panel; an own-part AAR has no federation ⇒ they say "no recording loaded" (V6's ReviewSource fills them).
        TimelinePanel = new ReplayTimelinePanel(null, () => 0, new NoExport(), new NoDialogs(),
            new PlaybackHistoryTracker(), new Fdp.Presentation.Abstractions.InspectorState());
    }

    public ComponentDiffPanel DiffPanel { get; }
    public ReplaySearchPanel SearchPanel { get; }
    public ReplayTimelinePanel TimelinePanel { get; }

    /// <summary>The recording file the analysis reads now, or null outside AAR.</summary>
    public string? FilePath => _s.RecordingFiles() is { Length: > 0 } files ? files[0] : null;

    private string? Unavailable() => FilePath is null ? NoRecording : null;

    /// <summary>The <see cref="ReviewHostServices"/> for <see cref="ReviewBundle"/>: the panels, and D3's members filled from this host's services.</summary>
    public ReviewHostServices Host(string perspective, string idPrefix, Vector4 colour, Func<EntityRepository?>? world = null) => new(
        perspective, idPrefix, colour, DiffPanel, SearchPanel, TimelinePanel,
        World: world, RecordingFiles: _s.RecordingFiles, CurrentFrame: _s.CurrentFrame, Seek: _s.Seek, Selection: _s.Selection);

    // ── the per-frame step ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Once a frame, from the host: points the search at the file, lands a pending selection, hands back a finished seek-to-change, and — when <paramref name="diffOpen"/> —
    /// refreshes the diff when the (file, frame, unit) moved (at most every 200 ms, so a playing replay does not read two frames per tick). ⭐ 0 B when nothing changed.
    /// </summary>
    public void Update(bool diffOpen)
    {
        string? path = FilePath;
        if (!ReferenceEquals(SearchPanel.CurrentFilePath, path)) SearchPanel.CurrentFilePath = path;

        if (_pendingSelect >= 0)
        {
            if (_s.Select(_pendingSelect) || --_pendingTtl <= 0) _pendingSelect = -1;
        }
        int found = _scanFound;
        if (found != int.MinValue) { _scanFound = int.MinValue; _scanning = false; DiffPanel.IsSearching = false; if (found >= 0) _s.Seek(found); }

        if (!diffOpen) return;
        if (path is null) { if (_diffPath is not null || DiffPanel.CurrentDiffs.Count > 0) { _diffPath = null; _lastKey = null; DiffPanel.CurrentDiffs = Array.Empty<DiffNode>(); } return; }
        long frame = _s.CurrentFrame();
        int unit = _s.Selection() ?? -1;
        if (_lastKey is not null && ReferenceEquals(_diffPath, path) && _diffFrame == frame && _diffUnit == unit) return;
        long now = Environment.TickCount64;
        if (now - _diffAt < 200) return;
        _diffAt = now;
        _lastKey = path; _diffPath = path; _diffFrame = frame; _diffUnit = unit;
        DiffPanel.CurrentDiffs = unit < 0 || frame <= 0 ? Array.Empty<DiffNode>() : ComputeDiff(path, (int)frame, unit);
    }

    /// <summary>The unit's change at <paramref name="frame"/>: its dump there against its dump one frame earlier, both read from the file (D4).</summary>
    public System.Collections.Generic.IReadOnlyList<DiffNode> ComputeDiff(string path, int frame, int networkId)
    {
        var ctx = _s.Reader.Of(path);
        if (ctx?.Playback is not { } playback || frame <= 0 || frame >= playback.TotalFrames) return Array.Empty<DiffNode>();
        var resolver = new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver();
        System.Text.Json.Nodes.JsonNode? Dump(int at)
        {
            ctx.SeekToFrame(at, suppressHistory: true);
            var repo = ctx.SandboxRepo;
            var e = NetworkIdResolver.FindEntityByNetworkId(repo, networkId);
            return !e.IsNull && repo.IsAlive(e) ? _s.Serializer.SerializeEntity(repo, e, resolver, repo.GetSnapshotableMask()) : null;
        }
        var before = Dump(frame - 1);
        var after = Dump(frame);
        return _diffService.ComputeTreeDiff(before, after, 0.0);
    }

    // ── what a click does ────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ V2's acceptance: a search hit SEEKS the host to its frame and SELECTS its entity (the seek lands a frame or so later, so the selection is retried until the world holds the unit).</summary>
    public void SeekToHit(int frame, Entity hit)
    {
        // ⭐ R-6 / V7 — a jump through the transport's seam is a history waypoint (the drawer's own RecordJump: where you were, then where you went)
        if (_s.History?.Invoke() is { } history)
        {
            long cur = _s.CurrentFrame();
            if (cur >= 0 && cur != frame) { history.PushWaypoint((int)cur, Entity.Null); history.PushWaypoint(frame, Entity.Null); }
        }
        _s.Seek(frame);
        SelectReaderEntity(hit, frame);
    }

    private void SelectReaderEntity(Entity e) => SelectReaderEntity(e, -1);

    /// <summary>An entity handle from the throwaway reader's world (a search hit, a diff link) ⇒ its network id there ⇒ the host's selection.</summary>
    private void SelectReaderEntity(Entity e, int frame)
    {
        if (e.IsNull) return;
        var ctx = _s.Reader.Of(FilePath);
        if (ctx is null) return;
        if (frame >= 0) ctx.SeekToFrame(frame, suppressHistory: true);
        var repo = ctx.SandboxRepo;
        if (!repo.IsAlive(e) || !repo.HasComponent<NetworkIdentity>(e)) return;
        int id = repo.GetComponentRO<NetworkIdentity>(e).Value;
        if (!_s.Select(id)) { _pendingSelect = id; _pendingTtl = 600; }
    }

    private void SeekToChange(int direction)
    {
        if (_scanning || FilePath is not { } path || _s.Selection() is not { } unit) return;
        _scanning = true; DiffPanel.IsSearching = true;
        int start = (int)Math.Max(0, _s.CurrentFrame());
        var serializer = _s.Serializer;
        var excluded = new System.Collections.Generic.HashSet<string>();
        foreach (var t in DiffPanel.ExcludedTypes) excluded.Add(t.Name);
        double epsilon = DiffPanel.IsEpsilonIgnored ? 0.001 : 0.0;
        Task.Run(() =>
        {
            int? hit = null;
            try
            {
                hit = EntityChangeFinder.Find(path, start, repo => NetworkIdResolver.FindEntityByNetworkId(repo, unit), direction,
                    (repo, e) => serializer.SerializeEntity(repo, e, new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver(), repo.GetSnapshotableMask()),
                    name => !excluded.Contains(name), epsilon);
            }
            catch (Exception) { /* a file that cannot be re-read: nothing found */ }
            _scanFound = hit ?? -1;   // handed back to Update(): the seek is the host's, on its main thread
        });
    }

    private sealed class NoExport : IRecordingExportService
    {
        public void ExportToJson(string input, string output, JsonExportOptions opts) { }
    }

    private sealed class NoDialogs : Fdp.Presentation.Abstractions.IFileDialogService
    {
        public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter) => Task.FromResult<string?>(null);
        public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter) => Task.FromResult<string?>(null);
        public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter) => Task.FromResult<string[]?>(null);
    }
}

/// <summary>⭐ R-2 / V2 — the ONE edit service the Replay search panel draws its predicate forms with (the Replay Browser built it inline; both hosts now call this).</summary>
public static class ReviewSearchServices
{
    public static IComponentEditService BuildEditService() => new ComponentEditServiceBuilder()
        .RegisterFieldEditor<Type>(new Fdp.Presentation.Editing.TypeFieldEditor())
        .RegisterFieldEditor<Fdp.Toolkit.Replication.AreaLink>(new Fdp.Presentation.Editing.PickableLeafFieldEditor(typeof(Fdp.Toolkit.Replication.AreaLink)))
        .RegisterFieldEditor<SearchPredicateDto>(new Fdp.Presentation.Editing.PredicateValueFieldEditor())
        .Build();
}
