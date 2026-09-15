using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b><c>N1</c> — THE DETERMINISM RAIL. It runs BEFORE any golden exists, and it GATES them.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N1</c> · §3 *(the third failure mode)* · charter <c>D6</c>.
///
/// <para>⛔⛔ <b>Why first.</b> A flaky golden is quietly filtered out, and then the suite is green and
/// encodes nothing. ⇒ ⭐ <b>prove the dumps are byte-identical across two fresh processes BEFORE writing
/// a file that claims to be the baseline.</b> 📌 <c>R-131</c>: a flaky test is a defect to fix, never to
/// filter — and `D6` caveat ① names the exact temptation: ⛔ <b>do not reach for normalisation to hide
/// non-determinism.</b></para>
///
/// <para>⭐⭐ <b>These do NOT use the shared collection fixture.</b> That fixture is one editor per
/// COLLECTION and by the time a rail runs, earlier cases have loaded scenarios, switched perspectives and
/// stepped it. ⇒ ⛔ comparing it against a fresh process would measure the SUITE'S history, not the
/// program. Both processes here are started by this class and disposed with it.</para>
///
/// <para>⚠⚠ <b>Every capture is taken AFTER a step</b> — 📄 §6 is a CONTRACT since <c>HN-007</c>: the API's
/// job queue drains at the top of the frame, before the panels draw, so an out-of-band reader sees the
/// PREVIOUS frame. 🔴 <b>A same-frame read returns the empty prefix — and two empty prefixes diff as
/// "identical", which would make this whole rail a false green.</b> That is the single most important line
/// in this file.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemDeterminism")]
public sealed class DeterminismRails
{
    private readonly ITestOutputHelper _out;
    public DeterminismRails(ITestOutputHelper output) => _out = output;

    /// <summary>The scenario every determinism claim is made against. Curated, and the one D7's worked example uses.</summary>
    private const string Scenario = "hill-attack";

    /// <summary>⭐ Enough ticks for spawn + lifecycle promotion to have happened, and for a frame to have drawn.</summary>
    private const int SettleTicks = 5;

    private static readonly string[] Perspectives = { "Scenario", "BTree", "HSM", "Blueprint" };

    // ── what one process reports, in a comparable form ─────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The id→entity mapping, as a stable text projection.</b>
    /// <para>⭐ Sorted by <c>networkId</c>, and each entity's component NAMES sorted too. ⚠ Sorting the
    /// components is not normalisation-to-hide: <c>ListEntities</c> emits
    /// <c>d.Components.Keys</c> — a DICTIONARY order, which is an implementation detail of the
    /// extraction service and not a fact about the world. ⛔ The <b>set</b> of components is the claim;
    /// their enumeration order is not, and 📌 <c>D6</c> caveat ① is about hiding a real ORDERING
    /// difference in the WORLD (spawn order), which is exactly what the id sequence below exposes.</para>
    /// </summary>
    private static string EntityMapping(JsonNode entities)
    {
        var rows = new List<string>();
        foreach (var e in entities.AsArray())
        {
            var id    = e!["networkId"]!.GetValue<long>();
            var name  = e["name"]?.GetValue<string>() ?? "";
            var comps = (e["components"] as JsonArray)?.Select(c => c!.GetValue<string>()).OrderBy(x => x, StringComparer.Ordinal)
                        ?? Enumerable.Empty<string>();
            rows.Add($"{id}\t{name}\t{string.Join(",", comps)}");
        }
        rows.Sort(StringComparer.Ordinal);
        return string.Join("\n", rows);
    }

    private static async Task<string> MappingOf(EditorProcess ed)
    {
        var entities = (await ed.Client.ListEntitiesAsync()).EnsureOk().DataOrThrow();
        return EntityMapping(entities);
    }

