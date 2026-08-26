using System.Text.Json.Nodes;
using Hrot.SystemTests.Goldens;
using Xunit.Abstractions;

namespace Hrot.SystemTests.Conformance;

/// <summary>
/// ⭐⭐⭐ <b>CROSS-HOST CONFORMANCE — the same binary in TWO modes, diffed by <c>PanelKind</c>, with a
/// THREE-WAY verdict.</b>
/// 📄 <c>DESIGN_Headless_Testability.md</c> § *"Cross-host conformance"* + §6 · <c>Architect_Question_54</c>
/// *(RESOLVED)* · <c>DESIGN_Perspective_Unification.md</c> §1d.
///
/// <para>🔒 <b>User, `2026-08-24`:</b> <i>"(a) `--mode all` answers MCP and its panels/gizmos match the
/// editor's; (b) the part-C editor goldens still pass; (c) BOTH driven by the SAME deterministic cluster-wide
/// step."</i></para>
///
/// <para>⛔⛔ <b>THE VERDICT IS THREE-WAY, AND THAT IS THE WHOLE POINT.</b> *"Present in the editor, absent in
/// <c>--mode all</c>"* is the EXPECTED state for everything not yet ported *(charter <c>D3</c>)* ⇒ a two-way
/// diff would be red on day one and stay red, which teaches everyone to ignore it. ⭐ So:
/// <list type="bullet">
/// <item>⭐ <b>SAME</b> — both modes publish that kind and the models agree;</item>
/// <item>🔴 <b>DIFFERENT</b> — both publish it and the models disagree ⇒ <b>a unification regression</b>,
/// named by JSON path;</item>
/// <item>⚪ <b>NOT-PRESENT</b> — one mode does not offer it, and 🔴🔴 <b>that must be DECLARED in the
/// committed baseline</b>, ⛔ never inferred from the panel simply being missing. 📌 Otherwise a genuinely
/// broken panel reads as *"not ported yet"* forever — the false green <c>D4</c> exists to kill.</item>
/// </list></para>
///
/// <para>⭐⭐ <b>Compared by <c>PanelKind</c>, not by <c>PanelId</c></b> — 📄 §4b: the id is the STORAGE key
/// *(goldens)*, the kind is the CONFORMANCE GROUPING key. 📐 And it has to be: the editor publishes
/// <c>editor_fdp_inspector</c> where the cluster publishes <c>cgf_fdp_inspector</c>; the same KIND
/// *(<c>entity-inspector</c>)* is the only thing they share.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemConformance")]
public sealed class ClusterConformanceRails
{
    private readonly ITestOutputHelper _out;
    public ClusterConformanceRails(ITestOutputHelper output) => _out = output;

    private const int SettleTicks = 3;

    /// <summary>⭐ The curated scenario both hosts are given, so their worlds are comparable.</summary>
    private const string ConformanceScenario = "hill-attack";

    /// <summary>
    /// ⭐⭐⭐ <b>EQUALISE THE WORLDS — the sequence the design always specified and could not execute.</b>
    /// 📄 <c>DESIGN_Headless_Testability.md</c> §Conformance *("load S in BOTH, then diff")* ·
    /// <c>MCP_Integration.md</c> § Group U.
    ///
    /// <para>⛔⛔ Until <c>HN-029</c> this was impossible: <c>POST /scenario/load</c> was hardwired to
    /// <c>IEditorLogic</c>, so <c>--mode all</c> answered <c>NOT_SUPPORTED_HERE(editor.authoring)</c> and
    /// <c>entity-inspector</c> could only be DECLARED divergent, never diagnosed.</para>
    ///
    /// <para>⭐ <b>LIVE, not edit</b> — deliberate: every host has live-load handlers, whereas <b>CGF has no
    /// edit-load handler</b> *(`UXI-37` ruling 65, a CGF-lane follow-up)*, so an edit load in
    /// <c>--mode all</c> is PARTIAL and would compare a half-loaded cluster.</para>
    /// </summary>
    private static async Task LoadLiveAsync(EditorProcess host, ITestOutputHelper output)
    {
        var r = await host.Client.LoadScenarioLiveAsync(ConformanceScenario, waitForReady: true);
        r.EnsureOk();
        output.WriteLine($"[{host.Mode}] loaded live: {r.Data?.ToJsonString()}");
    }

    // ══ the shared machinery ══════════════════════════════════════════════════

