using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// A compiled component predicate that can be evaluated against an entity.
/// </summary>
/// <param name="Delegate">The compiled predicate function.</param>
/// <param name="MandatoryComponents">Component types the entity must have (used for query filtering).</param>
public sealed record CompiledComponentPredicate(
    Func<EntityRepository, Entity, bool> Delegate,
    IReadOnlyList<Type> MandatoryComponents)
{
    /// <summary>
    /// The last <see cref="EntityRepository.GlobalVersion"/> at which this predicate was evaluated.
    /// Passed to <see cref="EntityRepository.QueryDelta"/> as <c>sinceVersion</c> to skip unchanged
    /// entity chunks. Defaults to 0 (scan everything on first evaluation). Reset to 0 automatically
    /// when the predicate is re-compiled (hot-reload) because <see cref="DataBreakpointManager.TryMountDelegate"/>
    /// creates a new <see cref="CompiledComponentPredicate"/> instance.
    /// </summary>
    public uint LastScanVersion { get; set; } = 0u;
}

/// <summary>
/// A compiled event scanner that checks a live bus for matching events each tick.
/// Holds a pre-allocated result buffer to avoid per-tick allocation.
/// </summary>
public sealed record CompiledEventScanner(EventScannerDelegate Delegate)
{
    private readonly List<SearchResultDto> _buffer = new(4);

    /// <summary>
    /// Scans <paramref name="bus"/> for matching events.
    /// Returns <c>true</c> if at least one matching event is present.
    /// </summary>
    public bool Evaluate(FdpEventBus bus, EntityRepository repo)
    {
        _buffer.Clear();
        Delegate(bus, 0, 0L, _buffer, repo, null);
        return _buffer.Count > 0;
    }
}

/// <summary>Delegate type for compiled spatial-position accessors.</summary>
internal delegate Vector2 SpatialPositionDelegate<T>(ref T component) where T : unmanaged;

