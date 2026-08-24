using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b><c>N5</c> — THE PLATOON APPROACHES THE COMPUTED BASELINE, NOT THE ORIGIN.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N5</c> · §4 <c>D7</c>'s worked example · <c>R-132</c>.
///
/// <para>⭐⭐⭐ <b>This is the assertion the programme's worst silent defect needed.</b> 📌 <c>R-132</c>
/// *(fixed in <c>9aa790d57</c>)*: <c>ApplyResolverOverlay</c> read <c>if (def.ParseParams == null)</c>, so a
/// GENERATED <c>ParseParams</c> won the slot over the curated geo-aware resolver. The two expect different
/// wire formats, so every mission-plan key hit <c>default: break</c> and the params region stayed
/// <b>zeros</b> — ⛔ <b>the platoon drove to <c>(0,0)</c> with every rail green, no exception and no log
/// line.</b></para>
///
/// <para>⭐⭐ <b>Why it is TWO claims and not one.</b> *"The platoon advanced"* was already green while the
/// defect was live *(<c>ScenarioBehaviorTests.Playing_hill_attack_advances_the_assault_force</c> — the force
/// moves either way, just to the wrong place)*. ⇒ the meaningful claim needs a DIRECTION: ⭐ the tanks CLOSE
/// ON the authored baseline, and ⛔ they do NOT close on the local origin. ⚠ Either half alone is
/// blessable; together they are not.</para>
///
/// <para>⛔⛔ <b>WHY THIS DOES NOT LIVE IN <c>ScenarioBehaviorTests</c>, which is where §9's <c>Q1</c> said to
/// put it — and it is a MEASURED reason.</b> 📐 `2026-08-24`, with <c>9aa790d57</c> reverted: run as part of
/// the FULL suite on the shared collection editor the case <b>PASSED</b>; run in its own process, and run
/// with only its own class, it <b>FAILED</b> *(closest-to-origin <c>613.9 m → 0.4 m</c>)*. ⇒ ⭐⭐⭐ <b>something
/// earlier in the suite makes the defect invisible</b> — the shared editor has by then loaded other
/// scenarios, staged variable writes and cycled preview. 🔴 <b>A flagship falsifiable claim that passes in
/// the suite while the defect is live is §3's failure mode exactly</b>, so it gets a FRESH process, the way
/// <c>DeterminismRails</c> does and for the same reason.</para>
///
/// <para>📐 <b>Measured on the fixed tree:</b> baseline local <c>(523, 401)</c>–<c>(532, 548)</c>; the tanks
/// start ~<c>x=446–455</c> and close to within <b>≈3 m</b> of it within ~8 s while their distance to the
/// origin GROWS by ~60 m. ⭐ The threshold sits far inside that, so this asserts the direction of travel,
/// ⛔ not a tuning constant.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemScenario")]
public sealed class PlatoonBaselineRails
{
    private readonly ITestOutputHelper _out;
    public PlatoonBaselineRails(ITestOutputHelper output) => _out = output;

    /// <summary>⭐ Well inside the measured ≈3 m closest approach — the claim is "it gets there", not "to the cm".</summary>
    private const double ReachedBaselineMetres = 60.0;

    [SystemSmokeFact]
    public async Task Playing_hill_attack_moves_the_platoon_toward_the_authored_baseline_not_the_origin()
    {
        await using var ed = await EditorProcess.StartAsync("baseline");
        var mcp = ed.Client;

        (await mcp.LoadScenarioAsync("hill-attack")).EnsureOk();
        (await mcp.EnterPreviewIfNeededAsync(startPaused: true)).EnsureOk();

        var (a, b) = await BaselineLocalAsync(mcp);
        _out.WriteLine($"authored baseline (local): ({a.X:0.0},{a.Y:0.0}) → ({b.X:0.0},{b.Y:0.0})");

        var before = await PositionsAsync(mcp);
        Assert.NotEmpty(before);

        var beforeToBaseline = ClosestToSegment(before, a, b);
        var beforeToOrigin   = ClosestToPoint(before, (0, 0));
        _out.WriteLine($"before: closest-to-baseline={beforeToBaseline:0.0} m, closest-to-origin={beforeToOrigin:0.0} m");

        (await mcp.PlayAsync()).EnsureOk();

        // ⭐ Poll for the CLAIM, not for a duration. ⭐⭐ And poll WITHOUT throwing on timeout: the interesting
        //   failure is not "it timed out", it is "it went the other way", and saying so needs BOTH distances.
        double reached = double.MaxValue;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            reached = ClosestToSegment(await PositionsAsync(mcp), a, b);
            if (reached <= ReachedBaselineMetres) break;
            await Task.Delay(500);
        }

