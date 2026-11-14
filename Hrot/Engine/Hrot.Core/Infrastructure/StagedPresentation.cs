using System;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Threading;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// ⭐⭐⭐ CE-3446 (docs/DESIGN_Threaded_Simulation_Presentation.md §15) — THE presentation side of a threaded node, one implementation for every
/// windowed host. The node ticks world S (<see cref="Sim"/>, the context's <c>World</c>) on the sim worker; the UI draws world A (<see cref="World"/>),
/// which takes the latest complete tick from the staging at its frame start (<see cref="Pull"/>); every UI write on A becomes a job on S
/// (<see cref="SimLink"/>, <see cref="SimJobs.Run(EntityRepository, Action{EntityRepository}, string)"/>). Nothing else crosses the threads.
/// <para>Composed ONLY by <c>SharedApplicationBootstrapper.BootstrapNode</c> from <see cref="HrotNodeConfig.HasPresentation"/> and the threaded
/// switch (R-4215: no host builds it itself). A host's differences are its HOOKS (<see cref="SimBefore"/>, <see cref="NodeFrame"/>,
/// <see cref="SimAfter"/>, <see cref="UiAfterPull"/>) and the UI-owned singletons it adds (<see cref="UiOwnedSingleton{T}"/>).</para>
/// <para>📐 Was <c>EditorSubsystem</c>'s (ComposeInitialPresentationCopy, SimStep, PullStaging, SimStepOnHostWorker) — the player had none, so a
/// threaded player drew nothing (<c>SubsystemOrchestrator.Draws</c>).</para>
/// </summary>
public sealed class StagedPresentation : IDisposable
{
    /// <summary>World A — the presentation's. Exists from construction, so a host's registrar can bind UI objects to it inside the bootstrap.</summary>
    public EntityRepository World { get; } = new();

    /// <summary>World S — the node's (set by <see cref="Compose"/>).</summary>
    public EntityRepository Sim { get; private set; } = null!;

    /// <summary>S's entity map (the node's), captured at <see cref="Compose"/> before the guard arms; null when the node has none.</summary>
    public NetworkEntityMap? SimMap { get; private set; }

    /// <summary>A's own entity map (rebuilt at each pull).</summary>
    public NetworkEntityMap Map { get; } = new();

    /// <summary>The UI's mirror of the node's orchestration bus (<c>HrotNodeContext.EventBus</c>): what the UI's read models read.</summary>
    public FdpEventBus UiEventBus { get; } = new();

    /// <summary>The cluster as the UI thread reads it (over <see cref="UiEventBus"/>).</summary>
    public Fdp.Toolkit.Orchestration.ClusterView UiClusterView { get; private set; } = null!;

    /// <summary>The UI → S queue (drained on the sim thread between ticks).</summary>
    public SimJobQueue Jobs { get; } = new();

    /// <summary>The S → A buffers (published by the sim after each tick).</summary>
    public StagingWorlds Staging { get; private set; } = null!;

    /// <summary>The component tables A takes (every snapshotable one unless a mask file names fewer).</summary>
    public BitMask512 TableMask;

    /// <summary>The singletons A owns (never copied from S) — the UI's own state.</summary>
    public BitMask512 SingletonExcept;

    /// <summary>The table mask is every snapshotable table (no mask file).</summary>
    public bool MaskAll { get; private set; } = true;

    /// <summary>The sim and the UI run on one thread (<c>HROT_THREADED_SINGLE=1</c>): the same step, no worker.</summary>
    public bool SingleThread => Fdp.ModuleHost.ThreadedKernelGate.ComposedSingleThread;

    // ── the host's hooks (its differences — the ORDER is this class's) ──────────────────────────────────────────────────────────────────
    /// <summary>Sim thread, after the jobs and uplinks, before the node frame (the host's sim-side work: a logic pump, its HTTP routes).</summary>
    public Action? SimBefore { get; set; }
    /// <summary>Sim thread: the host's node frame (<c>HrotNodeContext.Tick</c> with its own hooks). Required.</summary>
    public Action<float>? NodeFrame { get; set; }
    /// <summary>Sim thread, after the node frame, before the publish (readouts posted with <see cref="Staging"/>.Post).</summary>
    public Action? SimAfter { get; set; }
    /// <summary>UI thread, after a pull that took a newer tick; the argument is the UI time since the previous pulled tick.</summary>
    public Action<float>? UiAfterPull { get; set; }

    private long _tick;
    private readonly object _dtLock = new();
    private double _pendingDt;
    private float _pulledDt;
    private bool _hostWorker, _workerArmed, _composed;
    private Thread? _ownWorker;
    private volatile bool _stop;
    private long _faults;
    private readonly Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings _debugMirror = new();
    private volatile bool _debugMirrorJobPending;
    private Action<EntityRepository>? _debugMirrorJob;