/// <summary>
/// Concrete implementation of <see cref="IDataBreakpointManager"/>.
///
/// Triple-buffer contract (see DESIGN.md §5):
///   _preTickSnapshot  — filled by <see cref="DebugSnapshotProvider"/> every BeforeSync tick
///                        while at least one breakpoint is enabled.
///   _postTickSnapshot — filled exactly when a predicate fires (captures state at hit time).
///   _liveRepo         — the authoritative live <see cref="EntityRepository"/> owned by the kernel.
///
/// Reference-counted gate:
///   _activeBreakpointCount tracks the number of enabled breakpoints.
///   0 → 1: calls <see cref="DebugSnapshotProvider.SetEnabled(bool)"/> with true.
///   1 → 0: calls SetEnabled with false.
/// </summary>
public sealed class DataBreakpointManager
    : IDataBreakpointManager, IActiveViewProvider, IMutationInterceptor,
      Fdp.ModuleHost.Abstractions.IStagedWrites
{
    private readonly EntityRepository _liveRepo;
    private readonly EntityRepository _preTickSnapshot;
    private readonly EntityRepository _postTickSnapshot;
    private readonly DebugSnapshotProvider _snapshotProvider;
    private readonly IEngineDebugTimeController _timeController;
    private readonly IPredicateCompiler? _predicateCompiler;
    private readonly IEventScannerCompiler? _eventScannerCompiler;
    private readonly IBreakpointNotifier? _notifier;

    private readonly Dictionary<BreakpointId, Breakpoint> _breakpoints = new();
    private readonly Dictionary<BreakpointId, CompiledComponentPredicate> _componentPredicates = new();
    private readonly Dictionary<BreakpointId, CompiledEventScanner> _eventScanners = new();

    // Structural trackers: BreakpointId -> (Breakpoint, dto, set of entities known to have the component)
    private readonly Dictionary<BreakpointId, (Breakpoint bp, StructuralPredicateDto dto, HashSet<Entity> knownSet)>
        _structuralTrackers = new();

    // Spatial trackers: BreakpointId -> (Breakpoint, dto, set of entities currently inside the bounds, compiled position accessor)
    private readonly Dictionary<BreakpointId, (Breakpoint bp, SpatialBoundingPredicateDto dto, HashSet<Entity> insideSet, Func<EntityRepository, Entity, Vector2>? posAccessor)>
        _spatialTrackers = new();

    // Lifecycle trackers: BreakpointId -> (Breakpoint, dto, set of known-alive entities)
    private readonly Dictionary<BreakpointId, (Breakpoint bp, LifecyclePredicateDto dto, HashSet<Entity> knownAlive)>
        _lifecycleTrackers = new();
    // External-hit tag predicates: tag → list of (BreakpointId, optional remaining delegate).
    // Populated for ExternalHitTagPredicateDto conditions and for CompoundPredicateDto[And]
    // that contain at least one ExternalHitTagPredicateDto child.
    // The remaining delegate evaluates the non-ExternalHitTag children against the entity.
    private readonly Dictionary<string, List<(BreakpointId id, Func<EntityRepository, Entity, bool>? remainingDelegate)>>
        _externalHitPredicates = new(StringComparer.Ordinal);
    private readonly List<(Breakpoint bp, Entity entity)> _statefulHitsBuffer = new();
    private List<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)>? _cachedComponentPredicates;
    private List<(Breakpoint Breakpoint, CompiledEventScanner Scanner)>? _cachedEventScanners;
    private int _nextId = 1;
    private int _activeBreakpointCount;
    private bool _isPaused;
    private long _pausedTick;
    private readonly Queue<PendingDebugMutation> _pendingMutations = new();

    // Cache of component type -> CLR managed size (Unsafe.SizeOf<T>() via ComponentType<T>.Size).
    // Avoids repeated reflection on the hot path.
    private static readonly Dictionary<Type, int> _componentSizeCache = new();

    // ---- IDataBreakpointManager.IsPaused --------------------------------

    /// <inheritdoc/>
    public bool IsPaused => _isPaused;

    /// <inheritdoc/>
    public ISimulationView ActiveView => _isPaused ? (ISimulationView)_preTickSnapshot : _liveRepo;

    /// <inheritdoc/>
    public long PausedTick => _pausedTick;

    /// <inheritdoc/>
    public int PendingMutationsCount => _pendingMutations.Count;

    /// <summary>
    /// Exposes the pending mutation queue for testing.
    /// </summary>
    internal Queue<PendingDebugMutation> PendingMutationsQueue => _pendingMutations;

    /// <inheritdoc/>
    public bool HasMountedDelegates =>
        _componentPredicates.Count > 0 || _eventScanners.Count > 0 || HasStatefulTrackers
        || _externalHitPredicates.Count > 0;

    /// <inheritdoc/>
    public bool HasStatefulTrackers =>
        _structuralTrackers.Count > 0 || _spatialTrackers.Count > 0 || _lifecycleTrackers.Count > 0;

    /// <inheritdoc/>
    public IReadOnlyList<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)> MountedComponentPredicates
    {
        get
        {
            if (_cachedComponentPredicates != null) return _cachedComponentPredicates;
            _cachedComponentPredicates = new List<(Breakpoint, CompiledComponentPredicate)>(_componentPredicates.Count);
            foreach (var (id, compiled) in _componentPredicates)
            {
                if (_breakpoints.TryGetValue(id, out var bp))
                    _cachedComponentPredicates.Add((bp, compiled));
            }
            return _cachedComponentPredicates;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<(Breakpoint Breakpoint, CompiledEventScanner Scanner)> MountedEventScanners
    {
        get
        {
            if (_cachedEventScanners != null) return _cachedEventScanners;
            _cachedEventScanners = new List<(Breakpoint, CompiledEventScanner)>(_eventScanners.Count);
            foreach (var (id, scanner) in _eventScanners)
            {
                if (_breakpoints.TryGetValue(id, out var bp))
                    _cachedEventScanners.Add((bp, scanner));
            }
            return _cachedEventScanners;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Breakpoint> AllBreakpoints
    {
        get
        {
            var list = new List<Breakpoint>(_breakpoints.Count);
            foreach (var bp in _breakpoints.Values)
                list.Add(bp);
            return list;
        }
    }

    // ---- Events ---------------------------------------------------------

    /// <inheritdoc/>
    public event Action<Breakpoint, Entity>? OnBreakpointHit;

    /// <inheritdoc/>
    public event Action<bool>? OnPauseStateChanged;

    // ---- Construction ---------------------------------------------------

    /// <summary>
    /// Creates a <see cref="DataBreakpointManager"/>.
    /// All repository references must be pre-allocated by the caller and remain
    /// valid for the lifetime of this manager.
    /// </summary>
    /// <param name="liveRepo">The authoritative live entity repository.</param>
    /// <param name="preTickSnapshot">
    /// Repository owned by <paramref name="snapshotProvider"/>; populated every tick
    /// while the gate is open.
    /// </param>
    /// <param name="snapshotProvider">
    /// The <see cref="DebugSnapshotProvider"/> whose gate this manager controls.
    /// </param>
    /// <param name="timeController">Engine time-control surface.</param>
    /// <param name="predicateCompiler">
    /// Optional compiler for component-predicate breakpoints.
    /// When null, component predicates are not compiled and no component-path delegates
    /// are mounted.
    /// </param>
    /// <param name="eventScannerCompiler">
    /// Optional compiler for event-scanner breakpoints.
    /// When null, event predicates are not compiled and no event scanners are mounted.
    /// </param>
    /// <param name="notifier">
    /// Optional notification surface for hot-reload events.
    /// </param>
    public DataBreakpointManager(
        EntityRepository liveRepo,
        EntityRepository preTickSnapshot,
        DebugSnapshotProvider snapshotProvider,
        IEngineDebugTimeController timeController,
        IPredicateCompiler? predicateCompiler = null,
        IEventScannerCompiler? eventScannerCompiler = null,
        IBreakpointNotifier? notifier = null)
    {
        _liveRepo              = liveRepo              ?? throw new ArgumentNullException(nameof(liveRepo));
        _preTickSnapshot       = preTickSnapshot        ?? throw new ArgumentNullException(nameof(preTickSnapshot));
        _snapshotProvider      = snapshotProvider       ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _timeController        = timeController         ?? throw new ArgumentNullException(nameof(timeController));
        _predicateCompiler     = predicateCompiler;
        _eventScannerCompiler  = eventScannerCompiler;
        _notifier              = notifier;
        _postTickSnapshot      = new EntityRepository();
    }

    // ---- Internal test seams --------------------------------------------

    /// <summary>
    /// Exposes the pre-tick snapshot repository for testing.
    /// </summary>
    internal EntityRepository PreTickSnapshot => _preTickSnapshot;

    /// <summary>
    /// Exposes the post-tick snapshot repository for testing.
    /// </summary>
    internal EntityRepository PostTickSnapshot => _postTickSnapshot;

    // ---- Registry -------------------------------------------------------

    /// <inheritdoc/>
    public BreakpointId Add(Breakpoint breakpoint)
    {
        if (breakpoint == null) throw new ArgumentNullException(nameof(breakpoint));

        var id = new BreakpointId(_nextId++);
        var registered = breakpoint with { Id = id };
        _breakpoints[id] = registered;

        if (registered.Enabled)
        {
            AdjustGate(+1);
            TryMountDelegate(id, registered);
        }

        return id;
    }

    /// <inheritdoc/>
    public BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                                      int occurrenceThreshold = 1, string displayName = "",
                                      Guid? sourceElementId = null)
    {
        var bp = new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Condition           = condition,
            FilterEntity        = filter,
            OccurrenceThreshold = occurrenceThreshold >= 1 ? occurrenceThreshold
                : throw new ArgumentOutOfRangeException(nameof(occurrenceThreshold),
                      "Occurrence threshold must be >= 1. Pass 1 to pause on first hit."),
            Enabled             = true,
            DisplayName         = displayName,
            SourceElementId     = sourceElementId,
        };
        return Add(bp);
    }

    /// <inheritdoc/>
    public void Remove(BreakpointId id)
    {
        if (!_breakpoints.TryGetValue(id, out var bp))
            return;

        _breakpoints.Remove(id);
        UnmountDelegate(id);

        if (bp.Enabled)
            AdjustGate(-1);
    }

    /// <inheritdoc/>
    public void SetEnabled(BreakpointId id, bool enabled)
    {
        if (!_breakpoints.TryGetValue(id, out var bp))
            return;

        if (bp.Enabled == enabled)
            return;

        _breakpoints[id] = bp with { Enabled = enabled };

        if (enabled)
        {
            AdjustGate(+1);
            TryMountDelegate(id, _breakpoints[id]);
        }
        else
        {
            UnmountDelegate(id);
            AdjustGate(-1);
        }
    }

    /// <inheritdoc/>
    public void UpdateCondition(BreakpointId id, SearchPredicateDto? condition)
    {
        if (!_breakpoints.TryGetValue(id, out var bp))
            return;

        var updated = bp with { Condition = condition };
        _breakpoints[id] = updated;

        // Remount the compiled delegate for the new condition.
        UnmountDelegate(id);
        if (condition != null && updated.Enabled)
            TryMountDelegate(id, updated);
    }

    /// <inheritdoc/>
    public void MarkAsWatch(BreakpointId id, bool isWatch)
    {
        if (!_breakpoints.TryGetValue(id, out var bp)) return;
        _breakpoints[id] = bp with { IsWatch = isWatch };
    }

    /// <inheritdoc/>
    public void SaveWatches(string path)
    {
#pragma warning disable CS0618 // Type or member is obsolete — legacy compat
        WatchPersistence.Save(AllBreakpoints, path);
#pragma warning restore CS0618
    }

    /// <inheritdoc/>
    public void LoadWatches(string path)
    {
#pragma warning disable CS0618 // Type or member is obsolete — legacy compat
        var entries = WatchPersistence.TryLoad(path);
#pragma warning restore CS0618
        foreach (var entry in entries)
        {
            if (entry.Condition == null) continue;

            BreakpointId id;
            try
            {
                id = AddBreakpoint(entry.Condition, displayName: entry.DisplayName);
            }
            catch
            {
                // Compilation failed during Add — add disabled and mark broken.
                var failedBp = new Breakpoint
                {
                    Id          = BreakpointId.Invalid,
                    Condition   = entry.Condition,
                    Enabled     = false,
                    DisplayName = entry.DisplayName,
                    IsWatch     = true,
                    IsBroken    = true,
                };
                Add(failedBp);
                continue;
            }

            // Mark as watch (AddBreakpoint doesn't set IsWatch).
            if (_breakpoints.TryGetValue(id, out var bp))
                _breakpoints[id] = bp with { IsWatch = true };
        }
    }

    /// <inheritdoc/>
    public void OnHotReloadCompleted()
    {
        // Take a snapshot of all IDs to avoid modifying the dict during iteration.
        var ids = new List<BreakpointId>(_breakpoints.Keys);
        foreach (var id in ids)
        {
            if (!_breakpoints.TryGetValue(id, out var bp)) continue;

            // Always drop the stale compiled delegate — stale unmanaged pointers must not survive a reload.
            UnmountDelegate(id);

            if (bp.Condition == null || !bp.Enabled) continue;

            try
            {
                TryMountDelegate(id, bp);
                // Clear any previous broken flag on successful recompile.
                if (bp.IsBroken)
                    _breakpoints[id] = bp with { IsBroken = false };
            }
            catch
            {
                // Compilation failed (field removed / layout changed). Mark broken; retain DTO.
                _breakpoints[id] = bp with { IsBroken = true };
            }
        }
    }

    /// <inheritdoc/>
    public void OnHotReloadBegin()
    {
        if (!_isPaused) return;

        // Discard stale mutations before RequestContinue drains them.
        _pendingMutations.Clear();

        // Force unfreeze (restores post-tick snapshot, resumes time controller).
        RequestContinue();

        // Notify operator.
        _notifier?.Notify("Step abandoned due to reload");
    }

    // ---- Hit handling ---------------------------------------------------

    /// <summary>
    /// Called by <c>DataBreakpointSystem</c> (P2) when a compiled predicate fires.
    /// Implements the triple-buffer rewind: captures post-tick state, rewinds live
    /// repo to pre-tick, pauses the clock, and fires events.
    /// </summary>
    /// <param name="bp">The breakpoint that fired.</param>
    /// <param name="entity">The entity for which the predicate evaluated true.</param>
    public void OnHit(Breakpoint bp, Entity entity)
    {
        if (_isPaused) return; // already paused: drop same-tick re-entrant hits

        if (bp == null) throw new ArgumentNullException(nameof(bp));

        // Increment hit count on the registered record.
        if (!_breakpoints.TryGetValue(bp.Id, out var current))
            return;

        var updated = current with { HitCount = current.HitCount + 1 };
        _breakpoints[bp.Id] = updated;

        // Check occurrence threshold: only pause when the Nth hit is reached.
        if (updated.HitCount < updated.OccurrenceThreshold)
            return;

        // Capture post-execution state.
        _postTickSnapshot.SyncFrom(_liveRepo);

        // Rewind live world to start-of-tick state.
        _liveRepo.SyncFrom(_preTickSnapshot);

        // Halt the clock.
        _timeController.RequestPause();
        _isPaused = true;
        _pausedTick = _liveRepo.HasSingletonUnmanaged<GlobalTime>()
            ? _liveRepo.GetSingletonUnmanaged<GlobalTime>().TotalWallTicks
            : (long)_preTickSnapshot.SimulationTick; // fallback: frame clock (not memory clock)

        // Notify subscribers.
        OnBreakpointHit?.Invoke(updated, entity);
        OnPauseStateChanged?.Invoke(true);
    }

    // ---- Step / Continue ------------------------------------------------

    /// <inheritdoc/>
    public void RequestStep()
    {
        if (!_isPaused) return;

        // Restore end-of-tick state (clean step -- no resimulation, no event injection).
        _liveRepo.SyncFrom(_postTickSnapshot);

        // Apply staged mutations at the N+1 boundary.
        DrainPendingMutations(_liveRepo);

        _timeController.RequestStepOneTick();
        _isPaused = false;
        _pausedTick = 0L;
        // _pendingMutations is empty after drain; no explicit clear needed.

        OnPauseStateChanged?.Invoke(false);
    }

    /// <inheritdoc/>
    public void RequestContinue()
    {
        if (!_isPaused) return;

        // Restore end-of-tick state before resuming.
        _liveRepo.SyncFrom(_postTickSnapshot);

        // Apply staged mutations at the N+1 boundary.
        DrainPendingMutations(_liveRepo);

        _timeController.RequestResume();
        _isPaused = false;
        _pausedTick = 0L;
        // _pendingMutations is empty after drain; no explicit clear needed.

        OnPauseStateChanged?.Invoke(false);
    }

    // ---- Mutation staging ----------------------------------------------

    /// <inheritdoc/>
    public void StageMutation(Entity entity, Type componentType, object componentValue)
    {
        if (componentType == null) throw new ArgumentNullException(nameof(componentType));
        if (componentValue == null) throw new ArgumentNullException(nameof(componentValue));

        int typeId     = ComponentTypeRegistry.GetId(componentType);
        bool isManaged = !componentType.IsValueType;
        int sizeBytes  = isManaged ? 0 : GetEcsComponentSize(componentType);

        _pendingMutations.Enqueue(new PendingDebugMutation(
            entity, typeId, isManaged, componentValue, sizeBytes));
    }

    /// <inheritdoc/>
    public void StageMutation(Entity entity, Type componentType, object componentValue, object? baseline)
    {
        if (componentType  == null) throw new ArgumentNullException(nameof(componentType));
        if (componentValue == null) throw new ArgumentNullException(nameof(componentValue));

        // ⛔ A managed component has no byte layout to diff, and a null baseline means the caller
        //    cannot say what the designer changed — both fall back to the whole-component write.
        if (baseline == null || !componentType.IsValueType)
        {
            StageMutation(entity, componentType, componentValue);
            return;
        }

        int typeId    = ComponentTypeRegistry.GetId(componentType);
        int sizeBytes = GetEcsComponentSize(componentType);

        var after  = ToBytes(componentValue, sizeBytes);
        var before = ToBytes(baseline,       sizeBytes);

        int runs = 0;
        int i = 0;
        while (i < sizeBytes)
        {
            if (after[i] == before[i]) { i++; continue; }

            int start = i;
            while (i < sizeBytes && after[i] != before[i]) i++;
            int length = i - start;

            var payload = new byte[length];
            Buffer.BlockCopy(after, start, payload, 0, length);
            _pendingMutations.Enqueue(new PendingDebugMutation(
                entity, typeId, isManaged: false, payload, length, byteOffset: start));
            runs++;
        }

        // ⭐ An edit that changed nothing stages nothing — and that is a real case: the OK button
        //   commits whether or not the designer altered a value.
        _ = runs;
    }

    /// <inheritdoc/>
    public void StageFieldMutation(Entity entity, Type componentType, int byteOffset, ReadOnlySpan<byte> bytes)
    {
        int typeId = GuardFieldWrite(componentType, byteOffset, bytes.Length);

        // ⭐ COPIED, not aliased: the caller's span is very often a stack buffer or a slice of a
        //   rented array, and the queue outlives this call by at least one step.
        _pendingMutations.Enqueue(new PendingDebugMutation(
            entity,
            typeId,
            isManaged:  false,
            payload:    bytes.ToArray(),
            sizeBytes:  bytes.Length,
            byteOffset: byteOffset));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <c>MIN</c> — <c>AS-1b</c>: the LIVE WORLD's singleton, never the controller's
    /// <c>GetCurrentState()</c>.
    /// <para>⚠ <b>No clock at all ⇒ HALTED</b>, deliberately and not as a fallback: a world with no
    /// <c>GlobalTime</c> has no source of ticks, so nothing can overwrite a direct write. ⛔ The other
    /// answer would refuse every edit in such a world and blame the designer for a running simulation
    /// that does not exist.</para>
    /// </remarks>
    public bool IsClockHalted()
        => !_liveRepo.HasSingletonUnmanaged<GlobalTime>()
           || _liveRepo.GetSingletonUnmanaged<GlobalTime>().DeltaTime <= 0f;

    /// <inheritdoc/>
    public unsafe void WriteFieldNow(Entity entity, Type componentType, int byteOffset, ReadOnlySpan<byte> bytes)
    {
        int typeId = GuardFieldWrite(componentType, byteOffset, bytes.Length);

        // ⭐⭐⭐ THE SAME SURGICAL WRITER THE DRAIN USES — 📌 R-65/ruling 9: one implementation of
        //    "patch these bytes of this component", not two. ⛔ The rejected fallback was
        //    EntityRepository.SetComponentFieldRaw direct, which is `internal` to Fdp.Core and would
        //    have needed either InternalsVisibleTo or a SECOND public surgical-write surface.
        //
        // ⭐⭐ A SCRATCH BUFFER, FLUSHED HERE — 📌 EntityCommandBuffer.Playback IS SYNCHRONOUS
        //    (EntityCommandBuffer.cs:331): it applies every recorded op to the repo AT THE CALL, on
        //    the main thread. ⇒ the write has landed before this method returns.
        // ⛔⛔ DELIBERATELY *NOT* the repository's own per-thread buffer. That one is flushed by the
        //    kernel in BeforeSync — measured to happen even at dt = 0, so it WOULD have worked — but
        //    it makes a designer's edit depend on a kernel behaviour that could change silently, and
        //    it lands a frame late. ⭐ A scratch buffer owes the kernel nothing.
        // ⚠ Scoped: the buffer is disposed, so a partial write cannot leak into a later playback.
        using var ecb = new EntityCommandBuffer();
        fixed (byte* src = bytes)
            ecb.SetComponentFieldRaw(entity, typeId, byteOffset, src, bytes.Length);
        ecb.Playback(_liveRepo);
    }

    /// <summary>
    /// ⛔⛔ <b>The corruption gate, owned ONCE.</b> 📌 <c>Q32</c> §2.1: <i>"an out-of-range offset/size
    /// is MEMORY CORRUPTION, not a wrong value. Bounds-check against the registered component size and
    /// fail LOUDLY."</i>
    ///
    /// <para>⚠ The engine DOES check, in <c>ComponentTable.SetRawAt</c> — but that runs at PLAYBACK,
    /// on the sim thread, where nothing remains to attribute it to. ⭐ Checking here fails at the
    /// designer's OK button, naming the component and the range.</para>
    ///
    /// <para>⭐ <c>MIN</c> extracted this from <see cref="StageFieldMutation"/> so the staging arm and
    /// the write-now arm cannot drift into two different notions of "in range".</para>
    /// </summary>
    /// <returns>The registered component type id, which both callers need next.</returns>
    private static int GuardFieldWrite(Type componentType, int byteOffset, int length)
    {
        if (componentType == null) throw new ArgumentNullException(nameof(componentType));

        // ⛔ A managed component has no byte layout to patch. ⭐ Loud, not a fallback: forwarding to
        //    the whole-component path is R-65's clobber wearing the surgical path's name.
        if (!componentType.IsValueType)
            throw new ArgumentException(
                $"{componentType.Name} is a managed component and has no byte layout to patch. "
                + "Replace the object through StageMutation instead.", nameof(componentType));

        int componentSize = GetEcsComponentSize(componentType);
        if (byteOffset < 0 || length <= 0 || byteOffset + length > componentSize)
            throw new ArgumentOutOfRangeException(nameof(byteOffset),
                $"Field write [{byteOffset}, {byteOffset + length}) is outside "
                + $"{componentType.Name}, which is {componentSize} bytes.");

        return ComponentTypeRegistry.GetId(componentType);
    }

    /// <summary>
    /// ⭐ The managed byte image of a boxed unmanaged component — the same layout the ECS stores, so a
    /// diff over it names real component offsets. ⚠ <c>Marshal.StructureToPtr</c> is deliberately not
    /// used: it writes the MARSHALLED layout, which differs from the managed one on <c>bool</c>.
    /// </summary>
    /// <remarks>
    /// ⭐ BATCH 84 — the body moved to <see cref="ComponentBytes.Of"/> so the editor's live write and
    /// this diff produce the SAME image. ⛔ Two copies would be two answers to "what are this value's
    /// bytes?", and the wrong one is wrong only for <c>bool</c>.
    /// </remarks>
    private static byte[] ToBytes(object boxed, int sizeBytes) => ComponentBytes.Of(boxed, sizeBytes);

    /// <summary>
    /// Plays back all staged mutations into the repository via its command buffer.
    /// The ECB will be applied at the next tick boundary (when the kernel calls Tick()).
    /// No-op when the queue is empty.
    /// </summary>
    // ────────────────────────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ W4 — IStagedWrites. 📄 DESIGN_Staged_Live_Write.md §5 (the seam) · §4 (fork A).
    //    ⭐ This type ALREADY owned the staged set; W4 does not add a store, it EXPOSES the one that
    //      exists — 📌 R-13 "route, don't duplicate", and the whole reason fork A was chosen.
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HasPending => _pendingMutations.Count > 0;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <b>This IS <see cref="IsPaused"/></b>, and the seam names it <c>IsRewound</c> because that is
    /// what the drain cares about: 📌 <c>R-63</c> — while a breakpoint holds, the LIVE repo has been
    /// REWOUND to the pre-tick snapshot, and <c>RequestContinue</c> restores the post-tick one and
    /// drains itself. ⛔ A drain here would be overwritten by that restore.
    /// <para>⚠ Two names for one fact is deliberate: <c>IsPaused</c> is what the EDITOR asks,
    /// <c>IsRewound</c> is what the DRAIN asks, and they mean the same thing only because this
    /// implementation pauses by rewinding.</para>
    /// </remarks>
    public bool IsRewound => _isPaused;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐ The public face of the existing private drain. ⛔ Not a second implementation — 📌 <c>M-41</c>
    /// measured <c>DrainPendingMutations</c> as having no production caller OUTSIDE this class; this is
    /// the seam that finally gives it one.
    /// </remarks>
    public void DrainInto(Fdp.ModuleHost.Abstractions.ISimulationView view)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (view is EntityRepository repo) { DrainPendingMutations(repo); return; }

        throw new ArgumentException(
            $"DrainInto expects the live EntityRepository; got {view.GetType().Name}.", nameof(view));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐⭐ <b>THE QUERY BEHIND THE YELLOW</b> — 📄 §4's fork A, and 📌 <c>R-130</c> in one line:
    /// <i>pending ⟺ a mutation for this field sits un-drained.</i>
    ///
    /// <para>⭐⭐ <b>LAST WRITE WINS, and that is not an accident.</b> The queue may hold several
    /// mutations for one field *(a designer edits, then edits again, before the drain)*. ⛔ The FIRST
    /// match is the OLDEST — showing it would put a superseded number on screen in yellow. ⭐ The drain
    /// applies them in order, so the LAST one is what the field will actually become ⇒ that is what the
    /// panel must show.</para>
    ///
    /// <para>⚠ <b>Whole-component writes never match</b> *(<c>ByteOffset == -1</c>)*: this asks about a
    /// FIELD, and a whole-component stage does not tell us which fields the designer meant. ⛔ Claiming
    /// them all would yellow rows nobody touched.</para>
    /// </remarks>
    public bool TryGetPending(Entity entity, int typeId, int byteOffset, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (_pendingMutations.Count == 0) return false;

        bool found = false;
        foreach (var m in _pendingMutations)
        {
            if (!m.IsFieldWrite) continue;
            if (m.ComponentTypeId != typeId || m.ByteOffset != byteOffset) continue;
            if (!m.Target.Equals(entity)) continue;
            if (m.Payload is not byte[] payload) continue;

            bytes = payload;   // ⭐ keep going — the LAST match wins
            found = true;
        }
        return found;
    }

    private unsafe void DrainPendingMutations(EntityRepository repo)
    {
        if (_pendingMutations.Count == 0) return;

        var ecb = ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetCommandBuffer();
        while (_pendingMutations.TryDequeue(out var m))
        {
            if (m.IsManaged)
            {
                ecb.SetManagedComponentRaw(m.Target, m.ComponentTypeId, m.Payload);
            }
            else
            {
                var handle = GCHandle.Alloc(
                    m.Payload, GCHandleType.Pinned);
                try
                {
                    if (m.IsFieldWrite)
                    {
                        // ⭐⭐ Ruling 14 — the surgical write. Only the bytes the designer actually
                        //    changed are addressed, so the fields the SIM changed during the paused
                        //    tick survive the drain instead of reverting to their pre-tick values.
                        ecb.SetComponentFieldRaw(
                            m.Target, m.ComponentTypeId, m.ByteOffset,
                            (void*)handle.AddrOfPinnedObject(),
                            m.SizeBytes);
                    }
                    else
                    {
                        ecb.SetComponentRaw(
                            m.Target, m.ComponentTypeId,
                            (void*)handle.AddrOfPinnedObject(),
                            m.SizeBytes);
                    }
                }
                finally
                {
                    handle.Free();
                }
            }
        }
    }

    /// <inheritdoc/>
    public void OnExternalHit(string tag, Entity entity)
    {
        if (_externalHitPredicates.TryGetValue(tag, out var registrations))
        {
            foreach (var (bpId, remainingDelegate) in registrations)
            {
                if (!_breakpoints.TryGetValue(bpId, out var bp)) continue;
                if (!bp.Enabled) continue;

                bool shouldFire = remainingDelegate == null
                    || remainingDelegate(_liveRepo, entity);
                if (shouldFire)
                {
                    OnHit(bp, entity);
                }            }
        }
    }

    // ---- Internal gate --------------------------------------------------

    private void TryMountDelegate(BreakpointId id, Breakpoint bp)
    {
        if (bp.Condition == null) return;

        switch (bp.Condition)
        {
            case CompoundPredicateDto compound when HasExternalHitTag(compound):
            {
                // Collect tags from ExternalHitTagPredicateDto children
                var externalTags = compound.Conditions
                    .OfType<ExternalHitTagPredicateDto>()
                    .Select(e => e.Tag)
                    .ToList();

                // Build remaining predicate from non-ExternalHitTag children
                var remainingConditions = compound.Conditions
                    .Where(c => c is not ExternalHitTagPredicateDto)
                    .ToList();

                Func<EntityRepository, Entity, bool>? remainingDelegate = null;
                if (remainingConditions.Count > 0 && _predicateCompiler != null)
                {
                    SearchPredicateDto remainingPredicate = remainingConditions.Count == 1
                        ? remainingConditions[0]
                        : new CompoundPredicateDto { Operator = compound.Operator, Conditions = remainingConditions };
                    remainingDelegate = _predicateCompiler.CompileComponentPredicate(remainingPredicate);
                }

                foreach (var tag in externalTags)
                {
                    if (!_externalHitPredicates.TryGetValue(tag, out var tagList))
                    {
                        tagList = new List<(BreakpointId, Func<EntityRepository, Entity, bool>?)>();
                        _externalHitPredicates[tag] = tagList;
                    }
                    tagList.Add((id, remainingDelegate));
                }
                // Do NOT fall through to the component-predicate path;
                // ExternalHitTag compounds are evaluated only via OnExternalHit.
                break;
            }

            case ExternalHitTagPredicateDto tagDto:
            {
                if (!_externalHitPredicates.TryGetValue(tagDto.Tag, out var tagListStandalone))
                {
                    tagListStandalone = new List<(BreakpointId, Func<EntityRepository, Entity, bool>?)>();
                    _externalHitPredicates[tagDto.Tag] = tagListStandalone;
                }
                tagListStandalone.Add((id, null)); // null = always fires when tag matches
                break;
            }

            case PropertyMatchDto _:
            case CompoundPredicateDto _:
            case BehaviorParamPredicateDto _:
            case TraceBufferScanPredicateDto _:
            case BlueprintVariablePredicateDto _:
                if (_predicateCompiler != null)
                {
                    var del = _predicateCompiler.CompileComponentPredicate(bp.Condition);
                    var mandatory = _predicateCompiler.ExtractMandatoryComponents(bp.Condition);
                    _componentPredicates[id] = new CompiledComponentPredicate(del, mandatory);
                }
                break;

            case TransientEventPredicateDto eventDto:
                if (_eventScannerCompiler != null)
                {
                    var del = _eventScannerCompiler.CompileScanner(eventDto);
                    _eventScanners[id] = new CompiledEventScanner(del);
                }
                break;

            case StructuralPredicateDto structuralDto:
                _structuralTrackers[id] = (bp, structuralDto, new HashSet<Entity>());
                break;

            case SpatialBoundingPredicateDto spatialDto:
                _spatialTrackers[id] = (bp, spatialDto, new HashSet<Entity>(),
                    CompileSpatialPositionAccessor(spatialDto));
                break;

            case LifecyclePredicateDto lifecycleDto:
                _lifecycleTrackers[id] = (bp, lifecycleDto, new HashSet<Entity>());
                break;
        }
        _cachedComponentPredicates = null;
        _cachedEventScanners = null;
    }

    private void UnmountDelegate(BreakpointId id)
    {
        _componentPredicates.Remove(id);
        _eventScanners.Remove(id);
        _structuralTrackers.Remove(id);
        _spatialTrackers.Remove(id);
        _lifecycleTrackers.Remove(id);
        _cachedComponentPredicates = null;
        _cachedEventScanners = null;

        // Remove from external-hit registrations
        foreach (var tagList in _externalHitPredicates.Values)
            tagList.RemoveAll(entry => entry.id == id);
    }

    // ---- Stateful tracker evaluation ------------------------------------

    /// <inheritdoc/>
    public void EvaluateStatefulBreakpoints(EntityRepository repo)
    {
        _statefulHitsBuffer.Clear();
        var hits = _statefulHitsBuffer;

        EvaluateStructuralTrackers(repo, hits);
        EvaluateSpatialTrackers(repo, hits);
        EvaluateLifecycleTrackers(repo, hits);

        foreach (var (bp, entity) in hits)
            OnHit(bp, entity);
    }

    private void EvaluateStructuralTrackers(EntityRepository repo, List<(Breakpoint, Entity)> hits)
    {
        if (_structuralTrackers.Count == 0) return;

        foreach (var (bpId, (bp, dto, knownSet)) in _structuralTrackers)
        {
            if (!bp.Enabled) continue;

            int typeId = ComponentTypeRegistry.GetId(dto.ComponentType);
            if (typeId < 0) continue;

            int maxIdx = repo.MaxEntityIndex;
            for (int i = 0; i <= maxIdx; i++)
            {
                ref var compMask = ref repo.GetComponentMask(i);
                ref var meta     = ref repo.GetMetadata(i);
                if (!meta.IsActive) continue;

                Entity entity = repo.GetEntityByIndex(i);
                if (entity.IsNull) continue;

                if (bp.FilterEntity is { } fe && fe != entity) continue;

                bool present = ComputeEffectivePresence(ref compMask, ref meta, typeId, dto.AuthorityRequirement);
                bool was     = knownSet.Contains(entity);

                if (present && !was)
                {
                    knownSet.Add(entity);
                    if (dto.ModificationType == StructuralModification.Added ||
                        dto.ModificationType == StructuralModification.AnyChange)
                        hits.Add((bp, entity));
                }
                else if (!present && was)
                {
                    knownSet.Remove(entity);
                    if (dto.ModificationType == StructuralModification.Removed ||
                        dto.ModificationType == StructuralModification.AnyChange)
                        hits.Add((bp, entity));
                }
            }

            // Remove destroyed entities from the known set.
            var destroyed = repo.GetDestructionLog();
            for (int i = 0; i < destroyed.Count; i++)
                knownSet.Remove(destroyed[i]);
        }
    }

    private void EvaluateSpatialTrackers(EntityRepository repo, List<(Breakpoint, Entity)> hits)
    {
        if (_spatialTrackers.Count == 0) return;

        foreach (var (bpId, (bp, dto, insideSet, posAccessor)) in _spatialTrackers)
        {
            if (!bp.Enabled) continue;

            int maxIdx = repo.MaxEntityIndex;
            for (int i = 0; i <= maxIdx; i++)
            {
                ref var meta = ref repo.GetMetadata(i);
                if (!meta.IsActive) continue;

                Entity entity = repo.GetEntityByIndex(i);
                if (entity.IsNull) continue;
                if (bp.FilterEntity is { } fe && fe != entity) continue;

                Vector2 pos = posAccessor != null
                    ? posAccessor(repo, entity)
                    : ReadPosition2D(repo, entity, dto);  // fallback for managed components
                bool isInside  = IsInBounds(pos, dto.Bounds);
                bool wasInside = insideSet.Contains(entity);

                if (isInside && !wasInside)
                {
                    insideSet.Add(entity);
                    if (dto.TriggerEvent == BoundaryEvent.Entry ||
                        dto.TriggerEvent == BoundaryEvent.EntryOrExit)
                        hits.Add((bp, entity));
                }
                else if (!isInside && wasInside)
                {
                    insideSet.Remove(entity);
                    if (dto.TriggerEvent == BoundaryEvent.Exit ||
                        dto.TriggerEvent == BoundaryEvent.EntryOrExit)
                        hits.Add((bp, entity));
                }
            }

            // Remove destroyed entities from inside-set.
            var destroyed = repo.GetDestructionLog();
            for (int i = 0; i < destroyed.Count; i++)
                insideSet.Remove(destroyed[i]);
        }
    }

    private void EvaluateLifecycleTrackers(EntityRepository repo, List<(Breakpoint, Entity)> hits)
    {
        if (_lifecycleTrackers.Count == 0) return;

        foreach (var (bpId, (bp, dto, knownAlive)) in _lifecycleTrackers)
        {
            if (!bp.Enabled) continue;

            // Birth detection: iterate all active entities not yet in knownAlive.
            int maxIdx = repo.MaxEntityIndex;
            for (int i = 0; i <= maxIdx; i++)
            {
                ref var meta = ref repo.GetMetadata(i);
                if (!meta.IsActive) continue;

                Entity entity = repo.GetEntityByIndex(i);
                if (entity.IsNull) continue;
                if (bp.FilterEntity is { } fe && fe != entity) continue;
                if (knownAlive.Contains(entity)) continue;

                if (MatchesLifecycleCriteria(repo, entity, dto))
                {
                    knownAlive.Add(entity);
                    hits.Add((bp, entity));
                }
            }

            // Death detection via destruction log.
            var destroyed = repo.GetDestructionLog();
            for (int i = 0; i < destroyed.Count; i++)
            {
                Entity dead = destroyed[i];
                if (knownAlive.Remove(dead))
                    hits.Add((bp, dead));
            }
        }
    }

    // ---- Static helpers for stateful trackers ---------------------------

    private static bool ComputeEffectivePresence(
        ref BitMask512 componentMask,
        ref EntityMetadataCold meta,
        int typeId,
        AuthorityRequirement req) =>
        req switch
        {
            AuthorityRequirement.RequireAuthority =>
                componentMask.IsSet(typeId) && meta.AuthorityMask.IsSet(typeId),
            AuthorityRequirement.RequireGhost =>
                componentMask.IsSet(typeId) && !meta.AuthorityMask.IsSet(typeId),
            _ => componentMask.IsSet(typeId)
        };

    private static bool IsInBounds(Vector2 pos, BoundingBox2D bounds) =>
        pos.X >= bounds.Min.X && pos.X <= bounds.Max.X
     && pos.Y >= bounds.Min.Y && pos.Y <= bounds.Max.Y;

    /// <summary>
    /// Returns the CLR managed size of <paramref name="type"/> in bytes.
    /// Uses <c>ComponentType&lt;T&gt;.Size</c> (= <c>Unsafe.SizeOf&lt;T&gt;()</c>) rather than
    /// <c>Marshal.SizeOf</c>, which gives the interop layout size that may differ for
    /// components containing <c>fixed</c> buffers or bool fields with <c>[MarshalAs(UnmanagedType.I1)]</c>.
    /// </summary>
    private static int GetEcsComponentSize(Type type)
    {
        lock (_componentSizeCache)
        {
            if (_componentSizeCache.TryGetValue(type, out int cached))
                return cached;
            // ComponentType<T>.Size = Unsafe.SizeOf<T>() -- matches the ECS chunk stride.
            var genericType = typeof(ComponentType<>).MakeGenericType(type);
            var prop        = genericType.GetProperty("Size",
                BindingFlags.Public | BindingFlags.Static)!;
            int size        = (int)prop.GetValue(null)!;
            _componentSizeCache[type] = size;
            return size;
        }
    }

    /// <summary>
    /// Builds a compiled position accessor for unmanaged component type <paramref name="dto"/>.PositionComponentType.
    /// Returns null if the type is null, not a value type, or its field paths cannot be resolved.
    /// </summary>
    private static Func<EntityRepository, Entity, Vector2>? CompileSpatialPositionAccessor(
        SpatialBoundingPredicateDto dto)
    {
        Type? compType = dto.PositionComponentType;
        if (compType == null || !compType.IsValueType) return null;

        int typeId = ComponentTypeRegistry.GetId(compType);
        if (typeId < 0) return null;

        try
        {
            var method = typeof(DataBreakpointManager)
                .GetMethod(nameof(CompileSpatialPositionAccessorGeneric),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(compType);
            return (Func<EntityRepository, Entity, Vector2>)method.Invoke(null, new object[] { dto, typeId })!;
        }
        catch
        {
            return null; // Fall back to reflection-based ReadPosition2D if compilation fails.
        }
    }

    private static Func<EntityRepository, Entity, Vector2>
        CompileSpatialPositionAccessorGeneric<T>(SpatialBoundingPredicateDto dto, int typeId)
        where T : unmanaged
    {
        // Build an expression tree: (ref T comp) => new Vector2(comp.XPath, comp.YPath)
        var param = Expression.Parameter(typeof(T).MakeByRefType(), "comp");

        Expression xExpr = param;
        foreach (string seg in dto.PositionXPath.Split('.'))
            xExpr = Expression.PropertyOrField(xExpr, seg);
        if (xExpr.Type != typeof(float))
            xExpr = Expression.Convert(xExpr, typeof(float));

        Expression yExpr = param;
        foreach (string seg in dto.PositionYPath.Split('.'))
            yExpr = Expression.PropertyOrField(yExpr, seg);
        if (yExpr.Type != typeof(float))
            yExpr = Expression.Convert(yExpr, typeof(float));

        var ctor     = typeof(Vector2).GetConstructor(new[] { typeof(float), typeof(float) })!;
        var bodyExpr = Expression.New(ctor, xExpr, yExpr);
        var accessor = Expression.Lambda<SpatialPositionDelegate<T>>(bodyExpr, param).Compile();

        return (repo, entity) =>
        {
            if (!repo.HasComponentByTypeId(entity, typeId)) return Vector2.Zero;
            ref readonly T comp = ref repo.GetComponentRO<T>(entity);
            return accessor(ref Unsafe.AsRef(in comp));
        };
    }

    private static Vector2 ReadPosition2D(EntityRepository repo, Entity entity, SpatialBoundingPredicateDto dto)
    {
        Type compType = dto.PositionComponentType;
        if (compType == null) return Vector2.Zero;

        int typeId = ComponentTypeRegistry.GetId(compType);
        if (typeId < 0) return Vector2.Zero;

        object? comp = null;
        if (compType.IsValueType)
        {
            unsafe
            {
                void* ptr = repo.GetComponentPointer(entity, typeId);
                if (ptr == null) return Vector2.Zero;
                comp = Marshal.PtrToStructure(new IntPtr(ptr), compType);
            }
        }
        else
        {
            comp = repo.GetManagedComponentByTypeId(entity, typeId);
        }

        if (comp == null) return Vector2.Zero;

        float x = ReadFloatField(comp, dto.PositionXPath);
        float y = ReadFloatField(comp, dto.PositionYPath);
        return new Vector2(x, y);
    }

    private static float ReadFloatField(object obj, string fieldPath)
    {
        object? cur = obj;
        string[] segments = fieldPath.Split('.');
        foreach (string seg in segments)
        {
            if (cur == null) return 0f;
            Type t = cur.GetType();
            FieldInfo? fi = t.GetField(seg,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) { cur = fi.GetValue(cur); continue; }
            PropertyInfo? pi = t.GetProperty(seg,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi != null) { cur = pi.GetValue(cur); continue; }
            return 0f;
        }
        return cur is float f ? f : (cur is double d ? (float)d : 0f);
    }

    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="dto"/> uses <see cref="EntityIdentifierType.NetworkId"/>,
    /// which requires an <c>INetworkEntityMap</c> that is not yet wired into this manager.
    /// </exception>
    private static bool MatchesLifecycleCriteria(EntityRepository repo, Entity entity, LifecyclePredicateDto dto)
    {
        return dto.IdentifierType switch
        {
            EntityIdentifierType.EcsHandle =>
                entity.Index.ToString() == dto.TargetValue ||
                entity.ToString() == dto.TargetValue,

            EntityIdentifierType.NameSubstring =>
                dto.NameComponentType != null
                    ? ReadEntityName(repo, entity, dto) is { } n &&
                      n.Contains(dto.TargetValue, StringComparison.OrdinalIgnoreCase)
                    : entity.ToString().Contains(dto.TargetValue, StringComparison.OrdinalIgnoreCase),

            // Network-id lookup requires INetworkEntityMap, which is not injected into this manager.
            // To support this, pass an INetworkEntityMap to the DataBreakpointManager constructor
            // and resolve the entity in this branch. Until then, using NetworkId as identifier will throw.
            EntityIdentifierType.NetworkId => throw new NotSupportedException(
                "LifecyclePredicateDto with EntityIdentifierType.NetworkId requires an INetworkEntityMap " +
                "injected into DataBreakpointManager. Wire the network map via the constructor, " +
                "or use EcsHandle or NameSubstring instead."),
            _ => false
        };
    }

    private static string? ReadEntityName(EntityRepository repo, Entity entity, LifecyclePredicateDto dto)
    {
        int typeId = ComponentTypeRegistry.GetId(dto.NameComponentType!);
        if (typeId < 0) return null;
        if (!repo.HasComponentByTypeId(entity, typeId)) return null;

        object? comp = null;
        if (dto.NameComponentType!.IsValueType)
        {
            unsafe
            {
                void* ptr = repo.GetComponentPointer(entity, typeId);
                if (ptr == null) return null;
                comp = Marshal.PtrToStructure(new IntPtr(ptr), dto.NameComponentType);
            }
        }
        else
        {
            comp = repo.GetManagedComponentByTypeId(entity, typeId);
        }

        if (comp == null) return null;

        return ReadStringField(comp, dto.NamePropertyPath);
    }

    private static string? ReadStringField(object obj, string fieldPath)
    {
        object? cur = obj;
        string[] segments = fieldPath.Split('.');
        foreach (string seg in segments)
        {
            if (cur == null) return null;
            Type t = cur.GetType();
            FieldInfo? fi = t.GetField(seg,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) { cur = fi.GetValue(cur); continue; }
            PropertyInfo? pi = t.GetProperty(seg,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi != null) { cur = pi.GetValue(cur); continue; }
            return null;
        }
        return cur?.ToString();
    }

    private static bool HasExternalHitTag(CompoundPredicateDto compound)
        => compound.Conditions.Any(c => c is ExternalHitTagPredicateDto);

    private void AdjustGate(int delta)    {
        int previous = _activeBreakpointCount;
        _activeBreakpointCount += delta;

        if (previous == 0 && _activeBreakpointCount == 1)
            _snapshotProvider.SetEnabled(true);
        else if (previous == 1 && _activeBreakpointCount == 0)
            _snapshotProvider.SetEnabled(false);
    }
}
