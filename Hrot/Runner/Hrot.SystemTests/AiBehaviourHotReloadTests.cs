using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐ <c>CE-1321</c> (Brain View §12g BV-68) — the C# behaviour rebuild and hot swap, for real: <c>POST /ai/reload</c> builds
/// <c>Hrot.AI.Behaviors</c>, copies it into the editor's directory (🔴 measured before this: nothing did, so the toolbar button rebuilt
/// and never reloaded), and answers only after the swap. ⭐ Running units keep their task across it.
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemHotReload")]
public sealed class AiBehaviourHotReloadTests : SystemTestBase
{
    public AiBehaviourHotReloadTests(EditorProcessFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    [SystemSmokeFact]
    public async Task The_csharp_behaviours_rebuild_and_hot_swap_and_running_units_keep_their_task()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync("hill-attack"), startPaused: true);
        try
        {
            (await Mcp.StepAsync(5)).EnsureOk();
            var entities = (await Mcp.ListEntitiesAsync()).EnsureOk().Array();
            long id = 0; string? before = null;
            foreach (var e in entities)
            {
                long n = e!["networkId"]?.GetValue<long>() ?? 0;
                var slots = (await Mcp.GetEntitySlotsAsync(n)).Field("slots") as JsonArray;
                var task = slots?.FirstOrDefault(s => s?["owner"]?.GetValue<string>() == "Task");
                if (task is null) continue;
                id = n; before = task["behaviour"]?.GetValue<string>();
                break;
            }

            var r = (await Mcp.ReloadAiBehavioursAsync()).EnsureOk();
            Output.WriteLine(r.Data?.ToJsonString() ?? "");
            Assert.Equal("ok", r.String("build"));
            Assert.True(r.Bool("reloaded"), r.String("note"));
            Assert.True(r.Int("behaviours") > 0);

            if (id != 0)
            {
                (await Mcp.StepAsync(2)).EnsureOk();
                var after = (await Mcp.GetEntitySlotsAsync(id)).Field("slots") as JsonArray;
                var task = after?.FirstOrDefault(s => s?["owner"]?.GetValue<string>() == "Task");
                Output.WriteLine($"entity {id}: {before} → {task?["behaviour"]}");
                Assert.Equal(before, task?["behaviour"]?.GetValue<string>());   // the swap did not restart or drop the running task
            }
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }
}
