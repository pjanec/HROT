using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>What <see cref="VariableTableControl"/> will draw for one row.</b>
///
/// <para>⭐ Four INDEPENDENT booleans, not a priority enum: <c>Selected</c> and <c>Changed</c> are
/// answers to different questions *(the designer vs the simulation)* and must be able to be true at
/// once. ⛔ Collapsing them into one "row style" is exactly the mistake
/// <see cref="VariableTableView.IsSelected"/>'s own doc forbids.</para>
/// </summary>
public readonly record struct RowVisualState(bool Selected, bool Changed, bool Pending, bool Stale);

/// <summary>
/// ⭐⭐ <b>The Details/Watch variable table — the DRAWING half of <c>C-table</c>.</b>
///
/// <para>
/// ⭐⭐⭐ <b>It renders a <see cref="VariableTableView"/> and nothing else.</b> Every decision —
/// which rows exist, what they are called, how they nest, which are highlighted — was already made in
/// <see cref="VariableTableModel"/> and is covered by §9's headless rails. ⇒ ⚠ <b>this file is
/// deliberately thin</b>, because it is the one part no test can see.
/// </para>
///
/// <para>
/// ⛔ <b>It does NOT replace <c>VariablesPanelControl</c>.</b> That control is the Blackboard authoring
/// panel with its seven columns, per-section budgets and aliasing UI; retiring it is <c>C-watch</c>/
/// <c>C-outline</c>'s job and would be a large change no one can currently look at. ⚠ <b>The visual
/// check is suspended</b> — see the Batch 68 report for exactly what that leaves unverified.
/// </para>
///
/// <para>
/// ⭐ <b>Folding is <c>CollapsingHeader</c>, not new machinery</b> (§1b) — the same primitive
/// <c>VariablesPanelControl</c> already uses in three places.
/// </para>
/// </summary>
public sealed class VariableTableControl
{
    // 🔴 the sim changed it · 🟡 your edit has not landed. ⛔ Never the same colour: §4a's whole point.
    private static readonly Vector4 ChangedTint = new(0.90f, 0.20f, 0.20f, 0.22f);
    private static readonly Vector4 PendingTint = new(1.00f, 0.85f, 0.30f, 0.22f);
    private static readonly Vector4 StaleText   = new(0.55f, 0.55f, 0.55f, 1.00f);

    private readonly VariableValueFormatter _formatter;

    /// <summary>Raised when a row's VALUE cell is double-clicked ⇒ <c>EditScope.ForField</c> (§4).</summary>
    public event Action<VariableRow>? EditValueRequested;

    /// <summary>Raised when a row's NAME cell is double-clicked ⇒ <c>EditScope.WholeComponent</c> (§4).</summary>
    public event Action<VariableRow>? PropertiesRequested;

    /// <summary>
    /// ⭐⭐ Raises "Edit value…" for a row. ⛔ The ⋮ menu's own path goes through ImGui, which no
    /// headless test can drive — this is the same call it makes, and it is what lets a rail prove the
    /// gesture is ATTACHED rather than merely constructed.
    /// </summary>
    public void RaiseEditValueRequested(VariableRow row) => EditValueRequested?.Invoke(row);

    /// <summary>⭐ Raises "Properties…" for a row. Same reason as above.</summary>
    public void RaisePropertiesRequested(VariableRow row) => PropertiesRequested?.Invoke(row);

