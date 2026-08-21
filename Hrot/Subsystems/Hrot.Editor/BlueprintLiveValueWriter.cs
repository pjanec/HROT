using System;
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97c</c>) — <see cref="BlueprintLiveValueProvider"/>'s TWIN, on the WRITE side.</b>
///
/// <para>🔴🔴 <b>What was missing, measured.</b> <c>IBlueprintDebugSession.TryWriteWorkingStateField</c>
/// shipped in Batch 84 and <c>WriteLiveValue</c> shipped with it — and 📐 <b>Batch 96 measured ZERO
/// production call sites for either</b>: the composition root passed no <c>writeLive</c>, so
/// <c>VariableEditCommit.Commit</c> hit <c>if (writeLive is null) return LiveWriteUnavailable;</c> on
/// <b>every paused edit, on every host.</b> ⛔ That is <c>R-67</c> again — <i>"a production caller that
/// HAS a dependency must PASS it"</i> — and the reason it survived six batches is that the refusal is
/// a legitimate outcome, so a refusing editor looks exactly like a correctly-gated one.</para>
///
/// <para>⭐⭐ <b>Blueprint ONLY, and it says so.</b> BTree and HSM keep returning
/// <c>LiveWriteUnavailable</c> because they genuinely have no staged-write path — ⛔ <b>faking one
/// would be the unsafe route wearing the safe one's name</b> (📌 <c>VariableEditCommit</c>'s own
/// remark). ⚠ Their <c>writeLive</c> stays <c>null</c> at the composition root, deliberately.</para>
///
/// <para>⭐⭐⭐ <b>The entity comes from the SAME OBJECT the READ takes it from</b> — this store's
/// <see cref="EditorSelectionStore.SelectedEntity"/>, exactly as
/// <c>BlueprintLiveValueProvider.GetLiveObjects</c> does, ⛔ <b>NOT from <c>row.Origin.Entity</c></b>.
/// 📌 <c>R-78</c>: a Details row's origin carries <c>entity: default</c> as the CHAMELEON SENTINEL —
/// <i>"whoever is selected"</i> — so reading it would write to entity 0. ⚠ And even for a row that did
/// carry a concrete entity, the write must target whatever the READ displayed: if those two ever
/// disagree the designer edits one entity's value while looking at another's. ⭐ Making them read one
/// object is the only way that is true by construction rather than by care (📌 Batch 96's rule: <i>a
/// rail must take its input from the same object the UI takes it from</i>).</para>
///
/// <para>⭐ <b>Why <c>Func&lt;IBlueprintDebugSession?&gt;</c> and not the 36-member interface as a hard
/// dependency</b> — same reason the reader takes a factory: the ACTIVE session changes with the active
/// document. ⚠ Unlike the reader, this one does NOT narrow to a delegate: 📐 a real
/// <c>BlueprintDebugSession</c> costs three constructor arguments in a test
/// (<c>TheSessionWritesWhileFrozenTests</c> builds one in four lines), so the rail can drive the
/// PRODUCTION resolver and the PRODUCTION writer rather than a stub of them. ⛔ A narrowing delegate
/// pair here would have put the resolve→write join in an unrailed adapter at the composition root —
/// 📌 exactly the <c>R-67</c> shape this class exists to close.</para>
/// </summary>
public sealed class BlueprintLiveValueWriter
{
    private readonly Func<IBlueprintDebugSession?> _sessionFactory;
    private readonly EditorSelectionStore          _store;

