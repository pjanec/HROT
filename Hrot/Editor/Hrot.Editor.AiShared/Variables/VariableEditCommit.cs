using System;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 102 (<c>102b</c>) — the outcome of a live write, WITH ITS REASON.</b>
///
/// <para>🔴🔴 <b>Why the <c>bool</c> was not enough.</b> 📌 <c>M-36</c>: five distinct causes — nothing
/// selected · no document · an unresolvable name · a stale layout · not frozen — arrived here as one
/// bare <c>false</c>, so ⛔ <b>a correctly-gated editor and a broken wire looked IDENTICAL.</b> ⚠ That
/// cost a whole measurement session and three handoffs' worth of a wrong conclusion, and the coordinator
/// called the refusal <i>"correct"</i> three times over an <b>unbuilt capability</b>.</para>
///
/// <para>⭐⭐ <b><see cref="Reason"/> is the HOST's sentence, passed through verbatim.</b> ⛔ Not an enum:
/// the causes are the host's — <c>IBlueprintDebugSession</c> lives ABOVE this assembly, which is the
/// same reason <see cref="WriteLiveValue"/> is a delegate at all. ⇒ ⭐ this assembly must not enumerate
/// causes it cannot see, and a future host with a cause nobody here imagined still says it.</para>
/// </summary>
/// <param name="Ok">⭐ The bytes landed.</param>
/// <param name="Reason">
/// ⭐ A sentence for the designer when <paramref name="Ok"/> is false. ⛔ <c>null</c> is legal and means
/// <i>"refused, and the host offered no reason"</i> — ⚠ the dialog then falls back to its generic text
/// rather than inventing one.
/// </param>
public readonly record struct LiveWriteOutcome(bool Ok, string? Reason)
{
    /// <summary>⭐ It landed.</summary>
    public static LiveWriteOutcome Landed => new(true, null);

    /// <summary>⭐ It did not, and here is why.</summary>
    public static LiveWriteOutcome Refused(string? reason) => new(false, reason);
}

