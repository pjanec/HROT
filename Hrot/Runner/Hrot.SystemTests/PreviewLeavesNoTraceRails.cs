using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b>A PREVIEW MUST LEAVE NO TRACE — the rails, and today they pin the DEFECT.</b>
/// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §1 *(the requirement, in the user's words)* · §2
/// *(the measured mechanism)* · §6 *(the rails)*.
///
/// <para>🔒 <b>User, `2026-08-23`:</b> <i>"the reset is still wanted feature in scenario preview — when we
/// finish the preview the world resets but not so the network id allocator — so next preview (without
/// restart of the app) does not start from same id as previous preview, but continues. Not desired; for
/// repeated runs of the same we would like to have same ids."</i></para>
///
/// <para>⛔⛔ <b>THESE ARE TRIPWIRES, NOT THE FIX — and that is deliberate.</b> 📄 The handoff's item ⓪ says
/// to ENUMERATE what else a preview fails to rewind and <i>"say so and stop at the report rather than
/// fixing two subsystems under one item"</i>. 📐 The enumeration found <b>three</b> stale participants, and
/// 🔴 <b>fixing only the allocator would turn a silent drift into a THROWN EXCEPTION</b> —
/// <c>NetworkEntityMap.Register</c> throws <c>"NetworkId {id} already registered"</c> on a duplicate, and
/// the editor never prunes the map *(measured: <c>OfflineNetworkFactory</c> supplies a
/// <c>NullReplicationModule</c>, so <c>DisposalMonitoringSystem</c> is never registered)</para>
///
/// <para>⭐⭐ <b>So these rails record the CURRENT behaviour, precisely, and fail the day it changes</b> —
/// the same sanctioned shape as <c>ModeStartupRails</c>' <c>--mode ig</c> case and <c>HN-011</c>. ⇒ ⛔ the
/// requirement cannot be quietly forgotten, and ⛔ nobody can ship the allocator half alone without this
/// going red and naming why.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemPreview")]
public sealed class PreviewLeavesNoTraceRails : SystemTestBase
{
    public PreviewLeavesNoTraceRails(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    /// <summary>
    /// ⭐⭐ <b>A tkbType DISCOVERED from the running editor, never hard-coded.</b>
    /// <para>⚠⚠ 📐 Measured `2026-08-23`: <c>POST /entities/spawn</c> with a type that does not resolve
    /// answers <c>{"spawned":true, "reason":null}</c> and creates NOTHING — it publishes a
    /// <c>SpawnEntityCommand</c> on the bus and returns success unconditionally *(<c>HN-014</c>)*. ⇒ ⛔ a
    /// hard-coded type is indistinguishable from a broken spawn path, so the type comes from
    /// <c>GET /tkb/types</c>.</para>
    /// </summary>
    private async Task<long?> FirstSpawnableTkbTypeAsync()
    {
        var types = await Mcp.ListTkbTypesAsync();
        if (!types.Ok || types.Data is not JsonArray arr || arr.Count == 0)
        {
            Output.WriteLine($"GET /tkb/types gave nothing usable: {types.StatusCode} {types.Error}");
            return null;
        }
        var t = arr[0]!["tkbType"]!.GetValue<long>();
        Output.WriteLine($"using tkbType {t} ({arr[0]!["name"]}) of {arr.Count} available");
        return t;
    }

    // ⭐⭐⭐ HN-017's END-TO-END RAIL IS NOT HERE, AND THAT IS MEASURED, NOT LAZY.
    //
    // 📄 The requirement — "two consecutive previews produce the same ids" — is asserted in
    //    Fdp.Toolkits.Tests/Orchestration/APreviewLeavesNoTraceTests, against the REAL PreviewStateBracket
    //    and the REAL allocators, and 4 of its 9 rails were shown to go RED on a revert probe.
    //
    // ⛔⛔ A system-level version was WRITTEN AND REMOVED: it must read the allocated ids from
    //    GET /entities (POST /entities/spawn carries none — HN-014), and HN-015 makes that endpoint answer
    //    500 after any runtime spawn. ⇒ the rail could only ever be red for a reason unrelated to its own
    //    claim, and R-131 forbids shipping that.
    // ⭐ The tripwire below is what keeps HN-015 visible; invert it, then re-add the end-to-end rail.

    /// <summary>
    /// 🔴🔴🔴 <b>TRIPWIRE — <c>HN-015</c>: ONE runtime-spawned entity breaks <c>GET /entities</c> ENTIRELY.</b>
    ///
    /// <para>📐 <b>Measured `2026-08-23`:</b> spawn an <c>M1 Abrams</c> *(tkbType 100, discovered from
    /// <c>GET /tkb/types</c>)* inside a running preview, then call <c>GET /entities</c> ⇒ <b>HTTP 500:
    /// <i>".NET number values such as positive and negative infinity cannot be written as valid
    /// JSON"</i></b>. ⇒ ⛔⛔ a single entity carrying a non-finite float takes down the whole listing, not
    /// just its own row.</para>
    ///
    /// <para>⭐⭐⭐ <b>And the capability to prevent it is ALREADY BUILT AND WIRED TO NOTHING.</b> 📐
    /// <c>Hrot.Editor/DebugApi/DebugApiSafeFloatConverters.cs</c> defines
    /// <c>NonFiniteFloatSentinelConverter</c>, <c>NonFiniteDoubleSentinelConverter</c> and
    /// <c>DebugApiConverterHelpers</c> — which write <c>"Infinity"</c>/<c>"NaN"</c> as string sentinels,
    /// exactly this failure's fix — and 🔴 <b><c>grep</c> for their use across the repo returns ZERO
    /// application sites.</b> 📌 The built-but-unwired shape, and the reason an agent loses
    /// <c>GET /entities</c> permanently after any runtime spawn.</para>
    ///
    /// <para>⚠ <b>Why this matters to THIS batch:</b> it is what blocks measuring <c>HN-012</c> *(the
    /// preview id drift)* through the API at all — the drift is real and code-measured, but the endpoint
    /// that would show it dies first. ⇒ ⛔ <c>HN-015</c> is a prerequisite for the preview rails, not a
    /// side issue.</para>
    ///
    /// <para>⛔ <b>When the converters are applied this reddens</b> — then assert the listing SUCCEEDS and
    /// close <c>HN-015</c>.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task A_runtime_spawn_breaks_the_entity_listing_HN_015()
    {
        var tkbType = await FirstSpawnableTkbTypeAsync();
        Assert.True(tkbType.HasValue, "no TKB type is available, so no spawn can be attempted");

        (await Mcp.EnterPreviewIfNeededAsync(startPaused: false)).EnsureOk();
        (await Mcp.PlayAsync()).EnsureOk();
        await Task.Delay(300);

        // ⭐ The listing works BEFORE the spawn — so the failure is attributable to the spawn, not to the
        //   endpoint being broken generally. ⛔ Without this the rail would prove much less.
        (await Mcp.ListEntitiesAsync()).EnsureOk();

        (await Mcp.SpawnEntityAsync(tkbType!.Value)).EnsureOk();
        await Task.Delay(600);

        var after = await Mcp.ListEntitiesAsync();
        Output.WriteLine($"after a runtime spawn, GET /entities → {after.StatusCode}: {after.Error}");

        Assert.Equal(500, after.StatusCode);
        Assert.Contains("infinity", (after.Error ?? "").ToLowerInvariant());

        // ⭐ Leave the fixture usable for the cases that follow: exit the preview so the spawned entity is
        //   rewound away. ⚠ The collection shares one editor — a rail that poisons it fails its neighbours.
        (await Mcp.ExitPreviewAsync()).EnsureOk();
        await Mcp.StepAsync(2);
    }
}
