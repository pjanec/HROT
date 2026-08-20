using System;
using Fdp.Presentation.Editing;
using ImGuiNET;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-327</c> — the SURFACE for the variable edit session. Everything behind it already
/// shipped.</b>
///
/// <para>📌 <b>Design basis:</b> <c>DESIGN_Variable_Details_And_Editing.md</c> §3–§4 · <b>ruling 5</b>:
/// <i>"opens a StructEdit-based editing window, OK / Cancel, initialised to the variable's current
/// value."</i></para>
///
/// <para>🔴🔴 <b>What was actually missing.</b> Batch 84 built the ENTIRE headless path — gesture →
/// <see cref="VariableEditLauncher"/> → session → <see cref="VariableEditGestureBinder.Accept"/> → the
/// run-state arm → the declaration or the world. ⛔ <b><c>Open</c> returned an <c>IEditSession</c> and
/// NOTHING DREW IT.</b> ⇒ ⭐ this type is the surface and nothing else: it owns no policy, no commit
/// and no validation.</para>
///
/// <para>⭐⭐ <b>Ruling 9 — it does NOT re-implement the field editor.</b> The fields are drawn by
/// <see cref="ComponentEditDrawer"/>, the same drawer <c>InspectorWindow</c> and
/// <c>ComponentEditWindow</c> use. ⛔ <b>And it does not reuse <c>ComponentEditWindow</c> itself</b>,
/// which is entity-and-component shaped: it commits through <c>IInspectableSession.SetComponent</c>
/// and self-terminates when the target ENTITY dies. ⚠ At authoring time a variable has no entity, and
/// the commit target is the DECLARATION — a different destination, chosen by
/// <see cref="VariableEditCommit"/>. ⇒ ⭐ share the drawer, not the window.</para>
///
/// <para>⭐⭐ <b>TWO SCOPES, ONE DIALOG</b> *(design §3)*: <i>"Edit value…"</i> ⇒
/// <c>EditScope.ForField</c>, <i>"Properties…"</i> ⇒ <c>EditScope.WholeComponent</c>. ⛔ The scope is
/// chosen by the BINDER before the session exists; this class only reads
/// <see cref="VariableEditGestureBinder.LastAction"/> for the TITLE. ⚠ Same lifecycle, same OK/Cancel,
/// same validation — a second dialog per scope would be two implementations of one concept.</para>
///
/// <para>⭐⭐⭐ <b>Refusals are GREYED WITH A TOOLTIP, never a click that dead-ends.</b> 📌 <b>User,
/// <c>2026-08-17</c>:</b> <i>"showing explanatory tooltip would be better than allowing user to click
/// the button and then saying that it is not possible — same information value, no false
/// expectations."</i> ⇒ ⭐ <see cref="VariableEditCommit.TargetFor"/> is asked BEFORE the button is
/// drawn, so <c>RefusedRunning</c> greys OK up front; ⚠ <c>LiveWriteUnavailable</c> cannot be known in
/// advance — the run state ALLOWED the write and the mechanism did not arrive — so it is rendered
/// AFTER the attempt, which is the honest ordering for each.</para>
/// </summary>
public sealed class VariableEditModal
{
    private readonly VariableEditGestureBinder _binder;
    private readonly Func<VariableRunState>    _runState;

    /// <summary>⭐ Set when an <see cref="VariableEditGestureBinder.Accept"/> refused, so the dialog can
    /// say WHY instead of vanishing. ⛔ Cleared when the next session opens.</summary>
    private VariableEditCommit.Outcome? _refusal;

    private bool _open;

    /// <param name="idScope">
    /// ⭐⭐ Distinguishes this instance's popup from every other one — in production the registrar's
    /// perspective suffix, the same way every window takes an <c>idOverride</c>. ⛔ Null or empty means
    /// "the only modal", which is what a headless harness with one instance wants.
    /// </param>
    public VariableEditModal(
        VariableEditGestureBinder binder, Func<VariableRunState> runState, string? idScope = null)
    {
        _binder   = binder   ?? throw new ArgumentNullException(nameof(binder));
        _runState = runState ?? throw new ArgumentNullException(nameof(runState));
        PopupId   = string.IsNullOrEmpty(idScope) ? Title : $"{Title}##{idScope}";
        TableId   = string.IsNullOrEmpty(idScope) ? "##vedit" : $"##vedit_{idScope}";
    }

