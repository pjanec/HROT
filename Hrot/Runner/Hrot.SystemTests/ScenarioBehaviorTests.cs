using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// Scenario-behaviour cases (task H5): assertions about what the simulation DOES, not merely about
/// whether an endpoint answers. The capability suite proves the controls work; these prove the
/// world behaves.
///
/// <para><b>The pattern, so cases grow with the curated set</b> — each case is four steps:</para>
/// <list type="number">
/// <item><description><b>Reach a known world.</b> <c>LoadAndPreviewAsync(scenario)</c> — the load
/// awaits <c>OperatingEdit</c> host-side, so the world is really there when it returns.</description></item>
/// <item><description><b>Read the "before".</b> Sample the state you are about to make a claim
/// about, through <c>GET /entities</c> — never assume a starting value.</description></item>
/// <item><description><b>Run the world.</b> <c>PlayAsync()</c>, then poll with
/// <c>WaitUntilAsync</c>. ⛔ Never assert straight after play: the sim advances over frames, so a
/// bare assert tests timing luck.</description></item>
/// <item><description><b>Assert an OUTCOME with a tolerance.</b> "moved at least N metres", not an
/// exact coordinate — a float-exact expectation turns any tuning change into a red.</description></item>
/// </list>
///
/// <para>⚠ Cases here share the collection's single editor, so each one pauses the clock again when
/// it is done; a case that leaves the world running changes what the next one measures.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemScenario")]
public sealed class ScenarioBehaviorTests : SystemTestBase
{
    public ScenarioBehaviorTests(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    /// <summary>
    /// The assault force in <c>hill-attack</c> actually advances when the scenario runs — the
    /// end-to-end proof that a curated world loads, its missions materialise, its behaviours tick,
    /// and movement reaches the components an observer reads.
    ///
    /// <para>Measured baseline: with the platoon executing <c>PlatoonHillAttack</c>, an entity
    /// covers roughly 30 m in the first 6 s. The threshold below is deliberately far under that —
    /// the claim is "the force advances", not "it advances at exactly this speed".</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task Playing_hill_attack_advances_the_assault_force()
    {
        const double MovedEnoughMetres = 5.0;

        await LoadAndPreviewAsync("hill-attack", startPaused: true);
        try
        {
            var before = await PositionsAsync();
            Assert.NotEmpty(before);

            (await Mcp.PlayAsync()).EnsureOk();

            var moved = await WaitUntilAsync(
                async () => FurthestMoved(before, await PositionsAsync()),
                m => m.Distance >= MovedEnoughMetres,
                $"an entity in hill-attack to advance at least {MovedEnoughMetres} m",
                timeout: TimeSpan.FromSeconds(45));

            Output.WriteLine($"entity {moved.NetworkId} advanced {moved.Distance:0.0} m");
            Assert.True(moved.Distance >= MovedEnoughMetres);
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    /// <summary>
    /// Pausing actually stops the world — the other half of the control loop, and the property
    /// every breakpoint and inspection case silently depends on.
    /// </summary>
    [SystemSmokeFact]
    public async Task Pausing_holds_the_world_still()
    {
        await LoadAndPreviewAsync("hill-attack", startPaused: true);
        try
        {
            (await Mcp.PlayAsync()).EnsureOk();
            await WaitForSimTimeAsync(0.1, TimeSpan.FromSeconds(20));

            (await Mcp.PauseAsync()).EnsureOk();

            // Let any in-flight frame finish, then confirm the clock is genuinely still.
            await Task.Delay(TimeSpan.FromSeconds(1));
            var settled = (await Mcp.GetStatusAsync()).EnsureOk().Double("simTime");
            await Task.Delay(TimeSpan.FromSeconds(2));
            var later = (await Mcp.GetStatusAsync()).EnsureOk().Double("simTime");

            Assert.Equal(settled, later, precision: 3);
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private readonly record struct Moved(long NetworkId, double Distance);

    /// <summary>Every entity's XY position, keyed by network id. Entities without one are skipped.</summary>
    private async Task<Dictionary<long, (double X, double Y)>> PositionsAsync()
    {
        var listed = (await Mcp.ListEntitiesAsync(component: "SimTransform")).EnsureOk().Array();
        var result = new Dictionary<long, (double X, double Y)>();

        foreach (var row in listed)
        {
            var id = row?["networkId"]?.GetValue<long>();
            if (id is null) continue;

            var dump = await Mcp.GetEntityAsync(id.Value);
            if (!dump.Ok) continue;   // a straggler id is not this test's subject

            if (ReadPosition(dump) is { } pos)
                result[id.Value] = pos;
        }
        return result;
    }

    private static Moved FurthestMoved(
        Dictionary<long, (double X, double Y)> before,
        Dictionary<long, (double X, double Y)> after)
    {
        var best = new Moved(0, 0);
        foreach (var (id, start) in before)
        {
            if (!after.TryGetValue(id, out var now)) continue;
            double dx = now.X - start.X, dy = now.Y - start.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > best.Distance) best = new Moved(id, distance);
        }
        return best;
    }

    /// <summary>Reads <c>SimTransform.Position</c>, which the dump serializes as <c>[x,y,z]</c>.</summary>
    private static (double X, double Y)? ReadPosition(ApiResult dump)
    {
        var position = dump.Field("Components")?["SimTransform"]?["Position"];
        return position switch
        {
            JsonArray arr when arr.Count >= 2 => (arr[0]!.GetValue<double>(), arr[1]!.GetValue<double>()),
            JsonObject obj when obj["X"] is not null => (obj["X"]!.GetValue<double>(), obj["Y"]!.GetValue<double>()),
            _ => null,
        };
    }
}
