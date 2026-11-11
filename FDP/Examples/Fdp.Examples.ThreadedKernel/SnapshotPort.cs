using System.Collections.Concurrent;
using System.Diagnostics;
using Fdp.Core;

namespace Fdp.Examples.ThreadedKernel;

/// <summary>
/// ⭐ The handoff the kernel does not have yet (§13.3): the worker PUBLISHES a fresh snapshot at its sync point; the main thread LEASES the
/// latest one each frame and releases it. A snapshot is never overwritten while leased — the worker always syncs into a FREE one.
/// A snapshot leaves the pool rotation when it is neither the latest nor leased.
/// </summary>
public sealed class SnapshotPort
{
    public sealed class Snapshot
    {
        public readonly EntityRepository World;
        public long Tick;            // the worker tick it was published at
        public int Leases;           // guarded by the port's lock
        /// <summary>⭐ The events since the previous publish, COPIED on the worker (the accumulator's windows are a reused buffer, valid only
        /// until the next capture on the kernel's thread — the main thread must never read them). Consumed ONCE per snapshot.</summary>
        public readonly List<Bounce> Bounces = new();
        public Snapshot(EntityRepository world) => World = world;
    }

    private readonly object _lock = new();
    private readonly Stack<Snapshot> _free = new();
    private readonly WorkerStats _stats;
    private Snapshot? _latest;
    private int _created;

    private readonly EventAccumulator _events; private int _reader = -1;
    public SnapshotPort(WorkerStats stats, EventAccumulator events) { _stats = stats; _events = events; }

    public int SnapshotsCreated => _created;

    /// <summary>WORKER: sync the live world into a free snapshot and make it the latest.</summary>
    public void Publish(EntityRepository live)
    {
        var sw = Stopwatch.StartNew();
        Snapshot target;
        lock (_lock)
        {
            if (_free.Count > 0) target = _free.Pop();
            else
            {
                var repo = new EntityRepository();
                Schema.Register(repo);
                target = new Snapshot(repo);
                _created++;
            }
        }
        // The copy runs OUTSIDE the lock: only the worker touches a free snapshot.
        target.World.SyncFrom(live);
        target.World.CopyClockFrom(live);
        target.Tick = _stats.Ticks;
        // the events since the last publish — read on the WORKER (the kernel's thread), copied into the snapshot
        if (_reader < 0) _reader = _events.RegisterReaderAtCurrentFrame();
        target.Bounces.Clear();
        if (!_events.WindowIsCurrentFrame(_reader)) foreach (var b in _events.ReadWindow<Bounce>(_reader)) target.Bounces.Add(b);
        else foreach (var b in live.Bus.Read<Bounce>()) target.Bounces.Add(b);   // the window is just the current frame: the bus's read buffer
        _events.MarkRead(_reader);
        lock (_lock)
        {
            var old = _latest;
            _latest = target;
            if (old != null && old.Leases == 0) _free.Push(old);
        }
        _stats.PublishMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>MAIN: the latest snapshot, leased (null before the first publish).</summary>
    public Snapshot? Lease()
    {
        lock (_lock)
        {
            if (_latest == null) return null;
            _latest.Leases++;
            return _latest;
        }
    }

    /// <summary>MAIN: done reading — a snapshot that is no longer the latest goes back to the free list.</summary>
    public void Release(Snapshot s)
    {
        lock (_lock)
        {
            s.Leases--;
            if (s.Leases == 0 && !ReferenceEquals(s, _latest)) _free.Push(s);
        }
    }
}

/// <summary>
/// The presentation's write path: command buffers recorded on the MAIN thread, handed over, played back on the WORKER at the start of its
/// next tick (the CommandIngressSystem). Each carries the main-thread time it was submitted at, to measure the round trip.
/// </summary>
public sealed class CommandIngress
{
    private readonly ConcurrentQueue<(EntityCommandBuffer Buffer, long SubmittedAt)> _queue = new();
    private readonly ConcurrentBag<EntityCommandBuffer> _spare = new();
    public double LastApplyLatencyMs;     // submit → applied on World A
    public int Applied, Refused;

    public EntityCommandBuffer Rent() => _spare.TryTake(out var b) ? b : new EntityCommandBuffer();
    public void Submit(EntityCommandBuffer b) => _queue.Enqueue((b, Stopwatch.GetTimestamp()));

    /// <summary>WORKER: apply every queued buffer to World A.</summary>
    public void Drain(EntityRepository live)
    {
        while (_queue.TryDequeue(out var item))
        {
            try { item.Buffer.Playback(live); Applied++; }
            catch (Exception) { Refused++; }   // ⚠ PoC: a stale command (target destroyed) — the real thing validates first (DES-012 §3.3)
            LastApplyLatencyMs = Stopwatch.GetElapsedTime(item.SubmittedAt).TotalMilliseconds;
            item.Buffer.Clear();
            _spare.Add(item.Buffer);
        }
    }
}
