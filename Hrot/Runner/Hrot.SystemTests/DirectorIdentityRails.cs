using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐ <c>CE-4038</c> B0′ / <c>CE-1240</c> (docs/DESIGN_Film_Director.md §0, R-3038) — <b>the identity rail: filmed = scouted</b>. A seeded take
/// run THREE times by the director (each a fresh load, stepped once per frame; a scout with nothing baked, so the file is not changed), and
/// the world compared at EVERY CUT — each named unit's position and health, the rounds fired — and every beat's time.
///
/// <para>⭐ <b>A GATE since CE-3293 S5</b> (step-only systems receive every event since the last step, so stepped runs repeat). ⛔ SUPERSEDED:
/// "a REPORT until S5, a gate behind <c>HROT_DIRECTOR_IDENTITY_GATE=1</c>". ⚠ <c>HROT_DIRECTOR_IDENTITY_REPORT=1</c> turns it back into a
/// report (the differences printed, the rail green) — for measuring a regression, never for a gate.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemDirector")]
public sealed class DirectorIdentityRails : SystemTestBase
{
    public DirectorIdentityRails(EditorProcessFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    private const int Runs = 3;

    private async Task<JsonObject> ScoutOnceAsync(string scenario)
    {
        var started = await Mcp.DirectorScoutAsync(new JsonObject { ["scenario"] = scenario, ["take"] = "main", ["bake"] = false });
        Assert.True(started.Ok, $"director/scout: {started.StatusCode} {started.Error}");
        var until = DateTime.UtcNow.AddMinutes(6);
        while (DateTime.UtcNow < until)
        {
            var st = (await Mcp.DirectorStatusAsync()).EnsureOk().DataOrThrow().AsObject();
            var phase = st["phase"]?.ToString();
            if (phase is "Done" or "Stopped") return st["reports"]![0]!.AsObject();
            await Task.Delay(500);
        }
        await Mcp.DirectorStopAsync();
        throw new TimeoutException("the scout did not finish in 6 min");
    }

    /// <summary>Three takes whose beats are different cues: rounds fired (the M1's two shots), a blast, a melee blow.</summary>
    [Theory]
    [InlineData("ua-tank-ammo")]
    [InlineData("bt-grenade-posture")]
    [InlineData("ua-civ-melee")]
    public async Task CE1240_ATakeRunThreeTimes_IsTheSameAtEveryCut(string scenario)
    {
        var runs = new List<JsonObject>();
        for (int i = 0; i < Runs; i++) runs.Add(await ScoutOnceAsync(scenario));
        foreach (var (r, i) in runs.Select((r, i) => (r, i)))
        {
            Output.WriteLine($"run {i + 1}: {r["status"]} steps {r["steps"]} found {r["found"]?.ToJsonString()} cuts {r["cuts"]?.AsArray().Count} · "
                           + $"max steps in a frame {r["maxStepsInAFrame"]}, the kernel {r["maxRetiredInAFrame"]}, waits {r["waitFrames"]}");
            Assert.Equal(1, r["maxStepsInAFrame"]!.GetValue<int>());    // ⭐ the stepping rule, asked …
            Assert.Equal(1, r["maxRetiredInAFrame"]!.GetValue<int>());  // … and taken
        }

        var diffs = new List<string>();
        for (int i = 1; i < runs.Count; i++)
            diffs.AddRange(Compare(runs[0], runs[i]).Select(d => $"run 1 vs {i + 1}: {d}"));
        foreach (var d in diffs) Output.WriteLine("DIFF " + d);
        Output.WriteLine(diffs.Count == 0 ? $"IDENTICAL at every cut, {Runs} runs" : $"{diffs.Count} difference(s) across {Runs} runs");
        if (Environment.GetEnvironmentVariable("HROT_DIRECTOR_IDENTITY_REPORT") != "1")
            Assert.True(diffs.Count == 0, string.Join("\n", diffs));
    }

    /// <summary>Every difference between two reports: a beat's time, a cut's time, a unit's position (1 mm) or health, the rounds fired.</summary>
    internal static List<string> Compare(JsonObject a, JsonObject b)
    {
        var diffs = new List<string>();
        var fa = a["found"]!.AsObject();
        var fb = b["found"]!.AsObject();
        foreach (var key in fa.Select(kv => kv.Key).Union(fb.Select(kv => kv.Key)))
        {
            double? x = fa[key]?.GetValue<double>(), y = fb[key]?.GetValue<double>();
            if (x is null || y is null || Math.Abs(x.Value - y.Value) > 1e-6) diffs.Add($"beat {key}: {x?.ToString() ?? "-"} vs {y?.ToString() ?? "-"}");
        }
        var ca = a["cuts"]!.AsArray();
        var cb = b["cuts"]!.AsArray();
        if (ca.Count != cb.Count) diffs.Add($"{ca.Count} cuts vs {cb.Count}");
        for (int i = 0; i < Math.Min(ca.Count, cb.Count); i++)
        {
            var x = ca[i]!.AsObject();
            var y = cb[i]!.AsObject();
            string at = $"cut {i + 1} (shot {x["shot"]})";
            if (Math.Abs(x["t"]!.GetValue<double>() - y["t"]!.GetValue<double>()) > 1e-6) diffs.Add($"{at}: at {x["t"]} vs {y["t"]}");
            if (x["rounds"]!.GetValue<long>() != y["rounds"]!.GetValue<long>()) diffs.Add($"{at}: {x["rounds"]} rounds vs {y["rounds"]}");
            var ux = x["units"]!.AsObject();
            var uy = y["units"]!.AsObject();
            foreach (var name in ux.Select(kv => kv.Key).Union(uy.Select(kv => kv.Key)))
            {
                if (ux[name] is not JsonArray p || uy[name] is not JsonArray q) { diffs.Add($"{at}: {name} in one run only"); continue; }
                double dist = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(p[k]!.GetValue<double>() - q[k]!.GetValue<double>(), 2)));
                if (dist > 1e-3) diffs.Add($"{at}: {name} {dist:0.###} m apart");
                double? hp = p[3]?.GetValue<double>(), hq = q[3]?.GetValue<double>();
                if (hp != hq && (hp is null || hq is null || Math.Abs(hp.Value - hq.Value) > 1e-3)) diffs.Add($"{at}: {name} health {hp} vs {hq}");
            }
        }
        return diffs;
    }
}