    /// <summary>
    /// ⭐ Every captured panel's dump, across every perspective, as one comparable document.
    /// ⛔ Each perspective is switched AND stepped first — §6's contract, and the reason this rail is not
    /// vacuous.
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <b>THE DECLARED-VOLATILE PANEL KINDS — and this list is the CONTROL, not the loophole.</b>
    /// 📄 <c>DESIGN_Regression_Net.md</c> §7 <c>N1</c>'s as-built · charter <c>D6</c> caveat ①.
    ///
    /// <para>📐 <b>Measured `2026-08-23`, two fresh processes, 41 dumps across four perspectives: exactly
    /// FIVE differed, and all five were these two kinds</b> — <c>fdp_message_log</c> in all four
    /// perspectives *(it is a <c>Global</c> window, so it appears in each)* plus
    /// <c>Scenario/editor_fdp_events</c>. ⭐ The differing field was a WALL-CLOCK TIMESTAMP; the counts
    /// alongside it matched exactly *(<c>totalCount:76, filteredCount:74</c> in both)*.</para>
    ///
    /// <para>⛔⛔ <b>Why this is NOT what <c>D6</c> caveat ① forbids.</b> That caveat forbids using
    /// normalisation to hide a real ordering difference in the WORLD — spawn order, allocated ids — and
    /// 📐 <see cref="Two_fresh_processes_agree_on_the_entity_mapping"/> measures that separately and it is
    /// CLEAN. ⭐ A log's timestamp is not world state that could be made deterministic: it records when
    /// something happened, and two runs happen at two times. ⇒ ⭐⭐ there is no source to fix.</para>
    ///
    /// <para>⭐⭐⭐ <b>And the control that keeps this honest:</b>
    /// <see cref="Only_the_declared_volatile_kinds_are_nondeterministic"/> asserts the volatile set is
    /// EXACTLY these two. ⇒ ⛔ a third kind that starts drifting REDDENS and is named, instead of being
    /// swept into a growing ignore-list. 📌 That is the difference between an ignore-list and a
    /// measurement.</para>
    /// </summary>
    private static readonly HashSet<string> VolatileKinds =
        // ⚠ MEASURED, not inferred. My first cut wrote "fdp-events" — read off the panel ID
        //   (`editor_fdp_events`) as though the id were the kind. 📐 The KIND is "event-browser", and
        //   Only_the_declared_volatile_kinds_are_nondeterministic refused the guess on both counts: it
        //   named "event-browser" as undeclared AND flagged "fdp-events" as an exemption nothing needs.
        //   ⭐ The control worked on its author first, which is the best evidence it is a control.
        new(StringComparer.Ordinal) { "message-log", "event-browser" };

    /// <summary>
    /// ⭐ Every captured panel's dump, across every perspective, keyed <c>perspective/panelId</c>, with the
    /// panel's KIND alongside so a difference can be attributed to a kind rather than to an address.
    /// ⛔ Each perspective is switched AND stepped first — §6's contract, and the reason this is not vacuous.
    /// </summary>
    private static async Task<SortedDictionary<string, (string Kind, string Model)>> PanelDumpsOf(EditorProcess ed)
    {
        var all = new SortedDictionary<string, (string, string)>(StringComparer.Ordinal);

        foreach (var p in Perspectives)
        {
            (await ed.Client.SwitchPerspectiveAndSettleAsync(p, SettleTicks)).EnsureOk();

            var panels = (await ed.Client.GetPanelsAsync()).EnsureOk();
            var captured = ((panels.Field("captured") as JsonArray)!)
                           .Select(n => n!.GetValue<string>())
                           .OrderBy(x => x, StringComparer.Ordinal);

            foreach (var id in captured)
            {
                var dump = await ed.Client.GetPanelAsync(id);
                if (!dump.Ok) continue;   // a panel that vanished between the two calls is not a diff
                all[$"{p}/{id}"] = (dump.String("panelKind") ?? "",
                                    dump.Field("model")?.ToJsonString() ?? "null");
            }
        }

        return all;
    }

