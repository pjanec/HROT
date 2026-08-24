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
        ["entity-inspector"] = "the two hosts hold different worlds and the cluster cannot be given the "
                             + "editor's scenario (POST /scenario/load => NOT_SUPPORTED_HERE(editor.authoring)) "
                             + "- a FINDING, and the reason this entry exists",
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
    /// <para>⛔⛔ <b>And it pins the CROSS-LANE GAP honestly:</b> <c>hasMaster</c> is <c>false</c> in
    /// <c>--mode all</c> today, because the ack-gate's truth lives in a private field of
    /// <c>OrchestratorSubsystem</c> — a TIME-lane file this batch may not edit. ⇒ ⭐ the manifest SAYS the
    /// cluster step cannot be confirmed here, and this rail asserts it says so. 📌 The day the TIME lane
    /// exposes the master, this line reddens and is deleted — which is how a deferred gap stays visible.</para>
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

        // ⛔ The cross-lane gap, asserted so it cannot be forgotten.
        Assert.False(m["host"]!["hasMaster"]!.GetValue<bool>(),
            "the cluster manifest now reports a master — the TIME lane must have exposed "
          + "MasterSyncController. ⭐ Wire the dispatcher's ack-gate to it and delete this assertion.");

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
