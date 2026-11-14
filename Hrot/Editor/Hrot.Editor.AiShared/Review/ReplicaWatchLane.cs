using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.ReplayBrowser.Federation;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ Review §6.13 (RW-1…RW-5; mockup <c>docs/UX/replica-watch-visual.svg</c> ②) — the data of the Event Timeline's ONE lane <i>Replica watch · &lt;unit&gt;</i>: the SPANS during which a node believed a WATCHED item of the
/// SELECTED unit differently from its owner, beyond the item's tolerance, for longer than the lane's threshold (<see cref="ReplicaViews.ScanWatchSpans"/> — the same owner rule and node offsets as Details ▸ By node,
/// ONE implementation). It exists only in the MERGED view with a unit selected that has ≥ 1 watched item (<see cref="ReplicaWatchList"/>, empty by default ⇒ no lane). ⛔ The old always-on per-frame ticks are gone.
/// The scan runs in the background over its own reader of the group's files and restarts when the selection, the group, a node offset or the watch list changes; <see cref="Update"/> (once a frame, from
/// <see cref="ReviewTimelineTracks.Tracks"/>) only compares a key — 0 B steady — and lands a finished scan (<see cref="Version"/> moves).
/// </summary>
public sealed class ReplicaWatchLane : IDisposable
{
    private readonly ReplicaViews _views;
    private readonly IReviewView _review;   // ⭐ CE-4496 R-2 — the files, offsets and primary come from the published model (§2: file-only work reads the model, never the live manager)
    private readonly Func<int?> _selection;
    private readonly Func<int, string?> _unitName;
    private readonly Func<int, string?> _nodeName;

    private (bool on, Guid? exercise, int primary, int net, long offsets, int watch) _key;
    private CancellationTokenSource? _cts;
    private volatile IReadOnlyList<ReplicaSpan>? _done;
    private IReadOnlyList<ReplicaSpan> _spans = Array.Empty<ReplicaSpan>();
    private IReadOnlyList<ReplicaWatchItem> _items = Array.Empty<ReplicaWatchItem>();
    private string _label = "Replica watch";
    private int _version;

    /// <param name="watch">The session's watch (per unit items + the lane's threshold).</param>
    /// <param name="review">The review's view: the lane exists only while its model is MERGED.</param>
    /// <param name="selection">The selected unit's network id, or null.</param>
    /// <param name="unitName">The unit's name for the lane's label (null ⇒ its id).</param>
    /// <param name="nodeName">A node's name for the tooltip (null ⇒ <c>node N</c>).</param>
    public ReplicaWatchLane(ReplicaViews views, ReplicaWatchList watch, IReviewView review, Func<int?> selection, Func<int, string?>? unitName = null, Func<int, string?>? nodeName = null)
    {
        _views = views ?? throw new ArgumentNullException(nameof(views));
        Watch = watch ?? throw new ArgumentNullException(nameof(watch));
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _unitName = unitName ?? (_ => null);
        _nodeName = nodeName ?? (_ => null);
    }

    /// <summary>The session's watch list — the eye and the settings popup edit it.</summary>
    public ReplicaWatchList Watch { get; }

    /// <summary>The spans of the selected unit's watched items (primary node's frames); empty when no scan has landed / not merged.</summary>
    public IReadOnlyList<ReplicaSpan> Spans => _spans;

    /// <summary>The items the spans index into (the watch as it was when the scan started).</summary>
    public IReadOnlyList<ReplicaWatchItem> Items => _items;

    /// <summary>The lane exists: merged view, a selected unit, ≥ 1 watched item.</summary>
    public bool Active => _key.on && _items.Count > 0;

    /// <summary>The lane's label: <c>Replica watch · &lt;unit&gt;</c>.</summary>
    public string Label => _label;

    /// <summary>Moves whenever <see cref="Spans"/> or <see cref="Label"/> changed (a reader rebuilds only then).</summary>
    public int Version => _version;

    /// <summary>A scan is running.</summary>
    public bool Scanning => _cts != null && _done == null && _key.on;