    /// <param name="sessionFactory">
    /// ⭐ Resolves the ACTIVE blueprint debug session, or <c>null</c>. ⚠ 📌 <c>R-66</c> — a session
    /// existing means <i>"a blueprint DOCUMENT is open"</i>, ⛔ NOT <i>"the sim is frozen"</i>. The
    /// freeze gate is the session's own <c>IsPaused</c>, checked inside
    /// <c>TryWriteWorkingStateField</c> (📌 ruling 15), and it is NOT re-decided here.
    /// </param>
    /// <param name="store">Owns <see cref="EditorSelectionStore.SelectedEntity"/> — see the remarks.</param>
    public BlueprintLiveValueWriter(Func<IBlueprintDebugSession?> sessionFactory, EditorSelectionStore store)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _store          = store          ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// ⭐⭐ The <see cref="WriteLiveValue"/> the composition root hands to the Blueprint registrar.
    ///
    /// <para>⭐ <b><c>false</c> is a REFUSAL, never a failure to notice</b>: no entity, no session, an
    /// unresolvable name, a payload of the wrong width, or a session that is not frozen. 📌 The UI
    /// greys a control on this answer, so ⛔ it must not throw for any of them.</para>
    ///
    /// <para>⛔⛔ <b>THE SIZE GUARD IS NOT DEFENSIVE PADDING.</b> 📌 <c>Q32</c> §2.1: <i>"an
    /// out-of-range offset is MEMORY CORRUPTION, not a wrong value."</i> A payload wider than the field
    /// overruns into the NEIGHBOURING variable's bytes, and a narrower one leaves half the old value in
    /// place — ⭐ both are silent, and neither shows up as a wrong number in the cell being edited.
    /// ⚠ <c>Blackboard1024</c> is ONE component shared by BTree, HSM and Blueprint at disjoint offsets,
    /// so the neighbour may not even be a blueprint's.</para>
    ///
    /// <para>⭐⭐ <b>The offset is passed EXACTLY as resolved</b> — 📌 Batch 102 (<c>102a</c>): it is now
    /// component-absolute, because each dispatch kind's layout applies its own transform in the
    /// resolver. ⛔ This class must not adjust it; see <c>WorkingStateFieldRef.ComponentOffsetBytes</c>.</para>
    /// </summary>
    public bool Write(VariableRow row, ReadOnlySpan<byte> bytes) => TryWrite(row, bytes).Ok;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 102 (<c>102b</c>) — THE production <see cref="WriteLiveValue"/>, and it carries
    /// the REASON across the assembly boundary.</b>
    ///
    /// <para>⭐ A projection of <see cref="TryWrite"/>, ⛔ not a second implementation — the refusal
    /// vocabulary stays here, where the causes are knowable, and only the SENTENCE crosses. 📌
    /// <c>LiveWriteOutcome.Reason</c>: <c>Hrot.Editor.AiShared</c> must not enumerate causes it cannot
    /// see.</para>
    /// </summary>
    public LiveWriteOutcome WriteLive(VariableRow row, ReadOnlySpan<byte> bytes)
    {
        var attempt = TryWrite(row, bytes);
        return attempt.Ok ? LiveWriteOutcome.Landed : LiveWriteOutcome.Refused(attempt.Message);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 102 (<c>102b</c>) — the same write, but it SAYS WHICH REFUSAL IT IS.</b>
    ///
    /// <para>🔴🔴 <b>Why this exists.</b> Five distinct causes collapsed into one bare <c>false</c> ⇒
    /// ⛔ <b>a correctly-gated editor and a broken wire looked IDENTICAL</b>, which cost a whole
    /// measurement session and three handoffs' worth of a wrong conclusion *(📌 <c>M-36</c>)*.</para>
    ///
    /// <para>⚠ <c>VariableEditModal:41</c> is right that <c>LiveWriteUnavailable</c> <i>"cannot be known
    /// in advance"</i> — ⛔ so OK is not greyed up front. ⭐ <b>But the message AFTER the click must name
    /// the cause</b>, and that is what this returns.</para>
    /// </summary>
    public LiveWriteAttempt TryWrite(VariableRow row, ReadOnlySpan<byte> bytes)
    {
        // ⭐ 1 — an entity must be selected, from the store the READ reads. See the class remarks.
        var entity = _store.SelectedEntity;
        if (entity is null) return LiveWriteAttempt.Refused(LiveWriteRefusal.NoSelectedEntity);

        // ⭐ 2 — a blueprint session must be active. ⛔ Not "the sim is frozen" — that is step 5's.
        var session = _sessionFactory();
        if (session is null) return LiveWriteAttempt.Refused(LiveWriteRefusal.NoDebugSession);

        // ⭐ 3 — NAME → (component, component-absolute offset, size).
        // ⚠⚠ Batch 102 CORRECTED a false premise that stood in this comment: it used to claim
        //    "a row whose value the designer can SEE is a row this can resolve". ⛔ That was UNTRUE for
        //    Instance blueprints, whose value the read displayed while the resolver refused outright
        //    (M-36). ⭐ 102a built the Instance arm, so it now holds for AiPrimitive AND Instance —
        //    ⛔ and still does NOT hold for any other dispatch kind, whose layout nobody has measured.
        var field = session.ResolveWorkingStateField(entity.Value, row.Origin.AssetId, row.Origin.VariablePath);
        if (field is null) return LiveWriteAttempt.Refused(LiveWriteRefusal.FieldNotResolvable);

        // ⛔⛔ 4 — the width must match EXACTLY. See the remarks: this is the corruption gate, not a
        //    tidiness check.
        if (bytes.Length != field.SizeBytes)
            return LiveWriteAttempt.Refused(LiveWriteRefusal.SizeMismatch, bytes.Length, field.SizeBytes);

        // ⭐ 5 — the CLOCK gate is the only remaining way this returns false. 📌 MIN: it used to be the
        //    session's own `_isPaused`, which a toolbar pause never set — so this arm fired while the
        //    designer was demonstrably stopped. ⇒ a `false` here now means one thing, and the sentence
        //    below can say it.
        return session.TryWriteWorkingStateField(
                   entity.Value, field.ComponentType, field.ComponentOffsetBytes, bytes)
            ? LiveWriteAttempt.Succeeded
            : LiveWriteAttempt.Refused(LiveWriteRefusal.SimulationAdvancing);
    }
}

/// <summary>
/// ⭐⭐⭐ <b>Batch 102 (<c>102b</c>) — WHY a live write did not happen.</b>
/// ⛔ Five causes that used to be one <c>false</c>. 📌 The whole point is that a designer, and a
/// measuring session, can tell a correct refusal from a broken wire.
/// </summary>
public enum LiveWriteRefusal
{
    /// <summary>⭐ It worked.</summary>
    None = 0,

