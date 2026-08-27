using System.Text.Json.Nodes;
using Hrot.SystemTests.Goldens;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SystemTests.Conformance;

/// <summary>
/// ⭐⭐⭐ <b>PHASE 2's SAFETY NET — the BEFORE/AFTER axis, per host.</b>
/// 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.4e.
///
/// <para>🔒 <b>USER QUESTION, `2026-08-27`:</b> <i>"how will you check that the unification does not destroy
/// what now works? will you use current editor as something that should not change?"</i></para>
///
/// <para>⛔⛔ <b>WHY THE EXISTING RAIL CANNOT ANSWER THAT.</b> Phase 0 built ONE axis:
/// <see cref="ClusterConformanceRails"/> compares the editor against <c>--mode all</c> — <b>host vs host</b>.
/// ⚠⚠ That axis is <b>BLIND to a change that affects both hosts identically</b>: if a shared bundle drops a
/// window, renames an id, or moves a perspective on every host at once, cross-host parity stays
/// <b>perfectly green</b>. ⇒ 🔴 and unification is <b>precisely</b> the class of change that moves every host
/// together, so that axis goes blind at the moment it is needed most. 📌 It would be the SEVENTH instance of
/// the rail-blindness pattern *(`CE-065`: unit rails green with strict mode off · `CE-072`: seven rails green
/// because production had moved behind a wrapper)</para>
///
/// <para>⭐⭐⭐ <b>THIS rail is the other axis: TODAY'S REGISTRATION, PINNED.</b> It captures, per runner mode,
/// <b>the sorted set of window IDS</b> a host registers, and asserts it is UNCHANGED. ⛔ It does not compare
/// hosts to each other at all — that is deliberately someone else's job.</para>
///
/// <para>⚠ <b>And why "use the current editor" is not enough on its own:</b> 📐 the ids are HOST-PREFIXED
/// (<c>editor_system_profiler</c> · <c>ig_system_profiler</c> · <c>simhost_system_profiler</c> ·
/// <c>cgf_system_profiler</c>). The editor's baseline covers <b>5 of the 20</b> cross-host instantiation
/// sites and proves nothing about whether <c>ig_system_profiler</c> still exists. ⇒ ⭐ every host needs its
/// own baseline, which is why this rail is per MODE.
/// ⚠ 📐 <b>ReplayBrowser names its two <c>rb_*</c></b> *(<c>rb_inspector</c>, <c>rb_events</c>)*, not
/// <c>*_fdp_*</c> — measured while inspecting the first capture.</para>
///
/// <para>📐 <b>THREE modes cover every site</b> *(slice ② measured <b>20 across 4 hosts</b>, not 22 across 5 —
/// ReplayBrowser's two are a DIFFERENT TYPE in a different assembly)* *(measured: <c>HrotRunnerConfiguration:124</c> expands
/// <c>all</c> to <c>orchestrator,simhost,ig,excon,cgf</c>, and <c>:181</c> forbids the editor coexisting with
/// IG/ExCon)</b>:</para>
/// <list type="bullet">
///   <item><c>editor</c> — the editor's 5 sites</item>
///   <item>⭐⭐ <c>all</c> — SimHost · IG · ExCon · CGF · Orchestrator, <b>five hosts in ONE process</b></item>
///   <item><c>replaybrowser</c> — ReplayBrowser's 2 *(its own types)*</item>
/// </list>
///
/// <para>⛔⛔ <b>THE ORDER THIS FORCES:</b> the goldens are captured on TODAY'S code, <b>before</b> any
/// registration site moves. ⚠⚠ A golden captured after the first bundle lands would enshrine whatever that
/// bundle did — 📌 <c>GoldenStore</c>'s own remarks call that *"a golden nobody has seen fail"*.</para>
///
/// <para>⚠ <b>LIMITS, stated so nobody over-trusts this</b> *(§5c.4e)*: ① ✅ <b><c>CE-076</c> CLOSED the
/// instrumentation gap</b> — <c>WindowManager.RegisterWindow</c> now declares every window, so
/// <c>GET /panels registered[]</c> is complete by construction *(the editor golden grew 55 → 64, purely
/// additive)*; ⚠ what survives is narrower: a LAZILY registered window is absent until its perspective has
/// been visited, which is why the capture walks them —
/// <see cref="The_instrumentation_gap_is_measured_not_assumed"/> prints the live numbers; ② ids are <b>not
/// pixels</b> — this catches a dropped, renamed or added window,
/// ⛔ never a panel that renders wrong; ③ <c>captured[]</c> is frame-dependent, so the assertions read
/// <c>registered[]</c>; ④ ⛔⛔ <b>a window that KEEPS its id but MOVES PERSPECTIVE is NOT covered</b> — the
/// first version tried to and the field was VACUOUS *(see <see cref="CaptureRegisteredAsync"/>)*, so the
/// <c>CE-071</c>-`B1` class of defect is explicitly OUT of this net until <c>focus_panel</c> is folded in.</para>
///
/// <para>⛔ <c>T3</c> — the async lane. ⚠ Boots real processes under Xvfb; never a foreground blocker.</para>
///
/// <para>⛔⛔ <b><c>CE-081</c> — the <c>Category</c> trait below is NOT decoration; without it this whole
/// file was UNREACHABLE from <c>scripts/run-system-tests.sh</c>.</b> 📐 Measured `2026-08-27`: the script
/// filters on <c>(Category=SystemSmoke|Category=SystemModes)</c>, and this class declared only
/// <c>lane=T3</c> ⇒ a run named at these rails printed <i>"No test matches the given testcase filter"</i>
/// and exited <b>0</b>. ⚠⚠ <b>A silent zero-test green</b> — the rail-blindness shape, this time in the
/// harness rather than the assertion. ⭐ <c>SystemModes</c> is the right bucket: these are parameterised
/// by runner mode, exactly like <c>ModeStartupRails</c>.</para>
/// </summary>
[Trait("Category", "SystemModes")]
[Trait("lane", "T3")]
public sealed class TheUiBaselineIsPinnedPerHostRails
{
    private readonly ITestOutputHelper _out;
    public TheUiBaselineIsPinnedPerHostRails(ITestOutputHelper output) => _out = output;

