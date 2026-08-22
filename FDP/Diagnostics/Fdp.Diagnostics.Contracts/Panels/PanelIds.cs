namespace Fdp.Diagnostics.Contracts.Panels;

/// <summary>
/// ⭐⭐⭐ <b><c>U1d</c> — THE PANEL-ID RULE, and it RESOLVES A CONTRADICTION IN THE DESIGN.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>.
///
/// <para>⛔⛔ <b>The design says two different things, and they are incompatible:</b>
/// <list type="bullet">
///   <item>§"Perf &amp; correctness" — <i>"use the window-manager registration id"</i>;</item>
///   <item>§Example — the payload is literally <c>"panelId": "entity-blueprints"</c>, which is
///   <b>not</b> a window-manager id.</item>
/// </list></para>
///
/// <para>📐 <b>MEASURED, and the §Example half wins on both counts:</b>
/// <list type="number">
///   <item>⛔ <b>Half the panels have no window-manager id at all.</b> <c>BlueprintEditorWindowBase</c>
///   *(the pilot's own base class)* declares <c>Title</c> and <b>nothing else</b> — there is no id to use.</item>
///   <item>⛔⛔ <b>Where ids DO exist they are PERSPECTIVE-SUFFIXED</b> — <c>ai_runtime_inspector_btree</c>
///   vs <c>ai_runtime_inspector_hsm</c> — because they must be unique per dock slot. ⇒ ⭐ using them as
///   panel ids would give the SAME logical panel a DIFFERENT key per host, which is precisely what
///   cross-host conformance diffs by. 📌 <b>The window-manager id is unique by construction; a panel id must
///   be STABLE by construction. Those are opposite requirements.</b></item>
/// </list></para>
///
/// <para>⇒ ⭐⭐ <b>THE RULE: a panel id is a declared, lower-kebab-case literal naming WHAT THE PANEL IS</b>,
/// ⛔ never derived from a window id, a dock slot, a perspective or a type name. ⭐ Cross-host ids live
/// <b>here</b>, in the contracts assembly both hosts already reference, so <i>"the same panel"</i> is a
/// compile-time fact rather than two string literals that agree today.</para>
///
/// <para>⚠ <b>A panel private to ONE host may declare its own literal locally</b> — ⛔ only ids that must
/// MATCH ACROSS HOSTS earn a constant here. 📌 Adding every id would make this file a second registry that
/// rots against the panels; ⭐ the fan-out adds a constant exactly when a second host grows the same panel.</para>
/// </summary>
public static class PanelIds
{
    /// <summary>
    /// ⭐ The entity-blueprints panel — <c>U-obs-1</c>'s pilot, and the design's own §Example id, verbatim.
    /// </summary>
    public const string EntityBlueprints = "entity-blueprints";
}