    /// <summary>Faults the sim step caught (each logged, the first 20).</summary>
    public long Faults => Interlocked.Read(ref _faults);

    /// <summary>A singleton the UI owns on A (its own state, never overwritten by S). Before <see cref="Compose"/>.</summary>
    public void UiOwnedSingleton<T>() where T : class => SingletonExcept.SetBit(ManagedComponentType<T>.ID);

    /// <summary>
    /// The initial copy and the plumbing, once the node is built: A takes S's tables, singletons and event registrations; A gets its own entity map,
    /// the job queue, the staging (the orchestration bus mirrored), the link that turns UI writes into jobs, and the world guard.
    /// <paramref name="installUiOwned"/> installs the UI-owned singletons A needs from the start (the station mode on A).
    /// </summary>
    public void Compose(HrotNodeContext node, Action<EntityRepository>? installUiOwned)
    {
        if (_composed) throw new InvalidOperationException("The staged presentation is composed once.");
        _composed = true;
        var a = World; var s = node.World;
        Sim = s;
        SimMap = s.HasSingletonManaged<NetworkEntityMap>() ? s.GetSingletonManaged<NetworkEntityMap>() : null;
        a.SyncFrom(s, s.GetSnapshotableMask(true), includeTransient: true);
        a.CopySingletonsFrom(s, default);
        a.Bus.CopyRegistrationsFrom(s.Bus);
        installUiOwned?.Invoke(a);
        SingletonExcept.SetBit(ManagedComponentType<NetworkEntityMap>.ID);
        SingletonExcept.SetBit(ManagedComponentType<SimLink>.ID);
        Map.RebuildFromWorld(a);
        a.SetSingletonManaged(Map);
        // a measurement may name fewer tables (HROT_TWO_WORLDS_MASKS + HROT_TWO_WORLDS_MASK); the product copies every snapshotable one
        string? maskPath = Environment.GetEnvironmentVariable("HROT_TWO_WORLDS_MASKS");
        string maskName = Environment.GetEnvironmentVariable("HROT_TWO_WORLDS_MASK") is { Length: > 0 } mn ? mn : "ui-no-inspector";
        var loaded = maskPath != null ? Fdp.ModuleHost.PublishProbe.LoadMask(maskPath, maskName) : null;
        MaskAll = loaded == null;
        var keep = TableMask;   // the tables a host already declared the UI's own (cleared below)
        TableMask = loaded?.Mask ?? s.GetSnapshotableMask(true);
        for (int i = 0; i < 512; i++) if (keep.IsSet(i)) TableMask.ClearBit(i);
        Staging = new StagingWorlds { TableMask = TableMask, SingletonExcept = SingletonExcept };
        Staging.RegisterStreamsFrom(s.Bus);
        Jobs.Outbox = Staging;
        Staging.AddBusPair(node.EventBus, UiEventBus);
        UiClusterView = new Fdp.Toolkit.Orchestration.ClusterView(UiEventBus, (int)node.NodeId);
        WorldGuard.Strict = !SingleThread;   // no lock: every off-thread read of S is a race
        a.SetSingletonManaged(new SimLink(Jobs, s));
        WorldGuard.ArmSyncMask(TableMask, SingletonExcept);
        WorldGuard.Arm(s, a, -1);   // until a worker arms with its id, the guard decides by the tracer's context
        Console.WriteLine("[STAGED] the node over S, the presentation over A; table mask " + (MaskAll ? "ALL types" : "'" + maskName + "'")
                          + "; single-threaded: " + (SingleThread ? "yes" : "no"));
    }

    /// <summary>
    /// ⭐⭐ CE-3446 — a READOUT: what the UI reads of a sim-side object that is not world data (a session, Run to …, the deploy flow). After every
    /// step <paramref name="readIfChanged"/> runs on the sim thread with the last readout it returned and answers a NEW immutable one only when
    /// something changed (null otherwise); <paramref name="applyOnUi"/> applies it on the UI thread, in order with the pull. 0 B when nothing changed.
    /// The UI's verbs go the other way as jobs (<see cref="Post"/> / <see cref="SimJobs.Run(EntityRepository, Action{EntityRepository}, string)"/>).
    /// </summary>
    public void AddReadout<T>(Func<T?, T?> readIfChanged, Action<T> applyOnUi) where T : class
        => _readouts.Add(new Readout<T>(readIfChanged, applyOnUi));

