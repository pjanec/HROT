using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-1</c>'s PILOT — the whole of what the Entity Blueprints panel SHOWS, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example *(this panel is the design's own worked
/// example)* + §Invariant.
///
/// <para>⛔⛔ <b>THE INVARIANT: <c>EntityBlueprintsPanel.Render</c> draws ONLY from this.</b> ⚠ Anything the
/// designer can see that is not a field here is <b>invisible to the dump</b> ⇒ ⭐ a test could go green over
/// a broken panel. 📌 <b>The reviewable smell in a diff is exactly that</b>: a drawn value whose source is
/// <c>_model</c>/<c>_registry</c> rather than <c>vm</c>.</para>
///
/// <para>⭐⭐ <b>What is deliberately NOT here, and why it is not a violation:</b> constant chrome — the
/// <c>"+ Add Blueprint..."</c> caption, the three column headers, the <c>Apply</c>/<c>Revert All</c>
/// captions. 📄 §Adoption's own rule: <i>"never refactor a static label."</i> ⇒ ⭐ <b>the invariant binds
/// STATE-DERIVED values</b>; a literal that cannot differ between two runs or two hosts carries no
/// information a conformance diff could use.</para>
///
/// <para>⭐ <b>Identity, not callbacks.</b> Rows carry an <c>AssetId</c> and the render calls the edit model
/// with it — ⛔ a delegate on the view-model would not serialise, and the model must stay dumpable.</para>
/// </summary>
public sealed record EntityBlueprintsViewModel : IPanelViewModel
{
    /// <inheritdoc/>
    public string PanelId => PanelIds.EntityBlueprints;

    /// <summary>⭐ The panel's own heading, drawn as-is.</summary>
    public string Title { get; init; } = "Entity Blueprints";

    /// <summary>⭐⭐ <see langword="false"/> ⇒ only <see cref="EmptyMessage"/> is drawn, nothing below it.</summary>
    public bool HasEntity { get; init; }

    /// <summary>⭐ The disabled line shown when there is no entity. ⚠ Non-null exactly when <see cref="HasEntity"/> is false.</summary>
    public string? EmptyMessage { get; init; }

    /// <summary>⭐ <c>"Running"</c> or <c>"Paused"</c> — drawn as <c>Sim: {SimState}</c>.</summary>
    public string SimState { get; init; } = "";

    /// <summary>⭐ The entity's current blackboard tier, as drawn.</summary>
    public string Tier { get; init; } = "";

    /// <summary>⭐ The projection bar's whole line — ⛔ composed HERE, so the dump carries what the eye reads.</summary>
    public string ProjectionLabel { get; init; } = "";

    /// <summary>⭐⭐ The machine-readable half of the same fact — an assertion should not have to parse the label.</summary>
    public string ProjectionStatus { get; init; } = "";

    /// <summary>⭐ The entries the <c>+ Add Blueprint...</c> popup offers.</summary>
    public IReadOnlyList<EntityBlueprintAddOption> AddOptions { get; init; } = Array.Empty<EntityBlueprintAddOption>();

    /// <summary>⭐ The table's rows — reality first, then staged adds, in the order drawn.</summary>
    public IReadOnlyList<EntityBlueprintRow> Rows { get; init; } = Array.Empty<EntityBlueprintRow>();

    /// <summary>⭐⭐ Whether <c>Apply</c> is enabled. ⛔ The DECISION lives here, not in the draw — an
    /// enablement rule fused into the render is a rule no test can reach.</summary>
    public bool CanApply { get; init; }

    /// <summary>⭐ Whether <c>Revert All</c> is enabled.</summary>
    public bool CanRevert { get; init; }

    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>⭐ One row of the blueprint table, exactly as drawn.</summary>
/// <param name="Name">The blueprint's display name.</param>
/// <param name="Status">The drawn status text — <c>Active</c> · <c>Remove pending</c> · <c>Add pending</c>.</param>
/// <param name="Emphasis">⭐⭐ The COLOUR ROLE, not a colour: <c>none</c> · <c>warning</c> · <c>success</c>.
/// ⛔ A <c>Vector4</c> here would put presentation in the model and make cross-host diffs fail on theming.</param>
/// <param name="ActionLabel">The row button's caption — <c>Remove</c> · <c>Restore</c> · <c>Cancel</c>.</param>
/// <param name="AssetId">⭐ The identity the action acts on. ⚠ Not a callback — the model must serialise.</param>
/// <param name="ActionScope">⚠ ImGui id-scope suffix, so two rows' buttons stay distinct. Not user-visible.</param>
public sealed record EntityBlueprintRow(
    string Name,
    string Status,
    string Emphasis,
    string ActionLabel,
    Guid   AssetId,
    string ActionScope);

/// <summary>⭐ One entry in the add-blueprint popup.</summary>
/// <param name="Label">The visible text — the name, plus <c>(attached)</c>/<c>(staged)</c> when it cannot be picked.</param>
/// <param name="State">⭐ <c>selectable</c> · <c>attached</c> · <c>staged</c> — why it is or is not offerable.</param>
/// <param name="AssetId">The identity <c>StageAdd</c> is called with.</param>
/// <param name="ActionScope">⚠ ImGui id-scope suffix. Not user-visible.</param>
public sealed record EntityBlueprintAddOption(
    string Label,
    string State,
    Guid   AssetId,
    string ActionScope);
