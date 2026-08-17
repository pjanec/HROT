using System;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>U-6</c> — the variables list, as a drawable a DETAILS panel can host.</b>
///
/// <para>📌 <b><c>Q32</c> ruling 1:</b> <i>"Details hosts the list of vars, as designed."</i>
/// 📌 <b><c>Q32</c> ruling 6:</b> <i>"The same Details panel is REUSED for every asset type — HSM,
/// BTree, Blueprint ⇒ this is a cross-host deliverable, not a blueprint one."</i></para>
///
/// <para>⭐⭐ <b>Why this type is thin, and why it is in <c>AiShared</c>.</b> 📌 <b>ruling 9</b> is the
/// acceptance criterion — <i>"no keeping two implementations for the same concept"</i> — and
/// <see cref="VariableTableControl"/> already renders a row list while knowing nothing about its
/// source. ⛔ <b>This batch is PLACEMENT and ROUTING, not construction:</b> a blueprint-local copy of
/// the table would be the exact thing <c>U-6</c> exists to prevent.</para>
///
/// <para>⭐ <b>The host draws it; it does not own a window.</b> That is what lets one Details panel per
/// perspective host the same list — ⛔ a <c>ManagedWindow</c> here would have forced a second Details
/// window on Blueprint, which already has one.</para>
///
/// <para>⚠ <b>The VALUE column's run-state meaning is NOT this batch</b> *(sequencing row 58,
/// <i>"Then values as a second slice"</i>)*. At authoring time there is no entity, so a source without
/// a byte reader renders <c>(pending)</c> — ⛔ not <c>&lt;unreadable&gt;</c>, which would claim a decode
/// failure that never happened.</para>
/// </summary>
public sealed class VariableDetailsSection
{
    private readonly VariableTableControl _control;
    private readonly VariableTableModel   _model;

    /// <param name="formatter">
    /// ⭐ The one value formatter, shared with the standalone table and the Watch — ⛔ two formatters
    /// would be two places to fix a rendering rule.
    /// </param>
    public VariableDetailsSection(VariableValueFormatter formatter, VariableTableColumns? columns = null)
    {
        if (formatter is null) throw new ArgumentNullException(nameof(formatter));

        _control = new VariableTableControl(formatter);
        _model   = new VariableTableModel(
            new FixedVariableRowSource(Array.Empty<VariableRow>()),
            columns ?? VariableTableColumns.Details);
    }

    /// <summary>⭐ The constructed model — a rail asserts on THIS, not on whatever built it.</summary>
    public VariableTableModel Model => _model;

    /// <summary>⭐ The constructed control, so a host can bind its two gestures (rows 59 / 59c).</summary>
    public VariableTableControl Control => _control;

    /// <summary>
    /// What the current list is — e.g. <c>"Variables"</c> or <c>"Local Variables — Tick"</c>.
    /// ⭐ Null when nothing is shown. ⛔ The heading is not decoration: ruling 2 routes between a
    /// GLOBAL list and a GRAPH-SCOPED one, and they are otherwise identical tables.
    /// </summary>
    public string? Heading { get; private set; }

    /// <summary>True once a source has been supplied. ⭐ A rail surface, and the host's draw gate.</summary>
    public bool HasContent => Heading != null;

    /// <summary>
    /// ⭐⭐ Points the list at a source. 📌 <b><c>Q32</c> ruling 2</b>, the panel's whole navigation
    /// model: <i>"Selection routes: click a global in My Blueprint ⇒ the list of globals / working
    /// state. Click a local ⇒ the locals of the currently selected graph."</i>
    /// </summary>
    public void Show(string heading, IVariableRowSource source)
    {
        if (string.IsNullOrEmpty(heading)) throw new ArgumentException("heading is required", nameof(heading));
        _model.Source = source ?? throw new ArgumentNullException(nameof(source));
        Heading       = heading;
    }

    /// <summary>⭐ Lets go, so a stale list cannot outlive the selection that produced it.</summary>
    public void Clear()
    {
        Heading      = null;
        _model.Source = new FixedVariableRowSource(Array.Empty<VariableRow>());
    }

    /// <summary>
    /// Draws the heading and the table. ⛔ No-op when nothing is shown — the HOST decides what to draw
    /// instead, because "nothing selected" reads differently in a node inspector than in a table.
    /// </summary>
    public void Draw(string id)
    {
        if (!HasContent) return;
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        ImGuiNET.ImGui.TextUnformatted(Heading!);
        ImGuiNET.ImGui.Separator();
        _control.Draw(id, _model.Build());
    }
}
