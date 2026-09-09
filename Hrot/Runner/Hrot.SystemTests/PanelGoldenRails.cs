using System.Text.Json.Nodes;
using Hrot.SystemTests.Goldens;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b><c>N3</c> — the first slice of panel goldens, and <c>D7</c>'s pairing rule enforced by
/// construction.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N3</c> · §4 <c>D7</c> · §4b · §6 *(the capture CONTRACT)*.
///
/// <para>⭐⭐⭐ <b>A BUDGET, NOT A SWEEP</b> *(§4's named trap: 150 golden files nobody reads, all re-blessed
/// the first time someone touches a panel base class)*. ⭐ <b>Six goldens across all four perspectives</b>,
/// each one paired with 1–3 assertions on the fields that carry MEANING — 📌 <c>D7</c>: a re-bless may change
/// the noise but ⛔ cannot silently change the meaning, because the assertion still fires.</para>
///
/// <para>⛔⛔ <b>WHY SIX AND NOT FORTY — the measurement that shaped this, and it contradicts the design's
/// own rule of thumb.</b> §4 says *"golden the large derived structure (a variable table, a node tree, 200
/// rows); assert the 3–10 field panels."* 📐 Measured `2026-08-24` over all <b>41</b> captured dumps of
/// <c>hill-attack</c>:
/// <list type="bullet">
/// <item>⭐ <b>only TWO dumps exceed 10 KB</b> — <c>editor/_gizmo</c> *(128 KB)* and <c>fdp_message_log</c>
/// *(19–30 KB)*, and <b>both are excluded</b>: the log is a declared-volatile kind *(<c>N1</c>)* and the
/// gizmo frame is §9's <c>Q2</c> *("not in the first slice — a 64-byte union projected per shape, high churn
/// for low early value")*;</item>
/// <item>⚠ <b>the rest are 86 B – 4.1 KB</b> ⇒ by the design's own field-count rule most of the editor is
/// assertion-territory, and the golden budget is genuinely SMALL. ⭐ That is a finding about the panels, not
/// a shortfall in the batch.</item>
/// </list></para>
///
/// <para>⛔⛔ <b>AND THE CEILING WORTH NAMING: the authoring perspectives can only be captured EMPTY.</b>
/// 📐 The debug API has <b>no endpoint that opens an AI asset</b> *(48 routes enumerated; <c>/scenario/load</c>
/// is the only content-loading one)* ⇒ the BTree / HSM / Blueprint panels publish their no-asset state and a
/// golden of them pins the skeleton, not a populated document. ⭐ Still worth having *(the section list, the
/// byte budgets and the empty-state contract are all real regressions if they move)*, ⛔ but it is not
/// coverage of authoring. Filed as <c>MX-013</c>.</para>
///
/// <para>⚠ <b>Every capture goes through <c>SwitchPerspectiveAndSettleAsync</c></b> — §6 is a CONTRACT since
/// <c>HN-007</c>: the API's job queue drains before the panels draw, so a same-frame read returns the
/// EMPTY PREFIX of the current frame. 🔴 A golden captured that way would be a golden of nothing.</para>
///
/// <para>⭐ <b>Capture:</b> <c>PANEL_GOLDEN_CAPTURE=1</c>, then INSPECT the files before committing.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemGolden")]
public sealed class PanelGoldenRails : IClassFixture<GoldenCaptureFixture>
{
    private readonly GoldenCaptureFixture _fx;
    private readonly ITestOutputHelper _out;

