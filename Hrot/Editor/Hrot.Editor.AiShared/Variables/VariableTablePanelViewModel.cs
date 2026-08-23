using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-2</c> — THE THIN WRAPPER that makes a <see cref="VariableTableView"/> dumpable.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Adoption <c>U-obs-2</c>.
///
/// <para>⛔⛔ <b>§Adoption says <i>"they already have <c>VariableTableModel</c>; just make it
/// <c>IPanelViewModel</c> + register"</i>. 📐 MEASURED, that cannot work, for two reasons:</b>
/// <list type="number">
///   <item>⭐ <b><c>VariableTableModel</c> is the BUILDER, not the per-frame model</b> — the frame's model
///   is what <c>Build()</c> returns, a <see cref="VariableTableView"/>. Dumping the builder would dump the
///   configuration, not what the designer sees.</item>
///   <item>⭐⭐ <b>The view is SHARED BY THREE HOSTS</b> — the variables table, the shared watch, and the
///   Blueprints watch — so it cannot carry one fixed <c>PanelId</c>: they are different panels.</item>
/// </list>
/// ⇒ ⭐ <b>the wrapper supplies the identity the shared view cannot have</b>, and owns nothing else.</para>
///
/// <para>⭐⭐⭐ <b>ADDRESS vs KIND, and why both are constructor arguments.</b>
/// 🔒 <b>User, <c>2026-08-22</c>:</b> <i>"how will the MCP server know what the panel id to ask for if the
/// panel does not have unique id no matter what model it is showing?"</i>
/// ⇒ ⛔ three watches are live at once *(one per perspective)*: with a shared id they overwrite each other
/// in the snapshot and <c>GET /panels/watch</c> is ambiguous. ⇒ ⭐ <b><c>panelId</c> is the host's own
/// window id</b> *(unique by construction)*, ⭐⭐ <b><c>panelKind</c> is <c>watch</c> / <c>variables</c></b>
/// *(identical across hosts, which is what conformance groups by)*.</para>
///
/// <para>⚠ <b>Variables and Watch are NOT the same panel</b>, measured: different row SOURCE *(all of the
/// asset's variables vs only the pinned ones)* and a different COLUMN SET *(<c>Details</c> vs
/// <c>Watch</c>)*. ⛔ One kind for both would make a conformance diff compare a table against a subset.</para>
/// </summary>
public sealed class VariableTablePanelViewModel : IPanelViewModel
{
    private readonly VariableTableView _view;

    /// <param name="panelId">⭐ The ADDRESS — the host window's own id, unique among live panels.</param>
    /// <param name="panelKind">⭐ The KIND — <see cref="PanelIds.Variables"/> or <see cref="PanelIds.Watch"/>.</param>
    /// <param name="view">The frame's built view. ⚠ Wrapped, never copied — the wrapper adds identity only.</param>
    public VariableTablePanelViewModel(string panelId, string panelKind, VariableTableView view)
    {
        if (string.IsNullOrWhiteSpace(panelId))   throw new ArgumentException("A panel address is required.", nameof(panelId));
        if (string.IsNullOrWhiteSpace(panelKind)) throw new ArgumentException("A panel kind is required.", nameof(panelKind));

        PanelId   = panelId;
        PanelKind = panelKind;
        _view     = view ?? throw new ArgumentNullException(nameof(view));
    }

    /// <inheritdoc/>
    public string PanelId { get; }

    /// <inheritdoc/>
    public string PanelKind { get; }

    /// <summary>⭐ The wrapped view, for a host that needs it back *(the draw already holds it)*.</summary>
    public VariableTableView View => _view;

    /// <summary>
    /// ⭐⭐ <b>Hand-written rather than <c>PanelDump.Of(this)</c>, and the reason is load-bearing.</b>
    /// ⛔ <see cref="VariableRow"/> carries <b>delegates</b> *(<c>ReadValue</c>, <c>AssetTick</c>)* and a
    /// <c>Type</c> — ⚠ reflection over it would either throw or emit noise no assertion could use. ⇒ ⭐ the
    /// dump projects the <b>DISPLAYED</b> shape: what the table actually puts on screen, ⛔ not the machinery
    /// behind it. 📄 §"Open questions" ① allows exactly this — <i>"with a hook for custom cases"</i>.
    /// </summary>
    public JsonNode Dump()
    {
        var rows = new JsonArray();
        foreach (var row in _view.AllRows)
        {
            rows.Add(new JsonObject
            {
                ["name"]      = _view.DisplayNameOf(row),   // ⭐ the DISPLAYED name (qualified when it must be)
                ["shortName"] = row.ShortName,
                ["type"]      = row.TypeText,
                ["kind"]      = row.RowKind.ToString(),
                ["stale"]     = row.IsStale,
                ["written"]   = row.HasEverBeenWritten,
                ["highlight"] = _view.HighlightOf(row).ToString(),
                ["selected"]  = _view.IsSelected(row),
            });
        }

        var groups = new JsonArray();
        foreach (var group in _view.Groups)
            groups.Add(DumpGroup(group));

        return new JsonObject
        {
            ["panelId"]      = PanelId,
            ["panelKind"]    = PanelKind,
            ["columns"]      = _view.Columns.ToString(),
            ["valueMode"]    = _view.ValueMode.ToString(),
            ["selectedPath"] = _view.SelectedVariablePath,
            ["rowCount"]     = _view.AllRows.Count,
            ["rows"]         = rows,
            ["groups"]       = groups,
        };
    }

    /// <summary>⭐ Groups nest, so the dump does too — ⚠ a flattened list would lose the very structure
    /// §9's grouping rules are about.</summary>
    private JsonObject DumpGroup(VariableRowGroup group)
    {
        var children = new JsonArray();
        foreach (var child in group.Children) children.Add(DumpGroup(child));

        var rowNames = new JsonArray();
        foreach (var row in group.Rows) rowNames.Add((JsonNode)_view.DisplayNameOf(row));

        return new JsonObject
        {
            ["facet"]     = group.Facet.ToString(),
            ["header"]    = group.Header,
            ["highlight"] = _view.HighlightOf(group).ToString(),
            ["rows"]      = rowNames,
            ["children"]  = children,
        };
    }
}
