namespace Fdp.Diagnostics.Contracts.Panels;

/// <summary>
/// ⭐⭐⭐ <b><c>U1d</c> — THE PANEL-ID RULE, and it RESOLVES A CONTRADICTION IN THE DESIGN.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>.
///
/// <para>⛔⛔ <b>The design appears to say two different things:</b>
/// <list type="bullet">
///   <item>§"Perf &amp; correctness" — <i>"use the window-manager registration id"</i>;</item>
///   <item>§Example — the payload is literally <c>"panelId": "entity-blueprints"</c>, which is
///   <b>not</b> a window-manager id.</item>
/// </list></para>
///
/// <para>⭐⭐⭐ <b>THE RESOLUTION: they are not rivals. They are the TWO DIFFERENT JOBS one field was being
/// asked to do</b> *(user, <c>2026-08-22</c>: <i>"how will the MCP server know what the panel id to ask for
/// if the panel does not have unique id no matter what model it is showing"</i>)*:
/// <list type="number">
///   <item>⭐⭐ <b><c>PanelId</c> — THE ADDRESS.</b> <c>GET /panels/{id}</c> resolves it and the snapshot is
///   keyed by it ⇒ ⛔ it must be <b>unique among live panels</b>, or an agent cannot say which watch it
///   means and one silently overwrites the other. ⇒ §"Perf &amp; correctness" was <b>right about this</b>:
///   a per-perspective window's registration id *(<c>ai_watch_btree</c>)* is unique <i>by construction</i>.</item>
///   <item>⭐⭐ <b><c>PanelKind</c> — THE LOGICAL NAME.</b> Conformance groups by it ⇒ it must be
///   <b>identical across hosts and perspectives</b>. ⇒ §Example was <b>right about this</b>.</item>
/// </list>
/// ⚠⚠ <b>Unique-by-construction and stable-by-construction are opposite properties</b>, which is why one
/// field could never carry both — ⛔ and why this contract's first cut, which had only <c>PanelId</c>, was
/// wrong. 📌 <b>It was invisible on the pilot because a singleton panel's address and kind are the same
/// string.</b></para>
///
/// <para>⚠ <b>One panel family still has NO window id</b>: <c>BlueprintEditorWindowBase</c> declares
/// <c>Title</c> and nothing else. ⇒ ⭐ a singleton there declares a literal for both roles; ⛔ if such a
/// panel ever becomes multi-instance it must gain a real address first.</para>
///
/// <para>⇒ ⭐⭐ <b>What lives in THIS file: KINDS</b> — lower-kebab-case, in the contracts assembly both
/// hosts already reference, so <i>"the same panel"</i> is a compile-time fact rather than two literals that
/// happen to agree. ⛔ <b>Addresses do NOT live here</b>: they are per-instance and belong to whatever
/// registers the window. ⚠ Only kinds that must MATCH ACROSS HOSTS earn a constant — 📌 adding every one
/// would make this a second registry that rots against the panels.</para>
/// </summary>
public static class PanelIds
{
    /// <summary>
    /// ⭐ The entity-blueprints panel — <c>U-obs-1</c>'s pilot, and the design's own §Example id, verbatim.
    /// ⚠ A singleton, so it serves as both kind and address.
    /// </summary>
    public const string EntityBlueprints = "entity-blueprints";

    /// <summary>⭐ The variables table — ALL of an asset's variables, <c>Details</c> columns.
    /// ⛔ Not the same panel as <see cref="Watch"/>: 📐 different source, different column set.</summary>
    public const string Variables = "variables";

    /// <summary>⭐ The watch — PINNED rows only, <c>Watch</c> columns. ⚠ Multi-instance: one per
    /// perspective, plus the Blueprints host's own ⇒ ⛔ **each needs its own address**; this is the KIND
    /// they share.</summary>
    public const string Watch = "watch";

