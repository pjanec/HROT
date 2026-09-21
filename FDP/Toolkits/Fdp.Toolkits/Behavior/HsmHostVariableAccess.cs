using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>E7a</c> / <c>E3b</c> — THE FIRST IMPLEMENTATION OF <see cref="IHostVariableAccess"/>.</b>
/// 📄 <c>DESIGN_Parameter_Model.md</c> §3.4 · <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.7.
///
/// <para>🔴🔴 <b>That interface has been DECLARED-NOT-IMPLEMENTED since <c>2026-08-16</c></b> — zero
/// implementers, and every resolver received <c>null</c>. ⛔ Not neglect: it was added early on purpose,
/// because widening <see cref="ParseParamsDelegate"/> is a breaking change to every resolver and doing
/// that twice is the avoidable cost. ⭐ <b>What it was waiting for is a call site where a host actually
/// exists</b> — and <c>E3a</c>+<c>E3b-0</c> built one: the hosted occurrence's seed.</para>
///
/// <para>⭐⭐ <b>It addresses the HOST's params region by NAME</b>, through the map the host's own
/// registrar emitted (<see cref="HsmParamBindings.RegisterVariables"/>) — the same <c>packedFields</c>
/// list that drives the host's <c>ParseParams</c>. ⛔ Never a raw offset: a cross-asset read is
/// <c>StructureHash</c>-versioned, so a name can be re-resolved after a layout change and an offset
/// cannot (§3.4).</para>
///
/// <para>⛔⛔ <b>READ-ONLY, and that is a ruling, not an omission.</b> A resolver never writes its host
/// — a write path here would be a SECOND supply mechanism (ruling 9), and <c>Q41-A1</c> routes the
/// write case through shared slots instead.</para>
///
/// <para>⚠ <b>Valid for the CALLING FRAME ONLY.</b> It holds a pointer into the host's
/// <c>BrainBlackboard</c>; a structural change can move the chunk. ⭐ It is constructed at the child's
/// activation, used, and dropped — which is exactly <i>resolve-once</i> (§3.1), not live binding.</para>
/// </summary>
public readonly unsafe struct HsmHostVariableAccess : IHostVariableAccess
{
    private readonly uint _machineId;
    private readonly byte* _hostParams;
    private readonly int _hostParamsSize;

    /// <param name="machineId">
    /// The HOST machine's <c>StructureHash</c> — <c>InstanceHeader.MachineId</c>, the same identity
    /// <see cref="HsmOccurrence.KeyFor"/> folds into the occurrence key.
    /// </param>
    /// <param name="hostParams">Base of the host entity's <c>BrainBlackboard.BehaviorParameters</c>.</param>
    /// <param name="hostParamsSize">Its length, so a read can be bounds-checked rather than trusted.</param>
    public HsmHostVariableAccess(uint machineId, byte* hostParams, int hostParamsSize)
    {
        _machineId = machineId;
        _hostParams = hostParams;
        _hostParamsSize = hostParamsSize;
    }

    /// <summary>
    /// ⭐ Reads a host variable by name. ⛔ <c>false</c> — never a zero VALUE — when the name is absent,
    /// the machine is unknown, the sizes disagree, or the read would leave the params region.
    ///
    /// <para>⚠ <b>The size check is a TYPE check in disguise, and it is the honest one available.</b>
    /// The map records the packed byte size; a <c>T</c> of a different width is a layout disagreement
    /// and fails. ⛔ It cannot catch two same-width types (an <c>int</c> read as a <c>float</c>) — the
    /// packed map carries no CLR type, and pretending otherwise would be worse than saying so.</para>
    /// </summary>
    public bool TryRead<T>(string variableName, out T value) where T : unmanaged
    {
        value = default;
        if (!TryResolve(variableName, sizeof(T), out int offset)) return false;

        value = Unsafe.ReadUnaligned<T>(ref *(_hostParams + offset));
        return true;
    }

    /// <summary>
    /// ⭐ Byte-wise read for a caller that knows the shape but not the CLR type. ⛔ Writes NOTHING on
    /// failure — a partial copy would be the silent-corruption case this interface exists to avoid.
    /// </summary>
    public bool TryReadBytes(string variableName, Span<byte> destination, out int written)
    {
        written = 0;
        if (!HsmParamBindings.TryGetVariable(_machineId, variableName, out int offset, out int size))
            return false;
        if (destination.Length < size) return false;
        if (!InBounds(offset, size)) return false;

        new ReadOnlySpan<byte>(_hostParams + offset, size).CopyTo(destination);
        written = size;
        return true;
    }

    private bool TryResolve(string variableName, int expectedSize, out int offset)
    {
        offset = 0;
        if (!HsmParamBindings.TryGetVariable(_machineId, variableName, out int at, out int size))
            return false;
        if (size != expectedSize) return false;     // a layout disagreement — fail closed
        if (!InBounds(at, size)) return false;

        offset = at;
        return true;
    }

    private bool InBounds(int offset, int size)
        => _hostParams != null && offset >= 0 && size >= 0 && offset + size <= _hostParamsSize;

    /// <summary>
    /// ⭐⭐ Builds the access for the occurrence the kernel has stamped, or <c>null</c> when there is no
    /// host to read.
    ///
    /// <para>⛔ <b><c>null</c> is the DEFINED value for "no host"</b> (§3.4), so a resolver can answer
    /// <i>"do I have a host?"</i> without a sentinel. ⚠ An HSM instance with no registered variables
    /// yields an access that resolves nothing — which fails closed, read by read, rather than
    /// pretending the host has none.</para>
    /// </summary>
    public static IHostVariableAccess? For(void* hsmInstance, byte* hostParams, int hostParamsSize)
    {
        if (hsmInstance == null || hostParams == null) return null;

        uint machineId = ((Fhsm.Kernel.Data.InstanceHeader*)hsmInstance)->MachineId;
        return new HsmHostVariableAccess(machineId, hostParams, hostParamsSize);
    }
}
