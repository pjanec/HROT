using System;
using Hrot.Editor.AiShared.Blackboard;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>The missing half of <c>C-dialog</c>: it binds the table's two GESTURES to the one
/// launcher.</b>
///
/// <para>
/// 🔴🔴 <b>What was measured in Batch 74.</b> <see cref="VariableTableControl"/> already RAISED
/// <c>EditValueRequested</c> and <c>PropertiesRequested</c>; <see cref="VariableEditLauncher"/>
/// already turned an action into the right <c>EditScope</c>; ⛔ <b>and nothing connected them</b> —
/// the launcher was constructed by nobody, so the <c>InspectorWindow</c> panel was the only live way
/// to edit a variable's default. 📌 <b>Two finished halves and no seam</b> is the shape this
/// programme keeps filing; this file is the seam.
/// </para>
///
/// <para>
/// ⭐⭐ <b>WIRING, not building</b> — 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §3, whose ruling
/// is old: <i>two menu items = the two <c>EditScope</c>s</i>. ⛔ It opens sessions through
/// <see cref="VariableEditLauncher"/>, which routes to <c>DefaultValueAuthoring.OpenSession</c> — the
/// ONE call site, pinned by <c>ExactlyOneCallSite_OpensAVariableEditSession</c>. <b>No second opener
/// is introduced here, and none may be.</b>
/// </para>
///
/// <para>
/// ⭐ <b>Headless, so the meaning is testable</b> even though the pixels are not: which gesture opens
/// which scope, and what run state makes it writable, are decisions — and decisions belong in rails.
/// ⚠ The visual half stays suspended (Batch 68).
/// </para>
///
/// <para>
/// ⚠ <b>The <c>InspectorWindow</c> panel STAYS</b> (user ruling: <i>no rush removals</i>). ⭐ Two entry
/// points over ONE implementation is what ruling 9 asks for — the node-scoped panel and this
/// asset-scoped table answer different questions.
/// </para>
/// </summary>
public sealed class VariableEditGestureBinder
{
    private readonly VariableEditLauncher _launcher;
    private readonly Func<VariableRow, BlackboardVariableEntry?> _entryResolver;
    private readonly Func<VariableRunState> _runState;

    /// <summary>
    /// ⭐ The session the last gesture opened, or <c>null</c> when the policy refused. ⚠ Exposed so the
    /// host can present it and so the rails can see WHICH scope was chosen — the decision this class
    /// exists to make.
    /// </summary>
    public IEditSession? ActiveSession { get; private set; }

    /// <summary>⭐ The action the last gesture mapped to. <c>null</c> before any gesture.</summary>
    public VariableEditAction? LastAction { get; private set; }

    /// <param name="launcher">The one launcher; ⛔ do not pass a second opener.</param>
    /// <param name="entryResolver">
    ///   Resolves a row to its blackboard entry. ⭐ A delegate rather than an asset reference because a
    ///   row already knows its own asset and entity (§1a) — the binder must not acquire an ambient one.
    /// </param>
    /// <param name="runState">
    ///   The CURRENT run state, read per gesture. ⚠ Not captured once: writability changes when the sim
    ///   starts or pauses, and a stale snapshot would offer an editable dialog mid-replay.
    /// </param>
    /// <param name="assetOf">
    ///   ⭐ Batch 84 — resolves a row to the asset whose declaration an INITIAL-value edit updates.
    ///   ⛔ Optional: a host with no authored asset behind the row (the Watch's pinned pins) still gets
    ///   the LIVE arm, which needs no asset.
    /// </param>
    /// <param name="writeLive">
    ///   ⭐ Batch 84 — the LIVE writer, used only while frozen (📌 ruling 15). ⛔ Optional for the same
    ///   reason, and its absence is reported as <c>LiveWriteUnavailable</c> rather than silently
    ///   becoming a refusal.
    /// </param>
    public VariableEditGestureBinder(
        VariableEditLauncher launcher,
        Func<VariableRow, BlackboardVariableEntry?> entryResolver,
        Func<VariableRunState> runState,
        Func<VariableRow, IBlackboardManagedAsset?>? assetOf = null,
        WriteLiveValue? writeLive = null)
    {
        _launcher      = launcher      ?? throw new ArgumentNullException(nameof(launcher));
        _entryResolver = entryResolver ?? throw new ArgumentNullException(nameof(entryResolver));
        _runState      = runState      ?? throw new ArgumentNullException(nameof(runState));
        _assetOf       = assetOf;
        _writeLive     = writeLive;
    }

    private readonly Func<VariableRow, IBlackboardManagedAsset?>? _assetOf;
    private readonly WriteLiveValue? _writeLive;

    /// <summary>⭐ The row the open session belongs to, or <c>null</c> when no session is open.</summary>
    public VariableRow? ActiveRow { get; private set; }

    /// <summary>⭐ What the last <see cref="Accept"/> did. <c>null</c> before any.</summary>
    public VariableEditCommit.Outcome? LastOutcome { get; private set; }

