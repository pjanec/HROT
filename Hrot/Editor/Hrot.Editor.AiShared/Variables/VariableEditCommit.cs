using System;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Row 59 — where an edit LANDS, and only the NOT-RUNNING half.</b>
///
/// <para>📌 <b><c>Q32</c> ruling 7, verbatim:</b> <i>"Write target follows run state: running ⇒ writes
/// the <b>live blackboard</b>; not running ⇒ writes the <b>initial value in JSON</b>."</i></para>
///
/// <para>⛔⛔ <b>The RUNNING arm is NOT here and must not be added here.</b> 📌 It is sequencing row
/// <c>59c</c>, and it needs the <b>ECB surgical field write</b> first *(ruling 14)*. ⇒ ⭐ a running
/// write attempted through this path would be the unsafe route wearing the safe one's name.</para>
///
/// <para>⚠⚠ <b>Correction, Batch 84 — <c>R-65</c>.</b> This comment used to justify the refusal with
/// <i>"the whole-component route <b>exceeds <c>MaxComponentSize</c></b> and cannot work."</i>
/// 📐 <b>That is FALSE:</b> <c>EntityCommandBuffer</c>'s guard is <c>componentSize &gt; MaxComponentSize</c>
/// and <c>Blackboard1024.ByteSize == 1024</c> — <b>it fits, exactly.</b> ⭐ <b>The true argument is
/// stronger:</b> <c>Blackboard1024</c> is <b>ONE component SHARED by BTree, HSM and Blueprint at
/// disjoint offsets</b>, so a whole-component write <b>clobbers other subsystems' state</b>.
/// ⇒ ⛔ <b>cite the sharing, never the size.</b></para>
///
/// <para>⭐ <b>One serializer, not a second one.</b> <c>DefaultValueAuthoring.CommitAndSerialize</c>
/// already owns commit→JSON *(and <c>Hydrate</c> owns the way back)*; this type only decides WHERE the
/// resulting JSON goes and refuses when it must.</para>
/// </summary>
public static class VariableEditCommit
{
    /// <summary>Why a commit did not land, or <see cref="Ok"/>.</summary>
    public enum Outcome
    {
        /// <summary>The initial value was written to the declaration.</summary>
        Ok,

        /// <summary>
        /// ⛔ The sim is up. ⭐ Not an error — the LIVE target is row <c>59c</c>'s, and refusing is the
        /// honest answer until the surgical write exists.
        /// </summary>
        RefusedRunning,

        /// <summary>⛔ The row cannot be written at all — node-owned, passthrough, or stale.</summary>
        RefusedReadOnly,
    }

    /// <summary>
    /// ⭐⭐ Commits <paramref name="session"/> and writes the result as the declaration's INITIAL
    /// value.
    ///
    /// <para>⚠ <b>The session is committed <i>only</i> when the write will land.</b> ⛔ Committing and
    /// then discarding would leave the designer's edit applied to a boxed copy nobody keeps — the edit
    /// would look accepted and vanish, which is worse than a refusal.</para>
    /// </summary>
    /// <param name="asset">The declaration's owner. ⛔ Null ⇒ nothing to write to.</param>
    /// <param name="runState">
    /// ⭐ Read through the SAME <see cref="VariableValue.ModeFor"/> the Value column uses, so the
    /// write target and the displayed value can never disagree about which arm is live.
    /// </param>
    public static Outcome CommitInitialValue(
        IEditSession           session,
        IBlackboardManagedAsset? asset,
        VariableRow            row,
        Type                   fieldType,
        VariableRunState       runState)
    {
        if (session is null)   throw new ArgumentNullException(nameof(session));
        if (fieldType is null) throw new ArgumentNullException(nameof(fieldType));

        if (!row.CanEverBeWritten) return Outcome.RefusedReadOnly;

        // ⭐ ONE question, asked in one place: is the initial value what this edit means?
        if (VariableValue.ModeFor(runState) != VariableValueMode.Initial) return Outcome.RefusedRunning;

        if (asset is null) return Outcome.RefusedReadOnly;

        var json = DefaultValueAuthoring.CommitAndSerialize(session, fieldType);
        asset.UpdateVariableDefaultValueJson(row.Origin.VariablePath, json);
        return Outcome.Ok;
    }
}