    /// <summary>⭐ Every captured panel in one host, keyed by KIND, with its id and canonical model.</summary>
    /// <remarks>
    /// ⭐ <c>internal</c>, not <c>private</c>, since <c>CE-016</c>: a second conformance file needs the
    /// same capture and a copy of it would be two implementations of one mechanism (ruling 9).
    /// ⚠ Deliberately the ONLY change this batch makes to this file — it is edited concurrently by
    /// another session, so the smallest possible edit was chosen over adding a method here.
    /// </remarks>
    internal static async Task<Dictionary<string, (string Id, string Model)>> CaptureByKindAsync(
        EditorProcess host, ITestOutputHelper output)
    {
        var byKind = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        var perspectives = ((await host.Client.ListPerspectivesAsync()).EnsureOk()
                            .Field("perspectives") as JsonArray)!
                           .Select(n => n!.GetValue<string>())
                           .ToArray();

        output.WriteLine($"[{host.Mode}] perspectives: [{string.Join(", ", perspectives)}]");

        foreach (var p in perspectives)
        {
            // ⭐ §6's CONTRACT: switch, STEP, then read. ⛔ A same-frame read returns the empty prefix.
            var switched = await host.Client.SwitchPerspectiveAsync(p);
            if (!switched.Ok) continue;                 // a perspective that refuses is not this rail's subject
            await host.Client.StepAsync(SettleTicks);   // ⚠ may be NOT_SUPPORTED_HERE — see the step rail
            await Task.Delay(150);

            var panels = (await host.Client.GetPanelsAsync()).EnsureOk();
            var captured = ((panels.Field("captured") as JsonArray)!)
                           .Select(n => n!.GetValue<string>()).ToArray();

            foreach (var id in captured)
            {
                var dump = await host.Client.GetPanelAsync(id);
                if (!dump.Ok) continue;

                var kind = dump.String("panelKind");
                if (string.IsNullOrEmpty(kind)) continue;

                // ⚠ First one wins: a kind can appear in several perspectives (a Global window appears in
                //   all of them), and conformance asks "does this HOST publish that kind", not "how often".
                if (!byKind.ContainsKey(kind))
                    byKind[kind] = (id, PanelNormalizer.CanonicalForConformance(dump.Field("model")));
            }
        }

        return byKind;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE COMMITTED KNOWN-ABSENT BASELINE — the golden of the capability diff.</b>
    /// 📄 <c>Architect_Question_54</c> § *"the gap the measured matrix leaves"*.
    ///
    /// <para>⭐ Each entry is a <c>PanelKind</c> legitimately published by only ONE mode today. ⇒ a genuine
    /// port is a <b>reviewed one-line deletion</b> from this list; a kind that goes missing WITHOUT being
    /// here is a <b>FAILURE</b>. ⛔ Nothing is auto-added: that would make the baseline a log of decay.</para>
    /// </summary>
    private static readonly HashSet<string> EditorOnlyKinds = new(StringComparer.Ordinal)
    {
        // ⭐⭐⭐ cgf==editor SLICE 1, `2026-08-25` — NINE ENTRIES DELETED, and the deletion is the
        //    deliverable. 📄 docs/DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md §5/§6.
        //    📐 `CgfSubsystem.BuildAiShell` now constructs the AiShared shell and registers the same
        //    windows under the same asset perspectives, so `--mode all` publishes:
        //      blackboard-authoring · my-blueprint · variables · watch · ai-breakpoints ·
        //      graph-canvas · details · runtime-inspector · diagnostics · bookmarks
        //    ⇒ each of those is now a SHARED kind and is DIFFED for real rather than exempted.
        //    ⛔ This is the "reviewed one-line deletion" this baseline's own doc describes; nothing was
        //    added to it in exchange (the three kinds that still differ are declared in
        //    DivergesByDesign, WITH their measured reason, ⛔ not hidden back in here).
        //
        // ⭐ WHAT REMAINS EDITOR-ONLY, and why each is genuinely not ported by slice 1:
        //    · graph-signature / entity-blueprints — Blueprint AUTHORING surfaces; slice 1 is
        //      viewing/diagnostics (§1: asset editing is a later slice).
        //    · data-breakpoint-manager — the editor's breakpoint AUTHORING window, likewise.
        "graph-signature", "entity-blueprints", "data-breakpoint-manager",
        // ⭐ EDITOR-SHELL surfaces with no cluster counterpart.
        "preview", "zone-editor", "editor-toolbar", "shared-orbat",
        // 🔴🔴 THE ONE THAT IS A FINDING, NOT A FEATURE GAP: the GIZMO FRAME.
        //    📐 Measured `2026-08-24`: the editor publishes kind `_gizmo` (GizmoFramePanel.Publish) and
        //    `--mode all` publishes NOTHING of the sort ⇒ the handoff's "dump K + the gizmo frame" can only
        //    be half-done today. ⛔ Declared here so the suite is green and the gap is VISIBLE, ⭐ and filed
        //    as a finding — this entry should be DELETED, not carried, once a cluster host publishes it.
        "_gizmo",
    };

    /// <summary>
    /// ⭐ Kinds only the CLUSTER publishes. ⚠ Not a defect either — the editor is one node and has no
    /// per-node ExCon/IG surfaces. 📐 The names are MEASURED from a real `--mode all` boot, not guessed
    /// *(a first cut guessed `orbat`/`data-monitor`/`system-profiler` and the rail refused all three)*.
    /// </summary>
    private static readonly HashSet<string> ClusterOnlyKinds = new(StringComparer.Ordinal)
    {
        "excon-data-monitor", "excon-der-entity-inspector", "excon-diagnostics", "excon-orbat",
        "ig-debug", "ig-entity-properties", "ig-mini-excon", "ig-waypoint-editor",
    };

    /// <summary>
    /// ⭐⭐⭐ <b>THE FOURTH VERDICT THE DESIGN DID NOT HAVE: *"same kind, DIFFERENT BY DESIGN"*.</b>
    /// 📄 <c>Architect_Question_54</c> Q54-1 gave three — SAME / DIFFERENT / NOT-PRESENT — and 📐 measuring
    /// the real pair produced a case none of them fits.
    ///
    /// <para>🔴🔴 <b>The measurement, `2026-08-24`:</b> of the four comparable shared kinds, TWO diverge for
    /// reasons that are not regressions:
    /// <list type="bullet">
    /// <item><c>entity-inspector</c> — ⚠⚠ <b>SUPERSEDED REASON, kept visible because it was quoted for a
    /// day.</b> It used to read <i>"the worlds CANNOT be equalised: <c>POST /scenario/load</c> in
    /// <c>--mode all</c> answers <c>NOT_SUPPORTED_HERE(editor.authoring)</c>"</i>. ⛔ That tooling gap is
    /// gone (<c>HN-029</c>) and so is the id divergence that replaced it as a second reason
    /// (<c>HN-037</c>, `2026-08-24`: both hosts now number <c>1000–1007</c>). ⭐ <b>The entry survives on ONE
    /// remaining, measured reason</b> — the IG node's inspector also lists a NODE-LOCAL entity
    /// (<c>networkId 0</c>, unnamed) the editor has no counterpart for. See the dictionary below;</item>
    /// <item><c>spawner</c> — the editor offers <b>14</b> TKB entries *(platforms)*, ExCon offers <b>9</b>
    /// *(composites: "Tank Platoon (Empty)", "Infantry Squad (Empty)")*. ⭐ A host-specific catalogue is the
    /// operator's business, not a unification bug.</item>
    /// </list></para>
    ///
    /// <para>⭐⭐ <b>Why a DECLARED set and not a silent skip:</b> the same argument <c>D4</c> makes for
    /// NOT-PRESENT — an undeclared exemption is indistinguishable from a defect. ⇒ each entry carries its
    /// REASON, and <c>A_declared_divergence_that_stopped_diverging_is_deleted</c> reddens if one of them
    /// starts agreeing. ⛔ An exemption nothing needs must be deleted, not carried.</para>
    /// </summary>
    private static readonly Dictionary<string, string> DivergesByDesign = new(StringComparer.Ordinal)
    {
        // ⭐⭐ REASON REPLACED `2026-08-24` (HN-029). It used to say the worlds "cannot be equalised" because
        //    POST /scenario/load answered NOT_SUPPORTED_HERE(editor.authoring). ⛔ That tooling gap is GONE:
        //    both hosts now load the same scenario live, and the entry survives for a MEASURED reason instead.
        // 📐 With hill-attack loaded live in both (8 entities each), the IG node's inspector lists 10 rows to
        //    the editor's 9: it carries a NODE-LOCAL entity (networkId 0, name null) the editor has no
        //    counterpart for. ⇒ the panel's row LIST legitimately differs even when the SCENARIO content
        //    matches — which `The_two_hosts_hold_the_same_loaded_world` now asserts directly.
        // ⭐⭐ HN-037, `2026-08-24`: the id divergence is GONE — both hosts number the same scenario
        //    1000-1007, asserted by The_two_hosts_number_the_same_entities_identically. ⇒ ONE reason remains,
        //    and it is named here so nobody re-reads this entry as still covering ids.
        ["entity-inspector"] = "the IG node's inspector also lists node-local entities (networkId 0, unnamed) "
                             + "that the editor has no counterpart for. NOT ids: those match since HN-037. "
                             + "The SCENARIO content is compared for real by The_two_hosts_hold_the_same_loaded_world "
                             + "and its ids by The_two_hosts_number_the_same_entities_identically",
        ["spawner"]          = "host-specific catalogue: the editor offers platforms, ExCon offers composites",

        // ══ cgf==editor SLICE 1, `2026-08-25` — THREE NEWLY-SHARED KINDS THAT STILL DIFFER ═══════
        // ⭐⭐ These are here because slice 1 made them SHARED (they were exempted wholesale as
        //    editor-only before) and each still differs for a reason that was MEASURED, not assumed.
        // ⛔⛔ Declaring is NOT "narrowing the diff to fake a pass": every one of them names the
        //    capability whose ABSENCE causes it, and each entry is deleted by the slice that adds it —
        //    `A_declared_divergence_that_stopped_diverging_is_deleted` reddens the moment one agrees.

        // 📐 Measured: `$.assetCount` 75 vs 0, `$.hasValidators` true vs false, 5 vs 3 diagnostics.
        // ⭐ CGF constructs an EMPTY AssetCatalog: it has no AiCatalogBuilder because it does not
        //   author or index assets, and the BTree/HSM validator assemblies are not referenced by
        //   Hrot.CGF at all. ⇒ this cell moves when asset INDEXING reaches CGF, not by wiring.
        ["diagnostics"] = "CGF indexes no authoring assets (empty AssetCatalog, no AiCatalogBuilder) and "
                        + "references neither validator assembly, so assetCount/hasValidators are "
                        + "legitimately 0/false there. Deleted when asset indexing reaches CGF.",

        // 📐 Measured: `$.registeredPaneCount` 1 vs 0.
        // ⭐ A runtime-inspector pane REQUIRES a debug session to bind to; the editor registers the
        //   Blueprint pane only inside `if (_blueprintDebugSession != null)`. CGF constructs no
        //   IBlueprintDebugSession, so a pane here could only ever answer null.
        ["runtime-inspector"] = "a pane binds to a debug session and CGF constructs none "
                              + "(no IBlueprintDebugSession); the editor registers its pane only when it "
                              + "has one. Deleted when debug sessions reach CGF.",

        // 📐 Measured: `$.mode` Paused vs Running, `$.focus` VariableOutline vs GraphCanvas.
        // ⭐⭐ `mode` is a REAL difference between the hosts, not a wiring gap: the editor has a
        //    PLANNING state with a halted clock, CGF is a cluster node whose world ticks from boot, and
        //    this rail deliberately does not pause or equalise the two (see its own comment).
        //    ⛔ Making CGF answer "Paused" would be a constant standing in for a clock reading — the
        //    silent-default shape this codebase keeps paying for.
        // ⚠⚠ REASON EXTENDED `2026-08-25` (slice 2). The `focus` half is GONE — with an asset open on
        //    both hosts the Details panel now names the SAME asset and the same focused pane, which
        //    `The_same_opened_asset_looks_the_same_on_both_hosts` asserts directly. ⭐ TWO measured
        //    reasons remain, and each names the capability whose absence causes it.
        ["details"] = "two measured reasons, both pre-dating slice 2: (1) $.mode Paused vs Running — the "
                    + "editor has a PLANNING state with a halted clock while a cluster node's world ticks "
                    + "from boot (CE-003), and the three-way rail deliberately does not equalise them; "
                    + "(2) $.offeredViewIds 3 vs 1 — details.runtime.Blueprint requires an "
                    + "IBlueprintDebugSession and CGF constructs none (CE-004). Reason (2) is deleted "
                    + "when debug sessions reach CGF. The panel DOES name the opened asset on both hosts "
                    + "since slice 2 — that half is asserted, not exempted.",

        // ══ cgf==editor SLICE 2, `2026-08-25` — the toolbar becomes SHARED and differs ═════════════
        // 📐 Measured: the editor publishes a populated main toolbar, `--mode all` publishes an EMPTY
        //    one — `EditorSubsystem` is the ONLY production caller of MainToolbar.RegisterEntry /
        //    RegisterSeparator, so a cluster host registers nothing.
        // ⭐⭐ Slice 2's job was to make the toolbar READABLE (it now publishes on both hosts even when
        //    it does not draw), ⛔ NOT to give CGF entries — design §7 hands that to whichever later
        //    slice ports a toolbar-controlled feature, and requires that slice to assert the affordance
        //    is present and SAME on CGF.
        ["main-toolbar"] = "the editor registers every main-toolbar entry and a cluster host registers "
                         + "none, so CGF's toolbar is legitimately EMPTY today (CE-016). Slice 2 made it "
                         + "readable, not populated. Deleted by the first slice that ports a "
                         + "toolbar-controlled feature to CGF (design §7).",
    };

    // ══ the rails ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE THREE-WAY DIFF.</b> Both modes boot the same binary, publish their panels, and every kind
    /// is classified SAME / DIFFERENT / NOT-PRESENT.
    ///
    /// <para>⛔ A <b>DIFFERENT</b> verdict fails and names the kind — that is the unification regression this
    /// suite exists for. ⛔ A NOT-PRESENT kind that is in NEITHER baseline set also fails: absence must be
    /// declared, and an undeclared one is indistinguishable from a broken panel.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_two_modes_agree_on_every_shared_panel_kind()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-all", mode: "all");

        // ⛔⛔ THIS RAIL DELIBERATELY DOES *NOT* LOAD A SCENARIO — and that is a measured decision, not an
        //    omission. 📄 The loaded-world comparison is its own rail now
        //    (`The_two_hosts_hold_the_same_loaded_world`, HN-029).
        //
        // 📐 Measured `2026-08-24`: equalising the worlds HERE made `mission` newly DIFFERENT
        //    (`selectedEntityId` 0 vs 9, `commitButtonEnabled` false vs true) — ⭐ ExCon's mission panel
        //    carries a LOCAL SELECTION once there is something to select. ⚠ Local selection is per-host by
        //    nature (a user clicks in one host, not the other), so folding it in here would have forced a
        //    whole-panel exemption for `mission` and hidden any real regression inside it.
        // ⇒ ⭐⭐ this rail asks "do the two hosts publish the same panel STRUCTURE", the other asks "did they
        //    load the same WORLD". Two questions, two rails, neither weakened to accommodate the other.
        var a = await CaptureByKindAsync(editor,  _out);
        var b = await CaptureByKindAsync(cluster, _out);

        _out.WriteLine($"editor kinds : {a.Count} — [{string.Join(", ", a.Keys.OrderBy(k => k, StringComparer.Ordinal))}]");
        _out.WriteLine($"cluster kinds: {b.Count} — [{string.Join(", ", b.Keys.OrderBy(k => k, StringComparer.Ordinal))}]");

        // ⛔ Anti-vacuity: two empty captures would "agree" perfectly. 📌 HN-007 is why this line exists.
        Assert.True(a.Count >= 8, $"the editor published only {a.Count} panel kinds — too few to compare");
        Assert.True(b.Count >= 8, $"the cluster published only {b.Count} panel kinds — too few to compare");

        // ⭐⭐ THE VOLATILE KINDS ARE EXCLUDED HERE TOO — and for the same measured reason `N1` excluded them
        //    from the goldens: they carry WALL-CLOCK content, so two processes can never agree. 📐 `N1`
        //    proved the set is exactly these two, in both directions, and that control still guards it.
        //    ⛔ This is not a widening: it is the same declaration applied to the same fact.
        var volatileKinds = new HashSet<string>(StringComparer.Ordinal) { "message-log", "event-browser" };

        var shared     = a.Keys.Intersect(b.Keys, StringComparer.Ordinal)
                          .Where(k => !volatileKinds.Contains(k))
                          .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var editorOnly = a.Keys.Except(b.Keys, StringComparer.Ordinal)
                          .Where(k => !volatileKinds.Contains(k))
                          .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var clusterOnly = b.Keys.Except(a.Keys, StringComparer.Ordinal)
                          .Where(k => !volatileKinds.Contains(k))
                          .OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var different = new List<string>();
        var declaredDiverged = new List<string>();
        foreach (var kind in shared)
        {
            if (string.Equals(a[kind].Model, b[kind].Model, StringComparison.Ordinal)) continue;

            var diffs = PanelNormalizer.Diff(JsonNode.Parse(a[kind].Model), JsonNode.Parse(b[kind].Model));
            var line  = $"{kind} ({a[kind].Id} vs {b[kind].Id}): {string.Join(" | ", diffs.Take(4))}";

            if (DivergesByDesign.ContainsKey(kind)) declaredDiverged.Add(line);
            else                                    different.Add(line);
        }

        foreach (var line in declaredDiverged) _out.WriteLine($"DIFFERENT-BY-DESIGN  {line}");

        _out.WriteLine($"SAME        : {shared.Length - different.Count - declaredDiverged.Count}");
        _out.WriteLine($"DIFFERENT   : {different.Count} (+{declaredDiverged.Count} declared by design)");
        _out.WriteLine($"NOT-PRESENT : {editorOnly.Length} editor-only, {clusterOnly.Length} cluster-only");

        // ⚪ NOT-PRESENT must be DECLARED.
        var undeclaredEditorOnly  = editorOnly.Except(EditorOnlyKinds, StringComparer.Ordinal).ToArray();
        var undeclaredClusterOnly = clusterOnly.Except(ClusterOnlyKinds, StringComparer.Ordinal).ToArray();

        Assert.True(undeclaredEditorOnly.Length == 0,
            $"panel kind(s) present in the EDITOR and absent in --mode all, and NOT in the known-absent "
          + $"baseline: [{string.Join(", ", undeclaredEditorOnly)}].\n"
          + "⭐ If that is a legitimate not-yet-ported feature, add it to EditorOnlyKinds as a reviewed "
          + "one-line change. ⛔ If it is a panel that BROKE, the baseline is not the place to hide it.");

        Assert.True(undeclaredClusterOnly.Length == 0,
            $"panel kind(s) present in --mode all and absent in the EDITOR, and NOT declared: "
          + $"[{string.Join(", ", undeclaredClusterOnly)}].");

        // 🔴 DIFFERENT is the regression.
        Assert.True(different.Count == 0,
            $"{different.Count} shared panel kind(s) DIFFER between the editor and --mode all:\n  "
          + string.Join("\n  ", different));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The manifest tells the truth about this host — including what it CANNOT confirm.</b>
    /// 📄 <c>Architect_Question_54</c> § Manifest scope.
    ///
    /// <para>⭐ Asserts the shape conformance depends on: every route classified, the perspectives routable,
    /// and the measured matrix agreeing with what the host actually does *(a perspective that reports
    /// <c>time.drive:false</c> must really refuse a step, and one that reports <c>true</c> must really take
    /// it)*.</para>
    ///
    /// <para>⭐⭐⭐ <b><c>hasMaster</c> is now <c>true</c> — <c>HN-028</c> closed.</b> 📌 It was <c>false</c>
    /// while the ack-gate's truth sat in a private field of <c>OrchestratorSubsystem</c>; that subsystem now
    /// exposes the one fact as <c>bool?</c> and the dispatcher reads it live. ⭐ The assertion did not go away
    /// when the gap closed — it INVERTED, so a regression that silently unwires the master reddens here rather
    /// than turning every cluster step back into an unconfirmed one.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_manifest_describes_this_host_truthfully()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-manifest", mode: "all");

        var m = (await cluster.Client.GetCapabilitiesAsync()).EnsureOk().DataOrThrow();

        Assert.Equal("all", m["mode"]!.GetValue<string>());

        // ⭐ Every route classified — an unclassified one means the matrix cannot speak for it.
        var unclassified = (m["unclassifiedRoutes"] as JsonArray)!.Select(n => n!.GetValue<string>()).ToArray();
        Assert.True(unclassified.Length == 0,
            $"route(s) with no capability classification: [{string.Join(", ", unclassified)}]. "
          + "⭐ Add the prefix to CapabilityManifest.CapabilityFor — deliberately, once.");

        var endpoints = (m["endpoints"] as JsonArray)!;
        Assert.True(endpoints.Count >= 40, $"the manifest describes only {endpoints.Count} endpoints");

        var perspectives = (m["host"]!["routablePerspectives"] as JsonArray)!
                           .Select(n => n!.GetValue<string>()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        _out.WriteLine($"routable: [{string.Join(", ", perspectives)}]");
        Assert.Equal(new[] { "ExCon", "IG", "Scenario", "SimHost" }, perspectives);

        // ⭐⭐ HN-028: the ack-gate is confirmable cluster-wide here. ⛔ If this reddens, the dispatcher lost its
        //    live read of OrchestratorSubsystem.IsAwaitingStepAcks and every /sim/step went back to being
        //    issued-but-unconfirmed — silent, and exactly what the gate exists to prevent.
        Assert.True(m["host"]!["hasMaster"]!.GetValue<bool>(),
            "--mode all reports NO master, so a step cannot be confirmed cluster-wide. ⭐ Check the "
          + "`acksPending` lambda in ClusterRunner/Program.cs still resolves OrchestratorSubsystem, and that "
          + "OrchestratorSubsystem.IsAwaitingStepAcks is non-null once Initialize() has run.");

        // ⭐⭐ THE MATRIX AGREES WITH BEHAVIOUR — the half that makes it a measurement and not a claim.
        var matrix = (m["matrix"] as JsonObject)!;
        foreach (var (perspective, row) in matrix)
        {
            bool canDrive = row!["time.drive"]!.GetValue<bool>();

            (await cluster.Client.SwitchPerspectiveAsync(perspective)).EnsureOk();
            var step = await cluster.Client.StepAsync(1);

            _out.WriteLine($"{perspective}: matrix says time.drive={canDrive}, step answered {step.StatusCode}");

            if (canDrive)
                Assert.True(step.Ok,
                    $"the matrix claims '{perspective}' can drive time, but POST /sim/step answered "
                  + $"{step.StatusCode}: {step.Error}");
            else
                Assert.Equal(501, step.StatusCode);
        }
    }

    /// <summary>
    /// ⭐⭐ <b><c>LOCKSTEP</c> (item ⑧) — after a cluster-wide step, the nodes that expose a clock agree on
    /// sim time.</b>
    /// 📄 <c>DESIGN_Headless_Testability.md</c> §6b.
    ///
    /// <para>⭐ Driven through the ACTIVE perspective's own facade, so the step travels the operator's path.
    /// ⛔ No <c>Thread.Sleep</c> as the synchroniser — 📌 §6c names that as the correctness hazard; the read
    /// is taken after the request returns.</para>
    ///
    /// <para>⚠⚠ <b>Only SimHost and CGF are comparable, and that is a MEASURED limit, not a shortcut:</b>
    /// 📐 a node's sim time reaches the API through its <c>ITimeTransportFacade</c>, and only those two build
    /// one *(IG and ExCon report <c>time.drive:false</c>)*. ⇒ ⭐ two of the three roster nodes are checked
    /// here; the third would need IG to expose a facade, which is a feature, not a test fix.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task After_a_cluster_step_the_clocked_nodes_agree_on_sim_time()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-lockstep", mode: "all");

        async Task<double?> SimTimeOfAsync(string perspective)
        {
            (await cluster.Client.SwitchPerspectiveAsync(perspective)).EnsureOk();
            var state = await cluster.Client.GetSimStateAsync();
            var t = state.Field("totalTime");
            return t is null || t.GetValueKind() == System.Text.Json.JsonValueKind.Null
                ? null : t.GetValue<double>();
        }

        // ⭐⭐⭐ PAUSE FIRST. 📐 Measured `2026-08-24`: without this the cluster is FREE-RUNNING, and the two
        //    reads below happen milliseconds apart ⇒ CGF read 2.3507 s and SimHost 2.3988 s — a ~3-tick gap
        //    that is elapsed WALL TIME, not a lockstep violation. ⛔ Comparing clocks on a running cluster
        //    measures the harness's own latency.
        (await cluster.Client.SwitchPerspectiveAsync("Scenario")).EnsureOk();
        (await cluster.Client.PauseAsync()).EnsureOk();
        await Task.Delay(500);                       // let the pause reach the nodes
        (await cluster.Client.StepAsync(2)).EnsureOk();

        var scenario = await SimTimeOfAsync("Scenario");
        var simhost  = await SimTimeOfAsync("SimHost");
        var ig       = await SimTimeOfAsync("IG");

        _out.WriteLine($"simTime — Scenario(CGF)={scenario}, SimHost={simhost}, IG={ig}");

        Assert.NotNull(scenario);
        Assert.NotNull(simhost);

        // ⭐ Tolerance is ONE tick: the observing node can be a drained frame behind the roster (Q54's
        //   second-order point about an OBSERVER's clock).
        Assert.True(Math.Abs(scenario!.Value - simhost!.Value) <= 1.0 / 60.0 + 1e-3,
            $"the clocked nodes disagree on sim time after a cluster step: CGF={scenario}, SimHost={simhost}");

        // ⚠ Documented, not asserted as a defect: IG exposes no clock because it builds no facade.
        Assert.Null(ig);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE PAYOFF OF <c>HN-029</c>: the two hosts are given the SAME scenario and their loaded worlds
    /// are compared FOR REAL.</b> 📄 <c>DESIGN_Headless_Testability.md</c> §Conformance *("load S in BOTH, then
    /// diff")* · <c>MCP_Integration.md</c> § Group U.
    ///
    /// <para>⛔⛔ <b>This sequence was NOT EXECUTABLE before.</b> <c>POST /scenario/load</c> was hardwired to
    /// <c>IEditorLogic</c>, so <c>--mode all</c> answered <c>NOT_SUPPORTED_HERE(editor.authoring)</c> and the
    /// only honest verdict available for world content was *"declared divergent"*. ⇒ ⭐ this rail is the
    /// difference between an exemption and a measurement.</para>
    ///
    /// <para>⭐⭐ <b>What it compares, and why not the raw panel.</b> The inspector's row list is NOT the right
    /// unit: 📐 measured, the IG node also lists a NODE-LOCAL entity *(networkId 0, unnamed)* the editor has no
    /// counterpart for. ⛔ Comparing raw rows would therefore fail forever on a true difference and teach
    /// everyone to ignore it. ⭐ The SCENARIO's content is the set of <b>networked, named</b> entities — the
    /// ones <c>NetworkSpawningSystem</c> replicated from the same file — so those are what is asserted.</para>
    ///
    /// <para>⭐⭐ <b>The ids are compared by a SEPARATE rail, and as of <c>HN-037</c> they MATCH.</b> ⚠ This
    /// paragraph used to say the opposite and it is worth keeping the correction visible: 📐 measured
    /// `2026-08-24`, the two hosts gave <b>editor 1000–1007 vs cluster 2–9</b> because authored ids came from
    /// two allocator INSTANCES with two seeds. §11 unified them onto one authority reset to 1000 at the world
    /// boundary; ⇒ <see cref="The_two_hosts_number_the_same_entities_identically"/> now asserts the equality
    /// this rail once had to exclude.</para>
    ///
    /// <para>⚠ Deliberately NOT an ignore-list entry: an ignored JSON path would hide any regression under it.
    /// ⭐ Selecting the comparable SUBSET states positively what must match.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_two_hosts_hold_the_same_loaded_world()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-world-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-world-all", mode: "all");

        await LoadLiveAsync(editor,  _out);
        await LoadLiveAsync(cluster, _out);

        var inEditor  = await NetworkedEntitiesAsync(editor,  _out);
        var inCluster = await NetworkedEntitiesAsync(cluster, _out);

        _out.WriteLine($"editor  ({inEditor.Count}): {string.Join(", ", inEditor.Select(e => $"{e.Id}:{e.Name}"))}");
        _out.WriteLine($"cluster ({inCluster.Count}): {string.Join(", ", inCluster.Select(e => $"{e.Id}:{e.Name}"))}");

        // ⛔ Anti-vacuity: two empty sets agree perfectly. 📌 The same trap HN-007 set for the panel diff.
        Assert.True(inEditor.Count >= 5,
            $"the editor loaded only {inEditor.Count} networked entities from '{ConformanceScenario}' — too "
          + "few for this comparison to mean anything. ⭐ Check the load actually materialised the scenario.");

        var namesEditor  = inEditor.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var namesCluster = inCluster.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.True(namesEditor.SequenceEqual(namesCluster, StringComparer.Ordinal),
            $"the two hosts loaded '{ConformanceScenario}' and do NOT hold the same networked entities.\n"
          + $"  only in the EDITOR : [{string.Join(", ", namesEditor.Except(namesCluster, StringComparer.Ordinal))}]\n"
          + $"  only in --mode all : [{string.Join(", ", namesCluster.Except(namesEditor, StringComparer.Ordinal))}]\n"
          + "⭐ Both went through the same 2PC TransitionStateIntent{OperatingLive} on the same scenario file, "
          + "so a difference here is a genuine divergence in what the load produced — not a harness artefact.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>HN-037</c> CLOSED — THE ORDERING + PARITY RAIL.</b> 📄
    /// <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11f · handoff item ④.
    ///
    /// <para><b>What it replaces.</b> A tripwire, <c>The_two_hosts_allocate_ids_from_different_authorities</c>,
    /// asserted that the ids DIFFER — pinning a filed gap so it could not change unnoticed, and instructing
    /// its own deletion the day the authorities were unified. 📐 That day was `2026-08-24`: it reddened with
    /// <c>editor ids : [1000..1007] / cluster ids: [1000..1007]</c>, and is deleted here as it asked to be.
    /// ⭐ The gap is not merely un-pinned — it is replaced by the assertion that the gap is CLOSED, which is
    /// the difference between forgetting a finding and finishing it.</para>
    ///
    /// <para>⭐⭐ <b>Two properties, and the first is the one that could silently rot.</b></para>
    /// <list type="number">
    ///   <item><b>ORDERING</b> — the lowest authored id is exactly <c>1000</c> on BOTH hosts. ⚠⚠ This is
    ///   §11f's subtlety and it is a RACE, not an invariant: the cluster's <c>DdsIdAllocator</c> is CHUNKED
    ///   (<c>CHUNK_SIZE = 100</c>), so CGF gets <c>1000–1007</c> only if it pulls the first chunk AFTER the
    ///   world-boundary reset and BEFORE any other node draws one. ⭐ Measured-safe today (during
    ///   <c>LoadingLive</c> only CGF allocates; runtime spawns wait for <c>OperatingLive</c>) — ⛔ but "safe
    ///   today" is exactly the kind of claim that decays, and an out-of-order chunk pull would show up here
    ///   as <c>1100</c>, silently.</item>
    ///   <item><b>PARITY</b> — the two hosts produce the SAME id set for the same scenario. ⭐ A network id
    ///   is a portable name again: what is recorded on one host resolves on another.</item>
    /// </list>
    ///
    /// <para>⛔ <b>Deliberately asserted against <c>1000</c> as a literal, not against
    /// "whatever the editor produced".</b> Comparing the hosts to each other alone would stay green if BOTH
    /// drifted to 1100 — the reproducible block and the cross-host parity are two claims, and §11b derives
    /// them from one number precisely so both can be checked.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_two_hosts_number_the_same_entities_identically()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-ids-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-ids-all", mode: "all");

        await LoadLiveAsync(editor,  _out);
        await LoadLiveAsync(cluster, _out);

        var idsEditor  = (await NetworkedEntitiesAsync(editor,  _out)).Select(e => e.Id).OrderBy(i => i).ToArray();
        var idsCluster = (await NetworkedEntitiesAsync(cluster, _out)).Select(e => e.Id).OrderBy(i => i).ToArray();

        _out.WriteLine($"editor ids : [{string.Join(", ", idsEditor)}]");
        _out.WriteLine($"cluster ids: [{string.Join(", ", idsCluster)}]");

        // ⛔ Anti-vacuity: two empty sets are identical and start at nothing.
        Assert.True(idsEditor.Length >= 5,
            $"the editor produced only {idsEditor.Length} networked ids — too few for this to mean anything.");
        Assert.True(idsCluster.Length >= 5,
            $"--mode all produced only {idsCluster.Length} networked ids — too few for this to mean anything.");

        // ⭐ Each host must still be self-consistent.
        Assert.Equal(idsEditor.Length,  idsEditor.Distinct().Count());
        Assert.Equal(idsCluster.Length, idsCluster.Distinct().Count());

        Assert.True(idsEditor[0] == WorldIdBase,
            $"the editor's lowest authored id is {idsEditor[0]}, not {WorldIdBase}. ⭐ The world-boundary "
          + "reset either did not fire or did not reach the allocator the load handlers use "
          + "(ClusterMaster.ResetIdAuthorityIfWorldBoundary -> WorldIdAuthority.FromAllocator).");

        Assert.True(idsCluster[0] == WorldIdBase,
            $"--mode all's lowest authored id is {idsCluster[0]}, not {WorldIdBase}. ⚠ If it is {WorldIdBase + 100} "
          + "or higher, THIS IS THE CHUNK-ORDERING RACE (§11f): some node drew a chunk from the DDS master "
          + "between the world-boundary Req_Reset and CGF's first authored allocation. ⛔ That is a real "
          + "ordering defect, not a flaky test — fix the ordering, do not relax this bound.");

        Assert.True(idsEditor.SequenceEqual(idsCluster),
            "the editor and --mode all number the same scenario DIFFERENTLY — HN-037 has regressed.\n"
          + $"  editor : [{string.Join(", ", idsEditor)}]\n"
          + $"  cluster: [{string.Join(", ", idsCluster)}]\n"
          + "⭐ Both hosts reset the ONE id authority to 1000 at the world boundary, so the same scenario "
          + "must number identically. A difference means one host is allocating from something else.");
    }

    /// <summary>⭐ The first id every world hands out. Mirrors <c>WorldIdAuthority.WorldBase</c>, restated
    /// here as a literal on purpose: a rail that imported the production constant would follow it if someone
    /// changed it, and the whole point is that this number is a promise to the user.</summary>
    private const long WorldIdBase = 1000;

    /// <summary>
    /// ⭐ The SCENARIO-derived entities from the entity-inspector panel model.
    /// <para>⛔ Filters out node-local rows *(networkId 0 or no name)*: those are per-host by construction and
    /// are exactly what makes the raw row list incomparable.</para>
    /// </summary>
    private static async Task<List<(long Id, string Name)>> NetworkedEntitiesAsync(
        EditorProcess host, ITestOutputHelper output)
    {
        var byKind = await CaptureByKindAsync(host, output);

        Assert.True(byKind.TryGetValue("entity-inspector", out var inspector),
            $"[{host.Mode}] published no entity-inspector panel, so there is no world to compare. "
          + $"Kinds seen: [{string.Join(", ", byKind.Keys.OrderBy(k => k, StringComparer.Ordinal))}]");

        var model = JsonNode.Parse(inspector.Model);
        var rows  = model?["entities"] as JsonArray;
        Assert.NotNull(rows);

        var list = new List<(long, string)>();
        foreach (var row in rows!)
        {
            long id    = row?["networkId"]?.GetValue<long>() ?? 0;
            string? nm = row?["name"]?.GetValueKind() == System.Text.Json.JsonValueKind.String
                         ? row!["name"]!.GetValue<string>() : null;
            if (id == 0 || string.IsNullOrEmpty(nm)) continue;   // node-local, not scenario content
            list.Add((id, nm!));
        }
        return list;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A cluster step is CONFIRMED, not merely issued — <c>HN-028</c>'s postcondition.</b>
    /// 📄 <c>Architect_Question_54</c> § AS-BUILT *(deviation ①: the gate lives in the HTTP handler, because
    /// the ACK drain and <c>Step()</c> are both main-thread and gating inside <c>Step()</c> deadlocks)*.
    ///
    /// <para>⭐⭐ The promise <c>POST /sim/step</c> makes in <c>--mode all</c> is that when it answers, the tick
    /// has been acknowledged by every roster node. ⇒ the checkable postcondition is
    /// <c>isAwaitingStepAcks == false</c> AT THE MOMENT THE RESPONSE LANDS, together with a master being present
    /// to have answered at all.</para>
    ///
    /// <para>⚠⚠ <b>Stated honestly — this rail is a POSTCONDITION, not a proof the wait happened.</b> An
    /// un-wired gate can also read <c>false</c> here, by racing ahead of the intent instead of behind the ACKs.
    /// ⭐ What separates the two is the MUTATION recorded in the report: pinning the master to
    /// "always awaiting" must turn this step into a <c>504</c>. ⛔ A green here alone would not have earned the
    /// claim, and the report says so rather than letting the rail imply more than it measures.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task A_cluster_step_is_ack_confirmed_before_it_answers()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-ackgate", mode: "all");

        // ⭐ A master must exist, or "confirmed" is not a question this host can answer (HN-028's whole point).
        var manifest = (await cluster.Client.GetCapabilitiesAsync()).EnsureOk().DataOrThrow();
        Assert.True(manifest["host"]!["hasMaster"]!.GetValue<bool>(),
            "no master on this host — a step here is issued-but-unconfirmed, so the postcondition below is "
          + "vacuous. ⭐ Fix the `acksPending` wiring before reading anything into this rail.");

        (await cluster.Client.SwitchPerspectiveAsync("SimHost")).EnsureOk();
        (await cluster.Client.PauseAsync()).EnsureOk();
        await Task.Delay(500);                        // let the pause reach the roster

        var before = await cluster.Client.GetSimStateAsync();
        double t0  = before.Field("totalTime")!.GetValue<double>();

        var step = await cluster.Client.StepAsync(1);
        step.EnsureOk();

        var after = await cluster.Client.GetSimStateAsync();
        double t1 = after.Field("totalTime")!.GetValue<double>();
        bool awaiting = after.Field("isAwaitingStepAcks")!.GetValue<bool>();

        _out.WriteLine($"step: t={t0} -> {t1}, awaitingAcks after the answer = {awaiting}");

        Assert.False(awaiting,
            "POST /sim/step answered while the master was STILL awaiting step ACKs — the tick had not landed "
          + "on every roster node, so the gate either drained the wrong thing or was bypassed.");

        // ⭐ And the step actually moved the clock — a gate over a no-op would satisfy the line above.
        Assert.True(t1 > t0,
            $"the cluster step was confirmed but sim time did not advance ({t0} -> {t1}) — the gate is "
          + "watching a step that never executed.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE CONTROL ON THE EXEMPTION — a declared divergence that stopped diverging must be DELETED.</b>
    /// ⛔⛔ Without this, <see cref="DivergesByDesign"/> is an ignore-list that only grows, which is how a
    /// conformance suite stops meaning anything. 📌 The same inversion <c>N1</c> used for its volatile kinds
    /// and <c>N3</c> for the golden ignore-list — and in both cases the control caught its own author.
    /// </summary>
    [SystemSmokeFact]
    public async Task A_declared_divergence_that_stopped_diverging_is_deleted()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-ctl-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-ctl-all", mode: "all");

        var a = await CaptureByKindAsync(editor,  _out);
        var b = await CaptureByKindAsync(cluster, _out);

        var stillAgreeing = DivergesByDesign.Keys
            .Where(k => a.ContainsKey(k) && b.ContainsKey(k)
                     && string.Equals(a[k].Model, b[k].Model, StringComparison.Ordinal))
            .ToArray();

        Assert.True(stillAgreeing.Length == 0,
            $"declared-by-design divergence(s) [{string.Join(", ", stillAgreeing)}] now AGREE between the "
          + "two modes. \u2b50 That is good news: delete the entry from DivergesByDesign so the kind is "
          + "genuinely compared from now on.");
    }

    // \u2550\u2550 cgf==editor SLICE 1 \u2014 the acceptance rails \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550
    // \ud83d\udcc4 docs/DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md \u00a76.

    /// <summary>
    /// \u2b50\u2b50\u2b50 <b>THE HEADLINE \u2014 the three named panels of slice 1 are <c>SAME</c> per <c>PanelKind</c>,
    /// editor vs <c>--mode all</c>.</b>
    /// \ud83d\udcc4 <c>DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md</c> \u00a76 *("the same panels, same content,
    /// on CGF")*.
    ///
    /// <para>\u2b50\u2b50 <b>Named separately from <see cref="The_two_modes_agree_on_every_shared_panel_kind"/>
    /// on purpose.</b> That rail asks *"has anything regressed anywhere"*; \u26d4 it would also pass if these
    /// three kinds were quietly moved into <see cref="DivergesByDesign"/>. \u21d2 \u2b50 this one names the slice's
    /// OWN deliverable, so narrowing the diff cannot make it green.</para>
    ///
    /// <para>\u26a0 <b>Shown RED by reverting the registration:</b> \ud83d\udcd0 measured `2026-08-25` \u2014 with
    /// <c>CgfSubsystem.BuildAiShell</c> removed the cluster publishes none of these kinds and every
    /// assertion below fails on the "did not publish" arm, not on a model mismatch.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_asset_panels_are_the_same_on_both_hosts()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-slice1-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-slice1-all", mode: "all");

        var a = await CaptureByKindAsync(editor,  _out);
        var b = await CaptureByKindAsync(cluster, _out);

        // \u2b50 The design's own three, verbatim: the watch -> MyBlueprint -> asset-graph chain.
        string[] slice1Kinds = { "graph-canvas", "my-blueprint", "watch" };

        var missingOnEditor  = slice1Kinds.Where(k => !a.ContainsKey(k)).ToArray();
        var missingOnCluster = slice1Kinds.Where(k => !b.ContainsKey(k)).ToArray();

        // \u26d4 Anti-vacuity in BOTH directions: a kind absent from the editor makes the comparison
        //    meaningless, and one absent from the cluster is the slice simply not being wired.
        Assert.True(missingOnEditor.Length == 0,
            $"the EDITOR did not publish [{string.Join(", ", missingOnEditor)}] \u2014 the reference side of "
          + "this comparison is missing, so a green here would prove nothing.");

        Assert.True(missingOnCluster.Length == 0,
            $"--mode all did not publish [{string.Join(", ", missingOnCluster)}]. \u2b50 That is slice 1's "
          + "deliverable: CgfSubsystem.BuildAiShell must construct the AiShared shell and register the "
          + "asset-perspective windows.");

        var differing = new List<string>();
        foreach (var kind in slice1Kinds)
        {
            if (string.Equals(a[kind].Model, b[kind].Model, StringComparison.Ordinal)) continue;
            var diffs = PanelNormalizer.Diff(JsonNode.Parse(a[kind].Model), JsonNode.Parse(b[kind].Model));
            differing.Add($"{kind} ({a[kind].Id} vs {b[kind].Id}): {string.Join(" | ", diffs.Take(4))}");
        }

        _out.WriteLine($"slice 1 kinds SAME: {slice1Kinds.Length - differing.Count}/{slice1Kinds.Length}");

        Assert.True(differing.Count == 0,
            "slice 1's own panels DIFFER between the editor and --mode all:\n  "
          + string.Join("\n  ", differing)
          + "\n\u26d4 Do NOT declare these in DivergesByDesign \u2014 they are the slice's acceptance "
          + "criterion, and an exemption here would be the narrowing the design forbids.");
    }

    /// <summary>
    /// \u2b50\u2b50 <b>The asset perspectives are REACHABLE on <c>--mode all</c>.</b>
    /// \ud83d\udcc4 <c>DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md</c> \u00a76 *(second rail)* \u00b7 \u00a74's
    /// <c>sequenceDiagram</c> note *("GetPerspectives now derives Scenario, BTree, HSM, Blueprint")</para>
    ///
    /// <para>\u2b50\u2b50\u2b50 <b>Perspectives are EMERGENT from registration</b>, so this rail is the direct
    /// observation of the sequence diagram's claim \u2014 \u26d4 there is no list anywhere to assert against.
    /// \u26a0 It also pins the half <see cref="The_manifest_describes_this_host_truthfully"/> does NOT cover:
    /// <c>routablePerspectives</c> comes from the per-subsystem debug PROVIDERS and is deliberately
    /// unchanged by this slice *(still ExCon/IG/Scenario/SimHost)*, while <c>GET /perspectives</c> comes
    /// from the WINDOW MANAGER and grows. \ud83d\udccc Two different questions that a reader will otherwise
    /// conflate.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_cluster_offers_the_asset_perspectives()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-persp-all", mode: "all");

        var perspectives = ((await cluster.Client.ListPerspectivesAsync()).EnsureOk()
                            .Field("perspectives") as JsonArray)!
                           .Select(n => n!.GetValue<string>()).ToArray();

        _out.WriteLine($"[all] perspectives: [{string.Join(", ", perspectives)}]");

        foreach (var expected in new[] { "BTree", "HSM", "Blueprint" })
            Assert.True(perspectives.Contains(expected, StringComparer.Ordinal),
                $"--mode all does not offer the '{expected}' perspective. \u2b50 It is EMERGENT from window "
              + "registration, so its absence means CgfSubsystem registered no window owning it.");

        // \u2b50 And the node's own perspective is still there \u2014 \u26d4 the slice ADDS, it does not displace.
        Assert.Contains("Scenario", perspectives);

        // \u2b50\u2b50 Each asset perspective must actually be SWITCHABLE, not merely listed: a claimed
        //    perspective nothing can activate would let the capture loop above skip it silently
        //    (`if (!switched.Ok) continue;`) and this whole slice would read as green with no panels.
        foreach (var p in new[] { "BTree", "HSM", "Blueprint" })
        {
            var switched = await cluster.Client.SwitchPerspectiveAsync(p);
            Assert.True(switched.Ok, $"--mode all refused to switch to '{p}': {switched.Error}");
        }
    }

