using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Per-request context object passed to every <see cref="BinaryInterpreter"/> handler
/// and flusher.  Provides access to the target ECS context, a pre-allocated
/// scratchpad block for installer-specific temporary state, and dirty-mark tracking.
///
/// <para>
/// Create one context per apply invocation using
/// <see cref="BinaryInterpreter.CreateContext"/>.  The <see cref="ScratchpadData"/>
/// byte array is allocated once per context and reused across all handlers within
/// the same <see cref="BinaryInterpreter.Apply"/> call — zero per-record allocations.
/// </para>
///
/// <para>
/// This is a class (heap object), not a <c>ref struct</c>, because handlers may
/// interact with managed ECS components whose accessors require a heap reference.
/// See ATTR2-DESIGN.md A7.
/// </para>
/// </summary>
public sealed class BinaryPatchContext
{
    // ── ECS handles ──────────────────────────────────────────────────────────

    /// <summary>
    /// Live ECS world.  Null on the creation path (when using <see cref="ListPatchContext"/>).
    /// </summary>
    public EntityRepository? Repo { get; set; }

    /// <summary>
    /// Target entity.  Default value when unused on the creation path.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    /// Underlying patch context (either a <see cref="ListPatchContext"/> on the
    /// creation path or an <c>EcsPatchContext</c> on the live update path).
    /// All component access should go through this to preserve the
    /// <c>CanWrite/CanWriteManaged</c> authority guard contract.
    /// </summary>
    public IEntityPatchContext PatchContext { get; }

    // ── Scratchpad ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-allocated installer scratchpad block.  Size is determined at interpreter
    /// build time by the sum of all <see cref="BinaryInterpreterBuilder.ReserveScratchpad"/>
    /// calls.  Allocated once per context and zeroed before first use.
    /// </summary>
    public byte[] ScratchpadData { get; }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    /// <summary>
    /// Bitmask of installer subsystem bits dirtied during the current
    /// <see cref="BinaryInterpreter.Apply"/> call.
    /// Bit <c>N</c> is set by <see cref="MarkSubsystemDirty(int)"/>.
    /// After all records are processed, <see cref="BinaryInterpreter.Apply"/>
    /// iterates set bits and calls the registered flusher for each.
    /// </summary>
    public uint DirtySubsystemsMask { get; set; }

    /// <summary>
    /// Bitmask of descriptor ordinals touched during the current Apply call.
    /// Bit <c>N</c> is set by <see cref="MarkDescriptorDirty(long)"/>.
    /// Useful for building ACK bitmasks; the actual SmartEgress flush is driven by
    /// <c>PatchContext.FlushDirtyMarks()</c> at the end of Apply.
    /// </summary>
    public ulong DirtyDescriptorMask { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    internal BinaryPatchContext(IEntityPatchContext patchContext, int scratchpadSize)
    {
        PatchContext   = patchContext ?? throw new ArgumentNullException(nameof(patchContext));
        ScratchpadData = scratchpadSize > 0 ? new byte[scratchpadSize] : Array.Empty<byte>();
    }

    // ── Scratchpad access ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <c>ref T</c> into the installer-reserved scratchpad block at
    /// <paramref name="byteOffset"/>.  Uses <see cref="MemoryMarshal.Cast{TFrom,TTo}"/>
    /// — allocation-free.
    /// </summary>
    /// <typeparam name="T">Unmanaged scratchpad struct type.</typeparam>
    /// <param name="byteOffset">
    /// Byte offset returned by a prior <see cref="BinaryInterpreterBuilder.ReserveScratchpad"/>
    /// call.
    /// </param>
    public ref T GetScratchpad<T>(int byteOffset) where T : struct
        => ref MemoryMarshal.Cast<byte, T>(ScratchpadData.AsSpan(byteOffset))[0];

    // ── Dirty-mark helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Marks subsystem flusher bit <paramref name="bit"/> as dirty.
    /// After all records are processed, the corresponding flusher is called.
    /// </summary>
    /// <param name="bit">Zero-based bit index (0–31).</param>
    public void MarkSubsystemDirty(int bit)
        => DirtySubsystemsMask |= (1u << bit);

    /// <summary>
    /// Records that descriptor ordinal <paramref name="ordinal"/> was touched.
    /// Used for ACK bitmask construction; SmartEgress is flushed via
    /// <c>PatchContext.FlushDirtyMarks()</c>.
    /// </summary>
    /// <param name="ordinal">
    /// Descriptor type ordinal (e.g. <c>(long)EDescriptorType.dtEntityInfo</c>).
    /// Must be in range 0–63.
    /// </param>
    public void MarkDescriptorDirty(long ordinal)
    {
        if (ordinal >= 0 && ordinal < 64)
            DirtyDescriptorMask |= (1UL << (int)ordinal);
    }
}
