using System;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ Writes <paramref name="bytes"/> as <paramref name="row"/>'s LIVE value, returning whether it
/// landed. 📌 Ruling 15 — a host that is not frozen must answer <c>false</c>, ⛔ never throw: the UI
/// asks this to decide whether to grey a control (📌 the visual-check guide's <c>F3</c>:
/// <i>"every refusal GREYED WITH A TOOLTIP, not a click that dead-ends"</i>).
/// </summary>
public delegate bool WriteLiveValue(VariableRow row, ReadOnlySpan<byte> bytes);

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

        /// <summary>
        /// ⛔ The write target was the LIVE blackboard and no live writer was supplied, or it refused.
        /// ⭐ Distinct from <see cref="RefusedRunning"/>: the run state ALLOWED the write and the
        /// mechanism did not arrive — 📌 exactly the silent-default shape, so it gets its own word.
        /// </summary>
        LiveWriteUnavailable,
    }

    /// <summary>⭐⭐ Where an edit would land, given the run state.</summary>
    public enum Target
    {
        /// <summary>⭐ Not running ⇒ the declaration's initial value, as JSON.</summary>
        InitialValue,

        /// <summary>⭐ Frozen on a breakpoint or stepping ⇒ the live blackboard, surgically.</summary>
        LiveBlackboard,

        /// <summary>⛔ Free-running or replaying ⇒ nowhere. 📌 Ruling 15.</summary>
        Nowhere,
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE write-target decision, in one place.</b>
    ///
    /// <para>📌 <b>Ruling 15</b> <i>(user, and it NARROWS ruling 7)</i>: <i>"the change of runtime var
    /// makes sense <b>ONLY if sim is paused on breakpoint or deterministic time step</b>. at that time
    /// nothing else changes the blackboard."</i> ⇒ ⛔ <b>free-running REFUSES</b>, and that is a
    /// decision, not a later batch.</para>
    ///
    /// <para>⭐⭐ <b>Derived from <see cref="VariableValue.ModeFor"/>, not written beside it.</b> The
    /// displayed value and the write target must never disagree about which arm is live: if the cell
    /// shows the INITIAL value, the edit writes the initial value. ⚠ The paused/free-running split is
    /// the one thing <c>ModeFor</c> does NOT answer — it asks <i>"which value?"</i>, this asks
    /// <i>"may I, and where?"</i> — so it is layered ON TOP rather than duplicated.</para>
    /// </summary>
    public static Target TargetFor(VariableRunState runState)
        => VariableValue.ModeFor(runState) == VariableValueMode.Initial ? Target.InitialValue
         : runState == VariableRunState.Paused                          ? Target.LiveBlackboard
         :                                                                Target.Nowhere;

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
        if (TargetFor(runState) != Target.InitialValue) return Outcome.RefusedRunning;

        if (asset is null) return Outcome.RefusedReadOnly;

        var json = DefaultValueAuthoring.CommitAndSerialize(session, fieldType);
        asset.UpdateVariableDefaultValueJson(row.Origin.VariablePath, json);
        return Outcome.Ok;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 84 item 3 — the ONE commit, which arm it takes decided by
    /// <see cref="TargetFor"/>.</b>
    ///
    /// <para>📌 <b>Ruling 12:</b> <i>"it must work when the sim is FROZEN on a breakpoint or in
    /// deterministic stepping mode."</i> · 📌 <b>Ruling 11:</b> the Watch panel shares this mechanism —
    /// ⛔ it does not get its own.</para>
    ///
    /// <para>⛔ <b>The session is committed only when the write will land</b>, for the same reason as
    /// the initial arm: committing and discarding leaves the designer's edit applied to a boxed copy
    /// nobody keeps, so it looks accepted and vanishes.</para>
    /// </summary>
    /// <param name="writeLive">
    /// ⭐ The live writer, host-supplied. ⛔ A delegate rather than a session reference because
    /// <c>IBlueprintDebugSession</c> lives ABOVE this assembly — the same reason the value DECODER is
    /// injected. ⚠ <b>Null is NOT silently treated as "refuse"</b>: it returns
    /// <see cref="Outcome.LiveWriteUnavailable"/>, because the run state SAID yes and the mechanism did
    /// not arrive — 📌 that is the silent-default shape and it earns its own word.
    /// </param>
    public static Outcome Commit(
        IEditSession             session,
        IBlackboardManagedAsset? asset,
        VariableRow              row,
        Type                     fieldType,
        VariableRunState         runState,
        WriteLiveValue?          writeLive = null)
    {
        if (session is null)   throw new ArgumentNullException(nameof(session));
        if (fieldType is null) throw new ArgumentNullException(nameof(fieldType));

        if (!row.CanEverBeWritten) return Outcome.RefusedReadOnly;

        switch (TargetFor(runState))
        {
            case Target.InitialValue:
                return CommitInitialValue(session, asset, row, fieldType, runState);

            case Target.LiveBlackboard:
                if (writeLive is null) return Outcome.LiveWriteUnavailable;

                // ⭐ Committed FIRST so the boxed result exists, but only inside the arm that will
                //   land it — see the remarks.
                var value = session.Commit();
                var bytes = ComponentBytes.Of(value, ComponentBytes.SizeOf(fieldType));
                return writeLive(row, bytes) ? Outcome.Ok : Outcome.LiveWriteUnavailable;

            default:
                // ⛔ Free-running or replaying. 📌 Ruling 15 — a decision, not a gap.
                return Outcome.RefusedRunning;
        }
    }
}
