using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// D4 — fans one probe stream out to several <see cref="IBlueprintProbeSink"/>s, so more than one
/// observer can watch a single run (BP-35).
///
/// <para>
/// <see cref="DebugProbe.Sink"/> is a single reference, so attaching a second debugger used to mean
/// detaching the first. Set <c>DebugProbe.Sink</c> to one of these and <see cref="Add"/> each
/// observer instead — e.g. an interactive editor session alongside a recording/trace sink.
/// </para>
///
/// <para><b>Concurrency.</b> The sink list is copy-on-write: mutations take a lock and publish a new
/// array, while the probe path reads one <c>volatile</c> reference and iterates it. A sink added or
/// removed mid-tick therefore takes effect on the next event, and never tears or throws mid-iteration.
/// </para>
///
/// <para><b>Allocation.</b> Every forwarding member snapshots the array into a local and walks it
/// with an index loop — no enumerator, no closure, no allocation on the probe path. This matters:
/// probes fire per node-enter and per watched pin change, and <c>ProbeOverheadTests</c> holds the
/// budget.
/// </para>
///
/// <para><b>Exceptions are NOT swallowed.</b> A throwing sink propagates, exactly as it would if it
/// were wired to <c>DebugProbe.Sink</c> directly — so a broken observer is as visible here as it is
/// alone. The cost is that sinks after it miss that one event; catching instead would hide the bug,
/// which is the opposite of what a debug facility should do.
/// </para>
/// </summary>
public sealed class MultiplexingProbeSink : IBlueprintProbeSink
{
    private static readonly IBlueprintProbeSink[] s_empty = Array.Empty<IBlueprintProbeSink>();

    private readonly object _gate = new();
    private volatile IBlueprintProbeSink[] _sinks = s_empty;

    public MultiplexingProbeSink() { }

    /// <summary>Convenience: build a multiplexer over an initial set (nulls and duplicates ignored).</summary>
    public MultiplexingProbeSink(params IBlueprintProbeSink[] sinks)
    {
        if (sinks == null) return;
        foreach (var s in sinks) Add(s);
    }

    /// <summary>Current observers, in attach order. Snapshot — never mutates under the caller.</summary>
    public IReadOnlyList<IBlueprintProbeSink> Sinks => _sinks;

    public int Count => _sinks.Length;

    /// <summary>
    /// Attaches an observer. Returns <c>false</c> for <c>null</c> or an already-attached instance
    /// (reference equality), so a double-attach cannot silently double-deliver every event.
    /// </summary>
    public bool Add(IBlueprintProbeSink? sink)
    {
        if (sink == null || ReferenceEquals(sink, this)) return false;  // self-add would infinitely recurse

        lock (_gate)
        {
            var current = _sinks;
            for (int i = 0; i < current.Length; i++)
                if (ReferenceEquals(current[i], sink)) return false;

            var next = new IBlueprintProbeSink[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = sink;
            _sinks = next;
            return true;
        }
    }

    /// <summary>Detaches an observer. Returns <c>false</c> when it was not attached.</summary>
    public bool Remove(IBlueprintProbeSink? sink)
    {
        if (sink == null) return false;

        lock (_gate)
        {
            var current = _sinks;
            int index = -1;
            for (int i = 0; i < current.Length; i++)
                if (ReferenceEquals(current[i], sink)) { index = i; break; }
            if (index < 0) return false;

            if (current.Length == 1) { _sinks = s_empty; return true; }

            var next = new IBlueprintProbeSink[current.Length - 1];
            Array.Copy(current, 0, next, 0, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            _sinks = next;
            return true;
        }
    }

    /// <summary>Detaches every observer.</summary>
    public void Clear()
    {
        lock (_gate) { _sinks = s_empty; }
    }

    // ── IBlueprintProbeSink forwarding ───────────────────────────────────────

    public void OnNodeEnter(Entity self, string nodeId)
    {
        var sinks = _sinks;
        for (int i = 0; i < sinks.Length; i++) sinks[i].OnNodeEnter(self, nodeId);
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged
    {
        var sinks = _sinks;
        for (int i = 0; i < sinks.Length; i++) sinks[i].OnPinValueChanged(self, pinId, value);
    }

    public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName)
    {
        var sinks = _sinks;
        for (int i = 0; i < sinks.Length; i++) sinks[i].OnPeerCallEnter(self, peerAssetIdString, methodName);
    }

    public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName)
    {
        var sinks = _sinks;
        for (int i = 0; i < sinks.Length; i++) sinks[i].OnPeerCallExit(self, peerAssetIdString, methodName);
    }

    /// <summary>
    /// Forwarded explicitly rather than inherited from the interface's default no-op implementation —
    /// a default-implemented member is NOT virtual-dispatched through this composite, so relying on
    /// it would drop the never-silent collection-write diagnostic for every inner sink.
    /// </summary>
    public void OnCollectionWriteFailed(Entity self, string nodeId, string op, string reason)
    {
        var sinks = _sinks;
        for (int i = 0; i < sinks.Length; i++) sinks[i].OnCollectionWriteFailed(self, nodeId, op, reason);
    }

    /// <summary>
    /// Forwards the per-tick reset to every attached sink that is a debug session.
    ///
    /// <para>
    /// Needed because <see cref="DebugProbe.NewTick"/> resolves the session with
    /// <c>Sink as IBlueprintDebugSession</c>. This composite is a probe sink, not a session — it
    /// deliberately does not implement that far larger interface (breakpoints, watches, filters,
    /// attach/detach) — so without this hop the cast would fail and every session behind the
    /// multiplexer would silently stop receiving <c>OnNewTick</c>, quietly breaking per-frame
    /// breakpoint dedup.
    /// </para>
    /// </summary>
    public void NotifyNewTick()
    {
        var sinks = _sinks;
        for (int i = 0; i < sinks.Length; i++)
            (sinks[i] as IBlueprintDebugSession)?.OnNewTick();
    }
}
