using System;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// ⭐⭐⭐ <b>The ONE owner of the <c>+8</c>.</b>
///
/// <para>📌 <b><c>Q32</c> §2.1's sizing note, verbatim:</b> <i>"the read path uses
/// <c>8 + OffsetBytes</c> — there is an <b>8-byte header</b> before the fields. ⛔ <b>Whoever computes
/// the offset must own that <c>+8</c> in exactly one place, not two.</b>"</i></para>
///
/// <para>📐 <b>Measured before writing this (Batch 84):</b> the literal <c>8</c> appeared at <b>ten</b>
/// sites — ⭐ <b>two in the EDITOR's read path</b> (<c>BlueprintDebugSession.CaptureAiPrimitiveState</c>,
/// its layout arm and its definition arm) and ⚠ <b>eight in <c>AiPrimitiveEmitter</c> as string
/// literals inside GENERATED C#</b> (<c>memory + 8</c>). ⇒ the write would have made an eleventh.</para>
///
/// <para>⛔⛔ <b>The emitter's eight are deliberately NOT unified here.</b> They are emitted SOURCE
/// TEXT; routing them through this constant changes the generated text, which moves the compiler
/// goldens — and Batch 84's handoff marks a golden move as a <b>STOP</b>. ⭐ Filed rather than done:
/// the editor's copies are the ones that must agree with each other, because the read and the write
/// must address the same byte or the designer edits a field they were not looking at.</para>
///
/// <para>⚠ <b>Why this is not merely tidiness.</b> 📌 <c>Q32</c> §2.1, same note: <i>"an out-of-range
/// offset/size is <b>MEMORY CORRUPTION</b>, not a wrong value."</i> ⇒ a read path and a write path that
/// disagree by 8 bytes do not show a wrong number — they scribble on the neighbouring field, and on
/// <c>Blackboard1024</c> the neighbour may belong to BTree or HSM (📌 <c>R-65</c>).</para>
/// </summary>
public static class WorkingStateLayout
{
    /// <summary>
    /// ⭐ The <c>StructureHash</c> that precedes every working-state block. 📐 Written by the emitted
    /// prologue (<c>*(ulong*)memory = StructureHash</c>) and checked by the reader before it trusts a
    /// single field, which is what makes a stale layout show nothing rather than garbage.
    /// </summary>
    public const int HeaderBytes = sizeof(ulong);

    /// <summary>
    /// ⭐⭐ The byte offset of a working-state field <b>within the component</b>, from its offset within
    /// the working-state block. ⛔ This is the only correct way to turn one into the other.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// ⭐ A negative field offset is a broken layout, not a field near the start — 📌 failing LOUDLY
    /// here is cheaper than a silent negative index at the memcpy.
    /// </exception>
    public static int ComponentOffsetOf(int fieldOffsetBytes)
        => fieldOffsetBytes >= 0
            ? HeaderBytes + fieldOffsetBytes
            : throw new ArgumentOutOfRangeException(nameof(fieldOffsetBytes),
                  $"A working-state field offset must not be negative (was {fieldOffsetBytes}).");

    /// <summary>
    /// ⭐ Whether <paramref name="sizeBytes"/> at <paramref name="fieldOffsetBytes"/> fits inside a
    /// component of <paramref name="componentSizeBytes"/>, <b>header included</b>.
    /// ⛔ The header is the half a caller forgets, which is exactly why the check lives with the
    /// constant rather than at each call site.
    /// </summary>
    public static bool Fits(int fieldOffsetBytes, int sizeBytes, int componentSizeBytes)
        => fieldOffsetBytes >= 0
        && sizeBytes        >= 0
        && (long)HeaderBytes + fieldOffsetBytes + sizeBytes <= componentSizeBytes;
}
