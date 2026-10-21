using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐ <c>CE-4038</c> B0′ (docs/DESIGN_Film_Director.md §0, R-3038; HANDOFF_Backend_To_Ui_Film_Director.md) — <b>the two-run identity
/// rail</b>: one seeded take run twice by the director, stepped once per frame (a scout with nothing baked, so the file is not changed), and
/// the world compared at EVERY CUT — each named unit's position and health, the rounds fired — and every beat's time.
///
/// <para>⭐ <b>A REPORT now, a GATE after CE-3293 S5.</b> Until step-only systems receive every event since the last step, stepped runs are
/// not guaranteed to repeat (🔒 R-3034 measured them differing, CE-3291), so this prints the differences and passes. ⭐ Set
/// <c>HROT_DIRECTOR_IDENTITY_GATE=1</c> once S5 lands and it FAILS on any difference — then "the filmed run equals the scouted run" is
/// accepted (§0).</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemDirector")]
public sealed class DirectorIdentityRails : SystemTestBase
{
    public DirectorIdentityRails(EditorProcessFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    /// <summary>The take every identity claim is made against: two tank rounds, cut on each (F-45).</summary>
    private const string Scenario = "ua-tank-ammo";

    private async Task<JsonObject> ScoutOnceAsync()
    {
        var started = await Mcp.DirectorScoutAsync(new JsonObject { ["scenario"] = Scenario, ["take"] = "main", ["bake"] = false });
        Assert.True(started.Ok, $"director/scout: {started.StatusCode} {started.Error}");
        var until = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < until)
        {
            var st = (await Mcp.DirectorStatusAsync()).EnsureOk().DataOrThrow().AsObject();
            var phase = st["phase"]?.ToString();
            if (phase is "Done" or "Stopped") return st["reports"]![0]!.AsObject();
            await Task.Delay(500);
        }
        await Mcp.DirectorStopAsync();
        throw new TimeoutException("the scout did not finish in 4 min");
    }

    [Fact]
    public async Task CE4038_ATakeRunTwice_IsTheSameAtEveryCut_ReportNowGateAfterS5()
    {
        var a = await ScoutOnceAsync();
        var b = await ScoutOnceAsync();
        Output.WriteLine($"run 1: {a["status"]} steps {a["steps"]} found {a["found"]?.ToJsonString()} · max steps in a frame {a["maxStepsInAFrame"]}");
        Output.WriteLine($"run 2: {b["status"]} steps {b["steps"]} found {b["found"]?.ToJsonString()}");
        Assert.Equal(1, a["maxStepsInAFrame"]!.GetValue<int>());   // ⭐ the stepping rule held in both — a GATE today
        Assert.Equal(1, b["maxStepsInAFrame"]!.GetValue<int>());

        var diffs = Compare(a, b);
        foreach (var d in diffs) Output.WriteLine("DIFF " + d);
        Output.WriteLine(diffs.Count == 0 ? "IDENTICAL at every cut" : $"{diffs.Count} difference(s) between the two runs (a REPORT until CE-3293 S5)");
        if (Environment.GetEnvironmentVariable("HROT_DIRECTOR_IDENTITY_GATE") == "1")
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
