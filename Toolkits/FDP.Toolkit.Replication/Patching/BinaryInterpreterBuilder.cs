using System;
using System.Collections.Generic;

namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Fluent builder that registers handlers and flushers for a
/// <see cref="BinaryInterpreter{TRecord}"/>.
///
/// <para>
/// Typical usage — create the builder, add one or more
/// <see cref="IBinaryAttributeInstaller{TRecord}"/>s, then call <see cref="Build"/>:
/// <code>
/// BinaryInterpreter&lt;AttributeRecord&gt; interp =
///     new BinaryInterpreterBuilder&lt;AttributeRecord&gt;(r =&gt; r.AttributeId)
///         .AddInstaller(new EntityDataAttributeInstaller())
///         .AddInstaller(new SimTransformAttributeInstaller(geoTransform))
///         .Build();
/// </code>
/// </para>
/// </summary>
/// <typeparam name="TRecord">
/// The application-layer attribute record type.  Must be a struct.
/// </typeparam>
public sealed class BinaryInterpreterBuilder<TRecord> where TRecord : struct
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly Func<TRecord, ushort> _getIdFunc;
    private readonly Dictionary<ushort, Action<BinaryPatchContext, TRecord>> _handlerDict = new();
    private readonly Action<BinaryPatchContext>[] _flushers = new Action<BinaryPatchContext>[32];
    private readonly List<Action<BinaryPatchContext>> _preApplyHandlers = new();
    private ushort _maxId = 0;
    private int    _scratchpadSize = 0;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="getIdFunc">
    /// Delegate that extracts the <see cref="ushort"/> attribute ID from a record.
    /// Used by <see cref="BinaryInterpreter{TRecord}.Apply"/> as the dispatch key.
    /// </param>
    public BinaryInterpreterBuilder(Func<TRecord, ushort> getIdFunc)
    {
        _getIdFunc = getIdFunc ?? throw new ArgumentNullException(nameof(getIdFunc));
    }

    // ── Registration API ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers a handler for a specific attribute ID.
    /// The handler is called whenever <see cref="BinaryInterpreter{TRecord}.Apply"/> encounters
    /// a record with the matching ID.
    /// </summary>
    /// <param name="id">The 16-bit attribute ID to route to <paramref name="handler"/>.</param>
    /// <param name="handler">
    /// Handler delegate.  Receives the current <see cref="BinaryPatchContext"/> and the
    /// matched record.  Must not capture any mutable state; use
    /// <see cref="ReserveScratchpad"/> for per-call transient state.
    /// </param>
    /// <returns>This builder (fluent API).</returns>
    public BinaryInterpreterBuilder<TRecord> RegisterHandler(
        ushort id,
        Action<BinaryPatchContext, TRecord> handler)
    {
        _handlerDict[id] = handler ?? throw new ArgumentNullException(nameof(handler));
        if (id > _maxId) _maxId = id;
        return this;
    }

    /// <summary>
    /// Registers a delegate that is invoked once per <see cref="BinaryInterpreter{TRecord}.Apply"/>
    /// call, <em>after</em> the scratchpad has been zeroed but <em>before</em> the dispatch
    /// loop begins.  Use this to pre-populate installer scratchpad fields from the entity's
    /// current state, eliminating the need for per-handler <c>Initialized</c> flag checks.
    /// </summary>
    /// <param name="handler">Pre-apply delegate.</param>
    /// <returns>This builder (fluent API).</returns>
    public BinaryInterpreterBuilder<TRecord> RegisterPreApplyHandler(Action<BinaryPatchContext> handler)
    {
        _preApplyHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    /// <summary>
    /// Registers a deferred flusher that is invoked once per
    /// <see cref="BinaryInterpreter{TRecord}.Apply"/> call if bit <paramref name="bit"/> is
    /// set in <see cref="BinaryPatchContext.DirtySubsystemsMask"/>.
    /// A handler marks the bit via <see cref="BinaryPatchContext.MarkSubsystemDirty"/>.
    /// </summary>
    /// <param name="bit">Zero-based bitmask index (0–31).</param>
    /// <param name="flusher">Flusher delegate invoked at the tail-end of Apply.</param>
    /// <returns>This builder (fluent API).</returns>
    public BinaryInterpreterBuilder<TRecord> RegisterSubsystemFlusher(
        int bit,
        Action<BinaryPatchContext> flusher)
    {
        if (bit < 0 || bit >= 32)
            throw new ArgumentOutOfRangeException(nameof(bit), "Bit index must be in range 0–31.");
        _flushers[bit] = flusher ?? throw new ArgumentNullException(nameof(flusher));
        return this;
    }

    /// <summary>
    /// Reserves a contiguous block of <paramref name="bytes"/> in the shared
    /// <see cref="BinaryPatchContext.ScratchpadData"/> allocation and returns the
    /// starting byte offset for this installer's block.
    /// </summary>
    /// <param name="bytes">Number of bytes to reserve (must be &gt; 0).</param>
    /// <returns>Byte offset into <see cref="BinaryPatchContext.ScratchpadData"/>.</returns>
    public int ReserveScratchpad(int bytes)
    {
        if (bytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Must reserve at least 1 byte.");

        int offset = _scratchpadSize;
        _scratchpadSize += bytes;
        return offset;
    }

    /// <summary>
    /// Invokes <see cref="IBinaryAttributeInstaller{TRecord}.Install"/> on
    /// <paramref name="installer"/>, allowing it to register its handlers and
    /// claim a scratchpad offset.
    /// </summary>
    /// <param name="installer">The installer to add.</param>
    /// <returns>This builder (fluent API).</returns>
    public BinaryInterpreterBuilder<TRecord> AddInstaller(IBinaryAttributeInstaller<TRecord> installer)
    {
        (installer ?? throw new ArgumentNullException(nameof(installer))).Install(this);
        return this;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns an immutable <see cref="BinaryInterpreter{TRecord}"/> from all
    /// registered handlers, flushers, and scratchpad size.
    /// </summary>
    public BinaryInterpreter<TRecord> Build()
    {
        // Flat handler array: index = attribute ID → handler (null = unregistered).
        int tableSize = _handlerDict.Count > 0 ? _maxId + 1 : 0;
        var handlers = new Action<BinaryPatchContext, TRecord>[tableSize];
        foreach (var (id, handler) in _handlerDict)
            handlers[id] = handler;

        // Snapshot the flusher array.
        var flushersCopy = (Action<BinaryPatchContext>[])_flushers.Clone();

        // Snapshot the pre-apply handler list.
        var preApplyCopy = _preApplyHandlers.Count > 0
            ? _preApplyHandlers.ToArray()
            : Array.Empty<Action<BinaryPatchContext>>();

        return new BinaryInterpreter<TRecord>(handlers, flushersCopy, preApplyCopy, _scratchpadSize, _getIdFunc);
    }
}