    /// <summary>⭐ One golden per mode, named so the file says what it pins.</summary>
    private const string ScenarioPrefix = "ui-baseline";

    /// <summary>
    /// ⭐⭐ Capture the host's REGISTERED window-id set — <b>the sorted id list, and nothing else.</b>
    ///
    /// <para>⭐ <c>registered[]</c>, not <c>captured[]</c>: registration is the structural fact that must not
    /// change, and it does not depend on whether a frame happened to draw that panel.</para>
    ///
    /// <para>⛔⛔ <b>TWO FIELDS WERE REMOVED AFTER THE FIRST CAPTURE WAS INSPECTED</b> *(`2026-08-27`)* — 📌 the
    /// inspection <c>GoldenStore</c> demands, and it found both defects in this rail rather than in the
    /// product:</para>
    /// <list type="number">
    ///   <item>⛔ <b><c>perspectives[]</c> was VACUOUS.</b> 📐 Measured: <c>GET /panels</c>'s
    ///   <c>registered[]</c> is <b>process-wide, NOT perspective-scoped</b> — 54 of the editor's 55 windows
    ///   came back listing all four perspectives, because the field recorded which perspectives the CAPTURE
    ///   VISITED, not which the window belongs to. ⚠⚠ It would therefore <b>NOT</b> have caught
    ///   <c>CE-071</c>'s `B1` *(a window bound to a perspective nothing registers)*, which was the headline
    ///   claim made for this rail. ⇒ ⛔ shipping it would have been <b>false confidence</b>, and the one
    ///   window whose list differed made it a flaky-diff source too.</item>
    ///   <item>⛔ <b><c>kind</c> was FRAME-DEPENDENT.</b> 📐 Empty for <b>18 of 55</b>: it is inverted from
    ///   <c>kinds{}</c>, which is derived from <c>captured[]</c>. ⇒ a later run where those panels happen to
    ///   draw would redden this rail for no product change. ⚠ Exactly the limit this file's own remarks
    ///   warn about — and the first version violated it.</item>
    /// </list>
    ///
    /// <para>⇒ ⭐⭐⭐ <b>What remains is stable and is the real prize:</b> a dropped window, a renamed id, or a
    /// silently added one is a diff. 📌 §6: *"a tidier rename silently resets users' layouts"*.</para>
    ///
    /// <para>⚠⚠ <b>WHAT IS THEREFORE NOT COVERED — say it, do not imply it:</b> a window that KEEPS its id but
    /// MOVES perspective. ⭐ The stable source for that is <c>focus_panel</c>, which returns
    /// <c>{panelId, perspective, isOpen, isPinned}</c> <b>per panel</b> — ⛔ but it has SIDE EFFECTS *(it
    /// opens/focuses, and pins a foreign-perspective window)*, so folding it in needs its own design pass.
    /// Filed rather than bolted on.</para>
    /// </summary>
    private static async Task<JsonArray> CaptureRegisteredAsync(EditorProcess host, ITestOutputHelper output)
    {
        var perspectives = ((await host.Client.ListPerspectivesAsync()).EnsureOk()
                            .Field("perspectives") as JsonArray)!
                           .Select(n => n!.GetValue<string>())
                           .OrderBy(p => p, StringComparer.Ordinal)
                           .ToArray();

        output.WriteLine($"[{host.Mode}] perspectives: [{string.Join(", ", perspectives)}]");

        var ids = new SortedSet<string>(StringComparer.Ordinal);

        // ⭐ Still walk the perspectives: a window can be registered LAZILY when its perspective is first
        //   activated, so a single-perspective read would miss it. ⛔ The walk widens the SET; it does not
        //   attribute a perspective to an id (see the remarks — registered[] is process-wide).
        foreach (var p in perspectives)
        {
            var switched = await host.Client.SwitchPerspectiveAsync(p);
            if (!switched.Ok) continue;
            // ⭐ §6's contract: switch, STEP, then read — a same-frame read returns the empty prefix.
            await host.Client.StepAsync(2);
            await Task.Delay(150);

            var panels = (await host.Client.GetPanelsAsync()).EnsureOk();
            foreach (var n in (panels.Field("registered") as JsonArray) ?? new JsonArray())
                ids.Add(n!.GetValue<string>());
        }

        output.WriteLine($"[{host.Mode}] registered windows: {ids.Count}");
        return new JsonArray(ids.Select(i => (JsonNode)i!).ToArray());
    }

