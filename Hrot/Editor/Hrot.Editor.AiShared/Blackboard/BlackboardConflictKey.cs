using System;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b><c>W7a</c> — the ONE definition of a conflict suppression's writer-pair key.</b>
///
/// <para>
/// 📄 <c>Blackboard_Authoring_Detailed_Design.md</c> §9.3: <b>suppression is PER-PAIR, not
/// per-variable</b> — <i>"a new aliasing relationship on the same variable would surface a fresh
/// diagnostic"</i>. ⇒ the key identifies the two writers, and the variable name is carried alongside
/// it, never folded in.
/// </para>
///
/// <para>
/// ⛔⛔ <b>Why this is a shared helper and not two call sites.</b> The format was built inline inside
/// <c>BlackboardAliasDropValidator</c>, and <c>W7a</c> makes the HSM validator ask the same question.
/// 🔴 <b>Two constructions of one key is the sharpest possible version of this bug:</b> the designer
/// clicks <i>Suppress</i>, one surface goes quiet and the other does not, and nothing anywhere is
/// wrong enough to fail. ⇒ <b>one function, both callers.</b>
/// </para>
///
/// <para>
/// ⚠ <b>Order-independent by construction.</b> A pair is unordered — <c>(A, B)</c> and <c>(B, A)</c>
/// are the same conflict — so the two ids are sorted before joining. ⛔ Sorting the FORMATTED strings
/// rather than the <see cref="Guid"/>s is deliberate: that is the shipped comparison, and changing it
/// would silently invalidate every suppression already persisted in an asset.
/// </para>
/// </summary>
public static class BlackboardConflictKey
{
    /// <summary>The stable key for the unordered pair <paramref name="a"/>/<paramref name="b"/>.</summary>
    public static string ForWriterPair(Guid a, Guid b)
    {
        var left  = a.ToString("N");
        var right = b.ToString("N");
        return string.CompareOrdinal(left, right) < 0 ? $"{left}_{right}" : $"{right}_{left}";
    }
}
