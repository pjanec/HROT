using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// ⭐⭐⭐ <b><c>S3</c> / <c>BP-01</c> — every type the editor OFFERS can also be SHOWN.</b>
///
/// <para>
/// 🔴🔴 <b>Seven of the eighteen could not be.</b> <c>Vector2/3/4</c>, <c>Quaternion</c> and
/// <c>FixedString32/64/128</c> are in <see cref="StaticTypeRegistry.EditorOfferableTypeIds"/> — a
/// designer picks them from the variable dropdown — and <c>MarshalFromBytes</c> fell through to
/// <c>return bytes</c> for all seven. ⛔ <i>"The watch panel shows raw hex"</i> was never a panel bug;
/// the decoder had no struct arm and <c>ResolveType</c> could not even find the type.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The rail is the SET, not a list of seven names.</b> It reads the offerable ids at run time,
/// so widening the picker (which is exactly what <c>S5</c> does next) drags this gate along with it
/// instead of leaving it pinned to today's eighteen.
/// </para>
/// </summary>
public sealed class OfferableTypeWatchDecodeTests
{
    /// <summary>
    /// ⭐ <b>The closed-set sweep.</b> ⚠ Asserts BOTH halves, because they fail differently and only
    /// one of them is visible: an unresolvable type makes the field <b>silently skipped</b>, while an
    /// undecodable one at least shows hex.
    /// </summary>
    [Fact]
    public void EveryOfferableType_ResolvesAndDecodesToAValue()
    {
        var failures = new List<string>();

        foreach (var id in StaticTypeRegistry.EditorOfferableTypeIds)
        {
            Assert.True(StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = id }, out var ir),
                $"'{id}' is offered by the picker but the registry cannot resolve it.");

            var clr = BlueprintDebugSession.ResolveType(ir.FullName);
            if (clr is null)
            {
                failures.Add($"{id} ({ir.FullName}): ResolveType returned null — the watch panel skips "
                    + "the field entirely, showing nothing at all.");
                continue;
            }

            var decoded = BlueprintDebugSession.MarshalFromBytes(new byte[ir.SizeBytes], clr);
            if (decoded is byte[])
                failures.Add($"{id} ({ir.FullName}, {ir.SizeBytes} B): MarshalFromBytes returned raw "
                    + "bytes — the watch panel shows hex for a type the picker offers.");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {StaticTypeRegistry.EditorOfferableTypeIds.Count} offerable types "
            + "cannot be shown:\n  • " + string.Join("\n  • ", failures));
    }

    /// <summary>
    /// ⭐⭐ <b>Decoded with the MANAGED model, and the VALUE is right.</b> ⚠ A green sweep above only
    /// proves *something* came back — <c>Marshal.PtrToStructure</c> would satisfy it too, and would
    /// read the marshalled layout instead of the one the generated writer stores. ⇒ pin a real value.
    /// </summary>
    [Fact]
    public void AVectorDecodesToItsActualComponents()
    {
        var bytes = new byte[12];
        System.Runtime.InteropServices.MemoryMarshal.Write(bytes, new Vector3(1.5f, -2f, 3.25f));

        var decoded = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(Vector3));

        Assert.Equal(new Vector3(1.5f, -2f, 3.25f), Assert.IsType<Vector3>(decoded));
    }

    /// <summary>
    /// ⭐⭐ <b>The bound is EXACTNESS, and it is what keeps the arm safe.</b> A slice shorter than the
    /// type would have <c>MemoryMarshal.Read</c> read past its end. ⛔ So a size mismatch falls back to
    /// raw bytes rather than throwing or reading a neighbour's memory — the watch panel must never
    /// take the debug session down, and it must never show a value it invented.
    /// </summary>
    [Fact]
    public void AByteCountThatDoesNotMatchTheType_StaysRawBytes()
    {
        Assert.IsType<byte[]>(BlueprintDebugSession.MarshalFromBytes(new byte[8],  typeof(Vector3)));
        Assert.IsType<byte[]>(BlueprintDebugSession.MarshalFromBytes(new byte[16], typeof(Vector3)));
    }

    /// <summary>
    /// ⭐ <b>A MANAGED value type is refused</b> — its bytes are references, and reading them as data
    /// would print a pointer as a number. ⚠ Checked with the CLR's own
    /// <c>RuntimeHelpers.IsReferenceOrContainsReferences</c>, the same test the JIT applies to an
    /// <c>unmanaged</c> constraint, so this can never disagree with what the blackboard would accept.
    /// </summary>
    [Fact]
    public void AManagedValueTypeIsRefused()
    {
        Assert.False(BlueprintDebugSession.TryReadStruct(
            new byte[System.Runtime.InteropServices.Marshal.SizeOf<ManagedPair>()],
            typeof(ManagedPair), out _));
    }

    private struct ManagedPair
    {
        public int Count;
        public string Label;   // the reference that makes the whole struct managed
    }
}
