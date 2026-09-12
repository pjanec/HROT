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
    /// ⭐ Records in <see cref="DirtyDescriptorMask"/> that ordinal <paramref name="ordinal"/> was touched.
    /// ⚠ A <b>REPORT</b> of what this Apply touched — ⛔ not the egress mechanism.
    ///
    /// <para>⭐⭐⭐ <b><c>Q59-E</c> — this no longer forwards anywhere, and no longer needs to.</b> Under
    /// <c>AX-015</c> it forwarded to <c>IEntityPatchContext.MarkDescriptorDirty</c> so the binary installers
    /// could reach SmartEgress. ⇒ that seam member is <b>DELETED</b>: an applier now records the COMPONENT it
    /// wrote and <c>DescriptorOwnershipMap</c> — fed by what the network layer declares — supplies the
    /// descriptors. ⭐ So the installers no longer name a descriptor at all, and neither does any FDP type.</para>
    ///
    /// <para>⚠⚠ <b>Nothing in PRODUCTION reads this mask</b> — measured under <c>AX-015</c> and still true.
    /// ⭐ It is retained because it is a documented capability of the generic interpreter *(reset per
    /// <c>Apply</c>, exercised by <c>BinaryInterpreterTests</c>)*, and <c>Q59</c>'s scope is the attribute
    /// vocabulary, not this. ⛔ "No rush removals": ⚠ but do not mistake it for an egress path.</para>
    /// </summary>
    /// <param name="ordinal">An opaque ordinal. Must be in range 0–63 to be recorded.</param>
    public void MarkDescriptorDirty(long ordinal)
    {
        if (ordinal >= 0 && ordinal < 64)
            DirtyDescriptorMask |= (1UL << (int)ordinal);
    }
}