    /// <summary>⛔ Nothing is selected — the store the READ reads has no entity.</summary>
    NoSelectedEntity,

    /// <summary>⛔ No blueprint document is open. ⚠ 📌 <c>R-66</c>: this is NOT "the sim is running".</summary>
    NoDebugSession,

    /// <summary>
    /// ⛔ The variable's name resolved to no address. ⚠ Since <c>102a</c> this means the dispatch kind
    /// is neither <c>AiPrimitive</c> nor <c>Instance</c>, the entity carries no blackboard component,
    /// the slot is unallocated, or the stored <c>StructureHash</c> does not match the definition —
    /// ⭐ a STALE LAYOUT, which the read also refuses to display.
    /// </summary>
    FieldNotResolvable,

    /// <summary>⛔⛔ The payload width is not the field's width. 📌 <c>Q32</c> §2.1 — the corruption gate.</summary>
    SizeMismatch,

    /// <summary>
    /// ⛔ <b>The simulation clock is ADVANCING.</b> ⭐ The one refusal a designer can undo, and since
    /// <c>MIN</c> the only one the session's gate can produce.
    ///
    /// <para>⚠⚠ <b>Renamed from <c>NotFrozen</c> deliberately</b>, so the compiler revisited every
    /// reader. 📌 <c>NotFrozen</c> named a SESSION FLAG *(<c>BlueprintDebugSession._isPaused</c>)*, and
    /// that flag is exactly what <c>MIN</c> stopped gating on — a name that still said "frozen" would
    /// keep pointing at the mechanism <c>AS-3</c> removed.</para>
    /// </summary>
    SimulationAdvancing,
}

/// <summary>⭐ The outcome of one live-write attempt, with the numbers a size mismatch needs.</summary>
public readonly record struct LiveWriteAttempt(bool Ok, LiveWriteRefusal Refusal, int Got, int Expected)
{
    public static LiveWriteAttempt Succeeded => new(true, LiveWriteRefusal.None, 0, 0);

    public static LiveWriteAttempt Refused(LiveWriteRefusal why, int got = 0, int expected = 0)
        => new(false, why, got, expected);

    /// <summary>
    /// ⭐⭐ What the dialog shows. ⛔ Not an enum name: 📌 the visual guide's <c>F3</c> — a refusal the
    /// designer reads must say what to DO, and only <see cref="LiveWriteRefusal.SimulationAdvancing"/> is
    /// actionable by them.
    /// </summary>
    public string Message => $"{Sentence} [{Refusal}]";

    /// <summary>
    /// ⭐ The prose half. ⚠ <see cref="Message"/> appends the enum name because 📌 <b>three sessions
    /// were spent guessing WHICH of these five a screenshot showed</b> — ⛔ the five sentences are
    /// distinguishable to a reader who has them all in front of them, and to nobody else.
    /// </summary>
    private string Sentence => Refusal switch
    {
        LiveWriteRefusal.None               => "",
        LiveWriteRefusal.NoSelectedEntity   => "No entity is selected — pick one in the world or the outline.",
        LiveWriteRefusal.NoDebugSession     => "No blueprint document is open, so there is nothing to write into.",
        LiveWriteRefusal.FieldNotResolvable => "This variable has no live address on the selected entity — "
                                             + "its blueprint may not be attached, or its compiled layout is "
                                             + "out of date (recompile the blueprint).",
        LiveWriteRefusal.SizeMismatch       => $"Internal size mismatch: the editor produced {Got} bytes for a "
                                             + $"{Expected}-byte field, so the write was refused rather than "
                                             +  "risk the neighbouring value.",

        // ⭐⭐⭐ MIN (2026-08-21) — THE SENTENCE CAN TELL THE TRUTH AGAIN, because the GATE now does.
        // 📌 The history, kept because it is the point: the original read "The simulation is running —
        //    pause it to edit a live value", which was FALSE — the gate behind it was
        //    BlueprintDebugSession._isPaused, a SESSION-LOCAL flag a toolbar pause never sets, so it
        //    told a designer who HAD paused to pause. Batch 102's honest-refusal work then replaced it
        //    with a sentence describing that MECHANISM instead, deliberately vague about what to do,
        //    because at the time nothing drained a toolbar-paused write.
        // ⇒ ⭐⭐ MIN made the gate `!IsClockHalted()` — one source of "paused", the clock (R-126) — so
        //    this arm now means exactly what it says, and the actionable instruction is true for BOTH
        //    ways of stopping. ⛔ It is no longer a description of a limitation; it is a fact about the
        //    simulation.
        LiveWriteRefusal.SimulationAdvancing => "The simulation is running — pause it (the toolbar's "
                                             + "pause, or a breakpoint) to edit a live value.",
        _                                   => "The live write was refused.",
    };
}