/// <summary>
/// ⭐⭐ Writes <paramref name="bytes"/> as <paramref name="row"/>'s LIVE value, returning whether it
/// landed <b>and why not</b>. 📌 Ruling 15 — a host that is not frozen must answer a REFUSAL, ⛔ never
/// throw: the UI asks this to decide whether to grey a control (📌 the visual-check guide's <c>F3</c>:
/// <i>"every refusal GREYED WITH A TOOLTIP, not a click that dead-ends"</i>).
/// </summary>
public delegate LiveWriteOutcome WriteLiveValue(VariableRow row, ReadOnlySpan<byte> bytes);

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
        /// ⛔ <b>The run state does not route the edit to the arm that was asked for.</b>
        ///
        /// <para>⭐⭐⭐ <b><c>W3</c> renamed this from <c>RefusedRunning</c>, and the rename IS the
        /// change</b> — 📌 <c>R-126</c> deletes <i>"the sim is running"</i> as a reason to refuse, so a
        /// member still called <c>RefusedRunning</c> would name a rule that no longer exists.</para>
        ///
        /// <para>⭐ <b>Two sites produce it, and both are honest:</b> <see cref="CommitInitialValue"/>
        /// asked for the JSON arm while the run state routes live; and <see cref="Commit"/>'s
        /// <c>Replay</c> arm, which has no production producer *(see <see cref="Target.Nowhere"/>)*.
        /// ⛔ <b>Running no longer reaches either.</b></para>
        /// </summary>
        RefusedRunState,

        /// <summary>⛔ The row cannot be written at all — node-owned, passthrough, or stale.</summary>
        RefusedReadOnly,

        /// <summary>
        /// ⭐⭐⭐ <b>Batch 96 — the declaration's OWNER could not be resolved, so there is nowhere to
        /// write the initial value.</b>
        ///
        /// <para>🔴🔴 <b>Why this had to become its own word.</b> 📐 Measured: the production binder is
        /// constructed with <c>assetOf: null</c> *(<c>PerspectiveWorkspaceRegistrar</c>, and
        /// <c>assetOf</c> had <b>ZERO production call sites</b> — only two tests passed one)* ⇒
        /// <c>CommitInitialValue</c> hit <c>if (asset is null) return Outcome.RefusedReadOnly;</c> on
        /// <b>every OK, on every host, for every row, in the normal authoring state.</b></para>
        ///
        /// <para>⛔⛔ <b>And it then told the designer <i>"This row cannot be written — it is
        /// node-owned, a passthrough, or stale"</i>, which was a LIE about a perfectly ordinary
        /// variable.</b> 📌 The user's rule is <i>"same information value, no false expectations"</i> —
        /// ⭐ a refusal that misnames its own cause is worse than a silent one, because it sends the
        /// designer to fix the wrong thing.</para>
        ///
        /// <para>⚠ <b>It is still reachable on Blueprint after the wire</b>: <c>asset</c> is an
        /// <c>IBlackboardManagedAsset</c> and <c>BlueprintAsset</c> is not one — 📌 the same vocabulary
        /// mismatch <c>95a</c> fixed for READING, unfixed for WRITING. ⭐ Filed, and now it says so.</para>
        /// </summary>
        RefusedNoDeclarationOwner,

        /// <summary>
        /// ⛔ The write target was the LIVE blackboard and no live writer was supplied, or it refused.
        /// ⭐ Distinct from <see cref="RefusedRunState"/>: the run state ALLOWED the write and the
        /// mechanism did not arrive — 📌 exactly the silent-default shape, so it gets its own word.
        /// </summary>
        LiveWriteUnavailable,
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 102 (<c>102b</c>) — an outcome PLUS the host's own sentence.</b>
    ///
    /// <para>⚠ <b>Only the live arm can carry a <see cref="Detail"/>.</b> Every other outcome is decided
    /// in THIS assembly, where the dialog's own text already names the cause exactly; ⛔ the live arm is
    /// the one whose causes live above it — 📌 see <see cref="LiveWriteOutcome"/>.</para>
    /// </summary>
    /// <param name="Detail">⭐ The host's sentence, or <c>null</c> ⇒ the dialog uses its generic text.</param>
    public readonly record struct Result(Outcome Outcome, string? Detail)
    {
        public static Result Of(Outcome outcome) => new(outcome, null);
    }

    /// <summary>⭐⭐ Where an edit would land, given the run state.</summary>
    public enum Target
    {
        /// <summary>⭐ Not running ⇒ the declaration's initial value, as JSON.</summary>
        InitialValue,

        /// <summary>
        /// ⭐ <b>The live blackboard, surgically — STAGED.</b>
        /// ⭐⭐⭐ <c>W3</c>: this is now the target for <b>running as well as paused</b>
        /// *(<c>R-126</c>)*, and the bytes are pulled in by the kernel's <c>PreFrame</c> drain at the
        /// next advancing tick rather than written on the spot.
        /// </summary>
        LiveBlackboard,

        /// <summary>
        /// ⛔ <b>Replay ⇒ nowhere.</b>
        ///
        /// <para>⚠⚠ <b><c>W3</c> NARROWED this arm — it used to catch free-running too.</b>
        /// 📌 <c>R-126</c>: <i>"running is not a reason to refuse, it is a reason to STAGE."</i></para>
        ///
        /// <para>📐 <b>Measured, and stated so nobody reads this arm as live behaviour:</b>
        /// <c>RunStateSource.Resolve</c> yields only <c>Planning</c> / <c>Paused</c> / <c>Running</c> —
        /// ⛔ <b><c>Replay</c> has NO production producer</b>. ⭐ The arm is kept because
        /// <c>VariableEditPolicy.Resolve</c> already denies the dialog outright in <c>Replay</c>, and a
        /// second gate agreeing with the first costs nothing; ⛔ it is not a claim that anyone can
        /// reach it.</para>
        /// </summary>
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
    /// <remarks>
    /// ⭐⭐⭐ <b><c>W3</c> (<c>2026-08-22</c>) — RUNNING NOW LANDS ON THE LIVE ARM.</b>
    /// 📄 <c>DESIGN_Staged_Live_Write.md</c> §1's run-state table *(<b>running</b>: before <i>refused</i>,
    /// after <i>stages → yellow → drains next tick</i>)*.
    ///
    /// <para>⚠⚠ <b>This SUPERSEDES ruling 15's narrowing, which the summary above still quotes.</b>
    /// 🔒 Ruling 15 said the edit <i>"makes sense ONLY if sim is paused… at that time nothing else
    /// changes the blackboard."</i> ⛔ <b><c>R-126</c>, later and from the same user, overrules it
    /// directly:</b> <i>"I do not understand how comes that something can be unwritable… we should be
    /// able to write anything anywhere"</i> ⇒ <i>"<c>RefusedRunning</c> and
    /// <c>LiveWriteRefusal.NotFrozen</c> are deleted."</i></para>
    ///
    /// <para>⭐ <b>Ruling 15's REASON is honoured rather than discarded.</b> Its worry was that a
    /// running sim overwrites the designer's bytes. ⚠ It does not any more: the write STAGES, and the
    /// kernel's <c>PreFrame</c> drain applies it at the top of a tick — <b>before</b> <c>Input</c> and
    /// before any behaviour runs. ⇒ nothing races it within that tick.</para>
    /// </remarks>
    public static Target TargetFor(VariableRunState runState)
        => VariableValue.ModeFor(runState) == VariableValueMode.Initial ? Target.InitialValue
         : runState == VariableRunState.Replay                          ? Target.Nowhere
         :                                                                Target.LiveBlackboard;

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
        if (TargetFor(runState) != Target.InitialValue) return Outcome.RefusedRunState;

        // ⭐⭐⭐ Batch 98 (98a) — ASK THE ROW FIRST, exactly as ResolveEntry does for READING.
        // 🔴🔴 Measured: the asset arm below type-tests store.ActiveAsset against
        //    IBlackboardManagedAsset, and BlueprintAsset is not one ⇒ in PLANNING — the ordinary
        //    authoring state — OK returned RefusedNoDeclarationOwner on EVERY Blueprint variable,
        //    EVERY time. 📌 BP-355 named this asymmetry and it was never given to anyone.
        // ⭐ ONE preference order, not two mechanisms: PerspectiveWorkspaceRegistrar:836 already
        //   resolves a row's DECLARATION by asking the row and falling back to the store. This is
        //   that same order, for the write.
        // ⚠ The session is committed INSIDE the arm that will land it — see the remarks. A row whose
        //   source refuses (a read-only macro graph, BP1664) falls through to the asset arm rather
        //   than reporting a write nobody performed.
        if (row.WriteDefault is { } writeBack)
        {
            var carried = DefaultValueAuthoring.CommitAndSerialize(session, fieldType);
            if (writeBack(carried)) return Outcome.Ok;
            return Outcome.RefusedNoDeclarationOwner;
        }

        // ⭐⭐⭐ Batch 96 — its OWN outcome. 🔴 This used to return RefusedReadOnly, whose message names
        //    the row kind ("node-owned, a passthrough, or stale") — and the row is usually none of
        //    those. See RefusedNoDeclarationOwner for the measurement.
        // ⭐ Still reachable, and NOT dead code: a row built without a source-supplied write-back —
        //   a hand-constructed row, or any host that has an asset but no schema source — lands here.
        if (asset is null) return Outcome.RefusedNoDeclarationOwner;

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
        => CommitWithDetail(session, asset, row, fieldType, runState, writeLive).Outcome;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 102 (<c>102b</c>) — the same commit, carrying the host's REASON.</b>
    ///
    /// <para>⭐ <see cref="Commit"/> is a projection of this, not a second implementation — ⛔ the two
    /// cannot disagree about an arm. ⚠ It stays because most callers only ever ask <i>"did it
    /// land?"</i>, and widening every one of them would be churn for no information.</para>
    /// </summary>
    public static Result CommitWithDetail(
        IEditSession             session,
        IBlackboardManagedAsset? asset,
        VariableRow              row,
        Type                     fieldType,
        VariableRunState         runState,
        WriteLiveValue?          writeLive = null)
    {
        if (session is null)   throw new ArgumentNullException(nameof(session));
        if (fieldType is null) throw new ArgumentNullException(nameof(fieldType));

        if (!row.CanEverBeWritten) return Result.Of(Outcome.RefusedReadOnly);

        switch (TargetFor(runState))
        {
            case Target.InitialValue:
                return Result.Of(CommitInitialValue(session, asset, row, fieldType, runState));

            case Target.LiveBlackboard:
                // ⭐⭐⭐ Batch 102 (102b) — THE SILENT DEFAULT NAMES ITSELF.
                // ⛔ This branch is the shape M-36 is about: the run state SAID yes and the mechanism
                //    never arrived. ⚠ It used to be indistinguishable from a host that considered the
                //    write and refused it — ⇒ six batches of "the refusal is correct".
                if (writeLive is null)
                    return new Result(
                        Outcome.LiveWriteUnavailable,
                        "No live writer is installed for this editor, so a paused edit has nowhere to "
                        + "go. This is a missing capability on this host, not a property of the "
                        + "variable.");

                // ⭐ Committed FIRST so the boxed result exists, but only inside the arm that will
                //   land it — see the remarks.
                // ⭐⭐⭐ Batch 97 (97a) — UNWRAPPED, exactly as the JSON arm is. ⛔ A scalar session
                //    commits a ScalarEditBox<T>, whose LAYOUT is not the scalar's: writing its bytes
                //    into the blackboard would put the wrapper's image where the field lives.
                //    ⚠ Both arms or neither — a wrapper that leaks on one path only is worse than one
                //    that leaks on both, because half the feature would look correct.
                var value = ScalarEditBox.Unwrap(session.Commit(), fieldType);
                var bytes = ComponentBytes.Of(value, ComponentBytes.SizeOf(fieldType));

                // ⭐ The host's sentence is carried through UNCHANGED — ⛔ this assembly must not
                //   paraphrase a cause it cannot see. 📌 LiveWriteOutcome.Reason.
                var attempt = writeLive(row, bytes);
                return attempt.Ok
                    ? Result.Of(Outcome.Ok)
                    : new Result(Outcome.LiveWriteUnavailable, attempt.Reason);

            default:
                // ⛔ REPLAY ONLY, since W3. 📌 R-126 deleted the free-running refusal; and 📐 Replay has
                //    no production producer (RunStateSource.Resolve yields Planning/Paused/Running), so
                //    this arm is a second agreement with VariableEditPolicy rather than a live path.
                return Result.Of(Outcome.RefusedRunState);
        }
    }
}