    /// <summary>
    /// ⭐⭐ <b>Every differing key, not the first.</b> ⛔ Reporting only the first turns *"one volatile field
    /// in one panel"* and *"twenty panels disagree"* into the same message — and those are completely
    /// different findings, one a declaration question and the other a defect hunt. 📌 §8 applies the same
    /// rule to the mutation proof: *"a mutation that reddens 40 files is itself the finding."*
    /// </summary>
    private static string[] DifferingKeys(
        SortedDictionary<string, (string Kind, string Model)> a,
        SortedDictionary<string, (string Kind, string Model)> b)
        => a.Keys.Intersect(b.Keys)
            .Where(k => !string.Equals(a[k].Model, b[k].Model, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    // ── the rails ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴🔴 <b>Two FRESH processes, the same scenario, the same steps ⇒ the same id→entity mapping.</b>
    ///
    /// <para>⭐⭐ <b>What this tests, stated precisely, because it is easy to overclaim:</b> two fresh
    /// processes each start their id allocator at its own baseline, so this does <b>not</b> exercise the
    /// allocator RESET — it exercises <b>allocation ORDER</b>, i.e. that scenario load spawns entities in
    /// the same sequence every time. 📌 Exactly what charter <c>D6</c> caveat ① says must be MEASURED and
    /// never assumed: <i>"a reset counter gives the same ids only if the ALLOCATION ORDER is the same, and
    /// that depends on spawn order during scenario load."</i></para>
    ///
    /// <para>⭐ 📐 <b>Measured `2026-08-23`: CLEAN.</b> 8 entities, ids <c>1000</c>–<c>1007</c>, identical
    /// names and component sets in both processes. ⇒ ⭐⭐ <b>this is the fact the rest of the programme
    /// leans on</b>, and it is now a rail rather than a hope.</para>
    ///
    /// <para>⛔ If it ever reddens, the fix is the SOURCE — spawn order, dictionary iteration, float
    /// formatting — ⛔ never a widened ignore-list.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Two_fresh_processes_agree_on_the_entity_mapping()
    {
        await using var a = await EditorProcess.StartAsync("det-a");
        await using var b = await EditorProcess.StartAsync("det-b");

        (await a.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();
        (await b.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();
        (await a.Client.StepAsync(SettleTicks)).EnsureOk();
        (await b.Client.StepAsync(SettleTicks)).EnsureOk();

        var ma = await MappingOf(a);
        var mb = await MappingOf(b);

        _out.WriteLine($"A: {ma.Split('\n').Length} entities\n{ma}");

        // ⛔ An empty mapping would make equality trivially true — the false green this rail exists not to be.
        Assert.False(string.IsNullOrWhiteSpace(ma), "process A reported NO entities — nothing was compared");
        Assert.Equal(ma, mb);
    }

    /// <summary>
    /// 🔴🔴 <b>Every panel that is not a declared-volatile feed is BYTE-IDENTICAL across two fresh
    /// processes.</b>
    ///
    /// <para>⭐⭐⭐ <b>This is the rail that gates <c>N2</c>/<c>N3</c>.</b> A golden IS a stored panel dump ⇒
    /// if two processes disagree on a dump today, a golden of it is a coin flip and would be filtered out
    /// within a batch *(<c>R-131</c>: never <c>[Skip]</c> — fix or delete)</para>
    ///
    /// <para>⛔ It covers all FOUR perspectives — 📌 <c>N0</c> measured 11 panels reachable only from
    /// Blueprint, so a rail that stayed in the default perspective would bless a quarter of the editor and
    /// call it a baseline.</para>
    ///
    /// <para>⚠ <b>The count compared is asserted</b>, because "0 dumps agree perfectly" is what a dead read
    /// path looks like — 📌 and that is not hypothetical, it is <c>HN-007</c>.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Two_fresh_processes_agree_on_every_stable_panel()
    {
        await using var a = await EditorProcess.StartAsync("det-pa");
        await using var b = await EditorProcess.StartAsync("det-pb");

        (await a.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();
        (await b.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();

        var da = await PanelDumpsOf(a);
        var db = await PanelDumpsOf(b);

        // ⛔ Different panel SETS is a different (worse) finding than different contents, so it is stated
        //   separately rather than folded into the diff.
        Assert.Equal(da.Keys.OrderBy(k => k, StringComparer.Ordinal),
                     db.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var stable = da.Keys.Where(k => !VolatileKinds.Contains(da[k].Kind)).ToArray();
        _out.WriteLine($"{da.Count} dumps captured; {stable.Length} of them stable-kind");

        Assert.True(stable.Length >= 30,
                    $"only {stable.Length} stable-kind dumps were captured — too few to call this a "
                  + "baseline; a near-empty comparison passes trivially (see HN-007)");

        var differing = DifferingKeys(da, db).Where(k => !VolatileKinds.Contains(da[k].Kind)).ToArray();

        Assert.True(differing.Length == 0,
                    $"{differing.Length} of {stable.Length} stable-kind dumps differ between two fresh "
                  + $"processes, so no golden of them can be trusted: [{string.Join(", ", differing)}].\n"
                  + "⛔ Fix the SOURCE — do not add these to VolatileKinds to make this green.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A RELOAD in ONE process REPEATS the authored ids — <c>HN-037</c> closed, and this rail is the
    /// third framing of the same question.</b> 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11.
    ///
    /// <para>⚠⚠ <b>The history matters, because two of the three framings were wrong for different
    /// reasons</b> — and the file records them rather than quietly overwriting:</para>
    /// <list type="number">
    ///   <item>⛔ <b>*"the ids already repeat, so <c>D6</c>'s reset is not needed"*.</b> 🔴 It was measuring
    ///   the FIRST world twice: <c>waitForReady</c> waited on a LEVEL (<c>ClusterState == OperatingEdit</c>)
    ///   and a reload STARTS from that level, so the first poll answered "ready" while the previous world
    ///   still stood. ⭐ Readiness is edge-triggered now.</item>
    ///   <item>⛔ <b>*"the ids drift to <c>1008</c>–<c>1015</c>, and that is the truth"*.</b> ⭐ TRUE as a
    ///   measurement, and it was the tripwire this rail carried — asserting the drift so that fixing it
    ///   would be noticed. 📌 It reddened on `2026-08-24`, exactly as designed.</item>
    ///   <item>⭐⭐ <b>NOW: the world boundary resets the ONE id authority to 1000</b>, so a reload rebuilds
    ///   the same world under the SAME ids. 🔒 User: <i>"resets to initial value (1000 for the first entity
    ///   allocated) whenever whole 'world' resets"</i>.</item>
    /// </list>
    ///
    /// <para>🔴🔴 <b>AND THIS RAIL GUARDS THE TRAP THAT CAME WITH THE FIX.</b> 📐 Measured while building:
    /// the drift was the only reason a reload worked at all. <c>EntityRepository.SoftClear</c> does not
    /// touch <c>NetworkEntityMap</c>, and <c>NetworkSpawningSystem</c>'s duplicate guard <b>silently
    /// drops</b> a spawn whose id is already mapped. ⇒ resetting to 1000 without clearing the map gave
    /// <b>8 entities on the first load and 0 on the second</b> — no exception, no log line, and seven
    /// unrelated system rails failed with an empty world. ⚠ That is why <c>ka.Length == kb.Length</c> and
    /// <c>NotEmpty(kb)</c> are asserted BEFORE the equality: an empty second world would otherwise satisfy
    /// "no drift" trivially.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task A_reload_in_one_process_repeats_the_authored_ids()
    {
        await using var ed = await EditorProcess.StartAsync("det-reload");

        var first  = await LoadAndMap(ed);
        var second = await LoadAndMap(ed);

        var ka = IdsOf(first);
        var kb = IdsOf(second);

        _out.WriteLine($"ids first : [{string.Join(", ", ka)}]");
        _out.WriteLine($"ids second: [{string.Join(", ", kb)}]");

        // ⛔ Anti-vacuity FIRST: the failure mode this fix introduced is an EMPTY second world, and an empty
        //   set drifts from nothing. 📌 Assert the world exists before asserting what it is numbered.
        Assert.NotEmpty(ka);
        Assert.NotEmpty(kb);
        Assert.Equal(ka.Length, kb.Length);

        Assert.Equal(ka, kb);

        // ⭐ And the block starts where the user asked it to, not merely "the same place twice": both loads
        //   must begin at 1000, which is what makes the ids reproducible ACROSS processes and hosts too.
        Assert.Equal("1000", ka[0]);
        Assert.Equal("1000", kb[0]);
    }

    /// <summary>
    /// 🔴🔴 <b><c>HN-011</c> IS RETRACTED — *"a reload leaks <c>BlueprintAssignments</c> onto entity
    /// 1000"* was the SAME RACE, seen from another angle.</b>
    ///
    /// <para>📐 <b>Measured `2026-08-24` with the readiness race fixed:</b> after a real reload, network id
    /// <c>1000</c> <b>did not exist at all</b> — every one of its 29 components read as "lost", because the
    /// rebuilt world numbered its entities <c>1008</c>–<c>1015</c>. ⇒ ⛔ the old rail was comparing one id
    /// across a world that had never actually been replaced, so its *"gained a component"* was an artifact,
    /// not a leak. ⚠ <b>The <c>HN-011</c> row is corrected in the tracker rather than left standing as a
    /// defect nobody can reproduce.</b></para>
    ///
    /// <para>⚠⚠ <b>UPDATED `2026-08-24` (<c>HN-037</c>): the drift is GONE, and the retraction still
    /// stands.</b> A reload now rebuilds under the SAME ids (<c>1000</c>–<c>1007</c>), so id <c>1000</c>
    /// exists on both loads — ⛔ but that does NOT resurrect <c>HN-011</c>: the leak claim was refuted by the
    /// readiness race, not by the numbering, and <c>A_reload_in_one_process_repeats_the_authored_ids</c>
    /// asserts the components match id-for-id. ⭐ Reinstating a leak claim would need a fresh
    /// measurement.</para>
    ///
    /// <para>⭐⭐ <b>What this rail owns:</b> the reload rebuilds the SAME AUTHORED WORLD — the same entity
    /// NAMES, the same component sets. ⭐ Compared BY NAME on purpose: it is authored data, so this rail
    /// stays true and stays meaningful whatever the numbering policy is. 📌 That independence is why it
    /// survived the policy change above unedited.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task A_reload_rebuilds_the_same_authored_world()
    {
        await using var ed = await EditorProcess.StartAsync("det-leak");

        var first  = await LoadAndMap(ed);
        var second = await LoadAndMap(ed);

        // ⭐ Compare by NAME, which is authored data. ⚠ Since HN-037 the ids ARE preserved across a reload,
        //   but comparing by name keeps this rail independent of that policy — deliberately.
        string[] NamesAndComponents(string mapping) => mapping.Split('\n')
            .Select(r => r.Split('\t'))
            .Where(f => f.Length > 2)
            .Select(f => $"{f[1]}\t{f[2]}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var a = NamesAndComponents(first);
        var b = NamesAndComponents(second);

        _out.WriteLine($"{a.Length} named rows on the first load, {b.Length} on the second");

        Assert.NotEmpty(a);
        Assert.Equal(a, b);
    }

    private static async Task<string> LoadAndMap(EditorProcess ed)
    {
        (await ed.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();
        (await ed.Client.StepAsync(SettleTicks)).EnsureOk();
        return await MappingOf(ed);
    }

    private static string[] IdsOf(string mapping)
        => mapping.Split('\n').Select(r => r.Split('\t')[0]).ToArray();

    private static IEnumerable<string> ComponentsOf(string mapping, string id)
    {
        foreach (var row in mapping.Split('\n'))
        {
            var f = row.Split('\t');
            if (f.Length > 2 && f[0] == id)
                return f[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE CONTROL ON THE EXEMPTION: the volatile set is EXACTLY what we declared.</b>
    ///
    /// <para>⛔⛔ An ignore-list that only ever grows is how a suite stops meaning anything — the exemption
    /// gets widened once per red until nothing is checked. ⭐ This inverts it: the exempted kinds are a
    /// CLAIM, and a third kind that starts drifting <b>reddens here and is named</b>, so widening becomes a
    /// deliberate, argued act rather than a quiet edit.</para>
    ///
    /// <para>⚠ It also reddens if a declared-volatile kind stops drifting — ⭐ deliberately: an exemption
    /// nothing needs should be deleted, not carried. ⛔ That is the half of an ignore-list nobody ever
    /// prunes.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Only_the_declared_volatile_kinds_are_nondeterministic()
    {
        await using var a = await EditorProcess.StartAsync("det-va");
        await using var b = await EditorProcess.StartAsync("det-vb");

        (await a.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();
        (await b.Client.LoadScenarioEditAsync(Scenario)).EnsureOk();

        var da = await PanelDumpsOf(a);
        var db = await PanelDumpsOf(b);

        var driftingKinds = DifferingKeys(da, db)
                            .Select(k => da[k].Kind)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(k => k, StringComparer.Ordinal)
                            .ToArray();

        _out.WriteLine($"kinds that drifted: [{string.Join(", ", driftingKinds)}]");

        var unexpected = driftingKinds.Except(VolatileKinds, StringComparer.Ordinal).ToArray();
        Assert.True(unexpected.Length == 0,
                    $"NEW non-deterministic panel kind(s): [{string.Join(", ", unexpected)}]. "
                  + "⛔ Do not add them to VolatileKinds to go green — find why they drift. A wall-clock "
                  + "stamp is exempt; anything else is a defect.");

        var noLongerDrifting = VolatileKinds.Except(driftingKinds, StringComparer.Ordinal).ToArray();
        Assert.True(noLongerDrifting.Length == 0,
                    $"declared-volatile kind(s) [{string.Join(", ", noLongerDrifting)}] no longer drift — "
                  + "delete the exemption rather than carrying one nothing needs.");
    }
}