    /// <summary>⭐ The lane's gear was clicked: the host's settings popup (<see cref="ReplicaWatchSettings"/>) opens on the next frame. Read-and-cleared by <see cref="TakeSettingsRequest"/>.</summary>
    public void OpenSettings() => _settingsRequested = true;
    private bool _settingsRequested;
    /// <summary>True once after <see cref="OpenSettings"/>.</summary>
    public bool TakeSettingsRequest() { bool r = _settingsRequested; _settingsRequested = false; return r; }

    /// <summary>The node's name for a tooltip.</summary>
    public string NodeName(int node) => _nodeName(node) ?? ("node " + node);

    /// <summary>The selected unit's name (or <c>unit N</c>).</summary>
    public string UnitLabel => _unitName(Unit) ?? ("unit " + Unit);

    /// <summary>In the merged view.</summary>
    public bool Merged => _review.Model.IsMerged;

    /// <summary>The unit selected now (0 = none) — the lane's settings and the eye act on it.</summary>
    public int Unit => _selection() ?? 0;

    /// <summary>The tooltip of span <paramref name="i"/>: <c>&lt;label&gt; — &lt;node&gt; believed &lt;v&gt; while the owner &lt;owner&gt; had &lt;w&gt; · for &lt;d&gt; s (&lt;a&gt; – &lt;b&gt; s) · click = seek to its start</c>. Built when the lane rebuilds, never per frame.</summary>
    public string TooltipOf(int i)
    {
        var s = _spans[i];
        var b = s.Believers[0];
        string who = s.Believers.Count > 1 ? NodeName(b.Node) + " +" + (s.Believers.Count - 1) : NodeName(b.Node);
        return $"{_items[s.Item].Label} — {who} believed {b.Value} while the owner {NodeName(s.OwnerNode)} had {s.OwnerValue} · for {s.Duration:0.#} s ({s.StartSeconds:0.#} – {s.EndSeconds:0.#} s) · click = seek to its start";
    }

    /// <summary>Once a frame: restarts the scan when the (group, unit, offsets, watch) key moved, lands a finished one. 0 B when nothing changed.</summary>
    public void Update()
    {
        var m = _review.Model;
        int net = _selection() ?? 0;
        var items = net > 0 ? Watch.ItemsOf(net) : Array.Empty<ReplicaWatchItem>();
        if (!m.IsMerged || net <= 0 || items.Count == 0)
        {
            if (_key.on || _spans.Count > 0 || _items.Count > 0) { Cancel(); _key = default; _spans = Array.Empty<ReplicaSpan>(); _items = Array.Empty<ReplicaWatchItem>(); _version++; }
            return;
        }
        long offsets = 17;
        var parts = m.Participants;
        for (int i = 0; i < parts.Count; i++) offsets = offsets * 31 + parts[i].Node * 1_000_003L + parts[i].OffsetTicks;
        var key = (true, m.Facts.ExerciseId, m.PrimaryNode, net, offsets, Watch.Version);
        if (key != _key)
        {
            Cancel();
            _key = key;
            _label = "Replica watch · " + (_unitName(net) ?? ("unit " + net));
            _spans = Array.Empty<ReplicaSpan>(); _items = items.ToArray(); _version++;
            var paths = m.ParticipantPaths;   // immutable model data: safe to hand to the scan
            var off = m.NodeOffsets;
            int primary = m.PrimaryNode;
            var scanItems = _items; double threshold = Watch.ThresholdSeconds;
            var cts = _cts = new CancellationTokenSource();
            var token = cts.Token;
            Task.Run(() =>
            {
                try
                {
                    var found = _views.ScanWatchSpans(paths, off, primary, net, scanItems, threshold, token);
                    if (!token.IsCancellationRequested) _done = found;
                }
                catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or ObjectDisposedException) { /* a file that cannot be re-read: no lane */ }
            });
            return;
        }
        if (_done is { } done) { _done = null; _spans = done; _version++; }
    }

    private void Cancel()
    {
        _cts?.Cancel(); _cts = null; _done = null;
    }

    public void Dispose() => Cancel();
}
