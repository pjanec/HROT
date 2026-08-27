using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests.Conformance;

/// <summary>
/// ⭐⭐⭐ <b>PHASE 0 ② — MAP PARITY: what each host SUBMITS FOR DRAWING, compared as data.</b>
/// 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5.1 *(venue and channels)*, §5.2 *(what the
/// rail must and must NOT assert)*, §5.3 ②/③, §5.4 *(the limit)* and §5.6 *(<c>BP-487</c>, the seam this
/// needed).</para>
///
/// <para>🔒 <b>User, `2026-08-27`:</b> <i>"headless means no UI and UI is what we want the railed parity
/// for, so where the 'headless' is a blocker? why can't we compare what is shown on the maps, doesn't the
/// mcp server support reading the gizmo data?"</i> ⇒ ⭐⭐ <b>it does — <c>get_gizmo_frame</c>, and this file
/// is that comparison.</b> 📌 An earlier design section chose a HEADLESS venue for this and was a category
/// error: a panel publishes only when it DRAWS, so headless dumps come back empty. ⭐ The venue is TWO
/// WINDOWED processes under Xvfb, which is what <c>EditorProcess</c> already starts.</para>
///
/// <para>⛔⛔⛔ <b>WHAT THIS FILE MAY NEVER ASSERT — the standing constraint, and it is a USER RULING.</b>
/// 🔒 <i>"regarding ui and scenario editing and monitoring and debugging editor is obviously the source and
/// specimen … regarding network stuff like translator packs this is very different … similar situation is
/// with what modules and systems that should run in the subsystem, this is also very sensitive topic where
/// the unification does not apply."</i>
/// ⇒ ⭐⭐ this rail compares <b>SURFACES</b> — what a map OFFERS to draw — and each host's <b>internal
/// coherence</b>. ⛔ It must NEVER assert that two hosts RUN the same modules, systems or translators.
/// 📌 The trap is invisible from inside: a rail demanding the same primitive COUNT would be demanding the
/// same gizmo SYSTEMS, i.e. the same run-set, and would look like a successful unification.</para>
///
/// <para>⚠⚠ <b>THE LIMIT, stated so nobody over-claims it</b> *(§5.4)*: <c>get_gizmo_frame</c> reaches what
/// is <b>submitted for drawing</b>, ⛔ never what a human SEES. No rasterisation, no gizmo PICKING, no ImGui
/// hit-testing. ⇒ this rail REDUCES the eyes-only surface; it does not eliminate it, and a small
/// <c>--mode cgf</c> eyes pass stays part of acceptance. 📌 Claiming otherwise would repeat the
/// <c>CE-049</c> over-claim *(a rail that asserted a control was "present and enabled" rather than that it
/// had anything to offer)*.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemConformance")]
public sealed class TheMapsAgreeOnBothHostsRails
{
    private readonly ITestOutputHelper _out;
    public TheMapsAgreeOnBothHostsRails(ITestOutputHelper output) => _out = output;

    /// <summary>⭐ The same curated scenario both hosts are given, so their maps have the same subject.</summary>
    private const string Scenario = "hill-attack";

    /// <summary>⭐ CGF answers for the <c>"Scenario"</c> perspective — the one entry whose key and value differ.</summary>
    private const string MapPerspective = "Scenario";

    private const int SettleTicks = 3;

    /// <summary>
    /// ⭐⭐ One host's map frame: every primitive, plus the shape tally the comparison is keyed on.
    /// <para>⚠ <c>Truncated</c> is CARRIED, not dropped: a clipped frame makes *"the cluster draws fewer
    /// shapes"* unknowable rather than false, and a rail that cannot tell those apart is worse than none.</para>
    /// </summary>
    private sealed record MapFrame(
        int Count, bool Truncated, JsonArray Primitives,
        IReadOnlyDictionary<string, int> ByShape, IReadOnlyList<long> AnchorIds);

