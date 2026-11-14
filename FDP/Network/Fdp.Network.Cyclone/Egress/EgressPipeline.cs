using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Network.Cyclone.Egress
{
    /// <summary>Where the state scans of the <see cref="IOffFrameEgress"/> translators run.</summary>
    public enum EgressMode
    {
        /// <summary>In their egress system's slot, on the live world, on the main thread — today's behaviour.</summary>
        OnFrame = 0,
        /// <summary>On a replica synced at the end of <c>Export</c>, on background threads, while the next frame runs (one frame late).</summary>
        OffFrame = 1,
    }

    /// <summary>
    /// ⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7) — the state egress of one world OFF the main thread: at the end of
    /// <c>Export</c> (<see cref="Launch"/>, by <see cref="EgressPipelineLaunchSystem"/>) the live world is synced into a replica and the
    /// <see cref="IOffFrameEgress"/> translators scan it on background threads while the next frame runs; the next <c>Export</c> joins it
    /// (<see cref="Join"/>, the first egress or cleanup system to run) before any translator is touched again — so a state sample goes out one
    /// frame late and in order, an event (a shot) is never delayed, and a dispose is never overtaken.
    /// <para>⭐ An EXPERIMENT, OFF by default (user, 2026-11-10): <c>FDP_EGRESS_OFFFRAME=1</c> or <see cref="RequestedMode"/> at runtime (the
    /// debug API); the mode changes only at a join, when no scan runs. A scan that throws puts the pipeline back on the frame by itself.</para>
    /// <para>⭐ One per world, created by the world's launch system — a world with none (a CGF, a test without one) never goes off the frame,
    /// whatever the switch says: nothing would scan what its egress systems skip.</para>
    /// </summary>
    public sealed class EgressPipeline : IDisposable
    {
        private static readonly ConditionalWeakTable<EntityRepository, EgressPipeline> s_of = new();

        /// <summary>The switch's environment default: <c>FDP_EGRESS_OFFFRAME=1</c> ⇒ <see cref="EgressMode.OffFrame"/>.</summary>
        public static EgressMode DefaultMode
            => EnvVars.Read(EnvVars.FdpEgressOffframe) == "1" ? EgressMode.OffFrame : EgressMode.OnFrame;

        /// <summary>The pipeline of <paramref name="view"/>'s world, or none.</summary>
        public static bool TryGet(ISimulationView view, out EgressPipeline pipeline)
        {
            pipeline = null!;
            return view is EntityRepository repo && s_of.TryGetValue(repo, out pipeline!);
        }

        /// <summary>The pipeline of <paramref name="live"/> (created on first call).</summary>
        public static EgressPipeline Of(EntityRepository live, int workers = 0)
            => s_of.GetValue(live, l => new EgressPipeline(l, workers));

        /// <summary>The translators' work, in the order they scan (largest last time first).</summary>
        private sealed class Entry
        {
            public readonly IOffFrameEgress Translator;
            public readonly Fdp.ModuleHost.Scheduling.SystemProfileData? Profile;
            public double LastMs;
            public Exception? Error;
            public Entry(IOffFrameEgress t, Fdp.ModuleHost.Scheduling.SystemProfileData? p) { Translator = t; Profile = p; }
        }

        private readonly EntityRepository _live;
        private readonly EntityRepository _replica = new();
        private readonly List<Entry> _entries = new();
        private readonly HashSet<IOffFrameEgress> _registered = new();
        private Entry[] _order = Array.Empty<Entry>();
        private readonly int _workers;
        private readonly Action<int> _scanOne;
        private Thread? _thread;

        /// <summary>
        /// The scan thread's hand-off. ⭐ The thread holds THIS and a weak reference to the pipeline, never the pipeline itself: a pipeline
        /// holds its live world and a replica, and a thread that rooted it leaked two whole worlds per host — 📐 the integration suite ran out
        /// of memory with FDP_EGRESS_OFFFRAME=1 (2026-11-10). A pipeline nobody holds is collected; its finalizer ends the thread.
        /// </summary>
        private sealed class ScanSignal
        {
            public readonly SemaphoreSlim Go = new(0);
            public readonly ManualResetEventSlim Done = new(true);
            public volatile bool Stop;
        }

        private readonly ScanSignal _signal = new();
        private bool _running;
        private EgressMode _mode = EgressMode.OnFrame;
        // ⭐ the mode is taken ONCE per Export: by the first join after the last launch. ⛔ Every join re-reading the request let a switch that
        //   landed mid-Export (an HTTP thread) change the mode between a system that skipped its translators and the launch that should scan them
        //   — a frame lost or scanned twice (found by its rail, A_switch_requested_in_the_middle_of_Export_waits_for_the_next_join).
        private bool _modeOpen = true;
        private int _faultReported;

        private EgressPipeline(EntityRepository live, int workers)
        {
            _live = live;
            _workers = workers > 0 ? workers : DefaultWorkers();
            RequestedMode = DefaultMode;
            _scanOne = ScanOne;
        }

        private static int DefaultWorkers()
        {
            var env = EnvVars.Read(EnvVars.FdpEgressWorkers);
            if (int.TryParse(env, out int n) && n > 0) return n;
            return Math.Max(1, Math.Min(4, Environment.ProcessorCount / 4));
        }

        /// <summary>The mode asked for (the switch); it takes effect at the next <see cref="Join"/>.</summary>
        public EgressMode RequestedMode { get; set; }

        /// <summary>The mode in force for this frame's <c>Export</c> (set at the join).</summary>
        public EgressMode Mode => _mode;

        /// <summary>True while the marked translators are scanned off the frame.</summary>
        public bool IsOffFrame => _mode == EgressMode.OffFrame;

        /// <summary>Threads a scan uses (the scanning thread included); 1 ⇒ one translator after another.</summary>
        public int Workers => _workers;

        /// <summary>How many scans ran off the frame; how many threw (each put the pipeline back on the frame).</summary>
        public long Scans { get; private set; }
        public long Faults { get; private set; }

        /// <summary>The last scan's time (ms, its own thread), the last join's wait (ms, the main thread), the last launch's sync (ms).</summary>
        public double LastScanMs { get; private set; }
        public double LastJoinWaitMs { get; private set; }
        public double LastSyncMs { get; private set; }

        /// <summary>The translators registered (diagnostics, rails).</summary>
        public int TranslatorCount => _entries.Count;

        /// <summary>The replica the scans read (rails).</summary>
        internal EntityRepository Replica => _replica;

        /// <summary>An egress system hands over its marked translators, once (main thread).</summary>
        public void Register(IOffFrameEgress translator, Fdp.ModuleHost.Scheduling.SystemProfileData? profile)
        {
            if (!_registered.Add(translator)) return;
            _entries.Add(new Entry(translator, profile));
            _order = _entries.ToArray();
        }

        /// <summary>
        /// ⭐ The join — main thread, before any marked translator is touched in this <c>Export</c>: waits for the scan launched by the
        /// previous frame (usually done), makes its queued world writes on the live world, and — the first join of an Export only — takes the
        /// requested mode. Cheap and idempotent: every egress and cleanup system calls it first.
        /// </summary>
        public void Join()
        {
            if (_running)
            {
                long t0 = Stopwatch.GetTimestamp();
                _signal.Done.Wait();
                LastJoinWaitMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                _running = false;
                bool faulted = false;
                for (int i = 0; i < _order.Length; i++)
                {
                    var e = _order[i];
                    if (e.Error != null)
                    {
                        faulted = true;
                        if (Interlocked.Exchange(ref _faultReported, 1) == 0)
                            Console.Error.WriteLine($"[EgressPipeline] the off-frame scan of {e.Translator.TopicName} threw — back ON the frame: {e.Error}");
                        e.Error = null;
                    }
                    e.Profile?.RecordExecution(e.LastMs);
                    e.Translator.ApplyDeferred(_live);
                }
                if (faulted) { Faults++; RequestedMode = EgressMode.OnFrame; }
            }
            if (_modeOpen) { _mode = RequestedMode; _modeOpen = false; }
        }

        /// <summary>
        /// ⭐ The launch — main thread, LAST in <c>Export</c>: syncs the replica, hands it the egress signals, and starts the scan. Nothing
        /// while on the frame, with nothing registered, or while the world's egress is held (<c>EgressHold</c> — today's egress skips too).
        /// </summary>
        public void Launch()
        {
            Join();                 // a no-op when an egress or cleanup system already joined this Export (the mode stays this frame's)
            _modeOpen = true;       // the next Export takes the requested mode at its first join
            if (_mode != EgressMode.OffFrame || _order.Length == 0) return;
            if (Fdp.Toolkit.Replication.Services.EgressHold.IsHeld(_live)) return;

            long t0 = Stopwatch.GetTimestamp();
            _replica.SyncFrom(_live);
            _replica.CopyClockFrom(_live);
            Fdp.Toolkit.Replication.Utilities.EgressSignals.CopyTo(_live, _replica);
            LastSyncMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

            if (_thread == null)
            {
                var weak = new WeakReference<EgressPipeline>(this);
                var signal = _signal;
                _thread = new Thread(() => Loop(weak, signal)) { IsBackground = true, Name = "Egress off-frame scan" };
                _thread.Start();
            }
            _signal.Done.Reset();
            _running = true;
            _signal.Go.Release();
        }

        private static void Loop(WeakReference<EgressPipeline> weak, ScanSignal signal)
        {
            while (true)
            {
                signal.Go.Wait();
                if (signal.Stop || !weak.TryGetTarget(out var pipeline)) { signal.Done.Set(); return; }
                try { pipeline.RunScan(); }
                finally { signal.Done.Set(); }
                pipeline = null!;   // ⛔ never held across the wait (a Debug JIT keeps a local alive to the method's end)
            }
        }

        private void RunScan()
        {
            long t0 = Stopwatch.GetTimestamp();
            try { ScanAll(); }
            finally
            {
                LastScanMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                Scans++;
            }
        }

        private void ScanAll()
        {
            // ⭐ O-E — the translators over the shared worker pool, the largest (by its last scan) first, so the scan lasts about as long as
            //   its largest translator. ⛔ The candidate index's scope is opened HERE, on one thread, and built before the fan-out: the
            //   workers only read it (EntityRepository.PrepareQueryIndex).
            for (int i = 1; i < _order.Length; i++)   // insertion sort: a few dozen entries, nearly sorted, and no comparer allocated (R-2084)
            {
                var e = _order[i];
                int j = i - 1;
                while (j >= 0 && _order[j].LastMs < e.LastMs) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = e;
            }
            using var scope = _replica.QueryIndexScope();
            _replica.PrepareQueryIndex();
            if (_workers > 1 && _order.Length > 1 && Fdp.Core.Jobs.FdpWorkerPool.Shared.TryRun(_order.Length, _scanOne)) return;   // ⭐ POC 4 J-1 — the ONE pool, background lane
            for (int i = 0; i < _order.Length; i++) ScanOne(i);
        }

        private void ScanOne(int i)
        {
            var e = _order[i];
            long t0 = Stopwatch.GetTimestamp();
            try { e.Translator.ScanState(_replica, offFrame: true); }
            catch (Exception ex) { e.Error = ex; }
            e.LastMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        /// <summary>Waits for a running scan and stops the scanning thread.</summary>
        public void Dispose()
        {
            if (_running) { _signal.Done.Wait(); _running = false; }
            _signal.Stop = true;
            _signal.Go.Release();
            s_of.Remove(_live);
            GC.SuppressFinalize(this);
        }

        /// <summary>A pipeline nobody disposed (its host just went away): end the scan thread.</summary>
        ~EgressPipeline()
        {
            _signal.Stop = true;
            _signal.Go.Release();
        }
    }
}