        var afterToOrigin = ClosestToPoint(await PositionsAsync(mcp), (0, 0));
        _out.WriteLine($"after : closest-to-baseline={reached:0.0} m, closest-to-origin={afterToOrigin:0.0} m");

        Assert.True(reached <= ReachedBaselineMetres,
            $"no vehicle got within {ReachedBaselineMetres} m of the authored baseline in 45 s "
          + $"(closest {reached:0.0} m, was {beforeToBaseline:0.0} m before the run; distance to the local "
          + $"origin went {beforeToOrigin:0.0} m → {afterToOrigin:0.0} m).\n"
          + "⛔ This is R-132's signature: the curated geo resolver did not run, so the behaviour params are "
          + "all zeros and the baseline collapses to the origin. See 9aa790d57.");

        // ⛔ THE OTHER HALF. With the defect live the force converges on (0,0); it must not.
        Assert.True(afterToOrigin >= beforeToOrigin - ReachedBaselineMetres,
            $"the platoon CLOSED ON THE LOCAL ORIGIN (before {beforeToOrigin:0.0} m → after {afterToOrigin:0.0} m). "
          + "⛔ That is exactly what zeroed behaviour params look like (R-132 / 9aa790d57).");
    }

    // ── the expectation is COMPUTED from the scenario, never hard-coded ────────

    /// <summary>
    /// ⭐⭐ The authored baseline in LOCAL metres: read from the scenario's own mission plan and converted
    /// through the engine's <c>geo-to-local</c> — 📌 the same <c>ToCartesian</c> path the curated resolver
    /// uses.
    /// <para>⛔ Deliberately not a constant in this file: a hard-coded coordinate is the shape that silently
    /// stops describing the scenario it claims to be about.</para>
    /// </summary>
    private static async Task<((double X, double Y) A, (double X, double Y) B)> BaselineLocalAsync(McpClient mcp)
    {
        var plan = (await mcp.GetEntityAsync(1000)).EnsureOk()
                   .Field("Components")?["MissionPlan"]?["PlanData"]?["tasks"]?[0]?["behaviorParams"]
                   ?.GetValue<string>()
                   ?? throw new InvalidOperationException(
                       "entity 1000 published no MissionPlan behaviourParams — the scenario changed shape, so "
                     + "this assertion cannot compute its expectation (do not weaken it: fix the read).");

        var p = JsonNode.Parse(plan)!;
        return (await ToLocalAsync(mcp, p["baselineStart"]!), await ToLocalAsync(mcp, p["baselineEnd"]!));
    }

    private static async Task<(double X, double Y)> ToLocalAsync(McpClient mcp, JsonNode latLon)
    {
        var local = (await mcp.GeoToLocalAsync(latLon[0]!.GetValue<double>(), latLon[1]!.GetValue<double>()))
                    .EnsureOk().DataOrThrow();
        return (local["x"]!.GetValue<double>(), local["y"]!.GetValue<double>());
    }

    private static async Task<Dictionary<long, (double X, double Y)>> PositionsAsync(McpClient mcp)
    {
        var listed = (await mcp.ListEntitiesAsync(component: "SimTransform")).EnsureOk().Array();
        var result = new Dictionary<long, (double X, double Y)>();

        foreach (var row in listed)
        {
            var id = row?["networkId"]?.GetValue<long>();
            if (id is null) continue;

            var dump = await mcp.GetEntityAsync(id.Value);
            if (!dump.Ok) continue;   // a straggler id is not this rail's subject

            var position = dump.Field("Components")?["SimTransform"]?["Position"];
            if (position is JsonArray arr && arr.Count >= 2)
                result[id.Value] = (arr[0]!.GetValue<double>(), arr[1]!.GetValue<double>());
        }
        return result;
    }

    /// <summary>⭐ Closest approach of any entity to the baseline SEGMENT — the slots are spread along it.</summary>
    private static double ClosestToSegment(
        Dictionary<long, (double X, double Y)> positions, (double X, double Y) a, (double X, double Y) b)
        => positions.Count == 0 ? double.MaxValue : positions.Values.Min(p => DistanceToSegment(p, a, b));

    private static double ClosestToPoint(Dictionary<long, (double X, double Y)> positions, (double X, double Y) q)
        => positions.Count == 0 ? double.MaxValue
         : positions.Values.Min(p => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)));

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double wx = p.X - a.X, wy = p.Y - a.Y;
        double len2 = vx * vx + vy * vy;
        double t = len2 <= double.Epsilon ? 0 : Math.Clamp((wx * vx + wy * vy) / len2, 0, 1);
        double dx = p.X - (a.X + t * vx), dy = p.Y - (a.Y + t * vy);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