    /// <summary>
    /// ⭐⭐⭐ The baseline assertion, one mode at a time.
    /// <para>⛔ In capture mode *(<c>PANEL_GOLDEN_CAPTURE=1</c>)* this WRITES the golden; otherwise it
    /// compares and fails on any differing path. ⚠ A missing golden is an ERROR, never an auto-capture —
    /// <c>GoldenStore</c> enforces that, deliberately.</para>
    /// </summary>
    [Theory]
    [InlineData("editor")]
    [InlineData("all")]
    [InlineData("replaybrowser")]
    public async Task The_registered_window_set_is_unchanged(string mode)
    {
        await using var host = await EditorProcess.StartAsync($"ui-baseline-{mode}", mode: mode);

        var captured = await CaptureRegisteredAsync(host, _out);

        // ⛔ ANTI-VACUITY: an empty capture would make every comparison trivially pass. A host that
        //    registers no windows at all is a broken boot, not a valid baseline.
        Assert.NotEmpty(captured);

        var diffs = GoldenStore.CompareOrWrite($"{ScenarioPrefix}-{mode}", "registered-windows", captured);

        Assert.True(diffs.Count == 0,
            $"⛔ The registered window set for `--mode {mode}` CHANGED.\n"
          + $"   Differing paths ({diffs.Count}): {string.Join(", ", diffs.Take(20))}\n"
          + "   ⭐ Each path indexes the sorted window-id list — a dropped, renamed or added window.\n"
          + "   ⚠ If this is a DELIBERATE change, re-capture with PANEL_GOLDEN_CAPTURE=1, INSPECT the diff, "
          + "and commit it in the SAME commit as the code change — never separately.\n"
          + "   📌 A window id that moved silently resets users' saved layouts (§6).");
    }

    /// <summary>
    /// ⚠⚠ <b>THE LIMIT, MEASURED RATHER THAN ASSUMED.</b>
    ///
    /// <para><c>GET /panels</c> reports only <c>PanelSnapshot</c>-INSTRUMENTED windows. ⛔ If many registered
    /// windows are uninstrumented, the baseline above is a partial net and saying otherwise would be the
    /// over-claim <c>CE-049</c> made. ⭐ This rail records the actual numbers in the test output so the gap is
    /// a KNOWN quantity a later batch can act on.</para>
    ///
    /// <para>⛔ It deliberately asserts only a floor, not an exact count: the point is to MEASURE and REPORT,
    /// ⚠ not to freeze a number that legitimately grows as panels gain instrumentation.</para>
    /// </summary>
    [Theory]
    [InlineData("editor")]
    [InlineData("all")]
    public async Task The_instrumentation_gap_is_measured_not_assumed(string mode)
    {
        await using var host = await EditorProcess.StartAsync($"ui-gap-{mode}", mode: mode);

        var panels     = (await host.Client.GetPanelsAsync()).EnsureOk();
        var registered = ((panels.Field("registered") as JsonArray) ?? new JsonArray()).Count;
        var captured   = ((panels.Field("captured")   as JsonArray) ?? new JsonArray()).Count;

        var perspectives = ((await host.Client.ListPerspectivesAsync()).EnsureOk()
                            .Field("perspectives") as JsonArray)!.Count;

        _out.WriteLine($"[{mode}] registered={registered}  captured-this-frame={captured}  "
                     + $"perspectives={perspectives}");
        // ⭐⭐ CE-076 CLOSED THE GAP THIS LINE USED TO WARN ABOUT. `WindowManager.RegisterWindow` now calls
        //    PanelSnapshot.DeclareInstrumented(window.Id), so EVERY registered window is in `registered[]`
        //    by construction. ⚠ What remains is a much smaller, NAMED caveat: a window registered LAZILY
        //    (on first perspective activation) is absent until its perspective has been visited — which is
        //    why the capture above WALKS the perspectives and why the golden can hold one more id than a
        //    single read reports. 📐 Measured 2026-08-27: editor single-read 63, perspective-walk 64.
        _out.WriteLine($"[{mode}] ⭐ every registered window is declared (CE-076). ⚠ A LAZILY registered "
                     + "window still needs its perspective visited first — the capture walks them.");

        // ⛔ A floor, not an equality: zero would mean the capture surface is dead and every baseline
        //    comparison above is vacuous.
        Assert.True(registered > 0, $"`--mode {mode}` reported ZERO instrumented panels — the baseline net "
                                  + "would be vacuous. Check that the host actually drew a frame.");
    }
}
