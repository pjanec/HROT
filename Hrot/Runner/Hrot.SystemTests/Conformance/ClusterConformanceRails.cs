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
        // ⭐ AUTHORING — the editor's reason to exist; none of it is ported to a cluster node yet (D3).
        "blackboard-authoring", "my-blueprint", "graph-signature", "variables", "watch", "bookmarks",
        "ai-breakpoints", "graph-canvas", "details", "runtime-inspector", "diagnostics",
        "entity-blueprints", "data-breakpoint-manager",
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
    /// <item><c>entity-inspector</c> — the editor's world is empty at boot and the IG node's already holds one
    /// entity. ⛔⛔ <b>And the worlds CANNOT be equalised today:</b> <c>POST /scenario/load</c> in
    /// <c>--mode all</c> answers <c>NOT_SUPPORTED_HERE(editor.authoring)</c> — measured — because a cluster
    /// loads through the orchestrator's 2PC <c>PrepareLive</c>, not through <c>IEditorLogic</c>. ⇒ ⭐ the
    /// design's own sequence *("load S in both, then diff")* is <b>not executable yet</b>, so any
    /// world-CONTENT diff would be comparing two different worlds;</item>
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
        ["entity-inspector"] = "the IG node's inspector also lists node-local entities (networkId 0, unnamed) "
                             + "that the editor has no counterpart for; the SCENARIO content is compared for "
                             + "real by The_two_hosts_hold_the_same_loaded_world",
        ["spawner"]          = "host-specific catalogue: the editor offers platforms, ExCon offers composites",
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
    /// <para>🔴🔴 <b>And the ids are NOT compared, for a measured reason — this rail found it.</b> 📐 Both
    /// hosts load the same seven entities, in the same order, with the same names; their <c>networkId</c>s are
    /// <b>editor 1000–1007 vs cluster 2–9</b>, because the ids come from DIFFERENT ALLOCATOR AUTHORITIES *(the
    /// editor's offline allocator vs the cluster's centralised <c>DdsIdAllocatorServer</c> — `mgmt-1` §5.7)*.
    /// ⇒ ⭐ the NAMES are what a shared scenario guarantees; the id bases are a separate, filed finding, and
    /// <see cref="The_two_hosts_allocate_ids_from_different_authorities"/> pins the difference so it cannot
    /// change unnoticed.</para>
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
    /// ⚠⚠ <b>THE TRIPWIRE ON A FILED FINDING: the two hosts number the same entities differently.</b>
    /// 📄 charter <c>D6</c> *(deterministic network ids)* · <c>docs/DESIGN_Deterministic_Network_Ids.md</c> ·
    /// <c>docs/designs/mgmt-1/DESIGN.md</c> §5.7 *(the centralised allocator)*.
    ///
    /// <para>📐 Measured `2026-08-24`, same scenario loaded live in both: the editor's ids start at <b>1000</b>
    /// and the cluster's at <b>2</b>. ⭐ Each host is internally deterministic — ⛔ but a network id is NOT a
    /// portable name for an entity ACROSS hosts, which matters to anything that records an id in one host and
    /// replays it in another.</para>
    ///
    /// <para>⭐⭐ Asserted rather than merely noted, in the same spirit as the declared-divergence control: if
    /// the two authorities are ever unified this rail REDDENS and is deleted — which is how a known gap gets
    /// closed on purpose instead of drifting.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_two_hosts_allocate_ids_from_different_authorities()
    {
        await using var editor  = await EditorProcess.StartAsync("conf-ids-editor");
        await using var cluster = await EditorProcess.StartAsync("conf-ids-all", mode: "all");

        await LoadLiveAsync(editor,  _out);
        await LoadLiveAsync(cluster, _out);

        var idsEditor  = (await NetworkedEntitiesAsync(editor,  _out)).Select(e => e.Id).OrderBy(i => i).ToArray();
        var idsCluster = (await NetworkedEntitiesAsync(cluster, _out)).Select(e => e.Id).OrderBy(i => i).ToArray();

        _out.WriteLine($"editor ids : [{string.Join(", ", idsEditor)}]");
        _out.WriteLine($"cluster ids: [{string.Join(", ", idsCluster)}]");

        Assert.NotEmpty(idsEditor);
        Assert.NotEmpty(idsCluster);

        // ⭐ Each host must at least be self-consistent: ids unique within a host.
        Assert.Equal(idsEditor.Length,  idsEditor.Distinct().Count());
        Assert.Equal(idsCluster.Length, idsCluster.Distinct().Count());

        Assert.False(idsEditor.SequenceEqual(idsCluster),
            "the editor and --mode all now allocate the SAME network ids for the same scenario. ⭐ That is an "
          + "IMPROVEMENT, not a failure: the two allocator authorities have been unified (charter D6). "
          + "⛔ Delete this rail and remove the id-divergence finding from the tracker.");
    }

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
}