    private interface IReadout { void PublishIfChanged(StagingWorlds staging); }
    private sealed class Readout<T> : IReadout where T : class
    {
        private readonly Func<T?, T?> _read; private readonly Action<T> _apply; private T? _last;
        private readonly Action<EntityRepository> _applyLast;
        private T? _toApply;
        public Readout(Func<T?, T?> read, Action<T> apply)
        {
            _read = read; _apply = apply;
            _applyLast = _ => { if (Volatile.Read(ref _toApply) is { } r) _apply(r); };   // cached: a change allocates only its readout
        }
        public void PublishIfChanged(StagingWorlds staging)
        {
            if (_read(_last) is not { } r) return;
            _last = r;
            Volatile.Write(ref _toApply, r);
            staging.Post(_applyLast);
        }
    }
    private readonly System.Collections.Generic.List<IReadout> _readouts = new();

    /// <summary>
    /// ⭐⭐ CE-3446 — an UPLINK: what the SIM reads of a UI-side object (the 3-D map's build, which a load waits for). At every pull (each UI frame)
    /// <paramref name="readIfChangedOnUi"/> runs on the UI thread with the last value it returned and answers a NEW immutable one only when
    /// something changed; the returned reader gives the sim the latest (null before the first UI frame). The readout channel, the other way.
    /// </summary>
    public Func<T?> AddUplink<T>(Func<T?, T?> readIfChangedOnUi) where T : class
    {
        var u = new Uplink<T>(readIfChangedOnUi);
        _uplinks.Add(u);
        return u.Latest;
    }

    private interface IUplink { void Refresh(); }
    private sealed class Uplink<T> : IUplink where T : class
    {
        private readonly Func<T?, T?> _read; private T? _latest;
        public Uplink(Func<T?, T?> read) => _read = read;
        public void Refresh() { if (_read(Volatile.Read(ref _latest)) is { } v) Volatile.Write(ref _latest, v); }
        public T? Latest() => Volatile.Read(ref _latest);
    }
    private readonly System.Collections.Generic.List<IUplink> _uplinks = new();

    /// <summary>A table the UI owns on A (its rows are the UI's, never copied from S). Before <see cref="Compose"/>.</summary>
    public void UiOwnedTable<T>() where T : unmanaged => TableMask.SetBit(ComponentType<T>.ID);

    /// <summary>Posts a named action to the sim thread.</summary>
    public void Post(string label, Action action) => Jobs.Post(_ => action(), label);

    /// <summary>UI thread: the frame's time, for the sim worker (never more than a quarter second behind).</summary>
    public void AddTime(float dt)
    {
        lock (_dtLock) _pendingDt += dt;
        _pulledDt += dt;
    }

    /// <summary>
    /// UI thread, the frame start: A takes the latest complete tick (when newer), A's entity map and the cluster view follow, the host's
    /// <see cref="UiAfterPull"/> runs, and the per-unit debug requests made on A ride to S as a job.
    /// </summary>
    public bool Pull()
    {
        var a = World;
        for (int i = 0; i < _uplinks.Count; i++) _uplinks[i].Refresh();   // UI → sim: refreshed every UI frame, pulled tick or not
        bool pulled;
        using (AccessTrace.Scope("ui: staging pull", TraceContext.Presentation)) pulled = Staging.Pull(a);
        if (!pulled) return false;
        Map.RebuildFromWorld(a);
        UiClusterView.Update();
        float dt = _pulledDt; _pulledDt = 0f;
        UiAfterPull?.Invoke(dt);
        // the per-unit debug requests the UI made ride to S as a job: copied here into a mirror the job reads on the sim thread — one job in
        // flight at a time, the delegate cached (0 B per pull)
        if (!_debugMirrorJobPending && Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Peek(a) is { } uiDebug)
        {
            _debugMirror.TakeRequestsFrom(uiDebug);
            _debugMirrorJobPending = true;
            Jobs.Post(_debugMirrorJob ??= sim =>
            {
                Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Of(sim).TakeRequestsFrom(_debugMirror);
                _debugMirrorJobPending = false;
            }, "debug settings → S");
        }
        return true;
    }

    /// <summary>
    /// Sim thread: ONE step — the UI's jobs and uplinks (applied to S between ticks, in order), the host's sim-side work, the node frame, the host's
    /// readouts, then the publish into the buffer the UI is not pulling (the sim never waits for the UI).
    /// </summary>
    public void Step(float dt)
    {
        var s = Sim;
        using (AccessTrace.Scope("sim: jobs (UI writes, run controls)", TraceContext.Tick)) Jobs.Drain(s);
        Staging.TakeUplinks(s);
        SimBefore?.Invoke();
        (NodeFrame ?? throw new InvalidOperationException("The host gave the staged presentation no node frame."))(dt);
        SimAfter?.Invoke();
        for (int i = 0; i < _readouts.Count; i++) _readouts[i].PublishIfChanged(Staging);
        Staging.Publish(s, ++_tick);
    }