    /// <summary>⭐ The ImGui popup title — a rail surface, and what the designer reads.</summary>
    public const string Title = "Edit variable";

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 89 (<c>89b</c>) — the ImGui id, which is NOT the title.</b>
    ///
    /// <para>🔴 Once the modal joins the frame, <b>three registrars draw three modals every frame</b>
    /// — and <see cref="Title"/> was used for BOTH <c>OpenPopup</c> and <c>BeginPopupModal</c>, so all
    /// three shared ONE ImGui id. ⚠ That is correct today only because <c>if (!IsOpen) return</c> fires
    /// first for the other two: ⛔ <b>an undocumented guard standing between two popups with the same
    /// id.</b></para>
    ///
    /// <para>📌 <b>This repo has already paid for popup-id confusion once</b> —
    /// <c>AssetPickerModal:185-189</c> carries the diagnosis: <i>"the popup opens under one id while
    /// <c>BeginPopupModal</c> waits on another, so it never renders."</i></para>
    ///
    /// <para>⭐ Everything before <c>##</c> is what ImGui DISPLAYS; the whole string is the id. ⇒ the
    /// designer still reads <c>"Edit variable"</c> on every host, and the three ids are distinct.
    /// ⛔ <see cref="Title"/> stays a <c>const</c> — it is referenced by rails and it is genuinely the
    /// display title; ⭐ what became instance-scoped is the ID.</para>
    /// </summary>
    public string PopupId { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 96 (<c>96a</c>) — the id of the two-column table the drawer REQUIRES.</b>
    ///
    /// <para>🔴🔴 <b>What was wrong.</b> <see cref="ComponentEditDrawer"/>'s own doc-comment says
    /// <i>"Must be called inside a two-column <c>BeginTable</c>/<c>EndTable</c> block"</i>, and its
    /// <c>DrawLeafNode</c> opens with <c>TableNextRow()</c>. ⛔ <c>Draw</c> called it between two
    /// <c>Separator</c>s with <b>no table anywhere in this file</b> ⇒
    /// <list type="bullet">
    ///   <item><i>"Edit value…"</i> — its scope filtered the document to an EMPTY <c>SelectionRoot</c>
    ///   *(<c>96b</c>)* ⇒ zero children ⇒ <c>TableNextRow</c> never reached ⇒ ⭐ <b>the designer saw a
    ///   name and two separator lines with nothing between them</b> — lines 197 and 202.</item>
    ///   <item><i>"Properties…"</i> — kept the real node ⇒ ⛔⛔ the first <c>TableNextRow()</c> with no
    ///   table open <b>aborted the editor natively</b>.</item>
    /// </list>
    /// ⇒ ⭐⭐⭐ <b>this modal had never successfully drawn anything, for any variable, on any host.</b></para>
    ///
    /// <para>⚠ <b>Instance-scoped for the same reason <see cref="PopupId"/> is</b> — three registrars
    /// draw three modals. ⭐ Table ids are window-scoped in ImGui so a shared one would be harmless
    /// today, ⛔ but this repo has already paid for id confusion once *(see <see cref="PopupId"/>)* and
    /// the cost of being explicit is one string.</para>
    /// </summary>
    public string TableId { get; }

    /// <summary>
    /// ⭐⭐ <b>Batch 100 (<c>100b</c>) — the seed width that breaks the circularity.</b>
    ///
    /// <para>📐 <b>Chosen from a measurement, not taste.</b> The <c>Property</c> column is a fixed
    /// <c>180f</c>; at this width the <c>Value</c> column measured <b>305 px</b> in a real frame, which
    /// is comfortably past the drawer's <c>60 px</c> clamp and wide enough for <c>InputInt</c>'s field
    /// PLUS its <c>−</c>/<c>+</c> step buttons.</para>
    ///
    /// <para>⚠ <b>A seed, not a lock</b> — applied with <c>ImGuiCond.Appearing</c>, so a designer who
    /// resizes the dialog keeps their size. ⛔ It is deliberately not a per-host setting: 📌 one
    /// dialog, one shape *(ruling 9)*.</para>
    /// </summary>
    public const float DefaultWidth = 520f;

    // ── the headless half — every decision this dialog makes, without ImGui ──
    //
    // ⭐⭐ The draw path below is unreachable from a test (no ImGui context), so every DECISION is a
    //    property here and Draw does nothing but call them. ⛔ A decision taken inline in Draw is a
    //    decision no rail can see, which is how BP-327 shipped invisible in the first place.

    /// <summary>⭐ True while a session is open and the dialog should be on screen.</summary>
    public bool IsOpen => _binder.ActiveSession != null || _refusal != null;

    /// <summary>
    /// ⭐⭐ <b>Whether OK is CLICKABLE</b> — 📌 ruling 15 via <see cref="VariableEditCommit.TargetFor"/>,
    /// asked rather than restated. ⛔ A second copy of the run-state matrix here is exactly how the
    /// two would drift.
    /// </summary>
    public bool CanCommit
        => _binder.ActiveSession != null
        && !IsReadOnlyView
        && VariableEditCommit.TargetFor(_runState()) != VariableEditCommit.Target.Nowhere;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 96 — this dialog is a VIEW, not an editor, and it must be SHAPED as one.</b>
    ///
    /// <para>📌 The user's second visual check: <i>the dialog opens and THEN says "this row cannot be
    /// written"</i>. ⚠ <b>Opening is deliberate</b> — <c>VariableEditLauncher.Open</c>'s own comment:
    /// <i>"<c>ReadOnly</c> still OPENS — the design says properties are read-only mid-run, not absent;
    /// refusing to open would hide the values a designer wants to read."</i> ⭐ <b>That decision is
    /// defensible; presenting it as an editor with an OK button that then says no is not.</b>
    /// 📌 The user's own rule: <i>"same information value, no false expectations."</i></para>
    ///
    /// <para>⭐⭐ <b>The two refusals are DIFFERENT and are shown differently</b>, which is the whole
    /// point:
    /// <list type="bullet">
    ///   <item>⭐ <b>the ROW can never be written</b> *(node-owned · passthrough · stale)* ⇒ <b>no OK at
    ///   all</b> — nothing the designer does here could ever land, so offering the button is the false
    ///   expectation.</item>
    ///   <item>⭐ <b>the RUN STATE forbids it right now</b> *(free-running)* ⇒ <b>OK greyed with the
    ///   reason on hover</b> — 📌 the <c>2026-08-17</c> ruling, and it is actionable: pause and it
    ///   works.</item>
    /// </list></para>
    ///
    /// <para>⛔ It asks <see cref="VariableEditPolicy"/> rather than re-deriving the rule — a second
    /// copy of that matrix here is exactly how the two would drift.</para>
    /// </summary>
    public bool IsReadOnlyView
        => _binder.ActiveSession != null
        && _binder.ActiveRow is { } row
        && VariableEditPolicy.Resolve(
               _binder.LastAction ?? VariableEditAction.EditValue, _runState(), row)
           == VariableEditAvailability.ReadOnly;

    /// <summary>
    /// ⭐⭐ <b>Why this dialog is read-only</b>, or <c>null</c> when it is an editor. ⛔ Never a bare
    /// greyed control: the designer must be able to read the reason without clicking anything.
    /// </summary>
    public string? ReadOnlyReason => IsReadOnlyView
        ? _binder.ActiveRow!.RowKind switch
        {
            VariableRowKind.NodeOwned =>
                "Read-only: this variable is owned by the editor (auto-managed), so its value is not "
                + "authored here.",
            VariableRowKind.ReadOnlyPassthrough =>
                "Read-only: this variable is a passthrough — it is declared elsewhere and only "
                + "surfaced here.",
            _ =>
                "Read-only: a variable cannot be retyped while the simulation is up. Its current "
                + "values are shown for reference.",
        }
        : null;

    /// <summary>
    /// ⭐⭐⭐ <b>The tooltip a greyed OK carries, or <c>null</c> when OK is live.</b> 📌 The user's
    /// <c>2026-08-17</c> ruling in one string — ⛔ a refusal the designer can only discover by clicking
    /// is the thing that ruling forbids.
    /// </summary>
    public string? CommitRefusalReason
    {
        get
        {
            if (_binder.ActiveSession == null) return null;
            return VariableEditCommit.TargetFor(_runState()) == VariableEditCommit.Target.Nowhere
                ? "The simulation is running. A variable can only be changed while it is paused on a "
                  + "breakpoint or stepping — otherwise the next tick would overwrite the edit."
                : null;
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The message shown after a refused commit</b>, or <c>null</c>. ⚠ Distinct from
    /// <see cref="CommitRefusalReason"/>: that one is known BEFORE the click, this one only after.
    /// </summary>
    public string? RefusalMessage => _refusal switch
    {
        // ⭐⭐⭐ Batch 96 — the cause the designer actually hit, and it is NOT the row kind.
        VariableEditCommit.Outcome.RefusedNoDeclarationOwner =>
            "The edit could not be saved: the variable's declaration owner could not be resolved for "
            + "this row, so there is nowhere to write the initial value. Nothing was changed.",
        VariableEditCommit.Outcome.LiveWriteUnavailable =>
            "The edit could not be written to the live blackboard: no live writer is installed for "
            + "this host, or it refused the write. Nothing was changed.",
        VariableEditCommit.Outcome.RefusedRunning =>
            "The simulation is running, so the edit was not applied. Pause on a breakpoint or step, "
            + "then try again.",
        VariableEditCommit.Outcome.RefusedReadOnly =>
            "This row cannot be written — it is node-owned, a passthrough, or stale.",
        _ => null,
    };

    /// <summary>
    /// ⭐⭐⭐ <b>OK.</b> Commits through the binder and keeps the dialog up when the commit refused, so
    /// the designer is TOLD. ⭐ Returns the outcome so a rail need not read it back off the binder.
    /// </summary>
    public VariableEditCommit.Outcome Ok()
    {
        var outcome = _binder.Accept();
        _refusal = outcome == VariableEditCommit.Outcome.Ok ? null : outcome;
        if (_refusal == null) _open = false;
        return outcome;
    }

    /// <summary>
    /// ⭐⭐ <b>Cancel — and the declaration must be UNTOUCHED</b> *(guide <c>D7</c>)*. ⛔ It routes to
    /// <see cref="VariableEditGestureBinder.Cancel"/>, which disposes the session without committing;
    /// this class never touches the declaration itself, which is what makes D7 true by construction
    /// rather than by care.
    /// </summary>
    public void Cancel()
    {
        _binder.Cancel();
        _refusal = null;
        _open    = false;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 100 (<c>100c</c>) — what the title bar's <c>[x]</c> means.</b>
    ///
    /// <para>🔴🔴 <b>The defect this replaces.</b> ImGui clears the <c>ref bool</c> when <c>[x]</c> is
    /// clicked — ⛔ but <see cref="IsOpen"/> is <c>_binder.ActiveSession != null</c>, which <c>[x]</c>
    /// never touched. ⇒ the guard at the top of <see cref="Draw"/> let the next frame straight through,
    /// <c>OpenPopup</c> ran again, and <b>the dialog reappeared the frame after the designer closed
    /// it.</b> ⚠ It could only be dismissed through Cancel.</para>
    ///
    /// <para>⭐⭐ <b>Why this is a NAMED method rather than a call to <see cref="Cancel"/>.</b> It does
    /// exactly what Cancel does, and that identity is the point — ⛔ <b>but a rail cannot click
    /// <c>[x]</c></b>, and a rail that called <c>Cancel</c> would be asserting the button it wishes
    /// existed. ⭐ Naming the seam lets a rail drive <b>the code ImGui itself reaches</b>, and leaves
    /// exactly one unrailed line: the <c>if</c> in <see cref="Draw"/> that calls it.
    /// ⚠ <b>That one line is the honestly-faked layer</b> *(📌 <c>M-29</c>)* — ⛔ not the behaviour.</para>
    ///
    /// <para>⛔ <b>It DISCARDS.</b> A close box that commits would make the designer's escape hatch
    /// write — worse than one that fails to close.</para>
    /// </summary>
    public void CloseFromWindowChrome() => Cancel();

    /// <summary>⭐ Dismisses a refusal banner without reopening anything.</summary>
    public void DismissRefusal()
    {
        _refusal = null;
        _open    = false;
    }

    // ── the draw half — ImGui only, and deliberately decision-free ──────────

    /// <summary>
    /// Draws the modal. ⛔ No-op when no session is open and no refusal is pending.
    /// ⚠ <b>Every branch below asks a property above</b> — see the class remark.
    /// </summary>
    public void Draw()
    {
        if (!IsOpen) { _open = false; return; }

        if (!_open)
        {
            ImGui.OpenPopup(PopupId);
            _open = true;
        }

        // ⭐⭐⭐ Batch 100 (100b) — WITHOUT THIS THE NUMBER HAS NOWHERE TO DRAW.
        //
        // 📐 Measured in a real frame (100a): the popup's content width was 259.0 px, and the VALUE
        //    column inside it resolved to ComponentEditDrawer's 60 px CLAMP FLOOR — InputInt draws a
        //    field PLUS `−`/`+` step buttons as one group, so the digits were clipped away entirely.
        //
        // ⛔ It is NOT a StructEdit bug. This table's setup is byte-identical to the working reference
        //    (ComponentEditWindow:144–:149). ⭐⭐ The difference is the CONTAINER: a WidthStretch column
        //    inside an AlwaysAutoResize popup is CIRCULAR — the window sizes to its content while the
        //    content sizes to the window — so the stretch column resolves to nothing.
        //
        // ⭐ `Appearing`, deliberately: it seeds the size the first time the popup opens and then
        //    leaves the designer's own resize alone. ⛔ `Always` would fight them every frame.
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(DefaultWidth, 0), ImGuiCond.Appearing);

        // ⭐⭐⭐ Batch 100 (100c) — `[x]` MUST END THE SESSION, not flip a flag.
        //
        // 🔴 The bug this replaces: ImGui clears `_open` when `[x]` is clicked, but `IsOpen` is
        //    `_binder.ActiveSession != null`, which `[x]` never touched ⇒ the guard at the top let the
        //    next frame straight through and `OpenPopup` REOPENED what the designer had just closed.
        //    ⛔ The dialog was uncloseable except through Cancel.
        // ⭐ `[x]` now means exactly what Cancel means — discard the session — so there is ONE close
        //    path and no way for the two to disagree.
        bool wasOpen = _open;
        if (!ImGui.BeginPopupModal(PopupId, ref _open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (wasOpen && !_open) CloseFromWindowChrome();
            return;
        }

        var session = _binder.ActiveSession;

        if (session != null)
        {
            ImGui.TextUnformatted(_binder.ActiveRow?.ShortName ?? "");
            ImGui.Separator();

            // ⭐⭐⭐ Batch 96 (96a) — MIRRORS ComponentEditWindow.DrawClientArea steps 2–5, which is
            //    the reference caller the handoff named. 📐 Measured: SIX production call sites reach
            //    ComponentEditDrawer.DrawEditNode, and FIVE of them open a table
            //    (ComponentEditWindow:152 · ComponentReflector:406 · ReplaySearchPanel:155 ·
            //    InspectorWindow:303 · InspectorWindow:404). ⛔ This one did not, and that single fact
            //    produced BOTH reported failures — see TableId for the diagnosis.
            // ⛔ The DRAWER is not touched: it is Fdp.Presentation infrastructure with five other
            //    callers and its own rails. ⭐ The modal was the broken caller.

            // ⭐ Step 2 — rebuild BEFORE the table, exactly as the reference caller does. ⛔ Not inside
            //   it: DrawEditNode returns immediately while RebuildRequired holds, so a modal that never
            //   rebuilt would draw an empty table for ever once a rebuild was asked for.
            if (session.RebuildState == EditRebuildState.RebuildRequired)
                session.RebuildDocument();

            // ⭐ Steps 3–5. EndTable is inside the `if`, so it is reached on every path that opened the
            //   table — including DrawEditNode's own early RebuildRequired return, which returns into
            //   this block rather than out of it.
            if (ImGui.BeginTable(TableId, 2,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
                ImGui.TableSetupColumn("Value",    ImGuiTableColumnFlags.WidthStretch);

                var drawer = new ComponentEditDrawer(session, pickerCtx: null);
                drawer.DrawEditNode(session.Document.Root);

                ImGui.EndTable();
            }

            ImGui.Separator();

            // ⭐⭐⭐ Batch 96 — A VIEW IS SHAPED AS A VIEW. 🔴 The designer met an OK button that then
            //    said "this row cannot be written"; the open is deliberate (see IsReadOnlyView) but
            //    the editor shape was the false expectation.
            if (IsReadOnlyView)
            {
                ImGui.TextWrapped(ReadOnlyReason ?? "");
                ImGui.Separator();
                if (ImGui.Button("Close")) Cancel();
            }
            else
            {
                // ⭐ GREYED, with the reason on hover — never a click that dead-ends. ⚠ This arm is
                //   for a refusal the RUN STATE makes and the designer can undo by pausing.
                bool canCommit = CanCommit;
                if (!canCommit) ImGui.BeginDisabled();
                if (ImGui.Button("OK")) Ok();
                if (!canCommit)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(CommitRefusalReason ?? "");
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel")) Cancel();
            }
        }
        else if (RefusalMessage is { } message)
        {
            ImGui.TextWrapped(message);
            ImGui.Separator();
            if (ImGui.Button("Close")) DismissRefusal();
        }

        ImGui.EndPopup();

        // ⚠ The designer can dismiss a modal with Esc / the title-bar X, which flips `_open` behind
        //   our back. ⛔ Leaving the session open then would strand it — the next gesture would find a
        //   live session and reuse a stale one. ⭐ Treat it as Cancel, which is what it looks like.
        if (!_open && _binder.ActiveSession != null) Cancel();
    }
}
