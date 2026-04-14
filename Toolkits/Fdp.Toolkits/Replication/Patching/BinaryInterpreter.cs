using System.Numerics;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Zero-overhead dispatch engine: maps attribute IDs to strongly-typed handlers
/// registered by one or more <see cref="IBinaryAttributeInstaller{TRecord}"/> plug-ins.
///
/// <para>
/// Instances are created by <see cref="BinaryInterpreterBuilder{TRecord}.Build"/>.
/// Thread-safe and reusable — all mutable state lives in the per-call
/// <see cref="BinaryPatchContext"/> returned by <see cref="CreateContext"/>.
/// </para>
///
/// <para>
/// Dispatch model: <typeparamref name="TRecord"/> is mapped to a handler via the
/// <c>getIdFunc</c> delegate supplied at build time, giving O(1) lookup with no
/// branching beyond a null-check on each slot.
/// </para>
/// </summary>
/// <typeparam name="TRecord">The application-level attribute record type.</typeparam>
/// <remarks>
/// Instances are immutable after construction; obtain one via
/// <see cref="BinaryInterpreterBuilder{TRecord}"/>.
/// </remarks>
public sealed class BinaryInterpreter<TRecord> where TRecord : struct
{
    // ── Internal state (immutable after Build) ────────────────────────────────

    /// <summary>
    /// Handler dispatch table. Index = extracted attribute ID.
    /// Null entries are unregistered IDs and are silently skipped.
    /// </summary>
    private readonly Action<BinaryPatchContext, TRecord>[] _handlers;

    /// <summary>
    /// Per-bit flusher array. Entry at index <c>N</c> is invoked when bit <c>N</c>
    /// of <see cref="BinaryPatchContext.DirtySubsystemsMask"/> is set after all records
    /// have been processed.  Null entries indicate no flusher registered for that bit.
    /// </summary>
    private readonly Action<BinaryPatchContext>[] _flushers;

    /// <summary>
    /// Pre-apply handlers invoked once per <see cref="Apply"/> call after the scratchpad
    /// is zeroed, before the dispatch loop.  Used to pre-populate installer scratchpad
    /// state from the entity's current component values (e.g. existing geodetic position)
    /// without branching inside the hot dispatch loop.
    /// </summary>
    private readonly Action<BinaryPatchContext>[] _preApplyHandlers;

    /// <summary>Total scratchpad bytes to allocate per context.</summary>
    private readonly int _scratchpadSize;

    /// <summary>Delegate that extracts the dispatch key (attribute ID) from a record.</summary>
    private readonly Func<TRecord, ushort> _getIdFunc;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal BinaryInterpreter(
        Action<BinaryPatchContext, TRecord>[] handlers,
        Action<BinaryPatchContext>[] flushers,
        Action<BinaryPatchContext>[] preApplyHandlers,
        int scratchpadSize,
        Func<TRecord, ushort> getIdFunc)
    {
        _handlers         = handlers;
        _flushers         = flushers;
        _preApplyHandlers = preApplyHandlers;
        _scratchpadSize   = scratchpadSize;
        _getIdFunc        = getIdFunc;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Allocates a new <see cref="BinaryPatchContext"/> backed by
    /// <paramref name="patchCtx"/>, with a pre-zeroed scratchpad of the size
    /// determined at build time.  Allocates the scratchpad array once; reuse
    /// the returned context across multiple <see cref="Apply"/> calls where
    /// the entity and ECS context are the same.
    /// </summary>
    /// <param name="patchCtx">
    /// The underlying ECS patch context (e.g. <c>ListPatchContext</c> or
    /// <c>EcsPatchContext</c>).
    /// </param>
    /// <returns>A fresh <see cref="BinaryPatchContext"/> ready for use.</returns>
    public BinaryPatchContext CreateContext(IEntityPatchContext patchCtx)
        => new BinaryPatchContext(patchCtx, _scratchpadSize);

    /// <summary>
    /// Applies all records in <paramref name="records"/> to the ECS context
    /// encapsulated by <paramref name="ctx"/>, then runs deferred flushers and
    /// calls <see cref="IEntityPatchContext.FlushDirtyMarks"/>.
    /// </summary>
    /// <param name="ctx">
    /// The per-request context created by <see cref="CreateContext"/>.
    /// <see cref="BinaryPatchContext.DirtySubsystemsMask"/> is reset to zero at
    /// entry to support context reuse.
    /// </param>
    /// <param name="records">Attribute records to process.</param>
    public void Apply(BinaryPatchContext ctx, ReadOnlySpan<TRecord> records)
    {
        // Reset transient state so the context can be reused across Apply calls.
        ctx.DirtySubsystemsMask = 0;
        ctx.DirtyDescriptorMask = 0;
        // Predictably zero the scratchpad so installers never see stale data from a
        // previous Apply call.  Pre-apply handlers then re-populate it from current
        // entity state, removing per-handler Initialized flag branches.
        ctx.ScratchpadData.AsSpan().Clear();

        // ── Pre-apply phase ────────────────────────────────────────────────
        foreach (var preApply in _preApplyHandlers)
            preApply(ctx);

        // ── Dispatch phase ────────────────────────────────────────────────────
        foreach (ref readonly var record in records)
        {
            ushort id = _getIdFunc(record);
            if (id < _handlers.Length)
            {
                var handler = _handlers[id];
                handler?.Invoke(ctx, record);
            }
            // Unknown IDs: silently skipped (forward-compatibility).
        }

        // ── Flush phase ───────────────────────────────────────────────────────
        // Iterate only set bits — at most 32 flusher calls per Apply invocation.
        uint mask = ctx.DirtySubsystemsMask;
        while (mask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1; // clear lowest set bit

            var flusher = _flushers[bit];
            flusher?.Invoke(ctx);
        }

        // ── SmartEgress ───────────────────────────────────────────────────────
        ctx.PatchContext.FlushDirtyMarks();
    }
}