    /// <summary>
    /// \u2b50\u2b50 <b>The known-absent baseline SHRANK by exactly the kinds slice 1 ported \u2014 and by nothing
    /// else.</b> \ud83d\udcc4 \u00a76 *(third rail)* \u00b7 <c>Architect_Question_54</c> \u00a7 *"a genuine port is a reviewed
    /// one-line deletion from this list"</para>
    ///
    /// <para>\u26d4\u26d4 <b>This is the control on the DELETION, and it runs in the opposite direction to
    /// <see cref="A_declared_divergence_that_stopped_diverging_is_deleted"/>:</b> that one catches an
    /// exemption kept too long, this one catches a kind deleted from the baseline WITHOUT the cluster
    /// actually publishing it \u2014 which would turn the three-way diff's "undeclared editor-only" assertion
    /// into the thing that reddens, in a batch that has nothing to do with this slice.</para>
    ///
    /// <para>\u26a0 <b>Cheap and exact:</b> it asserts the cluster publishes every kind this slice claims to
    /// have ported. \u2b50 It does NOT re-assert the models \u2014 that is the headline rail's job.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_ported_kinds_are_really_published_by_the_cluster()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-ported-all", mode: "all");

        var b = await CaptureByKindAsync(cluster, _out);

        // \u2b50 The nine authoring kinds deleted from EditorOnlyKinds by slice 1, plus `bookmarks`.
        string[] ported =
        {
            "blackboard-authoring", "my-blueprint", "variables", "watch", "ai-breakpoints",
            "graph-canvas", "details", "runtime-inspector", "diagnostics", "bookmarks",
        };

        var absent = ported.Where(k => !b.ContainsKey(k)).ToArray();

        _out.WriteLine($"cluster kinds: {b.Count} \u2014 [{string.Join(", ", b.Keys.OrderBy(k => k, StringComparer.Ordinal))}]");

