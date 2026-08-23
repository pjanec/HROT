using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b><c>N0</c> — THE REACH. Every editor perspective is drivable, and each one publishes panels the
/// others do not.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N0</c> · §6 *(the capture protocol)* · §2 *(the gap that
/// blocked job ①)*.
///
/// <para>⛔⛔ <b>Why this is the batch's first item and not a nicety.</b> 📐 A panel publishes its
/// view-model only when its DRAW runs, and only the ACTIVE perspective draws. ⇒ before these two
/// endpoints the harness could see one perspective's panels and no others, so **the BTree, HSM and
/// Blueprint panels could not be captured at all** — a golden suite built on that would have covered a
/// quarter of the editor while looking complete.</para>
///
/// <para>⭐⭐ <b>The assertion that carries the weight is <see cref="Each_perspective_publishes_panels_the_others_do_not"/>.</b>
/// ⛔ *"the switch returned 200"* proves nothing: <c>A0</c> makes an unknown name a logged no-op, and a
/// perspective that switches but never draws publishes nothing. ⭐ The only statement worth making is that
/// the CAPTURED SET CHANGED.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemPanels")]
public sealed class PerspectiveReachTests : SystemTestBase
{
    public PerspectiveReachTests(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    private static string[] Names(ApiResult r)
        => ((r.Field("perspectives") as JsonArray)!).Select(n => n!.GetValue<string>()).ToArray();

    private static HashSet<string> Captured(ApiResult r)
        => ((r.Field("captured") as JsonArray)!).Select(n => n!.GetValue<string>()).ToHashSet();

    /// <summary>
    /// ⭐ The list is DERIVED from registered windows, so it states what this build actually offers.
    /// ⛔ Asserted as a set, not a count: a count that moved would say "something changed" and not what.
    /// </summary>
    [SystemSmokeFact]
    public async Task The_editor_offers_its_four_perspectives()
    {
        var r = (await Mcp.ListPerspectivesAsync()).EnsureOk();
        var names = Names(r);

        Output.WriteLine($"current={r.String("current")}, perspectives=[{string.Join(", ", names)}]");

        Assert.Contains("Scenario",  names);
        Assert.Contains("BTree",     names);
        Assert.Contains("HSM",       names);
        Assert.Contains("Blueprint", names);

        // ⛔ The perspective batch's three casualties must stay dead: the old id, the phantom scope-as-a-
        //   place, and two names that were only ever constructor literals.
        Assert.DoesNotContain("Editor",    names);
        Assert.DoesNotContain("Global",    names);
        Assert.DoesNotContain("Authoring", names);

        // ⭐ And the editor comes up on a REAL perspective — the 22-window blank launch BP-489 closed.
        Assert.Contains(r.String("current")!, names);
    }

    /// <summary>
    /// ⛔ An unknown name is REFUSED with the claimed set named, and the current perspective does not move.
    /// ⭐ This is what stops a typo in a golden-capture script from silently capturing the wrong workspace.
    /// </summary>
    [SystemSmokeFact]
    public async Task An_unknown_perspective_is_refused_and_nothing_moves()
    {
        var before = (await Mcp.ListPerspectivesAsync()).EnsureOk().String("current");

        var bad = await Mcp.SwitchPerspectiveAsync("NoSuchPerspective");

        Assert.False(bad.Ok);
        Assert.Equal(400, bad.StatusCode);
        // ⭐ The error NAMES the alternatives — an agent should not have to make a second call to recover.
        Assert.Contains("Claimed:", bad.Error ?? "");
        Assert.Contains("Scenario", bad.Error ?? "");

        var after = (await Mcp.ListPerspectivesAsync()).EnsureOk().String("current");
        Assert.Equal(before, after);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE THAT MATTERS: each perspective publishes a set of panels the others do not.</b>
    ///
    /// <para>⛔⛔ Three weaker assertions were available and all three would pass on a broken build:
    /// *"the switch returned 200"* *(<c>A0</c> no-ops silently)*, *"current changed"* *(a perspective can
    /// change without anything drawing)*, and *"captured is non-empty"* *(the previous perspective's
    /// capture satisfies it)*. ⭐ **Only a set DIFFERENCE proves the draw actually followed the
    /// switch.**</para>
    ///
    /// <para>⚠ It asserts each perspective's captured set is <b>non-empty and not identical to</b> the
    /// scenario one — ⛔ not that the sets are disjoint, because <see cref="WindowScope.Global"/> windows
    /// and pinned ones legitimately appear in every perspective *(that is what Global MEANS)*.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Each_perspective_publishes_panels_the_others_do_not()
    {
        var seen = new Dictionary<string, HashSet<string>>();

        foreach (var p in new[] { "Scenario", "BTree", "HSM", "Blueprint" })
        {
            (await Mcp.SwitchPerspectiveAndSettleAsync(p)).EnsureOk();

            var panels = (await Mcp.GetPanelsAsync()).EnsureOk();
            Assert.Equal(p, (await Mcp.ListPerspectivesAsync()).EnsureOk().String("current"));

            var captured = Captured(panels);
            seen[p] = captured;
            Output.WriteLine($"{p}: {captured.Count} captured — {string.Join(", ", captured.Take(6))}");

            // ⛔ Empty means the switch took and NOTHING drew — a live perspective with no panels is a
            //   finding, not a pass. 📌 And it is the exact symptom the frame-order defect produced.
            Assert.NotEmpty(captured);
        }

        // ⭐⭐ The graph perspectives must each differ from Scenario. ⛔ If they matched, the switch would be
        //   cosmetic and every graph golden would in fact be a copy of the scenario one.
        foreach (var p in new[] { "BTree", "HSM", "Blueprint" })
            Assert.True(!seen[p].SetEquals(seen["Scenario"]),
                        $"'{p}' captured exactly the same panels as 'Scenario' ⇒ the perspective switch "
                      + $"did not change what draws, so a golden for '{p}' would silently be Scenario's.");
    }

    /// <summary>
    /// ⭐⭐ <b>A panel that is UNREACHABLE from the default perspective becomes reachable.</b> 📄 §7 <c>N0</c>'s
    /// own done-when: *"captures a panel that was previously unreachable"*.
    /// ⛔ Stated as a difference rather than by naming a panel id: a hard-coded id would turn a layout
    /// change into a red that says nothing about the endpoint.
    /// </summary>
    [SystemSmokeFact]
    public async Task A_graph_perspective_reaches_panels_the_default_cannot()
    {
        (await Mcp.SwitchPerspectiveAndSettleAsync("Scenario")).EnsureOk();
        var fromScenario = Captured((await Mcp.GetPanelsAsync()).EnsureOk());

        (await Mcp.SwitchPerspectiveAndSettleAsync("Blueprint")).EnsureOk();
        var fromBlueprint = Captured((await Mcp.GetPanelsAsync()).EnsureOk());

        var onlyInBlueprint = fromBlueprint.Except(fromScenario).ToArray();
        Output.WriteLine($"reachable only from Blueprint: {string.Join(", ", onlyInBlueprint)}");

        Assert.NotEmpty(onlyInBlueprint);
    }
}