    /// <summary>
    /// ⭐⭐⭐ <b>Whether a <c>VariableEditGestureBinder</c> is actually attached to this table.</b>
    ///
    /// <para>📌 <b><c>R-67</c>, the fourth instance:</b> <i>"a rail that builds its own composition root
    /// cannot see a composition-root defect."</i> ⛔ Batch 83's dialog rails were GREEN while the
    /// production dialog did nothing, because every rail constructed its own registrar and passed
    /// <c>facetEditService</c> itself.</para>
    ///
    /// <para>⭐ An event's subscriber list is invisible from outside the declaring class, so a rail that
    /// fishes a window out of the real <see cref="Fdp.Presentation.WindowManager.WindowManager"/> had no
    /// way to ask <i>"are your gestures wired?"</i> — it could only re-do the wiring and assert on that.
    /// ⇒ ⛔ vacuous by construction. This property is how the ARTEFACT answers for itself.</para>
    ///
    /// <para>⚠ <b>Both, not either.</b> <c>Attach</c> subscribes the value gesture and the properties
    /// gesture together; a table with one of the two is a defect, not a variant.</para>
    /// </summary>
    public bool HasEditGestures => EditValueRequested != null && PropertiesRequested != null;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 100 (<c>100f</c>) — which row gestures THIS table offers, set by its host.</b>
    ///
    /// <para>📌 <b>User:</b> <i>"no one is interested in the other properties than the value in the
    /// Watch window."</i> ⇒ the two Watch surfaces answer <see cref="VariableTableGestures.Watch"/>.</para>
    ///
    /// <para>⭐ <b>Defaults to <see cref="VariableTableGestures.Default"/> HERE and only here</b>, and
    /// that is not the thing <c>U-5</c> forbids: ⛔ <c>U-5</c> is about an INTERFACE volunteering an
    /// answer on an implementer's behalf, and <c>IVariableTableHost.Gestures</c> has no default body —
    /// every host must answer. ⭐ This is a control that can be constructed without a host at all
    /// *(rails do it constantly)*, and the authoring menu is the right shape for one.</para>
    ///
    /// <para>⚠ Assigned by <c>PerspectiveWorkspaceRegistrar.AttachEditGestures</c>, which is the ONE
    /// place a host and its table meet — ⛔ not by each host, which would be five call sites to
    /// forget.</para>
    /// </summary>
    public VariableTableGestures Gestures { get; set; } = VariableTableGestures.Default;

    /// <summary>
    /// ⭐⭐ <b>What a double-click on the NAME cell raises</b> — extracted so it can be railed, because
    /// 📌 <c>R-21</c>/<c>R-62</c>: the draw itself cannot be driven by an ordinary test.
    /// ⭐ Returns the gesture raised, so a rail asserts the DECISION rather than a side effect.
    /// </summary>
    internal VariableEditAction RaiseNameCellDoubleClick(VariableRow row)
    {
        if (Gestures.OffersProperties)
        {
            PropertiesRequested?.Invoke(row);
            return VariableEditAction.Properties;
        }

        EditValueRequested?.Invoke(row);
        return VariableEditAction.EditValue;
    }

    public VariableTableControl(VariableValueFormatter formatter)
        => _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