    public PanelGoldenRails(GoldenCaptureFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private const string Scenario = GoldenCaptureFixture.Scenario;

    /// <summary>⭐ §6's contract: act, step, then read. Five ticks is what <c>N1</c> measured as sufficient.</summary>
    private const int SettleTicks = 5;

    /// <summary>
    /// ⭐⭐⭐ <b>THE BUDGET — the whole of it, in one table, so widening it is a visible act.</b>
    /// ⭐ Spread deliberately across all four perspectives *(📌 <c>N0</c> measured 11 panels reachable only
    /// from Blueprint — a slice that stayed in the default perspective would bless a quarter of the editor
    /// and call it a baseline)*.
    /// </summary>
    public static readonly (string Perspective, string PanelId)[] Budget =
    {
        // ⭐ Scenario — the only perspective whose panels carry LOADED WORLD content (see the class remarks).
        ("Scenario",  "editor_fdp_inspector"),         // 8 entities, ids 1000–1007
        ("Scenario",  "editor_shared_orbat"),          // the ORBAT hierarchy: 1 root + 4 subordinates
        ("Scenario",  "editor_spawner"),               // the TKB catalogue the spawner offers
        // ⭐ the authoring perspectives — skeleton + budget constants (empty-state, see the ceiling above)
        ("BTree",     "ai_blackboard_variables_btree"),
        ("HSM",       "ai_blackboard_variables_hsm"),
        ("Blueprint", "ai_my_blueprint_blueprint"),    // the seven-section skeleton
        // ⭐⭐⭐ cgf==editor SLICE 1, `2026-08-25` — A DELIBERATE WIDENING BY TWO, and the reason is a
        //    real hole rather than "more coverage is better" (the trap this table's own doc names).
        // 📐 The slice's acceptance is `ClusterConformanceRails.The_asset_panels_are_the_same_on_both_hosts`
        //    — graph-canvas · my-blueprint · watch, editor vs `--mode all`. ⛔ That rail compares the two
        //    hosts to EACH OTHER, so it stays green if BOTH regress the same way — and after this slice
        //    both hosts render those panels from the SAME AiShared classes, which makes an identical
        //    regression the LIKELY shape, not a far-fetched one.
        // ⭐ my-blueprint already had its reference here; these two did not. ⇒ two goldens close the
        //   third side of the triangle: the editor's own dump, pinned.
        ("Blueprint", "ai_canvas_blueprint"),          // the canvas's no-document state
        ("Blueprint", "ai_watch_blueprint"),           // the watch's empty table + column set
    };

    public static TheoryData<string, string> BudgetCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (p, id) in Budget) data.Add(p, id);
        return data;
    }

    // ══ the goldens ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The golden half of <c>D7</c>: *"did anything change?"*</b> — the whole dump, byte-for-byte
    /// after canonical key ordering.
    /// <para>⛔ On a red, the failure names the JSON PATHS that moved *(<c>N2</c>'s done-when)* — ⚠ and a
    /// legitimate change is re-blessed by a capture run and a **reviewed** file diff, never by widening an
    /// ignore-list.</para>
    /// </summary>
    [SystemSmokeTheory]
    [MemberData(nameof(BudgetCases))]
    public async Task Panel_matches_its_golden(string perspective, string panelId)
    {
        var model = await DumpAsync(perspective, panelId);

        var diffs = GoldenStore.CompareOrWrite(Scenario, panelId, model);

        if (GoldenStore.CaptureMode)
        {
            _out.WriteLine($"CAPTURED {GoldenStore.PathFor(Scenario, panelId)}");
            return;
        }

        Assert.True(diffs.Count == 0,
            $"'{panelId}' ({perspective}) differs from its golden in {diffs.Count} place(s):\n  "
          + string.Join("\n  ", diffs)
          + $"\n⭐ If this change is intended: re-capture with PANEL_GOLDEN_CAPTURE=1 and REVIEW the file "
          + "diff. ⛔ Never widen PanelNormalizer.IgnoredPaths to go green.");
    }

    // ══ the pairing — D7's rule, one case per golden ═══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>D7</c>'s PAIRING, and the reason it exists.</b> 📌 §4's worked example is this repo's own
    /// <c>R-132</c> defect: params silently zeroed, the platoon drove to <c>(0,0)</c>, every rail green — a
    /// golden would have reddened *(if one had existed)* but a **bulk re-bless was entirely plausible**.
    /// ⇒ ⭐⭐ these assertions are what a re-bless cannot silence.
    ///
    /// <para>⭐ One case per golden, so a failure names the panel AND the claim. ⛔ Not one case asserting
    /// six panels — that would print the same message for six different defects.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Meaning_entity_inspector_lists_the_eight_authored_entities()
    {
        var m = await DumpAsync("Scenario", "editor_fdp_inspector");

        Assert.Equal(8, m!["totalEntityCount"]!.GetValue<int>());

        var ids = (m["entities"] as JsonArray)!
                  .Select(e => e!["networkId"]?.GetValue<long?>())
                  .Where(v => v.HasValue).Select(v => v!.Value).ToArray();

        // ⭐ The authored ids, and N1 proved they repeat. ⛔ Not "there are some entities" — that passes on a
        //   world that loaded the wrong file.
        Assert.Equal(new long[] { 1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007 }, ids);
    }

    /// <summary>
    /// ⭐ <b>The ORBAT's SHAPE — a HIERARCHY, not a flat list.</b>
    ///
    /// <para>📐 Measured `2026-08-24`, and it corrected my first guess: <b>THREE</b> depth-0 roots, not one —
    /// the <c>Tank Platoon</c> with four subordinates plus two standalone <c>M1 Abrams</c>. ⭐ Asserting
    /// "exactly one root" would have been a rail encoding my assumption instead of the world.</para>
    ///
    /// <para>⚠⚠ <b>And it exposes something worth stating: the ORBAT lists SEVEN nodes for EIGHT entities.</b>
    /// 📐 Its <c>entityId</c>s are ECS indices <c>0,1,2,3,4,6,7</c> — index <b>5</b> is absent. ⭐ Not asserted
    /// as a defect *(an entity may legitimately be outside the order of battle)*, ⛔ but the counts are pinned
    /// so a silent change in WHICH entities appear reddens.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Meaning_orbat_is_a_hierarchy_not_a_flat_list()
    {
        var m = await DumpAsync("Scenario", "editor_shared_orbat");
        var nodes = (m!["nodes"] as JsonArray)!;

        Assert.Equal(7, nodes.Count);

        var roots = nodes.Where(n => n!["depth"]!.GetValue<int>() == 0).ToArray();
        Assert.Equal(3, roots.Length);

        // ⭐ The claim that makes it an ORBAT: one of the roots actually commands subordinates.
        var withChildren = roots.Where(n => n!["hasChildren"]!.GetValue<bool>()).ToArray();
        Assert.Single(withChildren);
        Assert.Equal(4, nodes.Count(n => n!["depth"]!.GetValue<int>() == 1));
    }

    /// <summary>
    /// ⭐ The spawner offers the TKB catalogue. ⚠ <c>tkbId 100</c> is named on purpose: 📌 <c>HN-014</c>
    /// measured that <c>POST /entities/spawn</c> reports success for a type that does not exist, and
    /// <c>100</c> *(M1 Abrams)* is the id that actually spawns. ⇒ this assertion pins the catalogue an agent
    /// must pick from.
    /// </summary>
    [SystemSmokeFact]
    public async Task Meaning_spawner_offers_the_tkb_catalogue()
    {
        var m = await DumpAsync("Scenario", "editor_spawner");
        var entries = (m!["filteredEntries"] as JsonArray)!;

        Assert.True(entries.Count >= 8, $"the spawner offers only {entries.Count} types");
        Assert.Contains(100L, entries.Select(e => e!["tkbId"]!.GetValue<long>()));
    }

    /// <summary>
    /// ⭐⭐ <b>The blackboard budgets — and this pairing is load-bearing beyond the panel.</b>
    /// 📌 <c>R-39</c> is an UNRECONCILED ruling: the param region is *documented* as 60 bytes and *enforced*
    /// as 100 by analyzer <c>FDP_001</c>. ⇒ ⭐ pinning what the panel PUBLISHES *(<c>inlineBudget</c>)* means
    /// the day that number moves, a rail says so instead of a doc drifting further from the analyzer.
    /// </summary>
    [SystemSmokeTheory]
    [InlineData("BTree", "ai_blackboard_variables_btree")]
    [InlineData("HSM",   "ai_blackboard_variables_hsm")]
    public async Task Meaning_blackboard_panel_publishes_the_byte_budgets(string perspective, string panelId)
    {
        var m = await DumpAsync(perspective, panelId);

        Assert.Equal(100, m!["inlineBudget"]!.GetValue<int>());
        Assert.Equal(928, m["heavyBudget"]!.GetValue<int>());
        // ⚠ No asset is open (no API can open one — MX-013), so the count is 0 BY CONSTRUCTION. Asserted so
        //   the golden's emptiness is a stated premise rather than an accident nobody noticed.
        Assert.Equal(0, m["variableCount"]!.GetValue<int>());
    }

    /// <summary>
    /// ⭐ The My-Blueprint skeleton: the seven sections a designer navigates by. ⛔ Their ORDER is the claim —
    /// <c>sortOrder</c> is what the panel renders by, so a reshuffle is a UX change, not noise.
    /// </summary>
    [SystemSmokeFact]
    public async Task Meaning_my_blueprint_publishes_its_section_skeleton()
    {
        var m = await DumpAsync("Blueprint", "ai_my_blueprint_blueprint");
        var sections = (m!["sections"] as JsonArray)!;

        var ids = sections.Select(s => s!["id"]!.GetValue<string>()).ToArray();
        // 📐 Measured `2026-08-24` — SEVEN sections, in this order. ⚠ My first cut guessed six and named a
        //   non-existent "eventgraph"; the rail refused the guess, which is what a rail is for.
        Assert.Equal(
            new[] { "graphs", "functions", "macros", "customevents", "variables", "localvariables", "parameters" },
            ids);

        var order = sections.Select(s => s!["sortOrder"]!.GetValue<int>()).ToArray();
        Assert.Equal(order.OrderBy(x => x).ToArray(), order);
    }

    /// <summary>
    /// ⭐⭐ <b>The canvas's NO-DOCUMENT contract</b> — the state both hosts publish before anything is
    /// opened, and the one slice 1 compares editor-vs-cluster.
    ///
    /// <para>⭐ The fields that MEAN something here are the three that say *"there is nothing open"*:
    /// ⛔ a canvas reporting <c>hasActiveDocument</c> with a null name, or a non-zero
    /// <c>openDocumentCount</c> with no active document, is an inconsistency the byte-diff would happily
    /// re-bless. ⚠ <c>assetKind</c> is pinned because it is what makes this canvas the BLUEPRINT one — the
    /// three canvases differ by nothing else in the empty state.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Meaning_the_blueprint_canvas_publishes_its_empty_state()
    {
        var m = await DumpAsync("Blueprint", "ai_canvas_blueprint");

        Assert.Equal("Blueprint", m!["assetKind"]!.GetValue<string>());

        // ⛔ The three must AGREE. 📐 The debug API has no endpoint that opens an AI asset
        //    (PanelGoldenRails' own "ceiling" remark), so the empty state is the only reachable one —
        //    ⭐ which makes its internal consistency the whole of what this panel can be asserted on.
        Assert.False(m["hasActiveDocument"]!.GetValue<bool>());
        Assert.Equal(0, m["openDocumentCount"]!.GetValue<int>());
        Assert.Null(m["activeDocumentName"]?.GetValue<string>());
    }

    /// <summary>
    /// ⭐⭐ <b>The watch's EMPTY-TABLE contract.</b> 📌 <c>BP-511</c>/<c>94g</c> made pinned rows durable, so
    /// *"nothing is pinned"* is now a claim with a persistence layer behind it — ⛔ a watch that came up
    /// with rows in a fresh process would mean a leaked session file, and the byte-diff alone would just
    /// bless it.
    ///
    /// <para>⭐ <c>valueMode</c> is the second claim and it is NOT decoration: 📌 Batch 100 (100e) — a watch
    /// left at the wrong run state picks <c>VariableValue.ModeFor</c>'s INITIAL arm and renders the
    /// authored default for ever while the sim holds a different number. ⇒ pinning the mode pins the
    /// defect's signature.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Meaning_the_blueprint_watch_publishes_an_empty_table()
    {
        var m = await DumpAsync("Blueprint", "ai_watch_blueprint");

        Assert.Equal(0, m!["rowCount"]!.GetValue<int>());
        Assert.Empty((m["rows"] as JsonArray)!);
        Assert.Null(m["selectedPath"]?.GetValue<string>());

        // ⭐ `Current`, not `Initial` — see the remarks; this is 100e's signature field.
        Assert.Equal("Current", m["valueMode"]!.GetValue<string>());
    }

    // ══ the controls — what keeps the goldens honest ═══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE CONTROL ON THE IGNORE-LIST: it is empty, and the goldens prove it stays justified.</b>
    ///
    /// <para>⛔⛔ An ignore-list that only grows is how a suite stops meaning anything. ⭐ This inverts it, the
    /// way <c>N1</c>'s <c>Only_the_declared_volatile_kinds_are_nondeterministic</c> does: instead of trusting
    /// that no golden carries wall-clock or machine-path content, it READS THE COMMITTED FILES and reddens
    /// naming the path.</para>
    ///
    /// <para>⭐ It is also the rail that would catch a golden captured on a developer's machine and committed
    /// with an absolute path baked in — 📐 the one machine-dependency measured in the corpus.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "SystemSmoke")]
    public void No_golden_carries_machine_or_wall_clock_content()
    {
        var offenders = new List<string>();

        foreach (var (file, model) in GoldenStore.Committed(Scenario))
        {
            foreach (var (path, value) in PanelNormalizer.Leaves(model))
            {
                var leaf = path.Split('.').LastOrDefault()?.Split('[')[0] ?? "";
                if (PanelNormalizer.WallClockFieldNames.Contains(leaf, StringComparer.OrdinalIgnoreCase))
                    offenders.Add($"{file}:{path} (wall-clock field)");

                if (value?.GetValueKind() == System.Text.Json.JsonValueKind.String
                    && PanelNormalizer.LooksLikeAbsolutePath(value.GetValue<string>()))
                    offenders.Add($"{file}:{path} (absolute path '{value.GetValue<string>()}')");
            }
        }

        Assert.True(offenders.Count == 0,
            "a committed golden carries machine- or time-dependent content, so it cannot be stable:\n  "
          + string.Join("\n  ", offenders)
          + "\n⭐ Fix the PANEL or drop it from the budget. ⛔ Adding it to PanelNormalizer.IgnoredPaths "
          + "makes the golden pass while proving less.");
    }

    /// <summary>
    /// ⚠ <b>The encoding <c>GoldenStore.FileNameFor</c> applies must not merge two panels into one file.</b>
    /// 📐 It exists because <c>editor/_gizmo</c> — a real panel id — contains a slash. ⛔ A collision would
    /// overwrite one golden with another and be invisible in the file.
    /// </summary>
    [Fact]
    [Trait("Category", "SystemSmoke")]
    public void The_golden_file_name_encoding_is_injective_over_the_budget()
    {
        Assert.DoesNotContain(Budget, b => b.PanelId.Contains('~', StringComparison.Ordinal));

        var byFile = Budget.GroupBy(b => GoldenStore.FileNameFor(b.PanelId), StringComparer.Ordinal)
                           .Where(g => g.Count() > 1)
                           .Select(g => $"{g.Key} ← [{string.Join(", ", g.Select(x => x.PanelId))}]")
                           .ToArray();

        Assert.True(byFile.Length == 0, $"two budgeted panel ids share a golden file: {string.Join("; ", byFile)}");
    }

    /// <summary>
    /// ⭐⭐ <b>Every golden in the budget has a pairing case — <c>D7</c>'s rule, checked rather than trusted.</b>
    /// ⛔ Without this, the pairing rule is a habit that decays the first time someone adds a golden in a
    /// hurry. ⭐ It reads the METHOD LIST, so a new golden with no assertion reddens here and names itself.
    /// </summary>
    [Fact]
    [Trait("Category", "SystemSmoke")]
    public void Every_golden_in_the_budget_is_paired_with_assertions()
    {
        // Each pairing case declares which panel(s) it means via its InlineData/name; the map is explicit
        // rather than inferred from method names, because an inferred link silently breaks on a rename.
        var paired = new HashSet<string>(StringComparer.Ordinal)
        {
            "editor_fdp_inspector",
            "editor_shared_orbat",
            "editor_spawner",
            "ai_blackboard_variables_btree",
            "ai_blackboard_variables_hsm",
            "ai_my_blueprint_blueprint",
            // ⭐ cgf==editor slice 1 — paired by Meaning_the_blueprint_canvas_publishes_its_empty_state
            //   and Meaning_the_blueprint_watch_publishes_an_empty_table.
            "ai_canvas_blueprint",
            "ai_watch_blueprint",
        };

        var unpaired = Budget.Select(b => b.PanelId).Where(id => !paired.Contains(id)).ToArray();
        Assert.True(unpaired.Length == 0,
            $"golden(s) with no pairing assertion: [{string.Join(", ", unpaired)}]. D7: every golden also "
          + "carries 1–3 assertions on the fields that MEAN something, or a re-bless can change meaning "
          + "silently.");

        var orphaned = paired.Where(id => !Budget.Any(b => b.PanelId == id)).ToArray();
        Assert.True(orphaned.Length == 0,
            $"pairing assertion(s) for panel(s) no longer in the budget: [{string.Join(", ", orphaned)}]");
    }

    // ── the one way this class reads a panel ───────────────────────────────────

    /// <summary>
    /// ⭐ Switch, settle, read — §6's contract, in the single place every case goes through so no case can
    /// forget it.
    /// </summary>
    private async Task<JsonNode?> DumpAsync(string perspective, string panelId)
    {
        // ⛔⛔ NO LOAD HERE. The fixture loaded the scenario exactly once — HN-011 makes a reload a
        //    world-state change, so re-loading per case would capture goldens of a leaked world.
        (await _fx.Client.SwitchPerspectiveAndSettleAsync(perspective, SettleTicks)).EnsureOk();

        var dump = (await _fx.Client.GetPanelAsync(panelId)).EnsureOk();
        var model = dump.Field("model");

        Assert.NotNull(model);   // ⛔ null means "nothing captured" — a golden of it would be a golden of nothing
        return model;
    }
}
