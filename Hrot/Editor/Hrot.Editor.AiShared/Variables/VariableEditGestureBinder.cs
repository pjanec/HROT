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
        if (ActiveSession is null || ActiveRow is not { } row)
        { LastOutcome = VariableEditCommit.Outcome.RefusedReadOnly; return LastOutcome.Value; }

        var fieldType = row.ClrType;
        if (fieldType is null)
        { LastOutcome = VariableEditCommit.Outcome.RefusedReadOnly; return LastOutcome.Value; }

        var outcome = VariableEditCommit.Commit(
            ActiveSession, _assetOf?.Invoke(row), row, fieldType, _runState(), _writeLive);

        // ⛔ The session is spent either way — a refused commit already left it uncommitted, and
        //    keeping it open would let a second Accept re-apply a stale edit.
        Close();
        LastOutcome = outcome;
        return outcome;
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

    private void Open(VariableRow row, VariableEditAction action)
    {
        LastAction    = action;
        ActiveSession = null;
        ActiveRow     = null;

        var entry = _entryResolver(row);
        if (entry is null) return;   // ⛔ the row's variable is gone — fail closed, never guess

        // ⭐ The policy decides; this class does not re-implement it. ⚠ A second copy of the
        //   run-state matrix here is exactly how the two would drift.
        ActiveSession = _launcher.Open(row, action, _runState(), entry);
        // ⭐ Only remembered when a session actually opened — Accept must never see a row whose
        //   dialog §5 denied.
        if (ActiveSession != null) ActiveRow = row;
    }
}
