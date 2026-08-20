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
    /// <para>⛔ <b>The offset is passed RAW, exactly as resolved.</b>
    /// <c>TryWriteWorkingStateField</c> applies the 8-byte working-state header itself through
    /// <c>WorkingStateLayout.ComponentOffsetOf</c> — ⚠ <b>adding it here would double-apply it</b> and
    /// scribble 8 bytes past the field. 📌 That is the specific mistake the rail pins.</para>
    /// </summary>
    public bool Write(VariableRow row, ReadOnlySpan<byte> bytes)
    {
        // ⭐ 1 — an entity must be selected, from the store the READ reads. See the class remarks.
        var entity = _store.SelectedEntity;
        if (entity is null) return false;

        // ⭐ 2 — a blueprint session must be active. ⛔ Not "the sim is frozen" — that is step 4's.
        var session = _sessionFactory();
        if (session is null) return false;

        // ⭐ 3 — NAME → (component, RAW offset, size). ⚠ row.Origin.VariablePath is the same key the
        //   read looks up in BlueprintStateSnapshot.FieldValues (both are VariableViewModel.Name), so
        //   a row whose value the designer can SEE is a row this can resolve.
        var field = session.ResolveWorkingStateField(entity.Value, row.Origin.AssetId, row.Origin.VariablePath);
        if (field is null) return false;

        // ⛔⛔ 4 — the width must match EXACTLY. See the remarks: this is the corruption gate, not a
        //    tidiness check.
        if (bytes.Length != field.SizeBytes) return false;

        // ⭐ 5 — RAW offset, as resolved. The header is the writer's to apply, and it applies it once.
        return session.TryWriteWorkingStateField(entity.Value, field.ComponentType, field.RawOffsetBytes, bytes);
    }
}