    /// <summary>
    /// ⭐⭐ <b>Batch 102 (<c>102b</c>) — the HOST's own sentence for the last refusal</b>, or
    /// <c>null</c> when there was none to carry. 📌 <c>M-36</c>: the live arm's causes are the host's,
    /// so the dialog must be able to show what the host said rather than a word this assembly guessed.
    /// </summary>
    public string? LastRefusalDetail { get; private set; }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 84 — the OK path, which DID NOT EXIST.</b>
    ///
    /// <para>🔴🔴 <b>Measured before building:</b> <c>VariableEditCommit</c> shipped complete and tested
    /// in Batch 83 with <b>ZERO production call sites</b> — the binder opened a session and nothing
    /// ever committed it. ⇒ ⛔ even the NOT-RUNNING write Batch 83 reported as landed could not land:
    /// the dialog opened, the designer typed, and the value went nowhere. ⚠ <b>The twelfth instance of
    /// this programme's recurring shape, and it was mine.</b></para>
    ///
    /// <para>⭐ One call closes the session and routes it to the ONE commit, which picks the arm from
    /// the run state (📌 ruling 15). ⛔ The Watch does not get a second one — 📌 ruling 11.</para>
    /// </summary>
    public VariableEditCommit.Outcome Accept()
    {
        LastRefusalDetail = null;

        if (ActiveSession is null || ActiveRow is not { } row)
        { LastOutcome = VariableEditCommit.Outcome.RefusedReadOnly; return LastOutcome.Value; }

        var fieldType = row.ClrType;
        if (fieldType is null)
        { LastOutcome = VariableEditCommit.Outcome.RefusedReadOnly; return LastOutcome.Value; }

        var result = VariableEditCommit.CommitWithDetail(
            ActiveSession, _assetOf?.Invoke(row), row, fieldType, _runState(), _writeLive);

        // ⛔ The session is spent either way — a refused commit already left it uncommitted, and
        //    keeping it open would let a second Accept re-apply a stale edit.
        Close();
        LastOutcome       = result.Outcome;
        LastRefusalDetail = result.Detail;
        return result.Outcome;
    }

    /// <summary>⭐ The Cancel path — discards without committing, so nothing lands.</summary>
    public void Cancel()
    {
        ActiveSession?.Cancel();
        Close();
    }

    private void Close()
    {
        ActiveSession = null;
        ActiveRow     = null;
    }

    /// <summary>
    /// ⭐⭐ Subscribes to a table's gestures. ⭐ Separate from the constructor so a host can build the
    /// binder before the control exists, and so a test can drive <see cref="OnEditValue"/> /
    /// <see cref="OnProperties"/> without an ImGui context.
    /// </summary>
    public void Attach(VariableTableControl table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        table.EditValueRequested  += OnEditValue;
        table.PropertiesRequested += OnProperties;
    }

    /// <summary>⭐ Unsubscribes, so a rebuilt table does not leave a second live subscription.</summary>
    public void Detach(VariableTableControl table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        table.EditValueRequested  -= OnEditValue;
        table.PropertiesRequested -= OnProperties;
    }

    /// <summary>⭐ The VALUE cell ⇒ <c>EditScope.ForField</c> — this variable's value alone.</summary>
    public void OnEditValue(VariableRow row) => Open(row, VariableEditAction.EditValue);

    /// <summary>⭐ The NAME cell ⇒ <c>EditScope.WholeComponent</c> — the whole properties object.</summary>
    public void OnProperties(VariableRow row) => Open(row, VariableEditAction.Properties);

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — raised when "Properties…" is chosen and the policy permits.</b>
    ///
    /// <para>📌 <c>R-108</c>/<c>R-109</c>: <i>"Properties… opens the DECLARATION"</i>, as a <b>CUSTOM
    /// form</b> — ⛔ <b>so there is no <c>IEditSession</c> for it</b>, and this binder cannot open one.
    /// ⇒ the host that owns the form subscribes here.</para>
    ///
    /// <para>⭐ The <c>bool</c> is <b>DIALOG-LEVEL editability</b>, already decided by
    /// <c>VariableEditPolicy</c> — ⛔ the subscriber must not re-derive it *(ruling 9)*. ⚠ A row the
    /// policy DENIES raises nothing at all.</para>
    ///
    /// <para>⚠ <b>No subscriber ⇒ nothing opens, and that is honest</b> rather than a dialog that does
    /// nothing. 📌 <c>BP-317</c>: BTree/HSM have no Properties form yet — filed, not faked.</para>
    /// </summary>
    public event Action<VariableRow, bool>? PropertiesRequestedForRow;

    /// <summary>⭐ True once a host has claimed the Properties form. ⭐ A rail surface — asserted on the
    /// CONSTRUCTED binder, ⛔ never on a composition root's source.</summary>
    public bool HasPropertiesHost => PropertiesRequestedForRow is not null;

    private void Open(VariableRow row, VariableEditAction action)
    {
        LastAction    = action;
        ActiveSession = null;
        ActiveRow     = null;

        // ⭐ The policy decides, once, for BOTH arms — ⛔ this class does not re-implement it, and the
        //   Properties arm must not grow a second copy of the matrix either.
        var availability = VariableEditPolicy.Resolve(action, _runState(), row);
        if (availability == VariableEditAvailability.Denied) return;

        // ⭐⭐⭐ Batch 99 (99a) — R-109: PROPERTIES IS NOT A StructEdit SESSION.
        // ⛔ Falling through to the launcher here is what made "Properties…" open the VALUE document
        //   (BP-359) — the two menu items are two OBJECTS, and only one of them is a struct.
        if (action == VariableEditAction.Properties)
        {
            PropertiesRequestedForRow?.Invoke(
                row, availability == VariableEditAvailability.Editable);
            return;
        }

        var entry = _entryResolver(row);
        if (entry is null) return;   // ⛔ the row's variable is gone — fail closed, never guess

        ActiveSession = _launcher.Open(row, action, _runState(), entry);
        // ⭐ Only remembered when a session actually opened — Accept must never see a row whose
        //   dialog §5 denied.
        if (ActiveSession != null) ActiveRow = row;
    }
}