    /// <summary>
    /// ⭐⭐⭐ Put a host in the map perspective with the scenario loaded, then read its frame.
    /// <para>⭐ <b>switch → STEP → read</b>, the contract the panel-capture loop already obeys: a same-frame
    /// read returns the empty prefix. ⛔ Not a <c>Thread.Sleep</c> as the synchroniser — the step is the
    /// barrier; the small delay only covers the render thread's own frame.</para>
    /// </summary>
    private async Task<MapFrame> ReadMapAsync(EditorProcess host)
    {
        (await host.Client.LoadScenarioLiveAsync(Scenario, waitForReady: true)).EnsureOk();

        var switched = await host.Client.SwitchPerspectiveAsync(MapPerspective);
        Assert.True(switched.Ok,
            $"[{host.Mode}] refused to switch to the '{MapPerspective}' perspective: {switched.Error}. "
          + "⭐ Without it this host is not showing a map at all and the comparison has no subject.");

        await host.Client.StepAsync(SettleTicks);   // ⚠ may be NOT_SUPPORTED_HERE — not this rail's subject
        await Task.Delay(250);

        // ⭐⭐ max is generous on purpose: the default 500 truncates a busy editor frame, and a truncated
        //    frame would make the shape tally a sample rather than a census.
        var frame = (await host.Client.GetGizmoFrameAsync(max: 5000)).EnsureOk().DataOrThrow();

        var primitives = (frame["primitives"] as JsonArray)!;
        var byShape = new Dictionary<string, int>(StringComparer.Ordinal);
        var anchorIds = new List<long>();

        foreach (var p in primitives)
        {
            var shape = p!["shape"]!.GetValue<string>();
            byShape[shape] = byShape.TryGetValue(shape, out var n) ? n + 1 : 1;

            // ⭐ SpatialAnchor is the ENTITY MARKER — the only primitive that names which entity it is for.
            if (shape == "SpatialAnchor" && p["networkId"] is { } id)
                anchorIds.Add(id.GetValue<long>());
        }

        var result = new MapFrame(
            frame["count"]!.GetValue<int>(),
            frame["truncated"]!.GetValue<bool>(),
            primitives,
            byShape,
            anchorIds);

        _out.WriteLine(
            $"[{host.Mode}] map frame: count={result.Count} truncated={result.Truncated} "
          + $"shapes={{{string.Join(", ", result.ByShape.OrderBy(k => k.Key, StringComparer.Ordinal)
                                                .Select(k => $"{k.Key}:{k.Value}"))}}} "
          + $"anchors=[{string.Join(", ", result.AnchorIds.OrderBy(x => x))}]");

        return result;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE HEADLINE — <c>--mode all</c>'s map draws the scenario, and its shapes are the editor's.</b>
    /// 📄 §5.3 ② *("the highest-value item — reaches what no model-level rail can")*.
    ///
    /// <para>🔴 <b>This rail exists because of a user-found symptom</b> *(`2026-08-27`)*: <i>the 2D map shows
    /// NO entities on some scenarios — <c>hill-attack</c> loads and the map is empty.</i> ⭐ That is a claim
    /// about what the map SUBMITS, so it is exactly what this channel measures — and ⛔ it was unmeasurable
    /// before <c>BP-487</c>, because <c>GET /panels/_gizmo</c> answered 404 on every cluster host.</para>
    ///
    /// <para>⛔⛔ <b>SUBSET, not equality — and this is the constraint, not a weakening.</b> The two hosts
    /// legitimately run different gizmo systems: the editor adds authoring overlays *(tool ghosts, placement
    /// previews, the selection halo)* that a CGF node has no reason to draw. ⇒ ⭐ the assertion is *"every
    /// shape the CLUSTER draws is one the EDITOR also draws"* — which catches a CGF-invented or mis-projected
    /// shape while leaving the run-set alone. ⚠ Demanding equal counts would be demanding equal SYSTEMS.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_maps_draw_the_same_scenario_on_both_hosts()
    {
        await using var editor  = await EditorProcess.StartAsync("map-parity-editor");
        await using var cluster = await EditorProcess.StartAsync("map-parity-all", mode: "all");

        var a = await ReadMapAsync(editor);
        var b = await ReadMapAsync(cluster);

        // ⛔⛔ ANTI-VACUITY, BOTH DIRECTIONS — the discipline the panel rails already enforce.
        //    📌 CE-053's lesson: a rail that supplies the input it is testing proves nothing; and CE-064's:
        //    a correct, universal assertion over an EMPTY collection is unreachable, not passing.
        Assert.True(a.Count > 0,
            "the EDITOR's map submitted NO primitives, so the reference side of this comparison is empty and "
          + "a green here would prove nothing. ⭐ Check the scenario loaded and the Scenario perspective is "
          + "the one showing the map.");

        Assert.True(b.Count > 0,
            "🔴 --mode all's map submitted NO primitives while the editor's submitted "
          + $"{a.Count}. ⭐ THIS IS THE USER'S `2026-08-27` SYMPTOM — 'the 2D map shows no entities'. "
          + "⛔ Do NOT weaken this rail: the gizmo systems that populate CGF's buffer are registered in "
          + "CgfSubsystem (GlobalGizmoManager + StatelessGizmoSystem, ~:851-890) and its DebugGizmoLayer "
          + "draws it (~:1096).");

        // ⚠ Truncation makes the tally a sample. Say so loudly rather than comparing samples.
        Assert.False(a.Truncated || b.Truncated,
            $"a map frame was TRUNCATED (editor={a.Truncated}, cluster={b.Truncated}), so the shape tally is "
          + "a sample and the subset claim below would be about the sample, not the frame. ⭐ Raise `max`.");

        // ⭐⭐⭐ THE SUBSET CLAIM — surface parity without touching the run-set.
        var clusterOnlyShapes = b.ByShape.Keys.Where(s => !a.ByShape.ContainsKey(s))
                                             .OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.True(clusterOnlyShapes.Length == 0,
            $"--mode all's map draws shape(s) the editor never draws: [{string.Join(", ", clusterOnlyShapes)}]. "
          + "⭐ Both hosts draw through the SAME gizmo registries, so a cluster-only shape means either a "
          + "CGF-invented primitive or a mis-projected union field — ⛔ not a legitimate host difference. "
          + "⚠ If CGF genuinely gained a map affordance the editor lacks, say so HERE, with the measurement.");

        // ⭐ And the reverse direction is REPORTED, never asserted: editor-only shapes are the expected
        //   state (authoring overlays), so this is diagnostics for the reader, not a verdict.
        var editorOnlyShapes = a.ByShape.Keys.Where(s => !b.ByShape.ContainsKey(s))
                                            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        _out.WriteLine($"editor-only shapes (EXPECTED — authoring overlays): [{string.Join(", ", editorOnlyShapes)}]");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The map marks the ENTITIES, and it marks the ones the world actually holds.</b>
    ///
    /// <para>⛔⛔ <b>Why this is separate from the headline rail, and why it is the stronger claim.</b>
    /// 📌 <c>count &gt; 0</c> is satisfied by a single grid line or one leftover annotation — so a map that
    /// draws its terrain and NONE of its entities passes the rail above. ⚠ That is precisely the reported
    /// symptom's shape *("the scenario loads, the map is empty")*, so the headline assertion alone would have
    /// been the third instance of the rail-blindness pattern this programme has now named twice
    /// *(<c>CE-049</c> asserted presence rather than substance; <c>CE-064</c> asserted over an empty set)*.</para>
    ///
    /// <para>⭐⭐ <b>Anchored to the WORLD, not to the other host.</b> The claim is per-host internal
    /// coherence — *"this host draws a marker for the entities THIS host holds"* — which is §5.2's second
    /// MUST. ⛔ Comparing the two hosts' anchor counts to each other would drift into run-set territory the
    /// moment one host culls off-screen entities and the other does not.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Each_hosts_map_marks_the_entities_its_own_world_holds()
    {
        await using var editor  = await EditorProcess.StartAsync("map-anchors-editor");
        await using var cluster = await EditorProcess.StartAsync("map-anchors-all", mode: "all");

        foreach (var host in new[] { editor, cluster })
        {
            var frame = await ReadMapAsync(host);

            // ⚠ `.Array()`, NOT `.Field("entities")`: 📐 measured — GET /entities returns a BARE JsonArray
            //   (DebugApiService.ListEntities builds `arr` and returns it), so an envelope lookup yields null
            //   and NREs. 📌 The first cut of this rail did exactly that and the T3 run caught it.
            var entities = (await host.Client.ListEntitiesAsync()).EnsureOk().Array();
            int worldCount = entities.Count;

            _out.WriteLine($"[{host.Mode}] world holds {worldCount} entities; map anchors {frame.AnchorIds.Count}");

            // ⛔ Anti-vacuity again: a host with an empty world cannot demonstrate anything about markers.
            Assert.True(worldCount > 0,
                $"[{host.Mode}] holds NO entities after loading '{Scenario}' live, so this host cannot "
              + "demonstrate that its map marks them. ⭐ That is a scenario-load failure, not a map failure — "
              + "The_two_hosts_hold_the_same_loaded_world diagnoses it.");

            Assert.True(frame.AnchorIds.Count > 0,
                $"🔴 [{host.Mode}] holds {worldCount} entities but its map submitted NO SpatialAnchor "
              + "primitive for any of them — the map is drawing its scene and none of its entities. "
              + "⭐ THIS IS THE PRECISE SHAPE OF THE USER'S `2026-08-27` SYMPTOM, and note that the "
              + "headline rail's `count > 0` does NOT catch it: terrain alone satisfies that.");

            // ⭐⭐ Every anchor names an entity the world really has. ⛔ Not the converse: culling and
            //   per-host layer settings legitimately leave some entities unmarked, so requiring full
            //   coverage would be asserting a run-set (which culling modules run) — the forbidden claim.
            // ⚠ A row without a networkId is skipped rather than assumed: node-local rows are per-host by
            //   construction (the entity-inspector's own divergence entry names them), and reading a missing
            //   field as 0 would quietly widen `known` by the very id the stray filter excludes.
            var known = entities.Where(e => e?["networkId"] is not null)
                                .Select(e => e!["networkId"]!.GetValue<long>())
                                .ToHashSet();
            var strays = frame.AnchorIds.Where(id => id != 0 && !known.Contains(id))
                                       .Distinct().OrderBy(x => x).ToArray();

            Assert.True(strays.Length == 0,
                $"[{host.Mode}] the map anchors networkId(s) [{string.Join(", ", strays)}] that "
              + "GET /entities does not list. ⭐ A marker for an entity that no longer exists is a stale "
              + "gizmo — the map showing something the world does not have.");
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-487</c> — THE MANIFEST TELLS THE TRUTH ABOUT EACH HOST'S MAP FEED.</b>
    /// 📄 §5.6 · <c>RULINGS.md</c> <c>R-133</c> *("the capability manifest is MEASURED, never DECLARED …
    /// a cell reported present that silently no-ops is worse than an absent one")*.
    ///
    /// <para>🔴 <b>The lie this replaces.</b> 📐 <c>CapabilityManifest</c> hard-coded
    /// <c>panels.gizmo = true</c> on <b>every</b> perspective row, on the strength of a comment calling the
    /// primitive buffer a *"process-wide static"*. ⛔ It is one buffer <b>per subsystem</b> and ExCon has
    /// none ⇒ <c>--mode all</c> advertised a feed that answered <b>404</b>. ⭐ The cell now comes from
    /// <c>dispatcher.Matrix()</c>, so this rail is the control on the FORWARDING — taken on the
    /// <b>CONSTRUCTED OBJECT</b> over MCP, ⛔ not on the composition root's source text, which is what the
    /// silent-default rule asks for.</para>
    ///
    /// <para>⚠⚠ <b>Why it lives HERE and not beside <c>time.drive</c> in
    /// <c>The_manifest_describes_this_host_truthfully</c>, where it belongs.</b> 📐 Measured `2026-08-27`:
    /// that rail is RED before its matrix loop is reached, on <c>unclassifiedRoutes</c> =
    /// <c>[/missions/{networkId}, …/run, …/task, …/tasks]</c> — a missing prefix in
    /// <c>CapabilityManifest.CapabilityFor</c>, which is the MCP lane's file and is now reported a THIRD
    /// time. ⇒ ⭐ an assertion added there would sit behind another lane's red and <b>gate nothing</b>.
    /// ⛔ This is a declared workaround, not a preference — 📌 the pointer in that rail says so, and says to
    /// move this back (and keep only ONE copy) when <c>/missions</c> is classified.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_manifest_tells_the_truth_about_each_hosts_map_feed()
    {
        await using var cluster = await EditorProcess.StartAsync("map-manifest-all", mode: "all");

        var m = (await cluster.Client.GetCapabilitiesAsync()).EnsureOk().DataOrThrow();
        var matrix = (m["matrix"] as JsonObject)!;

        var verdicts = new List<string>();

        foreach (var (perspective, row) in matrix)
        {
            bool claimed = row!["panels.gizmo"]!.GetValue<bool>();

            (await cluster.Client.SwitchPerspectiveAsync(perspective)).EnsureOk();
            await cluster.Client.StepAsync(1);
            await Task.Delay(150);

            var gizmo = await cluster.Client.GetGizmoFrameAsync(max: 1);
            verdicts.Add($"{perspective}: claims={claimed} answers={gizmo.StatusCode}");

            if (claimed)
                Assert.True(gizmo.Ok,
                    $"the matrix claims '{perspective}' has a gizmo feed, but GET /panels/_gizmo answered "
                  + $"{gizmo.StatusCode}: {gizmo.Error}. ⭐ Check that subsystem's CreateDebugProvider still "
                  + "passes `gizmoBuffer:` — BP-487's whole failure mode was a caller that HAD the buffer "
                  + "and did not pass it.");
            else
                Assert.False(gizmo.Ok,
                    $"the matrix claims '{perspective}' has NO gizmo feed, yet GET /panels/_gizmo answered "
                  + "OK. ⛔ A cell that under-reports is still the manifest lying — just in the direction "
                  + "nobody notices.");
        }

        _out.WriteLine($"panels.gizmo: [{string.Join(" | ", verdicts)}]");

        // ⛔⛔ ANTI-VACUITY, and it is the assertion that matters most: if every cell were `false` the loop
        //    above would pass while the cluster had NO map feed at all — 📌 which is EXACTLY the state
        //    BP-487 found. ⭐ Measured: CGF ("Scenario") and IG each build a DebugPrimitiveBuffer, and
        //    SimHost builds one only when it has a Visualization (null on a headless node).
        // ⚠ The bound is 2, not 3, and that is DELIBERATE: SimHost's feed is conditional on a window, so
        //   demanding 3 would make this rail depend on whether --mode all gave SimHost a viewport — a fact
        //   about the RUN-SET, which §5.2 forbids this rail from asserting.
        int withFeed = matrix.Count(kv => kv.Value!["panels.gizmo"]!.GetValue<bool>());
        Assert.True(withFeed >= 2,
            $"only {withFeed} of {matrix.Count} perspectives report a gizmo feed [{string.Join(" | ", verdicts)}]. "
          + "⭐ CGF/Scenario and IG both build a buffer unconditionally, so fewer than 2 means the provider "
          + "wiring regressed. ⛔ A green with 0 would mean the two-host MAP comparison reads nothing.");

        // ⭐ And the one host that must NOT claim it — the honest FALSE that proves the cell is measured
        //   rather than defaulted. ⛔ If ExCon ever gains a map, delete this deliberately.
        Assert.False(matrix["ExCon"]!["panels.gizmo"]!.GetValue<bool>(),
            "ExCon reports a gizmo feed, but a repo-wide search finds no DebugPrimitiveBuffer in Hrot.ExCon "
          + "at all. ⭐ Either it gained a map, or the cell went back to being hard-coded true.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>PHASE 0 ③(2) — CENTRING ON AN ENTITY DOES NOT KILL THE HOST.</b>
    /// 📄 §5.3 ③ · <c>CenterOnEntitySystem</c>'s own remarks *(the <c>CE-051</c> two-way reconciliation)</para>
    ///
    /// <para>🔴 <b>The user's second `2026-08-27` symptom, verbatim in the design: *"center-on-entity
    /// CRASHES"* on <c>--mode cgf</c></b>, with the suspicion recorded that the <c>E3</c>/<c>CE-051</c> path
    /// is the culprit — i.e. mine. ⭐ So this rail is written to REPRODUCE it, not to confirm health.</para>
    ///
    /// <para>⭐⭐ <b>Driven through <c>POST /entities/{id}/focus</c></b>, which publishes the very
    /// <c>CenterOnEntityCommand</c> the context menu publishes ⇒ the same shared system executes. ⚠⚠ <b>What
    /// this does NOT cover, said plainly:</b> the menu CLICK itself — ImGui hit-testing is beyond
    /// <c>get_gizmo_frame</c>'s reach *(§5.4)*, so if the crash lives in the menu-build path rather than in
    /// the command's execution, this rail stays green and the eyes pass is what finds it. ⭐ That is a real
    /// limit, and reporting it is better than a rail that implies it covered the click.</para>
    ///
    /// <para>⭐ <b>Single-host — "does it crash" is a per-host coherence question</b> *(§5.2's second MUST)*,
    /// ⛔ not a parity comparison.</para>
    ///
    /// <para>⛔⛔⛔ <b><c>--mode cgf</c> IS NOT A RUNNABLE VENUE, and this was MEASURED, not assumed
    /// (`2026-08-27`).</b> 📐 A first cut of this rail started <c>mode: "cgf"</c> and the process died with
    /// <b>exit code 134</b> before serving <c>/status</c>:
    /// <c>InvalidOperationException: [DdsIdAllocator] Publication match not established within 30 s.
    /// Hrot.Orchestrator must be running before this node starts.</c>
    /// *(<c>DdsIdAllocatorHelper.EnsureRouting</c> → <c>HrotNodeBuilder.Build</c> →
    /// <c>CgfSubsystem.Initialize:511</c>)*.
    /// ⇒ ⭐⭐ <b>a lone CGF node has an unmet PRECONDITION; it is not a defect and not the user's crash.</b>
    /// ⚠ The design's phrase *"the two new `--mode cgf` symptoms"* is shorthand for *"CGF's symptoms"* — the
    /// user was running <c>--mode all</c>, which supplies the orchestrator. ⇒ ⭐ this rail runs
    /// <c>--mode all</c> and drives the <c>Scenario</c> perspective, where CGF IS the map host.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Centring_on_an_entity_does_not_kill_the_cgf_host()
    {
        await using var cgf = await EditorProcess.StartAsync("map-center-all", mode: "all");

        (await cgf.Client.LoadScenarioLiveAsync(Scenario, waitForReady: true)).EnsureOk();
        (await cgf.Client.SwitchPerspectiveAsync(MapPerspective)).EnsureOk();
        await cgf.Client.StepAsync(SettleTicks);
        await Task.Delay(250);

        var entities = (await cgf.Client.ListEntitiesAsync()).EnsureOk().Array();
        var ids = entities.Where(e => e?["networkId"] is not null)
                          .Select(e => e!["networkId"]!.GetValue<long>())
                          .Where(id => id > 0)
                          .OrderBy(id => id)
                          .ToArray();

        // ⛔ Anti-vacuity: focusing nothing crashes nothing.
        Assert.True(ids.Length > 0,
            $"--mode all's Scenario perspective holds no networked entities after loading '{Scenario}' live, so "
          + "there is nothing to "
          + "centre on and this rail would pass without exercising the path at all.");

        _out.WriteLine($"[all/Scenario] centring on each of [{string.Join(", ", ids)}]");

        foreach (var id in ids)
        {
            var focus = await cgf.Client.FocusEntityAsync(id);
            Assert.True(focus.Ok,
                $"POST /entities/{id}/focus answered {focus.StatusCode}: {focus.Error}");

            // ⭐⭐ THE STEP IS THE POINT. The publish itself is harmless; CenterOnEntitySystem runs in
            //    PostSimulation, so the command is only EXECUTED on the next kernel tick. ⛔ A rail that
            //    asserted only on the POST would prove nothing about the system that reads it.
            await cgf.Client.StepAsync(2);
            await Task.Delay(100);

            // ⭐ The liveness check: a host that died answers nothing.
            var status = await cgf.Client.GetStatusAsync();
            Assert.True(status.Ok,
                $"after centring on entity {id} and stepping, --mode all no longer answers GET /status "
              + $"({status.StatusCode}: {status.Error}). 🔴 THAT IS THE USER'S REPORTED CRASH, reproduced. "
              + "⭐ Look first at CenterOnEntitySystem.TryResolvePosition (NetworkTransform then SimTransform "
              + "on a host that owns neither) and at the Func<MapCamera?> it was handed — a canvas replaced "
              + "by a perspective switch is exactly why that parameter is a delegate.");
        }

        // ⭐ And the camera actually moved somewhere real — ⛔ not to the origin, which is the precise
        //   failure CE-051 replaced (CGF set Camera.Target directly and MapCamera.Update overwrote it from
        //   _targetTarget, which CGF never set, so centring sent the view to 0,0).
        var frame = await ReadMapAsync(cgf);
        Assert.True(frame.Count > 0,
            "after centring, the cluster's map submits nothing at all — the camera was moved somewhere with "
          + "no content, which is the ORIGIN-snap shape CE-051 exists to prevent.");
    }
}
