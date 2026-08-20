using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b>Batch 90 — a live projection of an asset's variables, in whichever shape the host
/// ACTUALLY HAS.</b>
///
/// <para>📐 <b>Why this exists beside <see cref="ILiveBlackboardValueProvider"/>.</b> That interface
/// hands out name → <b><c>string</c></b> and has exactly ONE consumer —
/// <c>BlackboardAuthoringWindow</c> *(the standalone Blackboard Variables window)*. ⛔ **The Details
/// table never read it**, which is why <c>88a</c> made that window live on Blueprint and Details stayed
/// <c>(pending)</c> — 📌 <c>BP-334</c>.</para>
///
/// <para>⭐⭐ <b>ONE concept, TWO shapes, and the shape is not a style choice — it is what each host
/// holds:</b></para>
/// <list type="table">
///   <item>
///     <term><b>Blueprint</b></term>
///     <description>its live source is <c>BlueprintStateSnapshot.FieldValues</c>, an
///     <c>IReadOnlyDictionary&lt;string, object&gt;</c> the debug session <b>already decoded</b>
///     ⇒ ⭐ <see cref="GetLiveObjects"/>. ⛔ Re-encoding those to bytes so the byte arm can decode
///     them again is <c>REPORT_Batch88</c> §2.2's rejected option <c>(a)</c>.</description>
///   </item>
///   <item>
///     <term><b>BTree / HSM</b></term>
///     <description><c>LiveBlackboardValueProvider</c> projects
///     <c>(BrainBlackboard, Type, ByteOffset)</c> — ⭐⭐ <b>it HAS the bytes</b>, and only formats at
///     the very end ⇒ ⭐ <see cref="GetLiveBytes"/>, which fills the <c>readRaw</c> seam that was
///     designed for it and has been <c>null</c> since it was built. ⛔ Routing this host through the
///     object arm would throw away a WORKING change highlight *(§4a diffs bytes)*.</description>
///   </item>
/// </list>
///
/// <para>⛔⛔ <b>Deliberately NOT a <c>string</c> arm.</b> The pipeline is
/// <b>bytes → decoder → object → <c>VariableValueFormatter</c> → text</b>, and a string arm would hand
/// <b>notation</b> to the provider ⇒ two notations for one value — 📌 exactly the class of defect
/// <c>BP-01</c>/<c>C8</c> closed. ⭐ Both arms below enter the ONE formatter, which keeps ownership of
/// notation, elision, <c>&lt;unreadable&gt;</c> and the struct tooltip.</para>
///
/// <para>⭐ <b>Both members default to <c>null</c></b>, so an implementer declares only the arm it can
/// actually serve and ⛔ nobody is forced to fabricate the other. ⚠ <c>null</c> means <i>"I cannot
/// project this asset right now"</i>; ⭐ an EMPTY map means <i>"I can, and nothing is live"</i> — and
/// those are different facts to the cell.</para>
///
/// <para>⭐⭐⭐ <b>ABSENCE IS MEANINGFUL, and the honesty rule depends on it.</b> A name that is NOT a
/// key has not been written by this run ⇒ its cell reads <c>(pending)</c>. ⛔ <b>An implementer must
/// not pad the map with zeros for names it could not project</b> — 📌 guide row <c>C9</c>: <i>"a
/// variable declared but never written by the run reads <c>(pending)</c>"</i>, and a zero where
/// <c>(pending)</c> belongs is a REGRESSION, not a fix.</para>
/// </summary>
public interface ILiveVariableProjection
{
    /// <summary>⭐ Already-decoded CLR values by variable name, or <c>null</c> when this provider cannot
    /// project the asset. ⛔ Only keys it genuinely read — see the type remarks on absence.</summary>
    IReadOnlyDictionary<string, object>? GetLiveObjects(IEditableAsset asset) => null;

    /// <summary>⭐ Raw value bytes by variable name, or <c>null</c> when this provider cannot project
    /// the asset. ⭐⭐ Bytes keep §4a's change highlight LIVE, which is why a host that has them should
    /// supply them rather than objects.</summary>
    IReadOnlyDictionary<string, byte[]>? GetLiveBytes(IEditableAsset asset) => null;
}
