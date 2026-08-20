using System.Threading;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97d</c>) — <c>R-76</c>'s SECOND CLOCK, which had never been built.</b>
///
/// <para>📄 <b><c>DESIGN_Variable_Watch_Pinning.md</c> §4 (<c>R-76</c>) names TWO clocks:</b>
/// <list type="table">
///   <item><term>⭐ <b>VALUE</b></term><description><i>what does this field hold?</i> — fires
///   <b>every brain tick</b>, all rows. ⭐ That is <c>Fdp.Core.BehaviorFrame</c>, built by Batch 94.</description></item>
///   <item><term>⭐⭐⭐ <b>BINDING</b></term><description><i>which entity is this row ABOUT?</i> —
///   ⛔ <b>not the tick</b>: ⭐ <b>only on selection change</b>, and only for a chameleon row.</description></item>
/// </list></para>
///
/// <para>🔴🔴 <b>The gap this closes.</b> <see cref="VariableRowSampler"/> had <b>one</b> clock — the
/// behaviour pulse — so ⛔ <b>a selection change re-evaluated nothing</b>, and while time is stopped it
/// never would. 📌 The user: <i>"when entity selection changes, the watch row must update (accessor
/// evaluated) <b>even if time currently stopped</b>. this was for sure part of the original design."</i>
/// ⭐ Correct on every point.</para>
///
/// <para>⭐⭐ <b>Why a COUNTER and not a subscription.</b> It mirrors <c>BehaviorFrame</c> exactly, which
/// is the idiom Batch 94 established and which the sampler already knows how to read: a clock is
/// <b>polled</b>, and a panel that did not repaint has nothing to re-sample anyway. ⛔ A subscription
/// would need every panel to register and unregister — 📌 the shape <c>R-67</c> keeps filing.</para>
///
/// <para>⚠ <b>Over-firing is HARMLESS, under-firing is the bug.</b> A spurious bump costs one extra
/// accessor call on the next repaint; a missed one leaves the designer looking at another entity's
/// value. ⇒ ⭐ <c>SharedEntitySelection</c> bumps it on every real change, and nothing needs to prove
/// that no OTHER selection object exists.</para>
///
/// <para>⛔⛔ <b>This is NOT a weakening of <c>R-103</c></b> *(one accessor call per brain frame)*.
/// ⭐ <c>R-76</c> has always been two clocks; the value clock is unchanged and this one answers a
/// different question.</para>
/// </summary>
public static class EntityBindingFrame
{
    private static uint _counter;

    /// <summary>⭐ The current binding generation. ⛔ Not a time — only its CHANGES mean anything.</summary>
    public static uint Current => Volatile.Read(ref _counter);

    /// <summary>
    /// ⭐ Records that the entity a chameleon row is bound to may have moved.
    /// ⛔ Called by <c>SharedEntitySelection</c> and nothing else — 📌 the handoff: <i>"the signal
    /// already exists; do not invent a second one."</i>
    /// </summary>
    public static void Advance() => Interlocked.Increment(ref _counter);
}