    /// <summary>
    /// ⭐ The Details shell. ⚠⚠ <b>Earns a constant because a SECOND CLASS must agree with it</b> —
    /// <c>DetailsViewWindow</c> is the float/pin twin of <c>DetailsWindow</c>. ⛔ Two literals that happen
    /// to match today are not the same thing as one constant they both cite.
    /// </summary>
    public const string Details = "details";

    /// <summary>
    /// ⭐ The "My Blueprint" outline. ⚠⚠ <b>Two DIFFERENT implementations must agree</b>:
    /// <c>AiMyBlueprintWindow</c> (BTree/HSM) and <c>BlueprintMyBlueprintWindow</c> (Blueprint host).
    /// ⛔ The second is not converted yet — ⭐ when it is, it must cite THIS, not repeat the string.
    /// </summary>
    public const string MyBlueprint = "my-blueprint";

    /// <summary>
    /// ⭐ <c>U-obs-5</c>, group 4/5 — the generic ECS entity inspector. ⚠⚠ <b>TWO window classes of the
    /// same name host the same <c>EntityInspectorPanel</c></b>: <c>Fdp.Presentation.Windows.ReplayBrowser
    /// .FdpEntityInspectorWindow</c> and <c>Hrot.Presentation.Windows.FdpEntityInspectorWindow</c> — a
    /// second host, not a duplicate panel. Both must agree on the kind.
    /// </summary>
    public const string EntityInspector = "entity-inspector";

    /// <summary>
    /// ⭐ <c>U-obs-5</c>, group 4/5 — the generic diagnostic event browser. ⚠⚠ Two window classes named
    /// <c>FdpEventBrowserWindow</c> (in <c>Fdp.Presentation.Windows.ReplayBrowser</c> and
    /// <c>Hrot.Presentation.Windows</c>) host the same <c>EventBrowserPanel</c>; both must agree.
    /// </summary>
    public const string EventBrowser = "event-browser";

    // ── group-5 twin panels (Hrot.UI.Common.Panels, shipped from Hrot.Presentation) ─────────────
    //
    // ⚠⚠ Each of these panels is hosted by BOTH the Editor perspective (Hrot.Editor's
    // EditorWindows.cs) and the ExCon perspective (Hrot.ExCon's ExConWindows.cs /
    // ExConMock.cs) — two DIFFERENT window classes wrapping the SAME panel class, exactly the
    // "PanelKind must agree" case this file exists for. 📄 QUEUE_Panel_Observability_Sweep.md
    // group 5's twin-diff finding: a SEPARATE, unreferenced Hrot.UI.Common PROJECT holds a
    // stale copy of the same source under an identical namespace — that copy is dead (zero
    // ProjectReferences, absent from IOS-IG-SimHost.sln) and is NOT part of this kind scheme.

    /// <summary>⭐ The map-layer visibility config panel.</summary>
    public const string Config = "config";

    /// <summary>⭐ The TKB entity-catalog spawner panel.</summary>
    public const string Spawner = "spawner";

    /// <summary>⭐ The ORBAT drag-and-drop tree panel.</summary>
    public const string SharedOrbat = "shared-orbat";

    /// <summary>⭐ The Edit/Preview mode toggle panel.</summary>
    public const string Preview = "preview";

    /// <summary>⭐ The zone/road-network authoring panel.</summary>
    public const string ZoneEditor = "zone-editor";

    /// <summary>⭐⭐ The mission editor panel (<c>MissionPanel</c>, in <c>Hrot.Presentation</c> — NOT a
    /// group-5 twin, but hosted by BOTH <c>Hrot.Editor</c>'s <c>EditorMissionWindow</c> and
    /// <c>Hrot.ExCon</c>'s <c>ExConMissionWindow</c>). ⚠⚠ <b>Correction:</b> an earlier commit in this
    /// sweep stated "no ExCon host exists for this panel (measured)" — that measurement was wrong; it
    /// checked <c>MissionPanel</c> usages everywhere EXCEPT <c>ExConWindows.cs</c>, which does host it.
    /// Both hosts now cite this constant.</summary>
    public const string Mission = "mission";
}
