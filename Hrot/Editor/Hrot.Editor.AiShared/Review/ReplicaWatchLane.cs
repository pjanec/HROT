using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.ReplayBrowser.Federation;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ G-7 / CE-4354 S5 (docs/UX/DESIGN_Review_Perspective.md §6.4 I5, M-5) — the data of the Event Timeline's ONE lane <i>Replica differs · &lt;unit&gt;</i>: the frames (of the primary node's recording) at which any
/// replica of the SELECTED unit differs from its owner beyond ε (<see cref="ReplicaViews.ScanDifferingFrames"/> — the same differ as Details ▸ By node, ONE implementation). It exists only in the MERGED view
/// with a unit selected. The scan runs in the background over its own reader of the group's files and is restarted when the selection, the group or a node offset changes; <see cref="Update"/> (once a frame, from
/// <see cref="ReviewTimelineTracks.Tracks"/>) only compares a key — 0 B steady — and lands a finished scan (<see cref="Version"/> moves).
/// </summary>
public sealed class ReplicaDifferLane : IDisposable
{
    private readonly ReplicaViews _views;
    private readonly Func<FederatedReplayManager?> _manager;
    private readonly Func<int?> _selection;
    private readonly Func<int, string?> _unitName;

    private (FederatedReplayManager? m, int net, long offsets) _key;
    private CancellationTokenSource? _cts;
    private volatile IReadOnlyList<int>? _done;
    private IReadOnlyList<int> _frames = Array.Empty<int>();
    private string _label = "Replica differs";
    private int _version;

    /// <param name="manager">The merged view's manager, or null outside the merged view (no lane).</param>
    /// <param name="selection">The selected unit's network id, or null.</param>
    /// <param name="unitName">The unit's name for the lane's label (null ⇒ its id).</param>
    public ReplicaDifferLane(ReplicaViews views, Func<FederatedReplayManager?> manager, Func<int?> selection, Func<int, string?>? unitName = null)
    {
        _views = views ?? throw new ArgumentNullException(nameof(views));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _unitName = unitName ?? (_ => null);
    }

    /// <summary>The frames (primary node's frame indices) where a replica of the selected unit differs; empty when no scan has landed / not merged.</summary>
    public IReadOnlyList<int> Frames => _frames;

    /// <summary>The lane's label: <c>Replica differs · &lt;unit&gt;</c>.</summary>
    public string Label => _label;

    /// <summary>Moves whenever <see cref="Frames"/> or <see cref="Label"/> changed (a reader rebuilds only then).</summary>
    public int Version => _version;

    /// <summary>A scan is running.</summary>
    public bool Scanning => _cts != null && _done == null && _key.m != null;

    /// <summary>Once a frame: restarts the scan when the (group, unit, offsets) key moved, lands a finished one. 0 B when nothing changed.</summary>
    public void Update()
    {
        var m = _manager();
        int net = _selection() ?? 0;
        if (m == null || net <= 0)
        {
            if (_key.m != null || _frames.Count > 0) { Cancel(); _key = default; _frames = Array.Empty<int>(); _version++; }
            return;
        }
        long offsets = 17;
        foreach (var kv in m.NodeOffsets) offsets = offsets * 31 + kv.Key * 1_000_003L + kv.Value;
        var key = (m, net, offsets);
        if (key != _key)
        {
            Cancel();
            _key = key;
            _label = "Replica differs · " + (_unitName(net) ?? ("unit " + net));
            _frames = Array.Empty<int>(); _version++;
            var paths = new List<string>();
            foreach (var ctx in m.Contexts.Values) if (ctx.CurrentFdpPath is { } p) paths.Add(p);
            var off = new Dictionary<int, long>(m.NodeOffsets);
            int primary = m.LocalEntitiesProviderNodeId;
            var cts = _cts = new CancellationTokenSource();
            var token = cts.Token;
            Task.Run(() =>
            {
                try
                {
                    var found = _views.ScanDifferingFrames(paths.ToArray(), off, primary, net, token);
                    if (!token.IsCancellationRequested) _done = found;
                }
                catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or ObjectDisposedException) { /* a file that cannot be re-read: no lane */ }
            });
            return;
        }
        if (_done is { } done) { _done = null; _frames = done; _version++; }
    }

    private void Cancel()
    {
        _cts?.Cancel(); _cts = null; _done = null;
    }

    public void Dispose() => Cancel();
}
