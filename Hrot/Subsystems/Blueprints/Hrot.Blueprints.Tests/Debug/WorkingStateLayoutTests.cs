using System;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Batch 84 item 2 — the <c>+8</c> has ONE owner.</b>
///
/// <para>📌 <b><c>Q32</c> §2.1, verbatim:</b> <i>"the read path uses <c>8 + OffsetBytes</c> — there is
/// an <b>8-byte header</b> before the fields. ⛔ <b>Whoever computes the offset must own that
/// <c>+8</c> in exactly one place, not two.</b>"</i> · <i>"an out-of-range offset/size is <b>MEMORY
/// CORRUPTION</b>, not a wrong value."</i></para>
///
/// <para>📐 <b>Measured before building:</b> the literal <c>8</c> stood at <b>ten</b> sites — two in
/// the editor's read path and eight inside <c>AiPrimitiveEmitter</c> as GENERATED SOURCE TEXT.
/// ⭐ The two editor copies now route through this type; ⛔ the emitter's eight are deliberately left
/// alone, because rewriting emitted text moves the compiler goldens and the handoff marks that a
/// <b>STOP</b>. ⚠ Filed, not fixed.</para>
/// </summary>
public sealed class WorkingStateLayoutTests
{
    /// <summary>⭐ The header is the <c>StructureHash</c> the emitted prologue writes at offset 0.</summary>
    [Fact]
    public void TheHeaderIsOneStructureHash()
        => Assert.Equal(sizeof(ulong), WorkingStateLayout.HeaderBytes);

    /// <summary>⭐ A field's component offset is its block offset plus the header, and nothing else.</summary>
    [Theory]
    [InlineData(0,   8)]
    [InlineData(4,  12)]
    [InlineData(64, 72)]
    public void AFieldOffsetIsShiftedByTheHeader(int fieldOffset, int expected)
        => Assert.Equal(expected, WorkingStateLayout.ComponentOffsetOf(fieldOffset));

    /// <summary>
    /// ⛔ <b>A negative field offset THROWS.</b> ⭐ Not clamped to zero: a broken layout that quietly
    /// reads the header as if it were a field would show the structure hash's bytes as a value.
    /// </summary>
    [Fact]
    public void ANegativeFieldOffset_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => WorkingStateLayout.ComponentOffsetOf(-1));

    /// <summary>
    /// ⭐⭐⭐ <b>THE bug this type exists to prevent, pinned.</b> A 4-byte field at block offset
    /// <c>1020</c> looks like it fits a 1024-byte component — ⛔ <b>it does not</b>, because the header
    /// pushes it to <c>[1028, 1032)</c>. ⚠ <b>That is the off-by-header a caller who owns their own
    /// <c>+8</c> gets wrong</b>, and on <c>Blackboard1024</c> it is a write past the component
    /// (📌 <c>R-65</c>: shared by BTree, HSM and Blueprint).
    /// </summary>
    [Fact]
    public void AFieldThatFitsWithoutTheHeader_DoesNotFitWithIt()
    {
        Assert.False(WorkingStateLayout.Fits(fieldOffsetBytes: 1020, sizeBytes: 4, componentSizeBytes: 1024));
        Assert.True( WorkingStateLayout.Fits(fieldOffsetBytes: 1012, sizeBytes: 4, componentSizeBytes: 1024));
    }

    /// <summary>⭐ The exact last byte fits; one past it does not.</summary>
    [Fact]
    public void TheBoundaryIsInclusiveOfTheLastByte()
    {
        Assert.True( WorkingStateLayout.Fits(1016, 0, 1024));
        Assert.False(WorkingStateLayout.Fits(1016, 1, 1024));
        Assert.False(WorkingStateLayout.Fits(0,   -1, 1024));
    }
}