    /// <summary>The single-threaded form (<see cref="SingleThread"/>, or a headless host): the step, then the pull, on this thread.</summary>
    public void StepAndPull(float dt)
    {
        _pulledDt += dt;
        Step(dt);
        Pull();
    }

    /// <summary>
    /// UI thread, a host's frame start: single-threaded (<see cref="SingleThread"/>) the step and the pull here; otherwise the sim worker (the
    /// host's, or this object's own when the process starts none) gets the time and A pulls the latest complete tick.
    /// </summary>
    public void UiFrame(float dt)
    {
        if (SingleThread) { StepAndPull(dt); return; }
        if (!_hostWorker) EnsureOwnWorker();
        AddTime(dt);
        Pull();
    }

    /// <summary>The host's one sim worker runs <see cref="StepOnHostWorker"/> (no thread of this object's own).</summary>
    public void UseHostSimWorker() => _hostWorker = true;

    /// <summary>Is the step run by the host's sim worker?</summary>
    public bool OnHostWorker => _hostWorker;

    /// <summary>The host's sim worker: one step of the pending time; false when there was none.</summary>
    public bool StepOnHostWorker()
    {
        if (!_hostWorker || !_composed || _stop) return false;
        if (!_workerArmed) { _workerArmed = true; WorldGuard.Arm(Sim, World, Environment.CurrentManagedThreadId); }
        float dt = TakePendingDt();
        if (dt <= 0f) return false;
        RunStep(dt, "host worker");
        return true;
    }

    /// <summary>No host worker (a process that does not start one): this object's own sim thread. Idempotent.</summary>
    public void EnsureOwnWorker()
    {
        if (_hostWorker || _ownWorker != null || !_composed) return;
        _ownWorker = new Thread(OwnWorkerLoop) { IsBackground = true, Name = "HROT sim (staged presentation)" };
        _ownWorker.Start();
    }

    private void OwnWorkerLoop()
    {
        WindowsScheduling.OptOutCurrentThread();
        WorldGuard.Arm(Sim, World, Environment.CurrentManagedThreadId);
        long lastReport = Environment.TickCount64;
        while (!_stop)
        {
            float dt = TakePendingDt();
            if (dt <= 0f) { Thread.Sleep(1); continue; }
            RunStep(dt, "own worker");
            if (Environment.TickCount64 - lastReport > 5000) { Console.WriteLine(Report()); lastReport = Environment.TickCount64; }
        }
    }

    private float TakePendingDt()
    {
        lock (_dtLock)
        {
            float dt = (float)Math.Min(_pendingDt, 0.25);
            _pendingDt = _pendingDt - dt > 0.25 ? 0 : _pendingDt - dt;   // never more than a quarter second behind
            return dt;
        }
    }

    private void RunStep(float dt, string where)
    {
        try { Step(dt); }
        catch (Exception ex)
        {
            if (Interlocked.Increment(ref _faults) <= 20) Console.WriteLine("[STAGED] sim step fault (" + where + "): " + ex);
        }
        Fdp.ModuleHost.ThreadedKernelGate.Ticks++;
    }

    /// <summary>The staging's counters, one line (language-neutral).</summary>
    public string Report() => FormattableString.Invariant(
        $"[STAGING] published {Staging.Published} · skipped {Staging.Skipped} · pulled {Staging.Pulled} · last publish {Staging.LastPublishMs:F2} ms (max {Staging.MaxPublishMs:F2}) · last pull {Staging.LastPullMs:F2} ms · jobs run {Jobs.Executed} (faults {Jobs.Faults}) · step faults {Faults} · guard hits {WorldGuard.DistinctHits}");

    /// <summary>Stops this object's own worker (a host worker is the host's).</summary>
    /// <remarks>⭐ CE-4574 (X-5) — the own worker is JOINED before the worlds go (no timeout: a step still running would publish into a freed
    /// staging world). A host worker is stopped by its host first (<c>SubsystemOrchestrator.Shutdown</c> → <c>StopSimWorker</c>); a host step
    /// that still arrives after this finds <c>_stop</c> and does nothing.</remarks>
    public void Dispose()
    {
        _stop = true;
        if (_ownWorker is { } w && w != Thread.CurrentThread && !w.Join(5000))
        {
            Console.WriteLine("[STAGED] waiting for the sim worker to finish its step before the worlds are disposed …");
            w.Join();
        }
        Staging?.Dispose();
        World.Dispose();
    }
}
