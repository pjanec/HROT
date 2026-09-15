using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-07 Tier-1 gate — exercises Group G (breakpoints) endpoints against the
/// offline <see cref="EditorHarness"/>. No HTTP; runs fast.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch07Tests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonNode LifecycleConditionNode(string targetValue = "Alpha") =>
        JsonNode.Parse($$$"""
        {
            "$type": "Lifecycle",
            "IdentifierType": "NameSubstring",
            "TargetValue": "{{{targetValue}}}",
            "NamePropertyPath": "Name"
        }
        """)!;

    private static JsonNode AddBreakpointBody(string targetValue = "Alpha", string? name = null,
        int occurrenceThreshold = 1) =>
        new JsonObject
        {
            ["condition"] = LifecycleConditionNode(targetValue),
            ["name"] = name ?? "",
            ["occurrenceThreshold"] = occurrenceThreshold,
        };

    // ── AddBreakpoint ─────────────────────────────────────────────────────────

    [Fact]
    public void AddBreakpoint_Compound_RoundTrips()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        // Use a LifecyclePredicateDto (simple, no component registration needed)
        var body = AddBreakpointBody("TestUnit");
        var result = svc.AddBreakpoint(body).AsObject();

        Assert.NotNull(result["breakpointId"]);
        var bpId = result["breakpointId"]!.GetValue<string>();
        Assert.True(bpId.StartsWith("BP#"), $"Expected 'BP#N' format, got '{bpId}'");

        // Verify it appears in the list
        var list = svc.ListBreakpoints().AsArray();
        Assert.Single(list);
        var entry = list[0]!.AsObject();
        Assert.Equal(bpId, entry["id"]!.GetValue<string>());
    }

    [Fact]
    public void AddBreakpoint_NullCondition_ThrowsArgumentException()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        var body = new JsonObject(); // no condition key
        Assert.Throws<ArgumentException>(() => svc.AddBreakpoint(body));
    }

    [Fact]
    public void AddBreakpoint_WithOccurrenceThreshold_StoresCorrectly()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        var body = AddBreakpointBody("Alpha", name: "threshold-test", occurrenceThreshold: 5);
        svc.AddBreakpoint(body);

        var list = svc.ListBreakpoints().AsArray();
        Assert.Single(list);
        var entry = list[0]!.AsObject();
        Assert.Equal(5, entry["occurrenceThreshold"]!.GetValue<int>());
        Assert.Equal("threshold-test", entry["name"]!.GetValue<string>());
    }

    // ── ListBreakpoints ───────────────────────────────────────────────────────

    [Fact]
    public void ListBreakpoints_ReturnsAll()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        svc.AddBreakpoint(AddBreakpointBody("Alpha", name: "bp1"));
        svc.AddBreakpoint(AddBreakpointBody("Bravo", name: "bp2"));

        var list = svc.ListBreakpoints().AsArray();
        Assert.Equal(2, list.Count);

        // Check that each entry has required fields
        foreach (var item in list)
        {
            var obj = item!.AsObject();
            Assert.NotNull(obj["id"]?.GetValue<string>());
            Assert.NotNull(obj["conditionSummary"]?.GetValue<string>());
            Assert.NotNull(obj["enabled"]);
            Assert.NotNull(obj["occurrenceThreshold"]);
            Assert.NotNull(obj["hitCount"]);
            Assert.NotNull(obj["name"]);
        }
    }

    // ── RemoveBreakpoint ──────────────────────────────────────────────────────

    [Fact]
    public void RemoveBreakpoint_RemovesFromList()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        var result = svc.AddBreakpoint(AddBreakpointBody()).AsObject();
        var bpId = result["breakpointId"]!.GetValue<string>();

        Assert.Single(svc.ListBreakpoints().AsArray());

        svc.RemoveBreakpoint(bpId);

        Assert.Empty(svc.ListBreakpoints().AsArray());
    }

    [Fact]
    public void RemoveBreakpoint_UnknownId_ThrowsArgumentException()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        Assert.Throws<ArgumentException>(() => svc.RemoveBreakpoint("BP#99999"));
    }

    // ── GetBreakpointStatus ───────────────────────────────────────────────────

    [Fact]
    public void GetBreakpointStatus_InitiallyNotPaused()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        var status = svc.GetBreakpointStatus().AsObject();

        Assert.False(status["isPaused"]!.GetValue<bool>());
        Assert.Equal(0L, status["pausedTick"]!.GetValue<long>());
        Assert.Null(status["lastHit"]);
    }

    [Fact]
    public void OnBreakpointHit_Event_UpdatesHitState()
    {
        using var h = new EditorHarness(enableBreakpoints: true);
        var svc = h.BuildDebugApiService();

        // Register a breakpoint
        var addResult = svc.AddBreakpoint(AddBreakpointBody("Alpha")).AsObject();
        var bpId = addResult["breakpointId"]!.GetValue<string>();

        // Get the Breakpoint record from the manager
        var bp = h.BpManager!.AllBreakpoints[0];

        // Create a test entity and register it in the entity map
        var testEntity = h.Repo.CreateEntity();
        const long testNetworkId = 77777L;
        h.EntityMap.Register(testNetworkId, testEntity);

        // Directly invoke OnHit to trigger the OnBreakpointHit event
        // (DataBreakpointSystem calls this; it internally raises OnBreakpointHit which
        // the service subscribes to)
        h.BpManager.OnHit(bp, testEntity);

        // Verify GetBreakpointStatus reflects the injected hit
        var status = svc.GetBreakpointStatus().AsObject();
        Assert.NotNull(status["lastHit"]);
        var lastHit = status["lastHit"]!.AsObject();
        Assert.Equal(bpId, lastHit["breakpointId"]!.GetValue<string>());
        Assert.Equal(testNetworkId, lastHit["networkId"]!.GetValue<long>());
    }
}