        Assert.True(absent.Length == 0,
            $"kind(s) [{string.Join(", ", absent)}] were removed from the known-absent baseline but "
          + "--mode all does not publish them. \u26d4 The baseline is not the place to record an "
          + "intention: either register the window on CGF, or put the entry back with its reason.");

        // \u26d4 And the ones slice 1 did NOT port must still be absent \u2014 otherwise the baseline is stale in
        //   the other direction and a real regression could hide behind a stale entry.
        foreach (var stillEditorOnly in new[] { "graph-signature", "entity-blueprints", "data-breakpoint-manager" })
            Assert.False(b.ContainsKey(stillEditorOnly),
                $"--mode all now publishes '{stillEditorOnly}', which EditorOnlyKinds still declares "
              + "editor-only. \u2b50 Delete the entry \u2014 a stale exemption hides real regressions.");
    }

    // \u2550\u2550 cgf==editor SLICE 2 \u2014 a POPULATED asset \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550
    // \ud83d\udcc4 docs/DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md \u00a79.

    /// <summary>\u2b50 The asset kinds slice 2's open path is asserted against, in preference order.</summary>
    private static readonly string[] PreferredOpenKinds = { "Blueprint", "BTree", "Hsm" };

    /// <summary>
    /// \u2b50\u2b50 Pick the SAME asset on both hosts \u2014 by <c>sourceFilePath</c>, the address \u00a73a calls the human
    /// key. \u26d4 Never by index into <c>GET /assets</c>: the two hosts' catalogs are built by the same
    /// contributors but nothing promises the same ORDER, and comparing two different assets would be a
    /// green that means nothing.
    /// </summary>
    private static async Task<(string AssetId, string Path, string Kind)?> PickSharedAssetAsync(
        EditorProcess a, EditorProcess b, ITestOutputHelper output)
    {
        static async Task<Dictionary<string, (string Id, string Kind)>> IndexAsync(EditorProcess h)
        {
            var r = await h.Client.ListAssetsAsync();
            if (!r.Ok) return new Dictionary<string, (string, string)>(StringComparer.Ordinal);

            var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (var n in (r.Field("assets") as JsonArray) ?? new JsonArray())
            {
                var path = n!["sourceFilePath"]!.GetValue<string>();
                map[path] = (n["assetId"]!.GetValue<string>(), n["kind"]!.GetValue<string>());
            }
            return map;
        }

        var ia = await IndexAsync(a);
        var ib = await IndexAsync(b);

        output.WriteLine($"[{a.Mode}] assets: {ia.Count}   [{b.Mode}] assets: {ib.Count}");

        // ⭐⭐ NAME the difference rather than leaving two counts to be reasoned about. 📌 A count gap is
        //    exactly the kind of thing a report guesses at ("probably the scenario contributor") — ⛔ this
        //    prints the actual paths, so the next reader measures instead of inferring.
        foreach (var only in ia.Keys.Except(ib.Keys, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            output.WriteLine($"  only in {a.Mode}: {only}  (kind {ia[only].Kind})");
        foreach (var only in ib.Keys.Except(ia.Keys, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            output.WriteLine($"  only in {b.Mode}: {only}  (kind {ib[only].Kind})");

        var shared = ia.Keys.Intersect(ib.Keys, StringComparer.Ordinal)
                       .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (shared.Length == 0) return null;

        // \u2b50 Prefer a Blueprint: it is the kind whose Details/outline carry the most structure, so a
        //   SAME verdict on it is the strongest available. \u26a0 Falls back rather than skipping.
        foreach (var kind in PreferredOpenKinds)
        {
            var hit = shared.FirstOrDefault(p => string.Equals(ia[p].Kind, kind, StringComparison.Ordinal));
            if (hit != null) return (ia[hit].Id, hit, ia[hit].Kind);
        }

        var any = shared[0];
        return (ia[any].Id, any, ia[any].Kind);
    }

    /// <summary>
    /// \u2b50\u2b50\u2b50 <b>SLICE 2'S HEADLINE \u2014 the same asset, OPENED on both hosts, and the panels agree.</b>
    /// \ud83d\udcc4 \u00a79 *("assert graph + MyBlueprint + Details are SAME as the editor \u2014 NOT empty state")*.
    ///
    /// <para>\u26d4\u26d4 <b>Why slice 1's green was not enough, stated plainly.</b> \ud83d\udcd0 Slice 1 compared those
    /// same three kinds and passed \u2014 but with **no asset open on either host**, so it compared two EMPTY
    /// panels. \u26a0 Two empty panels agree perfectly. \u21d2 \u2b50 this rail is the first one whose SAME verdict is
    /// about CONTENT.</para>
    ///
    /// <para>\u2b50\u2b50 <b>The anti-vacuity guard is the whole rail</b>: it asserts the opened panels are
    /// non-empty BEFORE comparing them, so a regression that stops opening assets reddens here rather
    /// than quietly returning to comparing two empty models.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_same_opened_asset_looks_the_same_on_both_hosts()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-open-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-open-all", mode: "all");

        var pick = await PickSharedAssetAsync(editor, cluster, _out);

        Assert.True(pick != null,
            "the two hosts share NO indexed asset by sourceFilePath. \u2b50 That is slice 2's deliverable: "
          + "CgfSubsystem.BuildAssetCatalog must index the same asset tree the editor does. \u26d4 If both "
          + "catalogs are empty the source tree was not found \u2014 check the host log for the resolution "
          + "warning (ruling 67).");

        var (assetId, path, kind) = pick!.Value;
        _out.WriteLine($"opening {kind} '{path}' ({assetId}) on both hosts");

        foreach (var host in new[] { editor, cluster })
        {
            var opened = await host.Client.OpenAssetAndSettleAsync(assetId);
            Assert.True(opened.Ok, $"[{host.Mode}] could not open {assetId}: {opened.Error}");

            // \u2b50 The tab really is the active one \u2014 \u26d4 otherwise the canvas draws something else and the
            //   comparison below is about a different graph.
            var docs = (await host.Client.ListDocumentsAsync()).EnsureOk();
            Assert.Equal(assetId, docs.String("activeAssetId"));
        }

        var a = await CaptureByKindAsync(editor,  _out);
        var b = await CaptureByKindAsync(cluster, _out);

        // \u2b50\u2b50 The two panels whose CONTENT this slice delivers \u2014 compared whole, with no exemption.
        string[] kinds = { "graph-canvas", "my-blueprint" };

        foreach (var k in kinds.Concat(new[] { "details" }))
        {
            Assert.True(a.ContainsKey(k), $"the EDITOR did not publish '{k}' \u2014 the reference side is missing.");
            Assert.True(b.ContainsKey(k), $"--mode all did not publish '{k}'.");
        }

        // \u26d4\u26d4 ANTI-VACUITY \u2014 the point of the whole rail. A graph-canvas still reporting
        //    hasActiveDocument:false means nothing was opened, and the SAME verdict below would be the
        //    empty-vs-empty green slice 1 already had.
        foreach (var host in new[] { ("editor", a), ("cluster", b) })
        {
            var canvas = JsonNode.Parse(host.Item2["graph-canvas"].Model)!;
            Assert.True(canvas["hasActiveDocument"]?.GetValue<bool>() == true,
                $"[{host.Item1}] graph-canvas reports NO active document after opening {assetId} \u2014 this "
              + "rail would then be comparing two empty panels, which is exactly what it exists to stop.");
        }

        var differing = new List<string>();
        foreach (var k in kinds)
        {
            if (string.Equals(a[k].Model, b[k].Model, StringComparison.Ordinal)) continue;
            var diffs = PanelNormalizer.Diff(JsonNode.Parse(a[k].Model), JsonNode.Parse(b[k].Model));
            differing.Add($"{k} ({a[k].Id} vs {b[k].Id}): {string.Join(" | ", diffs.Take(4))}");
        }

        _out.WriteLine($"populated SAME: {kinds.Length - differing.Count}/{kinds.Length}");

        Assert.True(differing.Count == 0,
            $"with '{path}' OPEN on both hosts, panel(s) DIFFER:\n  " + string.Join("\n  ", differing)
          + "\n\u26d4 This is content, not empty state \u2014 do not exempt it.");

        // \u2b50\u2b50\u2b50 DETAILS \u2014 asserted for what this slice can honestly claim about it, and NOT more.
        //
        // \u26d4\u26d4 Its WHOLE-MODEL verdict is already a DECLARED divergence (`DivergesByDesign["details"]`),
        //    for two roots that both PRE-DATE slice 2 and neither of which slice 2 touches:
        //      \u00b7 `$.mode` Paused vs Running \u2014 the hosts' clocks genuinely differ (CE-003);
        //      \u00b7 `$.offeredViewIds` 3 vs 1 \u2014 `details.runtime.Blueprint` needs an IBlueprintDebugSession
        //        and CGF constructs none (CE-004/CE-007).
        // \u26d4 Re-asserting the whole model here would just restate that declaration; \u26d4 and comparing it
        //    with those fields filtered out would be the narrowing the design forbids.
        // \u2b50\u2b50 So this asserts the STRONGER, NEW thing instead: after slice 2 the Details panel is about
        //    THE OPENED ASSET on both hosts. \ud83d\udccc Before this batch it read `assetId: null` and
        //    "No document is open." on the cluster \u2014 that is the regression this line catches.
        foreach (var (label, cap) in new[] { ("editor", a), ("cluster", b) })
        {
            var det = JsonNode.Parse(cap["details"].Model)!;

            Assert.Equal(assetId, det["assetId"]?.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(det["assetName"]?.GetValue<string>()),
                $"[{label}] Details published no assetName for the opened asset.");
            var empty = det["emptyState"];
            Assert.True(empty is null || empty.GetValueKind() == System.Text.Json.JsonValueKind.Null,
                $"[{label}] Details is in an EMPTY state ('{empty}') while {path} is open.");
        }
    }

    /// <summary>
    /// \u2b50\u2b50 <b>The four drive verbs, end to end on the cluster</b> \u2014 discover \u00b7 open by PATH \u00b7 list tabs \u00b7
    /// activate. \ud83d\udcc4 \u00a79 *(second rail)* \u00b7 \u00a73a *(addressing)*.
    ///
    /// <para>\u2b50 Deliberately exercises the <b>path</b> form, not the Guid form the headline rail uses:
    /// \u26d4 the two addresses resolve through different code, and only one of them was covered otherwise.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_cluster_can_discover_open_and_switch_graph_tabs()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-drive-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;

        Assert.True(list.Count > 0,
            "GET /assets on --mode all returned NOTHING. \u2b50 Slice 2 populates CGF's AssetCatalog; an "
          + "empty list means the source asset tree was not found (see the host log \u2014 ruling 67).");

        // \u2b50 Every entry carries BOTH addresses \u2014 \u00a73a's contract, asserted rather than assumed.
        foreach (var n in list)
        {
            Assert.True(Guid.TryParse(n!["assetId"]!.GetValue<string>(), out _),
                "an asset's id is not a GUID \u2014 the URL-segment address must stay URL-safe.");
            Assert.False(string.IsNullOrWhiteSpace(n["sourceFilePath"]?.GetValue<string>()),
                $"asset '{n["name"]}' has no sourceFilePath \u2014 the HUMAN address is missing, so "
              + "open_asset_by_path cannot reach it.");
        }

        // \u2b50\u2b50 SUBFOLDERS \u2014 \u00a73a requires recursive indexing. \u26a0 Reported rather than asserted when the
        //    tree happens to be flat: a flat repo is not a defect, but a recursive one that indexed
        //    only the top level IS, and this line is what would show it.
        var subfoldered = list.Select(n => n!["sourceFilePath"]!.GetValue<string>())
                              .Where(p => p.Count(c => c == '/') >= 2).ToArray();
        _out.WriteLine($"assets: {list.Count}, of which {subfoldered.Length} live below the kind folder");

        var first = list[0]!;
        var path  = first["sourceFilePath"]!.GetValue<string>();
        var id    = first["assetId"]!.GetValue<string>();

        // \u2b50 Open by PATH \u2014 the body form.
        var opened = (await cluster.Client.OpenAssetByPathAsync(path)).EnsureOk();
        Assert.Equal(id, opened.String("assetId"));

        await cluster.Client.StepAsync(SettleTicks);

        var docs = (await cluster.Client.ListDocumentsAsync()).EnsureOk();
        Assert.Equal(id, docs.String("activeAssetId"));
        Assert.True((docs.Field("documents") as JsonArray)!.Count >= 1);

        // \u2b50\u2b50 Open a SECOND asset, then switch BACK \u2014 \u26d4 activating the only open tab would pass
        //    trivially and prove nothing about the switch.
        if (list.Count > 1)
        {
            var secondId = list[1]!["assetId"]!.GetValue<string>();
            (await cluster.Client.OpenAssetAsync(secondId)).EnsureOk();
            await cluster.Client.StepAsync(SettleTicks);

            Assert.Equal(secondId,
                (await cluster.Client.ListDocumentsAsync()).EnsureOk().String("activeAssetId"));

            (await cluster.Client.ActivateDocumentAsync(id)).EnsureOk();
            await cluster.Client.StepAsync(SettleTicks);

            Assert.Equal(id,
                (await cluster.Client.ListDocumentsAsync()).EnsureOk().String("activeAssetId"));
        }
        else
        {
            _out.WriteLine("only one asset indexed \u2014 the tab-SWITCH half of this rail did not run");
        }

        // \u2b50 And an unknown id is a typed refusal, \u26d4 not a 500 and not a silent no-op.
        var bogus = await cluster.Client.ActivateDocumentAsync(Guid.NewGuid().ToString());
        Assert.Equal(404, bogus.StatusCode);
    }

    /// <summary>
    /// \u2b50\u2b50 <b>The MAIN TOOLBAR is readable, on both hosts.</b> \ud83d\udcc4 \u00a76 item \u2464 \u00b7 \u00a77 *(the standing reminder
    /// that every later feature's toolbar affordance must be present and SAME on CGF)*.
    ///
    /// <para>\u26d4 Before slice 2 <c>MainToolbarManager</c> published nothing \u2014 its entries render through
    /// opaque delegates \u2014 so *"does this host offer that button?"* was unanswerable headlessly.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_main_toolbar_is_readable_on_both_hosts()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-toolbar-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-toolbar-all", mode: "all");

        var a = await CaptureByKindAsync(editor,  _out);
        var b = await CaptureByKindAsync(cluster, _out);

        Assert.True(a.ContainsKey("main-toolbar"),
            "the EDITOR does not publish the 'main-toolbar' kind \u2014 MainToolbarManager.RenderEntries "
          + "must call PublishSnapshot.");
        Assert.True(b.ContainsKey("main-toolbar"),
            "--mode all does not publish the 'main-toolbar' kind.");

        var editorEntries  = (JsonNode.Parse(a["main-toolbar"].Model)!["entries"] as JsonArray)!;
        var clusterEntries = (JsonNode.Parse(b["main-toolbar"].Model)!["entries"] as JsonArray)!;

        _out.WriteLine($"toolbar entries \u2014 editor: {editorEntries.Count}, cluster: {clusterEntries.Count}");

        // \u26d4 Anti-vacuity on the REFERENCE side: a toolbar model with no entries anywhere would make
        //   this rail vacuous. \u26a0 The CLUSTER's count is deliberately NOT asserted \u2014 see below.
        Assert.True(editorEntries.Count > 0,
            "the EDITOR published a main toolbar with ZERO entries \u2014 the reference side is empty, so "
          + "this rail would prove nothing.");

        // \u2b50 Every item carries the id a later slice will assert its affordance by (\u00a77).
        foreach (var e in editorEntries.Concat(clusterEntries))
            Assert.False(string.IsNullOrWhiteSpace(e!["id"]?.GetValue<string>()),
                "a toolbar item published with no id \u2014 the id is what \u00a77 asserts an affordance by.");

        // \u2b50\u2b50\u2b50 \u00a77 DISCHARGED, FOR THE FIRST TIME \u2014 `2026-08-25`, slice 3.
        //
        // \u26a0\u26a0 THIS ASSERTION USED TO READ `clusterEntries.Count == 0`, and that was CORRECT at the time:
        //    slice 2 measured `EditorSubsystem` as the ONLY caller of MainToolbar.RegisterEntry, so
        //    CGF's toolbar was legitimately empty, and the rail was written to REDDEN the day CGF
        //    registered its first entry. \u2b50\u2b50 It did exactly that \u2014 slice 3 added save + reload \u2014 which
        //    is the hand-off design \u00a77 was written to produce, working as intended.
        //
        // \u2b50 \u00a77's standing rule: *"a feature CONTROLLED FROM THE TOOLBAR must be wired AND instrumented
        //   on CGF too\u2026 every feature slice's acceptance must include 'its toolbar affordance is
        //   present and SAME on CGF'."* \u21d2 this now asserts the AFFORDANCES BY ID.
        var clusterIds = clusterEntries.Select(e => e!["id"]!.GetValue<string>()).ToArray();
        _out.WriteLine($"cluster toolbar ids: [{string.Join(", ", clusterIds)}]");

        foreach (var required in new[] { "SaveAllAiDocuments", "QuickReloadAiAsset" })
            Assert.True(clusterIds.Contains(required, StringComparer.Ordinal),
                $"--mode all's main toolbar does not offer '{required}'. \u2b50 Slice 3 wires save + hot "
              + "reload on CGF, and design \u00a77 requires a toolbar-controlled feature to be wired AND "
              + "instrumented here \u2014 not just its underlying command.");

        // \u26d4 And they must be VISIBLE, not registered-but-filtered-away: an entry bound to a
        //   perspective CGF never shows would satisfy the id check and offer the operator nothing.
        foreach (var e in clusterEntries)
        {
            var id = e!["id"]!.GetValue<string>();
            if (id is not ("SaveAllAiDocuments" or "QuickReloadAiAsset")) continue;
            Assert.True(e["visible"]!.GetValue<bool>(),
                $"toolbar entry '{id}' is registered on --mode all but NOT visible in the active "
              + "perspective \u2014 the affordance exists in the table and not on screen.");
        }
    }

    // ══ cgf==editor SLICE 3 — editing, save and hot reload ════════════════════
    // 📄 docs/DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md §7.

    /// <summary>
    /// ⭐⭐⭐ <b>SLICE 3'S HEADLINE — the cluster can SAVE and RELOAD an open asset over MCP.</b>
    /// 📄 §7 *("POST /assets/{id}/save persists; POST /assets/{id}/reload hot-applies")*.
    ///
    /// <para>⭐⭐ <b>It asserts the CYCLE, on the CLUSTER</b> — open, save, reload, and read the
    /// compiler's own verdict back. ⛔ A 200 alone would prove nothing: the route answers 200 for a
    /// FAILED compile too *(a failed compile is a legitimate outcome of editing, not an HTTP error)*,
    /// so the rail reads <c>status</c>.</para>
    ///
    /// <para>⚠⚠ <b>What it deliberately does NOT assert: that a SOFT reload keeps state and a HARD one
    /// resets it.</b> 📐 Measured `2026-08-25`: <c>QuickReloadResult</c> carries only
    /// <c>Succeeded</c>/<c>ErrorMessage</c>/<c>DurationMs</c> — ⛔ <b>no Cosmetic/Soft/Hard
    /// classification at all</b> — and <c>AiHotReloadCoordinator.OnHardReloadCompleted</c> is
    /// documented <i>"NOT fired for Quick Reloads"</i>. ⇒ that distinction lives on the ALC
    /// file-watcher path, which this slice does not wire. ⭐ Filed as <c>CE-023</c>; ⛔ asserting it
    /// here would be asserting a fact the code cannot produce.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_cluster_can_save_and_reload_an_open_asset()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-reload-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;

        Assert.True(list.Count > 0,
            "GET /assets returned nothing on --mode all - slice 2's catalog population regressed, so "
          + "there is no asset to save or reload.");

        // ⭐ A Blueprint: its reload path compiles the in-memory asset directly, so a success verdict
        //   says the most. ⚠ Falls back rather than skipping.
        var pick = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "Blueprint") ?? list[0];
        var id   = pick!["assetId"]!.GetValue<string>();
        var name = pick["name"]!.GetValue<string>();
        _out.WriteLine($"save/reload target: {name} ({pick["kind"]}) {id}");

        // ⛔ Save and reload act on OPEN documents, not on files - assert that refusal FIRST, because
        //   it is the contract that makes the id meaningful rather than decorative.
        var beforeOpen = await cluster.Client.ReloadAssetAsync(id);
        Assert.Equal(404, beforeOpen.StatusCode);

        (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

        var saved = (await cluster.Client.SaveAssetAsync(id)).EnsureOk();
        Assert.Equal(id, saved.String("assetId"));
        _out.WriteLine($"save status: {saved.String("status")}");

        // ⛔ A CLEAN document is not written - that is the shared command's contract, so this asserts
        //   the call was accepted and reported, ⛔ not that bytes changed. 📌 Asserting a write would
        //   need an EDIT first, and MCP cannot author one yet (AQ56's track).
        Assert.NotNull(saved.Field("sourceFilePath"));

        var reloaded = (await cluster.Client.ReloadAssetAsync(id)).EnsureOk();
        var status   = reloaded.String("status") ?? string.Empty;
        _out.WriteLine($"reload status: {status}");

        Assert.Equal(id, reloaded.String("assetId"));

        // ⭐⭐ THE VERDICT, from the compiler's own message. ⛔ Not `Ok` - the route answers 200 for a
        //    failed compile on purpose, so trusting the status code would bless a red compile.
        Assert.False(string.IsNullOrWhiteSpace(status), "the reload reported no status at all.");
        Assert.DoesNotContain("failed", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("threw",  status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no active", status, StringComparison.OrdinalIgnoreCase);

        // ⭐ And it compiled THIS asset - a status naming something else would mean the
        //   activate-then-reload step recompiled the wrong graph.
        Assert.Contains(name, status, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE LIVE VARIABLE-VALUE WRITE IS STILL OFF ON THE CLUSTER.</b>
    /// 📄 §7 *(fourth rail)* · the `2026-08-25` STEER *(the ONE place a gate is honest - the reason is
    /// <c>R-52</c> CORRUPTION, not policy)*.
    ///
    /// <para>⛔⛔ <b>Why this rail exists.</b> Slice 3 takes asset editing WHOLESALE, and the two write
    /// paths look similar from outside - ⚠ but the asset path writes a FILE and recompiles, while the
    /// live path stages a whole-component <c>Blackboard1024</c> write that clobbers a tick of BTree/HSM
    /// state. ⇒ a later slice that "turns on editing" could enable the wrong one, and nothing else
    /// would notice.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_live_variable_value_write_is_still_off_on_the_cluster()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-nolivewrite-all", mode: "all");

        var byKind = await CaptureByKindAsync(cluster, _out);

        Assert.True(byKind.ContainsKey("watch"),
            "--mode all does not publish 'watch' - slice 1's shell regressed, and this rail cannot "
          + "speak for the write path without it.");

        var watch = JsonNode.Parse(byKind["watch"].Model)!;
        var rows  = (watch["rows"] as JsonArray) ?? new JsonArray();

        var staged = rows.Where(r => string.Equals(
                             r!["highlight"]?.GetValue<string>(), "Staged", StringComparison.OrdinalIgnoreCase))
                         .ToArray();

        Assert.True(staged.Length == 0,
            $"the cluster's watch shows {staged.Length} STAGED row(s) - a live variable-value write path "
          + "is reachable on CGF. ⛔ That is R-52's whole-component clobber, and the steer keeps it OFF "
          + "on this host until the variable-model lane lands SetComponentFieldRaw.");
    }
    // ══════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ GROUP W — THE MCP AUTHORING RAILS (AQ56 / DESIGN_Mcp_Authoring.md §9)
    //
    // ⛔⛔ A RULE THESE RAILS LEARNED THE HARD WAY, and it is written here rather than in each of them:
    //    ⭐⭐⭐ NEVER `save` a COMMITTED asset from a rail. 📐 Measured `2026-08-25` — the first cut did,
    //    and `git status` came back with **372 deleted lines** in `ComponentCollectionDemo.bp.json`.
    //    ⚠ That is NOT a defect this batch introduced: `SaveActiveBlueprintCommand` STRIPS the projected
    //    pins and rewrites link endpoints to deterministic name-derived ids on the way to disk (design
    //    §3), so what the editor writes legitimately differs in SHAPE from the fatter committed file.
    //    ⇒ any save of any blueprint through the editor dirties the tree; slice 3's rail never noticed
    //    because it saved a CLEAN document, which is a no-op.
    // ⇒ ⭐ The save/reload half is exercised on an asset the rail CREATES and then DELETES, and the
    //    edit half runs against committed assets WITHOUT saving.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE HEADLINE: an agent READS a graph by its in-memory guids, EDITS it over MCP, the
    /// re-read shows the edit, and the edited graph still hot-reloads.</b>
    /// 📄 <c>DESIGN_Mcp_Authoring.md</c> §6 *(the sequence this walks, step for step)* · §9.
    ///
    /// <para>⭐⭐ <b>The round trip is the claim, and it is the only one that can be trusted.</b> ⛔ A rail
    /// that asserted only <i>"add-node answered 200"</i> would pass against a route that returns a fresh
    /// guid and mutates nothing — 📌 the exact shape of the <c>AddNode</c> failure
    /// <c>AuthoringPath.AddNode</c> documents. ⇒ every id this rail receives is spent on a RE-READ.</para>
    ///
    /// <para>⭐⭐⭐ <b>It also pins the VALIDATOR (design item ⑤) with a guaranteed negative.</b> Linking a
    /// pin to ITSELF has no legal reading in any host, so the refusal is deterministic — ⛔ unlike a
    /// legal wire, which depends on which two pins a given asset happens to offer. ⚠ That the refusal
    /// carries the HOST's own reason text is what proves the host validator ran rather than a check
    /// invented here.</para>
    ///
    /// <para>⚠⚠ <b>It REMOVES the node before reloading, and that is not tidying — it is the assertion.</b>
    /// 📐 Measured: adding a bare <c>WhenNode</c> to a committed blueprint makes it stop compiling
    /// (<i>"Blueprint compile failed: AST compilation failed"</i>) — correctly, because an unwired
    /// statement node is not a valid graph. ⇒ ⭐ reloading AFTER the removal asserts the thing worth
    /// asserting: <b>the edit path leaves the asset in a state the compiler still accepts</b>, and the
    /// remove genuinely restored it. ⛔ Reloading with the node still there would assert that the
    /// compiler rejects invalid graphs, which is not this surface's claim.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task An_agent_can_read_and_edit_a_graph_over_mcp()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-authoring-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;
        Assert.True(list.Count > 0,
            "GET /assets returned nothing - slice 2's catalog population regressed, so there is no "
          + "graph to author.");

        var pick = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "Blueprint") ?? list[0];
        var id   = pick!["assetId"]!.GetValue<string>();
        var name = pick["name"]!.GetValue<string>();
        _out.WriteLine($"authoring target: {name} ({pick["kind"]}) {id}");

        // A closed asset has no in-memory graph - that refusal is the contract, so assert it first.
        var beforeOpen = await cluster.Client.ReadAssetGraphAsync(id);
        Assert.Equal(404, beforeOpen.StatusCode);

        (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

        // ── the READ ──────────────────────────────────────────────────────────
        var graph = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
        var nodesBefore = graph.Int("nodeCount");
        var linksBefore = graph.Int("linkCount");
        _out.WriteLine($"read: {nodesBefore} node(s), {linksBefore} link(s)");

        Assert.NotNull(graph.Field("nodes"));
        Assert.Equal(id, graph.String("assetId"));
        Assert.True(nodesBefore > 0,
            $"'{name}' reads as an EMPTY graph. Either the serializer projects nothing, or the document "
          + "opened with no view state - the CE-015 shape, where an 'open' that leaves ViewState null is "
          + "indistinguishable from a working one at the canvas level.");

        // ── the CATALOG - what stops an agent guessing a kind id ──────────────
        var kinds = (await cluster.Client.ListNodeKindsAsync(id)).EnsureOk();
        var kindArr = (kinds.Field("kinds") as JsonArray)!;
        Assert.True(kindArr.Count > 0,
            $"the node catalog for '{name}' is EMPTY, so no node can be added by any means - human or "
          + "MCP. That is a host-composition defect, not an authoring one.");

        // ── the ADD - try catalog entries until one is accepted ───────────────
        //
        // A kind can be legitimately un-addable in a given graph (a container-only entry, a decorator
        // that needs a selected host). Walking a bounded prefix and asserting at least ONE succeeded is
        // the honest form of "an agent can add a node"; asserting a specific kind would pin an asset's
        // contents rather than the capability.
        string? addedNodeId = null, addedKind = null;
        JsonArray? addedPins = null;
        var refusals = new List<string>();

        foreach (var entry in kindArr.Take(12))
        {
            var kind = entry!["kind"]!.GetValue<string>();
            if (entry["isDeprecated"]?.GetValue<bool>() == true) continue;

            var added = await cluster.Client.AddGraphNodeAsync(id, kind, 64f, 64f);
            if (!added.Ok) { refusals.Add($"{kind}: {added.Error}"); continue; }

            addedNodeId = added.String("nodeId");
            addedKind   = added.String("kind");
            addedPins   = added.Field("pins") as JsonArray;
            break;
        }

        Assert.True(addedNodeId != null,
            $"every one of the first catalogued kinds was refused for '{name}'. The refusals were:\n  "
          + string.Join("\n  ", refusals)
          + "\nEach refusal is the host sink's own answer, so this is either a catalog that advertises "
          + "kinds the sink cannot build, or the add route no longer reaches the sink.");

        _out.WriteLine($"added {addedKind} -> {addedNodeId} with {addedPins?.Count ?? 0} pin(s)");

        // ⭐⭐⭐ THE ROUND TRIP - the returned guid must RESOLVE in a fresh read.
        var afterAdd = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
        var nodeIds  = ((afterAdd.Field("nodes") as JsonArray)!)
                       .Select(n => n!["nodeId"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

        Assert.True(nodeIds.Contains(addedNodeId!),
            $"add_graph_node returned {addedNodeId} and a fresh read does NOT contain it. The route "
          + "handed back an id that addresses nothing - which is exactly the failure the route's own "
          + "model re-read is supposed to catch before answering.");

        Assert.Equal(nodesBefore + 1, afterAdd.Int("nodeCount"));

        // ── the VALIDATOR (item 5) - a guaranteed-illegal wire, refused WITH A REASON ──
        Assert.True(addedPins is { Count: > 0 },
            "the add-node response carried no pins, so the link half of this rail cannot be driven. "
          + "The response carries pins deliberately - linking needs them.");

        var selfPin  = addedPins![0]!["pinId"]!.GetValue<string>();
        var selfLink = await cluster.Client.AddGraphLinkAsync(id, selfPin, selfPin);

        Assert.False(selfLink.Ok,
            "linking a pin to ITSELF was ACCEPTED. The host's ILinkValidator is not being consulted, "
          + "so MCP can author a graph the editor itself would reject.");
        Assert.Equal(400, selfLink.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(selfLink.Error),
            "the wire was refused with no reason. The reason is the host validator's own text, and "
          + "without it the caller cannot tell a validator refusal from a transport failure.");

        // ⛔⛔ IT MUST BE THE VALIDATOR THAT REFUSED, not the sink downstream of it.
        // 📐 Measured by a revert probe: disabling the ILinkValidator pre-check left this rail GREEN,
        //    because BlueprintCommandSink refuses a self-link too and the route 400s on that as well.
        //    ⇒ asserting only "it was refused" cannot tell the two apart, and the design's item ⑤ claim
        //    is specifically that the HOST VALIDATOR runs - the same check a dragged wire gets.
        // ⭐ "The editor refuses" is the validator arm's own prefix; the sink arm says "The host sink
        //    refused the link". The prefixes are the discriminator.
        Assert.StartsWith("The editor refuses", selfLink.Error!, StringComparison.Ordinal);
        _out.WriteLine($"validator refused a self-link: {selfLink.Error}");

        // ── RESTORE, then RELOAD ─────────────────────────────────────────────
        (await cluster.Client.RemoveGraphElementsAsync(id, nodes: new[] { addedNodeId! })).EnsureOk();

        var restored = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
        Assert.Equal(nodesBefore, restored.Int("nodeCount"));
        Assert.Equal(linksBefore, restored.Int("linkCount"));

        // ⭐ Reload compiles from the IN-MEMORY asset, so this asserts the edit+remove round trip left a
        //   graph the compiler still accepts. ⛔ No save: that would rewrite a COMMITTED file (see the
        //   block comment above this region).
        var reloaded = (await cluster.Client.ReloadAssetAsync(id)).EnsureOk();
        var status   = reloaded.String("status") ?? string.Empty;
        _out.WriteLine($"reload status: {status}");

        // The compiler's own verdict - NOT the status code, which is 200 for a failed compile on purpose.
        Assert.False(string.IsNullOrWhiteSpace(status), "the reload reported no status at all.");
        Assert.DoesNotContain("failed", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("threw",  status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(name, status, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>An agent can REMOVE what it added, and the removal goes through the editor's own Delete
    /// command.</b> 📄 <c>DESIGN_Mcp_Authoring.md</c> §7 ② · §10.
    ///
    /// <para>⭐ <b>Why a separate rail.</b> Remove is the one edit that does NOT build its own command -
    /// it SELECTS and invokes <c>editor.delete-selection</c>, so that incident links, reroutes and
    /// attachments are handled by the code that already knows about them. ⇒ what this pins is that the
    /// invocation actually reaches that command, ⛔ not that a <c>RemoveNodes</c> was applied.</para>
    ///
    /// <para>⚠ It also pins the ALL-OR-NOTHING refusal: naming one id that is not in the graph must
    /// remove NOTHING, because a partial delete is worse than a refusal.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task An_agent_can_remove_what_it_added_over_mcp()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-authoring-remove-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;
        Assert.True(list.Count > 0, "GET /assets returned nothing - no graph to author.");

        var pick = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "Blueprint") ?? list[0];
        var id   = pick!["assetId"]!.GetValue<string>();

        (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

        var kinds   = (await cluster.Client.ListNodeKindsAsync(id)).EnsureOk();
        var kindArr = (kinds.Field("kinds") as JsonArray)!;
        Assert.True(kindArr.Count > 0, "the node catalog is empty - nothing can be added, so nothing removed.");

        string? nodeId = null;
        foreach (var entry in kindArr.Take(12))
        {
            if (entry!["isDeprecated"]?.GetValue<bool>() == true) continue;
            var added = await cluster.Client.AddGraphNodeAsync(id, entry["kind"]!.GetValue<string>());
            if (!added.Ok) continue;
            nodeId = added.String("nodeId");
            break;
        }
        Assert.True(nodeId != null, "could not add any node, so the removal half cannot be driven.");

        var before = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk().Int("nodeCount");

        // ⛔ ALL-OR-NOTHING: a real id plus an id that is not here must remove NOTHING.
        var bogus   = Guid.NewGuid().ToString();
        var partial = await cluster.Client.RemoveGraphElementsAsync(id, nodes: new[] { nodeId!, bogus });
        Assert.False(partial.Ok,
            "a remove naming one id that is NOT in the graph was accepted. A partial delete leaves the "
          + "caller unable to tell what happened - the whole call must be refused.");
        Assert.Equal(before, (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk().Int("nodeCount"));

        // ⭐ The real removal.
        var removed = (await cluster.Client.RemoveGraphElementsAsync(id, nodes: new[] { nodeId! })).EnsureOk();
        _out.WriteLine($"removed {removed.Int("removedNodes")} node(s), {removed.Int("removedLinks")} link(s)");

        Assert.Equal(1, removed.Int("removedNodes"));

        var after   = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
        var nodeIds = ((after.Field("nodes") as JsonArray)!)
                      .Select(n => n!["nodeId"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

        Assert.False(nodeIds.Contains(nodeId!),
            $"remove_graph_elements reported success and node {nodeId} is STILL in the graph. The "
          + "editor's Delete command was not reached, or it acted on a different selection.");
        Assert.Equal(before - 1, after.Int("nodeCount"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>An agent can CREATE an asset, it appears in the catalog, and the full
    /// create → edit → save → reload cycle runs on it.</b>
    /// 📄 <c>DESIGN_Mcp_Authoring.md</c> §6 *(the sequence, end to end)* · §7 ③ · §10.
    ///
    /// <para>⭐⭐⭐ <b>"Appears in GET /assets" is not ceremony.</b> 📐 The catalog is CONTRIBUTOR-driven:
    /// it rebuilds from what the contributors enumerate, so an asset written outside the directory this
    /// host's contributor scans is invisible to every other route. ⇒ ⛔ a create that answered 200
    /// without this would hand back an id nothing can open. ⭐ That is why the create path returns the
    /// id only AFTER <c>FindByAssetId</c> resolves it.</para>
    ///
    /// <para>⭐⭐ <b>This is also the only rail that SAVES, and that is deliberate</b> — it saves an asset
    /// it created itself, in a sentinel folder it deletes afterwards. ⛔ Saving a COMMITTED asset
    /// rewrites it (see this region's block comment), so the save half of the design's sequence can only
    /// be asserted honestly on an asset the rail owns.</para>
    ///
    /// <para>⚠ <b>EDITOR mode, deliberately.</b> The create path needs the per-kind
    /// <c>INewAssetService</c> registry, the Blueprint source-root override and the per-contributor
    /// refresh; CGF composes none of them and answers 503 saying so. ⛔ That divergence is stated in the
    /// design (§10.3), not hidden by running this only where it passes.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task An_agent_can_create_edit_save_and_reload_its_own_asset()
    {
        const string SentinelFolder = "__mcp_rail_tmp";

        await using var editor = await EditorProcess.StartAsync("conf-authoring-create", mode: "editor");

        var before = (await editor.Client.ListAssetsAsync()).EnsureOk().Int("count");

        // A name derived from the process so a rerun in a dirty working tree does not fight leftovers.
        var name = $"McpAuthored{Environment.ProcessId}";

        var created = await editor.Client.CreateAssetAsync("BTree", name, SentinelFolder);
        _out.WriteLine($"create: {created.StatusCode} {created.Error ?? created.String("status")}");

        Assert.True(created.Ok,
            $"create_asset was refused on the EDITOR host: {created.Error}. This host composes the "
          + "INewAssetService registry, so a refusal here means the create path is no longer wired - "
          + "AttachAssetAuthoring is not being called, or the extraction from the New-Asset dialog "
          + "callback drifted.");

        var newId    = created.String("assetId");
        var filePath = created.String("sourceFilePath");

        try
        {
            Assert.False(string.IsNullOrWhiteSpace(newId),
                "create_asset answered ok with no assetId. The path returns the id only once the catalog "
              + "resolves it, so a missing id means it never got there.");

            var after = (await editor.Client.ListAssetsAsync()).EnsureOk();
            var ids   = ((after.Field("assets") as JsonArray)!)
                        .Select(a => a!["assetId"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

            Assert.True(ids.Contains(newId!),
                $"the created asset {newId} ('{name}') is NOT in GET /assets. It was written somewhere the "
              + "contributor does not scan, so nothing can open or edit it (ruling 67 - asset roots).");
            Assert.True(after.Int("count") > before,
                $"the catalog count did not grow ({before} -> {after.Int("count")}).");

            // ⭐ It is immediately authorable - which is the point of creating it.
            var graph = await editor.Client.ReadAssetGraphAsync(newId!);
            Assert.True(graph.Ok,
                $"the created asset is in the catalog but has no readable graph: {graph.Error}. "
              + "create_asset opens the new asset as a document precisely so authoring can start "
              + "without a second call.");

            // ── EDIT -> SAVE -> RELOAD on the rail's OWN asset ────────────────
            var kinds   = (await editor.Client.ListNodeKindsAsync(newId!)).EnsureOk();
            var kindArr = (kinds.Field("kinds") as JsonArray)!;
            Assert.True(kindArr.Count > 0, "the new asset's node catalog is empty - nothing can be authored.");

            string? nodeId = null;
            foreach (var entry in kindArr.Take(12))
            {
                if (entry!["isDeprecated"]?.GetValue<bool>() == true) continue;
                var added = await editor.Client.AddGraphNodeAsync(newId!, entry["kind"]!.GetValue<string>());
                if (!added.Ok) continue;
                nodeId = added.String("nodeId");
                break;
            }
            Assert.True(nodeId != null, "no catalogued kind could be added to the freshly created asset.");

            var reread = (await editor.Client.ReadAssetGraphAsync(newId!)).EnsureOk();
            Assert.Contains(((reread.Field("nodes") as JsonArray)!)
                                .Select(n => n!["nodeId"]!.GetValue<string>()),
                            nid => string.Equals(nid, nodeId, StringComparison.Ordinal));

            var saved = (await editor.Client.SaveAssetAsync(newId!)).EnsureOk();
            _out.WriteLine($"save status: {saved.String("status")}");

            Assert.False(saved.Bool("stillDirty"),
                $"the created asset is STILL dirty after save: {saved.String("status")}. Either the "
              + "document was never marked dirty by the edit, or it has no source path and the shared "
              + "Save-All command skipped it with a warning.");

            var reloaded = (await editor.Client.ReloadAssetAsync(newId!)).EnsureOk();
            var status   = reloaded.String("status") ?? string.Empty;
            _out.WriteLine($"reload status: {status}");
            Assert.False(string.IsNullOrWhiteSpace(status), "the reload reported no status at all.");
            Assert.DoesNotContain("threw", status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // ⭐⭐ Leave the working tree as it was found. ⛔ Name-guarded: nothing is deleted unless the
            //    path really sits inside the sentinel folder this rail asked create_asset to write to.
            CleanUpSentinelFolder(filePath, SentinelFolder, _out);
        }
    }

    /// <summary>
    /// ⭐ Deletes the throwaway folder a create-rail asked for, and NOTHING else.
    /// </summary>
    /// <remarks>
    /// ⚠ Three guards, because a test that deletes directories is worth being paranoid about: the path
    /// must be non-empty, the directory's own NAME must equal the sentinel, and it must still exist.
    /// ⛔ A path that fails any of them is REPORTED to the test output rather than guessed at.
    /// </remarks>
    private static void CleanUpSentinelFolder(string? assetPath, string sentinel, ITestOutputHelper output)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            output.WriteLine($"[cleanup] no sourceFilePath was reported - '{sentinel}' may be left behind.");
            return;
        }

        try
        {
            var dir = System.IO.Path.GetDirectoryName(assetPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (dir == null || !string.Equals(System.IO.Path.GetFileName(dir), sentinel, StringComparison.Ordinal))
            {
                output.WriteLine($"[cleanup] '{assetPath}' is not inside '{sentinel}' - leaving it alone.");
                return;
            }

            if (System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.Delete(dir, recursive: true);
                output.WriteLine($"[cleanup] removed {dir}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"[cleanup] could not remove the sentinel folder: {ex.Message}");
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MD-006</c>/<c>MD-007</c> — an agent can drive the CLUSTER-WIDE diagnostic dump the
    /// operator drives from the ExCon, and read the result the same panel reads.</b>
    /// 📄 <c>DESIGN_Mcp_Diagnostics_Federation.md</c> §8.5.
    ///
    /// <para>⭐⭐⭐ <b>The point of this rail is that NOTHING NEW COLLECTS ANYTHING.</b> 🔒 User ruling
    /// *(`2026-08-25`)*: *"in the UI as a user i can click and data gets collected and saved. the cluster
    /// wide collection works. No further aggregation needed."* ⇒ these two routes are a SECOND SURFACE on
    /// the built dump-diag pipeline — ⛔ the rail therefore asserts the SURFACE is reachable and honest,
    /// and does NOT re-assert that collection works.</para>
    ///
    /// <para>⚠⚠ <b>It deliberately does NOT wait for files.</b> 📐 The dump gathers on every selected node
    /// and then pulls to a NAS over SMB — ⛔ there is no NAS in this harness, so demanding a non-empty
    /// manifest would redden on the ENVIRONMENT rather than on the code. ⭐ What IS asserted is everything
    /// that is this surface's own responsibility: the route publishes, the status route answers from the
    /// panel's own read model, and an empty node list is REFUSED rather than silently read as "all".</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Conformance")]
    public async Task An_agent_can_drive_the_cluster_diagnostic_dump()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-clusterdump", mode: "all");

        // ── ① THE STATUS ROUTE READS THE PANEL'S OWN MODEL ───────────────
        var status = await cluster.Client.GetClusterDumpStatusAsync();
        Assert.True(status.Ok,
            $"GET /cluster/diagnostics/status was refused: {status.Error}. It reads ClusterUiCache — the "
          + "same read model ClusterDiagnosticsPanel renders — through the provider seam, so a refusal "
          + "here means no subsystem is supplying `dumpStatus:`.");

        _out.WriteLine($"status: inFlight={status.Bool("inFlight")} "
                     + $"manifestCount={status.Int("manifestCount")}");

        // ⭐ The shape must be complete even when nothing has been dumped yet: an EMPTY manifest is
        //   "none yet", ⛔ and the route must say so rather than omitting the field.
        Assert.NotNull(status.Field("manifestPaths"));
        Assert.NotNull(status.Field("inFlight"));

        // ── ② AN EMPTY NODE SET IS REFUSED ───────────────────────────────
        // ⛔⛔ The assertion that matters most here. Reading [] as "every node" would turn a caller's
        //    omission into a cluster-wide operation — and the editor's own panel DISABLES its button on
        //    exactly this condition, so accepting it would make MCP the one path that does what the UI
        //    refuses. 📌 The same parity argument as the 409 on a disabled editor command (MA-015).
        var empty = await cluster.Client.TriggerClusterDumpAsync(System.Array.Empty<int>());
        Assert.False(empty.Ok, "a dump with an EMPTY node list was ACCEPTED.");
        Assert.Contains("at least one node", empty.Error ?? string.Empty, StringComparison.Ordinal);
        _out.WriteLine($"empty selection refused: {empty.Error}");

        // ── ③ A REAL REQUEST PUBLISHES ───────────────────────────────────
        // ⭐ Node 1 is this host's own id (EditorProcess launches a single process).
        var triggered = await cluster.Client.TriggerClusterDumpAsync(new[] { 1 });
        _out.WriteLine($"trigger: {triggered.StatusCode} {triggered.Error ?? triggered.Data?.ToJsonString()}");

        Assert.True(triggered.Ok,
            $"POST /cluster/diagnostics/dump was refused: {triggered.Error}. The intent is published onto "
          + "whichever provider exposes an orchestration bus — the same publish path requestTransition "
          + "uses — so a refusal means `requestDiagnosticDump:` is not wired on any provider.");

        Assert.True(triggered.Bool("queued"),
            "the dump answered ok but did not report queued:true. This operation is ASYNCHRONOUS and the "
          + "response must say so — a caller that reads it as 'done' will look for files that are still "
          + "being gathered.");

        var tx = triggered.String("transactionId");
        Assert.False(string.IsNullOrWhiteSpace(tx),
            "no transactionId came back, so nothing correlates this request with its completion.");

        // ⭐⭐⭐ THE PROOF THAT THIS IS NOT ACCEPT-AND-DO-NOTHING.
        // ⛔⛔ A 200 with a transaction id proves only that the ROUTE ran. 📌 This surface has been bitten
        //    twice by exactly that gap (MA-004: an id resolving to nothing; MA-017: a command accepted
        //    that built nothing) ⇒ the rail reads the node's own output for the ClusterMaster's fan-out
        //    line carrying THIS transaction id. That is the pipeline confirming it took the request.
        // ⚠ Polled, because the master handles the intent on a later tick — an immediate read races it.
        string fanOut = string.Empty;
        for (int i = 0; i < 40 && fanOut.Length == 0; i++)
        {
            var log = cluster.EditorOutput;
            foreach (var line in log.Split('\n'))
                if (line.Contains(tx!, StringComparison.OrdinalIgnoreCase)
                 && line.Contains("fanned out", StringComparison.OrdinalIgnoreCase))
                { fanOut = line.Trim(); break; }
            if (fanOut.Length == 0) await Task.Delay(250);
        }

        _out.WriteLine($"master: {fanOut}");
        Assert.False(string.IsNullOrEmpty(fanOut),
            $"the route returned queued:true for transaction {tx}, but the ClusterMaster never logged "
          + "fanning it out. The intent was published onto a bus that does not reach a master, so the "
          + "route reports a cluster-wide operation it did not actually start.");

        // ⭐⭐ The status route still answers AFTER a trigger — that is the poll loop an agent will run.
        //   ⛔ No assertion that the manifest FILLED: there is no NAS in this harness, and asserting one
        //   would redden on the environment rather than the code.
        var after = await cluster.Client.GetClusterDumpStatusAsync();
        Assert.True(after.Ok, $"the status route stopped answering after a trigger: {after.Error}");
        _out.WriteLine($"after trigger: inFlight={after.Bool("inFlight")} "
                     + $"manifestCount={after.Int("manifestCount")}");

        // ── ④ R-133 — the capability cell is MEASURED ────────────────────
        var caps = (await cluster.Client.GetCapabilitiesAsync()).EnsureOk();
        Assert.Contains("diagnostics.clusterDump", caps.Data?.ToJsonString() ?? string.Empty,
                        StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MD-001</c>/<c>MD-002</c>/<c>MD-003</c> — a NON-EDITOR node answers for ITSELF: its own
    /// logs, its own architecture, its own capability cells.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §1 · §2.1 · §2.2.
    ///
    /// <para>⛔⛔⛔ <b><c>--mode simhost</c>, and the mode is the whole rail.</b> 📐 Measured: on
    /// <c>--mode all</c> the node INCLUDES the editor, and <c>Program.cs</c> hands the API to
    /// <c>EditorSubsystem</c> — the FULL surface. ⇒ every existing conformance rail runs against the
    /// editor-owned host, and **not one of them has ever exercised the cluster-limited
    /// <c>DebugApiService(dispatcher)</c> path**, which is exactly where the log-sink gap lived. ⚠ A rail
    /// on <c>--mode all</c> would have passed before this batch and proved nothing.</para>
    ///
    /// <para>⭐⭐ <b>This is the federation claim made checkable</b> *(§1)*: a SimHost node runs its own
    /// MCP endpoint on its own port with LIMITED capabilities — no authoring — and must still be able to
    /// report what it is running and what it has logged.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Conformance")]
    public async Task A_non_editor_node_reports_its_own_logs_and_architecture()
    {
        // ⛔⛔ NOT bare `--mode simhost`, and the reason is measured, not stylistic: a standalone SimHost
        //    node dies in `DdsIdAllocatorHelper.EnsureRouting` — *"Hrot.Orchestrator must be running before
        //    this node starts"* — so a one-subsystem rail cannot boot at all in this harness.
        // ⭐⭐ ORDER MATTERS and it is measured: `--mode all` expands to `orchestrator,simhost,ig,excon,cgf`
        //    and the orchestrator is FIRST for exactly this reason — the id allocator needs it serving
        //    before SimHost bootstraps. `simhost,orchestrator` dies the same way the bare mode does.
        // ⭐⭐ `orchestrator,simhost` is the smallest mode that BOOTS and still has NO EditorSubsystem,
        //    which is the only thing that matters here: `Program.cs` hands the API to the editor whenever
        //    one is present, so this is the cluster-limited `DebugApiService(dispatcher)` path.
        await using var node = await EditorProcess.StartAsync(
            "conf-diag-cluster", mode: "orchestrator,simhost");

        // ── ① THE FEDERATION — this node is NOT the editor ────────────────
        var caps = (await node.Client.GetCapabilitiesAsync()).EnsureOk();
        _out.WriteLine($"capabilities: {caps.Data}");

        // ── ② MD-001 — GET /logs actually answers ────────────────────────
        // ⛔⛔ Before this batch `Program.cs` built `new DebugApiService(dispatcher)` with NO sinks, so
        //    `_logSinks` fell to Array.Empty and this returned [] on every cluster-limited node — while
        //    the very same records fed the on-screen Message Log. 📌 The caller-had-the-value default.
        // ⚠ No line is INJECTED first: booting a node logs plenty by itself (the runner announces its
        //   mode, its subsystems and its API port), and a rail that had to manufacture its own input
        //   would not be testing that the node's OWN logging reaches the route.
        var logs = (await node.Client.GetLogsAsync(max: 50)).EnsureOk();
        var entries = logs.Data as JsonArray;

        _out.WriteLine($"logs: {entries?.Count ?? 0} record(s)");
        Assert.True(entries is { Count: > 0 },
            "GET /logs is EMPTY on a cluster-limited node. This node has certainly logged during boot, "
          + "so an empty answer means its DebugApiService was constructed with no log sinks — the "
          + "composition root is not passing MessageLogSinks.ForDiagnostics(...).");

        var first = entries![0]!;
        foreach (var field in new[] { "timestamp", "level", "logger", "message" })
            Assert.NotNull(first[field]);

        // ── ③ MD-002 — the node's OWN architecture ───────────────────────
        var arch = await node.Client.GetArchitectureDiagnosticsAsync();
        Assert.True(arch.Ok,
            $"GET /diagnostics/architecture was refused on a SimHost node: {arch.Error}. Diagnostics live "
          + "on the SHARED DebugApiService precisely so a non-editor node can answer for itself (design "
          + "§1); a refusal here means the route was wired to the editor-only path.");

        var subsystems = (arch.Field("subsystems") as JsonArray)!;
        _out.WriteLine($"architecture: {subsystems.Count} subsystem(s)");
        foreach (var sub in subsystems)
            _out.WriteLine($"  {sub!["subsystem"]} — {sub["moduleCount"]} module(s), "
                         + $"{sub["systemCount"]} system(s), {sub["translatorCount"]} translator(s)");

        Assert.True(subsystems.Count > 0,
            "the node reports ZERO subsystems. A SimHost node runs a ModuleHostKernel, so an empty list "
          + "means SubsystemDebugProvider was built without its `architecture:` accessor.");

        // ⭐⭐ The ANTI-VACUITY floor: a snapshot with no modules at all would satisfy "returns a payload"
        //    while proving the kernel was never read. ⛔ A running SimHost node HAS modules.
        var totalModules = subsystems.Sum(sub => sub!["moduleCount"]?.GetValue<int>() ?? 0);
        _out.WriteLine($"total modules across subsystems: {totalModules}");
        Assert.True(totalModules > 0,
            "every subsystem reported 0 modules. The snapshot is being taken from a null kernel, so the "
          + "route answers with a shape and no substance.");

        // ── ④ R-133 — the capability cell is MEASURED, not declared ──────
        // ⚠ Asserted through the manifest rather than by reading the code: an unclassified route prefix
        //   REDDENS CapabilityManifestRails, so this cell exists only because the kernel is really there.
        var capsRaw = caps.Data?.ToJsonString() ?? string.Empty;
        Assert.Contains("diagnostics.architecture", capsRaw, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MA-019</c>/<c>MA-020</c>/<c>MA-021</c>/<c>MA-022</c> — an agent DISCOVERS the recipes a
    /// CLUSTER node offers, CREATES from one by name, and the node's catalog gains it.</b>
    /// 📄 <c>Architect_Question_57_Cgf_Authoring_Packaging.md</c> Q57-A/B/C.
    ///
    /// <para>⭐⭐ <b><c>--mode all</c>, and that is the whole point.</b> 📐 <c>create_asset</c> shipped with
    /// <c>MA-002</c> and worked ONLY on the editor host, because the per-kind
    /// <c>INewAssetService</c> dictionary was composed behind <c>Hrot.Editor</c>. ⇒ a rail on the editor
    /// proves nothing about this: it is the CGF composition root that had the gap.</para>
    ///
    /// <para>⛔⛔ <b>Discovery and create are asserted TOGETHER, deliberately.</b> A list of recipes the
    /// create route cannot build from would be a capability reported and not held — 📌 the shape
    /// <c>MA-004</c> caught *(an id resolving to nothing)* and <c>MA-017</c> caught again *(a command
    /// accepted that built nothing)*. ⇒ every name this rail reads back, it then USES.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Conformance")]
    public async Task An_agent_can_discover_recipes_and_create_from_one_on_a_cluster_node()
    {
        const string SentinelFolder = "__mcp_recipe_rail_tmp";

        await using var cluster = await EditorProcess.StartAsync("conf-recipes", mode: "all");

        // ── ① DISCOVERY ──────────────────────────────────────────────────
        var listed = await cluster.Client.ListAssetRecipesAsync();
        Assert.True(listed.Ok,
            $"GET /assets/recipes was refused on a --mode all node: {listed.Error}. Before AQ57 this "
          + "host composed no per-kind INewAssetService registry at all; a refusal here means "
          + "AttachRecipes is no longer wired from ClusterRunner/Program.cs.");

        var recipes = (listed.Field("recipes") as JsonArray)!;
        _out.WriteLine($"kinds: {listed.Field("kinds")}");
        _out.WriteLine($"{recipes.Count} recipe(s) offered");

        Assert.True(recipes.Count > 0,
            "the node offers ZERO recipes. Every per-kind service offers at least a blank template, so "
          + "an empty list means the registry was attached empty rather than not attached.");

        // ⭐ The two facts the payload must carry, or the list is not actionable: a NAME the create route
        //   takes verbatim, and whether it is a blank template or a content recipe.
        foreach (var r in recipes)
        {
            Assert.False(string.IsNullOrWhiteSpace(r!["name"]?.GetValue<string>()),
                "a recipe came back with no name — the name is what create_asset takes as `recipe`.");
            Assert.NotNull(r["isBlankTemplate"]);
        }

        // ⭐⭐⭐ THE ANTI-VACUITY PROOF for the `describe` seam. 📌 `RecipePickerSource.describe` and
        //    `recipeCategory` were OPTIONAL and NO production caller passed them, so every recipe carried
        //    a null description while `BlueprintAsset.EditorMetadata.Recipe` held one — the silent-default
        //    shape (the caller HAD the value and did not pass it).
        // ⛔ Without this assertion the fix is unfalsifiable: a describe that always returns null would
        //   look exactly like the un-wired state it replaced.
        // ⚠ A FLOOR, not full coverage: the BTree/HSM Empty and Starter entries are synthetic and
        //   genuinely carry no metadata, so demanding a description on every recipe would redden on an
        //   honest answer.
        var described = recipes.Count(r => !string.IsNullOrWhiteSpace(r!["description"]?.GetValue<string>()));
        _out.WriteLine($"{described}/{recipes.Count} recipe(s) carry a description");

        Assert.True(described > 0,
            $"NOT ONE of {recipes.Count} recipes carries a description, though the Blueprint blank "
          + "templates set EditorMetadata.Recipe.Description in their own constructor. The `describe` "
          + "seam is not reaching RecipePickerSource — which is the exact un-wired state this slice fixed.");

        // ⭐⭐ Prefer a CONTENT recipe over a blank template: creating from a blank is what the route did
        //   BEFORE this slice, so it would not prove the by-name path is reached.
        var pick = recipes.FirstOrDefault(r =>
                       r!["isBlankTemplate"]!.GetValue<bool>() == false
                    && r["kind"]!.GetValue<string>() != "Blueprint")
                ?? recipes.FirstOrDefault(r => r!["kind"]!.GetValue<string>() != "Blueprint")
                ?? recipes[0];

        var pickKind = pick!["kind"]!.GetValue<string>();
        var pickName = pick["name"]!.GetValue<string>();
        var isBlank  = pick["isBlankTemplate"]!.GetValue<bool>();
        _out.WriteLine($"picked recipe: {pickKind}/'{pickName}' (blankTemplate={isBlank}, "
                     + $"description={pick["description"]?.GetValue<string>() ?? "<null>"})");

        // ── ② CREATE FROM IT, BY NAME ────────────────────────────────────
        var before   = (await cluster.Client.ListAssetsAsync()).EnsureOk().Int("count");
        var assetName = $"McpRecipe{Environment.ProcessId}";

        var created = await cluster.Client.CreateAssetAsync(
            pickKind, assetName, SentinelFolder, recipe: pickName);
        _out.WriteLine($"create: {created.StatusCode} {created.Error ?? created.String("status")}");

        Assert.True(created.Ok,
            $"POST /assets was refused on the cluster node: {created.Error}. AQ57-C is that CREATE needs "
          + "no new route — it needs the per-kind service dict wired at THIS host's composition root.");

        var newId    = created.String("assetId");
        var filePath = created.String("sourceFilePath");

        try
        {
            Assert.False(string.IsNullOrWhiteSpace(newId),
                "create_asset answered ok with no assetId. The id is returned only once the CATALOG "
              + "resolves it (MA-004), so a missing id means the file landed where this host's "
              + "contributor does not scan — ruling 67's own failure mode.");

            // ── ③ THE ROUND TRIP ─────────────────────────────────────────
            var after = (await cluster.Client.ListAssetsAsync()).EnsureOk();
            var ids   = ((after.Field("assets") as JsonArray)!)
                        .Select(a => a!["assetId"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

            Assert.True(ids.Contains(newId!),
                $"the created asset {newId} ('{assetName}') is NOT in GET /assets on the node that "
              + "created it. It exists on disk and nothing can address it.");
            Assert.True(after.Int("count") > before,
                $"the node's catalog did not grow ({before} -> {after.Int("count")}).");

            // ⭐ And it is immediately AUTHORABLE — the reason create opens it as a document.
            var graph = await cluster.Client.ReadAssetGraphAsync(newId!);
            Assert.True(graph.Ok,
                $"the created asset is catalogued but has no readable graph: {graph.Error}.");

            // ── ④ AN UNKNOWN RECIPE IS REFUSED, NEVER SILENTLY BLANKED ───
            // ⛔⛔ The single most important assertion here: falling back to the blank template would
            //    hand the caller a DIFFERENT asset than it asked for and report success — the
            //    silent-wrong-answer shape this surface has already been bitten by twice.
            var bogus = await cluster.Client.CreateAssetAsync(
                pickKind, assetName + "Bogus", SentinelFolder, recipe: "ThisRecipeDoesNotExist");

            Assert.False(bogus.Ok,
                "creating from a recipe name the host does not offer SUCCEEDED. It silently fell back to "
              + "the blank template, so the caller got something other than what it asked for.");
            Assert.Contains("is not a recipe", bogus.Error ?? string.Empty, StringComparison.Ordinal);
            _out.WriteLine($"unknown recipe refused: {bogus.Error}");

            // ── ⑤ MA-022 — the schema exporter is wired on THIS host ─────
            // 📌 The overnight MCP run filed `paramsSource: none:no-exporter-wired` as a one-line
            //    follow-up in this lane. ⚠ It is asserted as an ABSENCE of that one value, not as a
            //    positive source: a kind that genuinely is not an action correctly reports
            //    `none:not-an-action`, and demanding `exporter:*` would redden on honest answers.
            var kinds = (await cluster.Client.ListNodeKindsAsync(newId!)).EnsureOk();
            var kindArr = (kinds.Field("kinds") as JsonArray)!;
            var sources = new List<string>();
            foreach (var entry in kindArr.Take(8))
            {
                var schema = await cluster.Client.GetNodeKindSchemaAsync(
                    newId!, entry!["kind"]!.GetValue<string>());
                if (!schema.Ok) continue;
                sources.Add(schema.String("paramsSource") ?? "<none>");
            }

            _out.WriteLine($"paramsSource over {sources.Count} kind(s): "
                         + string.Join(", ", sources.Distinct()));

            // ⛔ Guard the assertion against vacuity: an empty `sources` would satisfy DoesNotContain
            //   while proving nothing at all.
            Assert.True(sources.Count > 0,
                "not one node kind resolved a schema, so the exporter claim below would be vacuous.");
            Assert.DoesNotContain("none:no-exporter-wired", sources);
        }
        finally
        {
            CleanUpSentinelFolder(filePath, SentinelFolder, _out);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Scenario authoring is WORLD manipulation: an agent can DELETE an entity, and the world
    /// loses it.</b> 📄 <c>DESIGN_Mcp_Authoring.md</c> §1 · §7 ④ *(`Q56-C`, resolved with the user:
    /// there is no such thing as editing a scenario FILE)*.
    ///
    /// <para>⭐⭐⭐ <b>Why delete and nothing else.</b> 📐 Measuring the existing <c>/entities/*</c> set
    /// against the four authoring verbs found three already built - <b>place</b> is
    /// <c>POST /entities/spawn</c>, <b>configure</b> is <c>/attribute</c> and <c>/component</c>,
    /// <b>assign</b> is <c>/attach-blueprint</c> - and <b>delete had no route at all.</b> ⇒ this rail
    /// covers the one op the surface was missing.</para>
    ///
    /// <para>⚠ <b>It LOADS a scenario first, and the first cut did not.</b> 📐 A bare editor boots with an
    /// EMPTY world, so the rail failed on <i>"GET /entities is empty"</i> — ⛔ which said nothing about
    /// the delete route. ⭐ <c>hill-attack</c> is the harness's standard world, as
    /// <c>DeterminismRails</c> and <c>CapabilitySmokeTests</c> already use it.</para>
    ///
    /// <para>⚠ <b>What it does NOT assert, stated rather than implied:</b> it does not read the saved
    /// scenario file back. ⭐ It asserts the WORLD lost the entity and that <c>scenario/save</c> then
    /// succeeds — and by `Q56-C` the file IS a snapshot of that world. ⛔ Parsing the written file would
    /// pin this rail to a serialization format that is not this batch's subject. ⭐ The save writes under
    /// the NAS base path, ⛔ not into the repo, so it leaves no working-tree change.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task An_agent_can_delete_an_entity_and_the_world_loses_it()
    {
        await using var editor = await EditorProcess.StartAsync("conf-authoring-delete", mode: "editor");

        (await editor.Client.LoadScenarioEditAsync("hill-attack")).EnsureOk();
        (await editor.Client.StepAsync(3)).EnsureOk();

        var entities = (await editor.Client.ListEntitiesAsync()).EnsureOk();
        var rows     = entities.Data as JsonArray ?? (entities.Field("entities") as JsonArray) ?? new JsonArray();

        Assert.True(rows.Count > 0,
            "GET /entities is empty after loading hill-attack, so there is no entity to delete. That is "
          + "a scenario-loading finding, not a delete-route one.");

        var victim = rows[0]!["networkId"]!.GetValue<long>();
        _out.WriteLine($"deleting entity {victim} of {rows.Count}");

        // ⛔ An unknown id is refused, NOT queued against nothing.
        var ghost = await editor.Client.DeleteEntityAsync(victim + 9_000_000);
        Assert.False(ghost.Ok, "deleting an entity that does not exist was accepted.");
        Assert.Equal(404, ghost.StatusCode);

        var deleted = (await editor.Client.DeleteEntityAsync(victim)).EnsureOk();
        Assert.True(deleted.Bool("queued"),
            "the delete did not report itself as queued. Teardown runs through the ELM lifecycle on a "
          + "later tick, and saying otherwise invites a caller to assert too early.");

        // Queued like spawn - step before asserting.
        (await editor.Client.StepAsync(5)).EnsureOk();

        var afterRows  = (await editor.Client.ListEntitiesAsync()).EnsureOk();
        var remaining  = afterRows.Data as JsonArray
                      ?? (afterRows.Field("entities") as JsonArray) ?? new JsonArray();
        var stillThere = remaining.Any(r => r!["networkId"]!.GetValue<long>() == victim);

        Assert.False(stillThere,
            $"entity {victim} is still in the world after DELETE + 5 ticks. The DestroyEntityCommand "
          + "was published but ELM teardown did not run, or it was published on a bus nothing reads.");

        // ⭐ And a snapshot taken now is a snapshot of THIS world - Q56-C's whole point.
        var savedScenario = await editor.Client.SaveScenarioAsync($"mcp-authoring-{Environment.ProcessId}");
        Assert.True(savedScenario.Ok,
            $"scenario/save failed after the delete: {savedScenario.Error}. Scenario authoring is world "
          + "manipulation followed by a snapshot, so a save that cannot run makes the delete pointless.");
    }
    // ══════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ GROUP X — THE UNION / DISCOVERY / COMMAND-BUS RAILS
    //    📄 DESIGN_Mcp_Authoring.md §10 · §10.7 · §11 · the handoff §5.
    //
    // ⛔⛔ The Group-W rule still binds: NEVER `save` a COMMITTED asset from a rail (see that region's
    //    block comment — an earlier cut cost 372 deleted lines in a committed blueprint). Everything
    //    below edits IN MEMORY and reads back; nothing here writes to disk.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE UNION ROUND-TRIP: variants the four typed verbs CANNOT express are applied over the
    /// one command route and read back.</b> 📄 §11.2 · §11.3 · handoff §5 *(union coverage)*.
    ///
    /// <para>⭐⭐ <b>What makes this the headline.</b> The typed verbs shipped in `MA-002` cover
    /// node/link/param/remove. ⛔ They cannot say <i>"decorate this BTree node"</i> or <i>"add a parallel
    /// region to this HSM state"</i> — which are exactly the host specifics the user asked authoring to
    /// reach. ⇒ this rail applies <b>attachments, comments, reroutes, collapse flags and a Batch</b>
    /// through <c>apply_graph_command</c> and asserts each is VISIBLE in a fresh read.</para>
    ///
    /// <para>⭐⭐⭐ <b>Read-back is the assertion, not the 200.</b> 📌 `MA-004` measured the failure this
    /// guards: the host sink can report success and build nothing. ⇒ every applied variant is spent on a
    /// re-read, and the serializer widening (`MA-011`) is what makes attachments and regions visible at
    /// all — ⛔ before it, an `AddAttachment` was unprovable.</para>
    ///
    /// <para>⚠ <b>A variant a given host REFUSES is LOGGED, not silently skipped</b> *(handoff §5)*. A
    /// host is entitled to reject a command its model has no place for; what the rail refuses to allow is
    /// that refusal going unrecorded.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_union_command_route_reaches_what_the_typed_verbs_cannot()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-union-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;
        Assert.True(list.Count > 0, "GET /assets returned nothing - no graph to author.");

        // ⭐⭐⭐ PER HOST, and finding out WHY is what two red runs of this rail bought.
        // 🔴 Run 1: `AddAttachment` on a BLUEPRINT returned Success having built NOTHING — attachments are
        //    a BTree/HSM concept and `BlueprintCommandSink` has no arm for them.
        // 🔴 Run 2, on a BTREE: `AddComment` came back "Unsupported: AddComment" — the BTree sink refuses
        //    comments outright — and a bare `AddAttachment` STILL built nothing, because the sink needs
        //    `hostProperties.paletteKind` to know WHICH concrete decorator to construct (that is what
        //    `PaletteEntryExecutor` passes when a human picks one).
        // ⇒ ⭐⭐ THREE things came out of it: the union route now verifies every MINTED id resolves before
        //    reporting success; the rail drives each variant on a host that OWNS it; and the attachment is
        //    built the way the picker builds it, from a catalog entry rather than from nothing.
        var btree     = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "BTree");
        var blueprint = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "Blueprint");

        var applied = new List<string>();
        var refused = new List<string>();

        // ── ① ATTACHMENTS on a BTREE — the decorator shape the typed verbs cannot express ──
        if (btree != null)
        {
            var id   = btree["assetId"]!.GetValue<string>();
            var name = btree["name"]!.GetValue<string>();
            _out.WriteLine($"attachment target: {name} (BTree) {id}");

            (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

            // The route describes itself first - nothing below guesses a payload.
            var types = (await cluster.Client.ListGraphCommandTypesAsync(id)).EnsureOk();
            var variants = (types.Field("variants") as JsonArray)!;
            _out.WriteLine($"the host advertises {variants.Count} command variant(s)");

            Assert.True(variants.Count > 20,
                $"the command route advertises only {variants.Count} variants; the union has ~35.");

            var advertised = variants.Select(v => v!["type"]!.GetValue<string>())
                                     .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in new[] { "AddAttachment", "AddRegion", "ChangeParent", "Batch", "AddComment" })
                Assert.True(advertised.Contains(required),
                    $"'{required}' is not advertised. That is a host specific the typed verbs cannot "
                  + "express, and it is the reason the union route exists.");

            var graph    = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
            var hostNode = ((graph.Field("nodes") as JsonArray)!).FirstOrDefault();
            Assert.True(hostNode != null, $"'{name}' reads as an empty graph - nothing to decorate.");
            var hostNodeId = hostNode!["nodeId"]!.GetValue<string>();

            // ⭐⭐ The catalog says which kinds are ATTACHMENTS: PaletteAction == AttachToSelected. ⛔ Not
            //    guessed - this is the kind-level structure fact discovery was extended to expose.
            var kinds = (await cluster.Client.ListNodeKindsAsync(id)).EnsureOk();
            var attachKind = ((kinds.Field("kinds") as JsonArray)!)
                .FirstOrDefault(k => string.Equals(k!["paletteAction"]?.GetValue<string>(),
                                                   "AttachToSelected", StringComparison.OrdinalIgnoreCase));

            if (attachKind != null)
            {
                var kindId = attachKind["kind"]!.GetValue<string>();
                _out.WriteLine($"attachment kind from the catalog: {kindId}");

                var add = await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
                {
                    ["type"]  = "AddAttachment",
                    ["host"]  = hostNodeId,
                    ["label"] = "mcp-rail",
                    // ⭐ The host property the picker passes: without it the sink cannot know WHICH
                    //   concrete decorator to build, and silently builds none.
                    ["hostProperties"] = new JsonObject { ["paletteKind"] = kindId },
                });

                if (add.Ok)
                {
                    applied.Add("AddAttachment");
                    var attachmentId = add.Field("newIds")?["attachmentId"]?.GetValue<string>();
                    Assert.False(string.IsNullOrWhiteSpace(attachmentId),
                        "AddAttachment succeeded and returned no attachmentId.");

                    // ⭐⭐ THE READ-BACK - what MA-011's serializer widening bought. Before it, an
                    //    attachment edit was unprovable.
                    var after = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
                    var attachIds = ((after.Field("attachments") as JsonArray) ?? new JsonArray())
                                    .Select(a => a!["attachmentId"]!.GetValue<string>())
                                    .ToHashSet(StringComparer.Ordinal);
                    Assert.True(attachIds.Contains(attachmentId!),
                        $"AddAttachment returned {attachmentId} and the read does NOT contain it - and the "
                      + "route's own minted-id check passed, so the serializer stopped emitting attachments.");

                    (await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
                    {
                        ["type"]        = "RemoveAttachments",
                        ["attachments"] = new JsonArray(attachmentId!),
                    })).EnsureOk();
                    applied.Add("RemoveAttachments");
                }
                else
                {
                    refused.Add($"AddAttachment on BTree: {add.Error}");
                }
            }
            else
            {
                refused.Add("no AttachToSelected kind in the BTree catalog - attachment half not driven");
            }

            // ⭐ A COSMETIC variant that every sink should take: collapse the node. Round-trip only.
            var collapse = await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
            {
                ["type"] = "SetNodeCollapsed", ["node"] = hostNodeId, ["collapsed"] = true,
            });
            if (collapse.Ok)
            {
                applied.Add("SetNodeCollapsed");
                await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
                {
                    ["type"] = "SetNodeCollapsed", ["node"] = hostNodeId, ["collapsed"] = false,
                });
            }
            else refused.Add($"SetNodeCollapsed on BTree: {collapse.Error}");
        }
        else
        {
            refused.Add("no BTree asset indexed - the attachment half could not be driven");
        }

        // ── ② COMMENTS + BATCH on a BLUEPRINT — the BTree sink refuses comments (measured) ──
        if (blueprint != null)
        {
            var id = blueprint["assetId"]!.GetValue<string>();
            _out.WriteLine($"comment target: {blueprint["name"]} (Blueprint) {id}");

            (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

            var batch = await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
            {
                ["type"]  = "Batch",
                ["label"] = "mcp-rail-batch",
                ["commands"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "AddComment", ["text"] = "batched-a",
                        ["position"] = new JsonObject { ["x"] = 500f, ["y"] = 500f },
                    },
                    new JsonObject
                    {
                        ["type"] = "AddComment", ["text"] = "batched-b",
                        ["position"] = new JsonObject { ["x"] = 560f, ["y"] = 500f },
                    }),
            });

            if (batch.Ok)
            {
                applied.Add("Batch(AddComment x2)");
                var newIds = batch.Field("newIds") as JsonObject ?? new JsonObject();

                // ⭐ Batch reports each nested command's minted id, INDEXED - without that a caller cannot
                //   address what a multi-step authoring sequence created.
                Assert.True(newIds.Count >= 2,
                    $"Batch minted {newIds.Count} id(s) for 2 nested AddComments.");

                var after = (await cluster.Client.ReadAssetGraphAsync(id)).EnsureOk();
                var texts = ((after.Field("comments") as JsonArray) ?? new JsonArray())
                            .Select(c => c!["text"]?.GetValue<string>() ?? "").ToArray();
                Assert.Contains("batched-a", texts);
                Assert.Contains("batched-b", texts);

                // ⭐⭐ Undoability is REPORTED and here it must be TRUE - both steps have derivable
                //    inverses, so the batch as a whole does.
                Assert.True(batch.Bool("undoable"),
                    "the Batch reported undoable:false though every nested AddComment has a derivable "
                  + "inverse. The inverse wiring regressed and the undo stack is silently losing entries.");

                foreach (var kv in newIds)
                {
                    var cid = kv.Value?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(cid)) continue;
                    await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
                    {
                        ["type"] = "RemoveComment", ["comment"] = cid!,
                    });
                }
                applied.Add("RemoveComment");
            }
            else
            {
                refused.Add($"Batch(AddComment) on Blueprint: {batch.Error}");
            }

            // ── ③ A REFUSAL that must stay a refusal ─────────────────────────
            var bogus = await cluster.Client.ApplyGraphCommandAsync(id, new JsonObject
            {
                ["type"] = "ThisVariantDoesNotExist",
            });
            Assert.False(bogus.Ok, "an unknown command type was ACCEPTED.");
            Assert.Contains("not a GraphCommand variant", bogus.Error ?? "", StringComparison.Ordinal);
        }
        else
        {
            refused.Add("no Blueprint asset indexed - the comment/batch half could not be driven");
        }

        _out.WriteLine($"applied: {string.Join(", ", applied)}");
        if (refused.Count > 0)
            _out.WriteLine($"REFUSED / NOT DRIVEN (logged, not skipped):\n  {string.Join("\n  ", refused)}");

        // ⛔ The floor: the union path must reach SOMETHING no typed verb could. A host refusing one
        //   variant is a finding worth logging; reaching none of them is the route being broken.
        Assert.True(applied.Count >= 2,
            "not one union command applied. The refusals were:\n  "
          + string.Join("\n  ", refused)
          + "\nThat is the command route failing, not a host specific.");
    }


    /// <summary>
    /// ⭐⭐⭐ <b>THE DISCOVERY/DOC-COVERAGE RAIL — every node kind the host offers resolves a schema.</b>
    /// 📄 §10.5 ③ *(the auto-discovery proof)* · §10.6 *(the doc-coverage rail)*.
    ///
    /// <para>⭐⭐⭐ <b>Why schema coverage is asserted at 100% and doc coverage is MEASURED.</b> A schema
    /// is derived from registries the host must already have populated to draw its palette ⇒ ⛔ a kind
    /// that cannot be described is a genuine defect, and the rail fails on it. ⚠ <b>Free-text prose is
    /// different</b>: §10.6 measured that it lives only in XML <c>&lt;summary&gt;</c> comments and is
    /// absent from every attribute — so demanding 100% today would redden on a gap this slice OPENED the
    /// door to closing (<c>EditDocAttribute</c>) rather than one it caused. ⇒ ⭐ the rail PRINTS the
    /// coverage and asserts a floor, so the number is visible and can be ratcheted.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Every_node_kind_the_host_offers_resolves_a_schema()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-discovery-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;
        Assert.True(list.Count > 0, "GET /assets returned nothing - no catalog to describe.");

        var pick = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "Blueprint") ?? list[0];
        var id   = pick!["assetId"]!.GetValue<string>();
        (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

        var kinds   = (await cluster.Client.ListNodeKindsAsync(id)).EnsureOk();
        var kindArr = (kinds.Field("kinds") as JsonArray)!;
        Assert.True(kindArr.Count > 0, "the node catalog is empty - there is nothing to describe.");

        _out.WriteLine($"catalog offers {kindArr.Count} kind(s)");

        var failures   = new List<string>();
        var documented = 0;
        var withParams = 0;

        foreach (var entry in kindArr)
        {
            var kind = entry!["kind"]!.GetValue<string>();
            var schema = await cluster.Client.GetNodeKindSchemaAsync(id, kind);

            if (!schema.Ok) { failures.Add($"{kind}: {schema.Error}"); continue; }

            // ⛔ A schema that describes nothing is not a schema. Every kind must at least come back with
            //   its own identity and a pin story (an empty pin list is a legitimate answer; a MISSING one
            //   means the projection failed).
            if (!string.Equals(schema.String("kind"), kind, StringComparison.Ordinal))
                failures.Add($"{kind}: schema came back describing '{schema.String("kind")}'");
            if (schema.Field("inputs") is null || schema.Field("outputs") is null)
                failures.Add($"{kind}: schema carries no pin lists");

            if (!string.IsNullOrWhiteSpace(schema.String("doc"))) documented++;
            if ((schema.Field("params") as JsonArray)?.Count > 0) withParams++;
        }

        // ⭐⭐⭐ THE AUTO-DISCOVERY PROOF: it fails the moment a registry adds a kind the route cannot
        //    describe, which is exactly what "measured, not authored" has to guarantee.
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {kindArr.Count} kind(s) could not be described:\n  "
          + string.Join("\n  ", failures.Take(15))
          + (failures.Count > 15 ? $"\n  … and {failures.Count - 15} more" : ""));

        var docPct = 100.0 * documented / kindArr.Count;
        _out.WriteLine($"DOC COVERAGE: {documented}/{kindArr.Count} kinds carry prose ({docPct:F0}%); "
                     + $"{withParams} resolve reflected params");

        // ⚠ The FLOOR, not the target. §10.6's EditDocAttribute is how this number gets ratcheted; the
        //   rail exists so the number is visible rather than assumed.
        Assert.True(docPct >= 25.0,
            $"only {docPct:F0}% of node kinds carry any documentation. The catalog's own Description is "
          + "the structural source and it has gone empty - that is a harvest regression, not a prose gap.");
    }

    /// <summary>
    /// ⭐⭐ <b>THE EDITOR COMMAND BUS — discoverable, describable, and invocable over MCP.</b>
    /// 📄 §10.7 · handoff §5 *(command coverage)*.
    ///
    /// <para>⭐⭐⭐ <b>The one assertion that matters most is the DISABLED refusal.</b> 📐
    /// <c>EditorCommandsImpl</c> will happily run a handler whose <c>IsEnabled</c> is false — the UI
    /// simply never offers it. ⇒ ⛔ without the pre-check, MCP would be the one path that can run what
    /// the editor greys out, which is precisely the parity this whole surface exists to preserve.</para>
    ///
    /// <para>⚠ It invokes only a SAFE command — <c>select-all</c>, whose effect is observable and whose
    /// inverse is <c>deselect</c>. ⛔ Invoking a destructive one to prove invocation works would be a rail
    /// that damages the thing it measures.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_editor_command_bus_is_discoverable_and_invocable_over_mcp()
    {
        await using var cluster = await EditorProcess.StartAsync("conf-uicommands-all", mode: "all");

        var assets = (await cluster.Client.ListAssetsAsync()).EnsureOk();
        var list   = (assets.Field("assets") as JsonArray)!;
        Assert.True(list.Count > 0, "GET /assets returned nothing.");

        var pick = list.FirstOrDefault(n => n!["kind"]!.GetValue<string>() == "Blueprint") ?? list[0];
        var id   = pick!["assetId"]!.GetValue<string>();

        // ⛔ The command set is PER DOCUMENT - assert the pre-open refusal, because it is the contract
        //   that makes "which commands?" a meaningful question rather than a global one.
        var beforeOpen = await cluster.Client.ListEditorCommandsAsync();
        _out.WriteLine($"before opening a document: {beforeOpen.StatusCode} {beforeOpen.Error ?? "ok"}");

        (await cluster.Client.OpenAssetAndSettleAsync(id)).EnsureOk();

        var commands = (await cluster.Client.ListEditorCommandsAsync()).EnsureOk();
        var arr = (commands.Field("commands") as JsonArray)!;
        _out.WriteLine($"the open document offers {arr.Count} editor command(s)");

        Assert.True(arr.Count > 0,
            "the open document offers NO editor commands. The set is built by the per-kind document "
          + "factory and hangs off AiCanvasContext.Commands - an empty set means the factory stopped "
          + "wiring it, which also breaks the canvas's own hotkeys.");

        // ⭐⭐ DOC COVERAGE for commands is asserted at 100%, unlike node kinds - and it can be, because
        //    EditorCommandDescriptor carries Description INLINE. No harvest, no excuse.
        var undocumented = arr.Where(c => string.IsNullOrWhiteSpace(c!["doc"]?.GetValue<string>()))
                              .Select(c => c!["id"]!.GetValue<string>()).ToArray();
        Assert.True(undocumented.Length == 0,
            $"{undocumented.Length} editor command(s) carry no description: "
          + string.Join(", ", undocumented)
          + ". The descriptor carries it inline, so this is a one-word fix at the registration site.");

        // ── describe one ─────────────────────────────────────────────────────
        var first  = arr[0]!["id"]!.GetValue<string>();
        var one    = (await cluster.Client.GetEditorCommandAsync(first)).EnsureOk();
        Assert.Equal(first, one.String("id"));

        var ghost = await cluster.Client.GetEditorCommandAsync("editor.this-does-not-exist");
        Assert.False(ghost.Ok, "an unknown command id was described successfully.");
        Assert.Equal(404, ghost.StatusCode);

        // ── invoke a SAFE one, and pin the DISABLED refusal ──────────────────
        var selectAll = arr.FirstOrDefault(c =>
            (c!["id"]!.GetValue<string>()).EndsWith("select-all", StringComparison.OrdinalIgnoreCase));

        if (selectAll != null)
        {
            var cmdId = selectAll["id"]!.GetValue<string>();
            var enabled = selectAll["isEnabled"]?.GetValue<bool>() ?? false;
            _out.WriteLine($"invoking {cmdId} (isEnabled={enabled})");

            var invoked = await cluster.Client.InvokeEditorCommandAsync(cmdId);

            if (enabled)
            {
                invoked.EnsureOk();
                Assert.True(invoked.Bool("invoked"));
            }
            else
            {
                // ⭐⭐⭐ THE PARITY ASSERTION: a command the editor greys out must be refused here too,
                //    with 409 - the request was well-formed and the id real; the live state refuses it.
                Assert.False(invoked.Ok,
                    $"{cmdId} reported isEnabled=false and MCP ran it anyway. That is the one path that "
                  + "can do what the editor refuses, which is exactly what this surface must never be.");
                Assert.Equal(409, invoked.StatusCode);
                Assert.Contains("DISABLED", invoked.Error ?? "", StringComparison.Ordinal);
            }
        }
        else
        {
            _out.WriteLine("no select-all command on this host - invocation half not driven (logged).");
        }

        var ghostInvoke = await cluster.Client.InvokeEditorCommandAsync("editor.this-does-not-exist");
        Assert.False(ghostInvoke.Ok, "invoking an unknown command id was accepted.");
        Assert.Equal(404, ghostInvoke.StatusCode);
    }
}
