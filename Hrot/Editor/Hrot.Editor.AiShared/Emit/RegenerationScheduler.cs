using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;

namespace Hrot.Editor.AiShared.Emit;

/// <summary>
/// Debounces rapid edits (e.g. a burst of <c>MoveNode</c> commands) into a single
/// save + regen flush, preventing the file watcher from firing after every keystroke.
///
/// <para>
/// <b>Deterministic / unit-testable design:</b> wall-clock time and the flush action
/// are injected via constructor parameters so tests can advance time and flush
/// synchronously without real timers.  In production, <see cref="Tick"/> is called
/// once per editor frame from <c>EditorSubsystem.DrawUI</c> (or a similar per-frame
/// hook), and <c>tickProvider</c> wraps <see cref="Environment.TickCount64"/>.
/// </para>
///
/// <para>
/// <b>Usage:</b>
/// <list type="number">
///   <item>Call <see cref="Schedule"/> whenever a command sink marks an asset dirty.</item>
///   <item>Call <see cref="Tick"/> once per frame.  When the debounce window has elapsed,
///       the scheduler calls <c>flushAction</c> for each pending asset and clears the queue.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RegenerationScheduler
{
    // ── Pending set (asset-id deduplication) ─────────────────────────────────

    private readonly Dictionary<Guid, IEditableAsset> _pending = new();

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// How long to wait after the last <see cref="Schedule"/> call before flushing,
    /// measured in the units returned by <c>tickProvider</c>.
    /// Defaults to 500 ms when using <see cref="Environment.TickCount64"/>.
    /// </summary>
    public long DebounceTicks { get; }

    private readonly Func<long>   _tickProvider;
    private readonly Action<IEditableAsset> _flushAction;

    private long _lastScheduledAt = long.MinValue;
    private bool _hasPending;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a scheduler with injected clock and flush action (enables deterministic tests).
    /// </summary>
    /// <param name="flushAction">
    ///   Called once per asset when the debounce window elapses.
    ///   In production this emits C# and writes the file; in tests it can be a spy.
    /// </param>
    /// <param name="tickProvider">
    ///   Returns the current "tick" value (monotonic counter).
    ///   Defaults to <see cref="Environment.TickCount64"/> (milliseconds).
    ///   Inject a fake counter in tests.
    /// </param>
    /// <param name="debounceTicks">
    ///   Number of ticks to wait after the last Schedule call before flushing.
    ///   Defaults to 500 (= 500 ms when using <see cref="Environment.TickCount64"/>).
    /// </param>
    public RegenerationScheduler(
        Action<IEditableAsset> flushAction,
        Func<long>? tickProvider     = null,
        long        debounceTicks    = 500)
    {
        if (flushAction is null) throw new ArgumentNullException(nameof(flushAction));
        if (debounceTicks < 0)   throw new ArgumentOutOfRangeException(nameof(debounceTicks));

        _flushAction   = flushAction;
        _tickProvider  = tickProvider ?? (() => Environment.TickCount64);
        DebounceTicks  = debounceTicks;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks <paramref name="asset"/> as needing regeneration.
    /// If the asset was already pending, the debounce window is reset from now.
    /// </summary>
    public void Schedule(IEditableAsset asset)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        _pending[asset.AssetId] = asset;
        _lastScheduledAt        = _tickProvider();
        _hasPending             = true;
    }

    /// <summary>
    /// Must be called once per frame (on the main thread).
    /// When the debounce window has elapsed since the last <see cref="Schedule"/> call,
    /// invokes <c>flushAction</c> for every pending asset and clears the queue.
    /// </summary>
    /// <returns>
    ///   The number of assets flushed this tick (0 when the debounce window has not elapsed
    ///   or there are no pending assets).
    /// </returns>
    public int Tick()
    {
        if (!_hasPending)
            return 0;

        long now     = _tickProvider();
        long elapsed = now - _lastScheduledAt;

        if (elapsed < DebounceTicks)
            return 0;

        // Flush: copy and clear atomically so re-entrant Schedule calls are safe.
        var toFlush = new List<IEditableAsset>(_pending.Values);
        _pending.Clear();
        _hasPending = false;

        foreach (var asset in toFlush)
            _flushAction(asset);

        return toFlush.Count;
    }

    /// <summary>
    /// Returns <c>true</c> when there are assets waiting to be flushed
    /// (regardless of whether the debounce window has elapsed).
    /// </summary>
    public bool HasPending => _hasPending;

    /// <summary>
    /// Number of distinct assets currently pending.
    /// </summary>
    public int PendingCount => _pending.Count;
}