    /// <summary>
    /// ⭐⭐⭐ <b>Exactly what this control will draw for one row — the ARTEFACT's own answer.</b>
    ///
    /// <para>🔴🔴 <b><c>B3</c>, and why the rail has to live here.</b> 📐 Measured: the selection chain
    /// was wired end to end — the outline set <c>SelectedVariablePath</c>, the section applied it,
    /// <see cref="VariableTableView.IsSelected"/> computed it — ⛔ <b>and this control never called
    /// <c>IsSelected</c>. Zero references.</b> ⇒ ⚠ <b>an INVERTED instance of the recurring pattern</b>:
    /// usually nothing constructs the thing; here everything constructs and routes it and the last
    /// consumer never asks.</para>
    ///
    /// <para>⛔⛔ <b>So a rail on <c>IsSelected</c> proves NOTHING</b> — it returned <c>true</c>
    /// throughout the defect. 📌 The <c>CellText</c> lesson from Batch 83: <b>ask what the CONTROL
    /// would draw.</b> ⭐ <see cref="DrawRows"/> and <see cref="DrawCell"/> read this and nothing else,
    /// so a rail here is as close to the pixels as a headless test can stand.</para>
    ///
    /// <para>⚠ <b>What it still cannot prove</b>, stated rather than implied: that ImGui renders the
    /// state. It proves the control ASKS and CARRIES it — the defect was the asking.</para>
    ///
    /// <para>⭐⭐ <b>Selection and change stay ORTHOGONAL</b> *(the view's own ruling)*: they travel in
    /// separate fields and are drawn by separate ImGui mechanisms — selection through
    /// <c>Selectable</c>'s selected flag, change/pending through the row background. ⇒ 📌 a row that is
    /// selected AND changed this tick shows <b>both</b>, and a collapsed header can never read
    /// "something changed" because the designer clicked.</para>
    /// </summary>
    public RowVisualState VisualStateOf(VariableTableView view, VariableRow row)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        var highlight = view.HighlightOf(row);
        return new RowVisualState(
            Selected: view.IsSelected(row),
            Changed:  highlight.Changed,
            Pending:  highlight.Pending,
            Stale:    row.IsStale);
    }

    public void Draw(string id, VariableTableView view)
    {
        if (view.Groups.Count == 0)
        {
            DrawRows(id, view, view.UngroupedRows);
            return;
        }
        foreach (var group in view.Groups) DrawGroup(id, view, group);
    }

    private void DrawGroup(string id, VariableTableView view, VariableRowGroup group)
    {
        // ⭐⭐⭐ A collapsed header inherits its children's state, so folding everything down still shows
        //     WHERE the activity is. Without it, folding only hides.
        var agg = view.HighlightOf(group);
        if (agg.Changed) ImGui.PushStyleColor(ImGuiCol.Header, ChangedTint);
        else if (agg.Pending) ImGui.PushStyleColor(ImGuiCol.Header, PendingTint);

        bool open = ImGui.CollapsingHeader($"{group.Header}##{id}_{group.Facet}_{group.Header}",
                                           ImGuiTreeNodeFlags.DefaultOpen);
        if (agg.Changed || agg.Pending) ImGui.PopStyleColor();

        if (!open) return;

        ImGui.Indent();
        foreach (var child in group.Children) DrawGroup(id, view, child);
        if (group.Children.Count == 0) DrawRows($"{id}_{group.Header}", view, group.Rows);
        ImGui.Unindent();
    }

    private void DrawRows(string id, VariableTableView view, IReadOnlyList<VariableRow> rows)
    {
        if (rows.Count == 0) return;

        var columns = view.Columns.Visible;
        if (!ImGui.BeginTable($"##vt_{id}", columns.Count,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        foreach (var c in columns)
        {
            if (c == VariableColumn.Type)
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 90f);
            else
                ImGui.TableSetupColumn(c.ToString(), ImGuiTableColumnFlags.WidthStretch);
        }
        ImGui.TableHeadersRow();

        for (int i = 0; i < rows.Count; i++)
        {
            var row   = rows[i];
            // ⭐⭐ Batch 87 — ONE read of the row's visual state, and it is the same call a rail makes.
            //    ⛔ A second, independent read here is how B3 happened: the view could answer and the
            //    renderer never asked.
            var state = VisualStateOf(view, row);

            ImGui.TableNextRow();
            // ⭐ Pending wins the row TINT when both apply: "my edit has not landed" is the actionable
            //   one, and §4a's requirement is that the two remain DISTINCT states -- which they do,
            //   because both booleans survive on the view for anything that needs them.
            // ⚠ SELECTION is deliberately NOT in this channel — see VisualStateOf.
            if (state.Pending)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(PendingTint));
            else if (state.Changed)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ChangedTint));

            ImGui.PushID(i);
            foreach (var c in columns) DrawCell(c, view, row, state);
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawCell(VariableColumn column, VariableTableView view, VariableRow row,
                          RowVisualState state)
    {
        ImGui.TableNextColumn();
        if (state.Stale) ImGui.PushStyleColor(ImGuiCol.Text, StaleText);

        switch (column)
        {
            case VariableColumn.Name:
                // ⭐⭐⭐ Batch 87 (B3) — THE call that was missing. The selected flag draws ImGui's own
                //    header highlight on the NAME cell, which is a channel the row background does not
                //    use ⇒ selection and "changed this tick" are visible AT THE SAME TIME.
                ImGui.Selectable(view.DisplayNameOf(row), state.Selected);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(VariableRowGrouping.FullPathTooltip(row));   // ⭐ full path, always
                    // ⭐ Double-click disambiguates BY CELL -- extending the existing convention, not
                    //   overriding it: the NAME cell opens the whole properties object.
                    // ⭐⭐⭐ ...but ONLY where the host offers that gesture. 🔴 Batch 100 (100f) made the
                    //    gesture set host-declared and gated the MENU at :321 — ⛔ this second entry
                    //    point was not gated, so a Watch row still opened Properties on double-click
                    //    *(user, 2026-08-20)*. ⚠ Two entry points, one of them gated, is exactly the
                    //    half-wired shape BP-360 had.
                    // ⭐ A host that does not offer Properties falls back to "Edit value…" rather than
                    //   doing nothing: the user asked for it, and a dead double-click on a live row
                    //   reads as a broken feature.
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && row.CanEverBeWritten)
                        RaiseNameCellDoubleClick(row);
                }
                DrawRowMenu(view, row);
                break;

            case VariableColumn.Type:
                ImGui.TextUnformatted(row.TypeText);
                break;

            case VariableColumn.Value:
                ImGui.TextUnformatted(_formatter.Cell(row, view.ValueMode));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(_formatter.Tooltip(row, view.ValueMode));
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && row.CanEverBeWritten)
                        EditValueRequested?.Invoke(row);
                }
                break;
        }

        if (state.Stale) ImGui.PopStyleColor();
    }

    /// <summary>
    /// ⭐⭐ The row menu (checklist 2.26). Right-click on the NAME cell — the same cell whose
    /// double-click opens Properties, so the two gestures share one target.
    ///
    /// <para>⛔ <b>Rename is deliberately ABSENT, and that is a finding, not an omission.</b> A
    /// <c>VariableRow</c> is an OBSERVATION — <c>(AssetId, Entity, Section, VariablePath)</c> plus a
    /// byte reader. It carries no asset handle, no schema source and no undo recorder, so there is
    /// nothing here that could rename a declaration. The blueprint side renames through
    /// <c>BlueprintDocumentFactory.RegisterMyBlueprintItemCommands</c>, off the My Blueprint OUTLINE,
    /// which does hold the asset. ⇒ ⭐ rename belongs to the outline, and offering a greyed entry here
    /// would restate the same "built but inert" shape this batch exists to remove.</para>
    ///
    /// <para>⚠ Both live entries respect <c>CanEverBeWritten</c>, so a stale or node-owned row shows
    /// them disabled rather than firing a dialog that would refuse.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ Batch 94 (<c>94f</c>) — raised when the designer toggles this row's watch pin.
    /// ⛔ The control does not own the store; the host wires this to <c>PinnedVariableRowSource</c>.
    /// </summary>
    public event Action<VariableRow>? WatchToggleRequested;

    /// <summary>
    /// ⭐ Asks the host whether this row is currently pinned, so the entry can be a real TOGGLE.
    /// ⛔ Null ⇒ "not pinned", which is the right answer for a host with no Watch panel.
    /// </summary>
    public Func<VariableRow, bool>? IsWatched { get; set; }

    /// <summary>
    /// ⛔ Rails only — raises <see cref="WatchToggleRequested"/> without an ImGui context.
    /// ⭐ 📌 <c>R-21</c>/<c>R-62</c>: the menu itself cannot be driven headlessly, so the rail exercises
    /// the WIRING and <c>VariableWatchGesture</c> covers the rule the menu renders.
    /// </summary>
    /// <remarks>
    /// ⭐⭐⭐ <b>Batch 96 (<c>96c</c>) — it takes the VIEW, exactly as <c>DrawRowMenu</c> does.</b>
    /// 📌 The handoff's rule: <i>a rail must take its input from the SAME OBJECT the UI takes it
    /// from</i>. 🔴 The old signature took a bare row, so a rail could hand it a source row the UI
    /// never has — and that is precisely why Batch 94's pin rail stayed green while the product froze.
    /// </remarks>
    internal void RaiseWatchToggleForTest(VariableTableView view, VariableRow row)
        => WatchToggleRequested?.Invoke(view.SourceOf(row));

    /// <summary>
    /// ⭐ The run state, for the watch gesture's refusal rule. ⛔ Set by the host beside its model's
    /// own <c>RunState</c>; the view carries a <c>VariableValueMode</c>, which is a narrower thing.
    /// ⚠ Defaults to <c>Planning</c>, where the gesture is ALLOWED — a host that forgets to set it
    /// gets the permissive state, and pinning while planning is explicitly legal *(spec §7)*.
    /// </summary>
    public VariableRunState RunState { get; set; } = VariableRunState.Planning;

    private void DrawRowMenu(VariableTableView view, VariableRow row)
    {
        if (!ImGui.BeginPopupContextItem()) return;

        // ⭐⭐⭐ Batch 97 (97b) — THE POLICY DECIDES, not the row kind alone.
        // 🔴 This used to read the ROW KIND alone, for BOTH entries, and nothing else. ⛔ VariableEditPolicy also knows Replay ⇒ Denied, so the entry was live
        //    in a state where clicking it opens NOTHING, with no explanation.
        // ⭐ The rule is VariableEditGesture.Decide, which CALLS the policy (ruling 9) — ⛔ a second
        //   spelling here is how the menu and the dialog would come to disagree.
        DrawEditItem(VariableEditGesture.EditValueLabel,  VariableEditAction.EditValue,  row,
                     () => EditValueRequested?.Invoke(row));

        // ⭐⭐⭐ Batch 100 (100f) — the SURFACE decides whether "Properties…" is on the menu.
        // 📌 User: "no one is interested in the other properties than the value in the Watch window."
        // ⛔ ABSENT, not greyed — and the distinction matters: greying says "not right now" (the F3
        //    convention, for a refusal the designer can undo by pausing), whereas this surface will
        //    never offer it. ⭐ A permanently-greyed item is clutter that teaches nothing.
        if (Gestures.OffersProperties)
            DrawEditItem(VariableEditGesture.PropertiesLabel, VariableEditAction.Properties, row,
                         () => PropertiesRequested?.Invoke(row));

        // ⭐⭐⭐ Batch 94 (94f) — ENTRY POINT 2 of the watch gesture (the other is the My Blueprint
        //    row menu). ⛔ One command, two surfaces: a one-surface gesture re-creates the split U-6
        //    removed.
        // ⭐ The RULE is VariableWatchGesture.Decide, not this draw path — 📌 R-21/R-62: no headless
        //   rail can drive ImGui, so the decision is asserted there and only the rendering is here.
        ImGui.Separator();
        var watch = VariableWatchGesture.Decide(row, RunState, IsWatched?.Invoke(row) ?? false);
        if (ImGui.MenuItem(watch.Label, null, false, watch.Enabled) && watch.Enabled)
            // ⭐⭐⭐ Batch 96 (96c) — THE SOURCE ROW, not the one this table drew. 🔴 `row` came out of
            //    VariableRowSampler, whose arms close over the pulse it was sampled on ⇒ pinning it
            //    freezes the Watch at that pulse for ever, which is the defect 94a removed.
            //    ⭐ The Watch has its OWN sampler and re-samples the camera it is handed.
            WatchToggleRequested?.Invoke(view.SourceOf(row));
        // ⭐⭐ Refused by GREYING WITH A REASON — ⛔ never a click that dead-ends.
        if (!watch.Enabled && watch.DisabledReason is { } why && ImGui.IsItemHovered(
                ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(why);

        ImGui.EndPopup();
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 97 (<c>97b</c>) — one edit menu entry, greyed with a reason when the policy denies
    /// it.</b> ⭐ Shaped exactly like the watch entry above it, deliberately — 📌 two spellings of "draw
    /// a refusable menu item" is how the two would come to look different to the designer.
    ///
    /// <para>⚠ <b>A <c>ReadOnly</c> entry stays ENABLED</b> and opens shaped as a view *(Batch 96)*.
    /// ⛔ Greying it would hide values the designer asked to read — 📌 <c>VariableEditLauncher.Open</c>'s
    /// own doc-comment.</para>
    /// </summary>
    private void DrawEditItem(
        string label, VariableEditAction action, VariableRow row, Action raise)
    {
        var gesture = VariableEditGesture.Decide(row, action, RunState);

        if (ImGui.MenuItem(label, null, false, gesture.Enabled) && gesture.Enabled)
            raise();

        // ⭐⭐ Refused by GREYING WITH A REASON — ⛔ never a click that dead-ends.
        if (!gesture.Enabled && gesture.DisabledReason is { } why && ImGui.IsItemHovered(
                ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(why);
    }
}
