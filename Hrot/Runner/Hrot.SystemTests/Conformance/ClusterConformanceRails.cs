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
    private static async Task<Dictionary<string, (string Id, string Model)>> CaptureByKindAsync(
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
        ["details"] = "run state genuinely differs (the editor is halted/Planning, the cluster node's "
                    + "world ticks) and focus follows from which panes each host has. This rail does not "
                    + "equalise the clocks by design.",
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
}
