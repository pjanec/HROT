using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-16 Tier-1 tests — actionable error messages.
///
/// Each test asserts that a semantic, user-correctable error BOTH describes
/// what was wrong AND names the discovery endpoint that resolves it.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch16Tests
{
    private const long TestNetworkId = 90_160L;

    // ── T01: Unknown eventType names GET /commands ─────────────────────────────

    /// <summary>
    /// SendCommand with an unrecognised eventType returns an error that names GET /commands.
    /// </summary>
    [Fact]
    public void SendCommand_UnknownEventType_ErrorNamesGetCommands()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var (result, error) = svc.SendCommand("NopeNope__NonExistent", null, wait: false);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("NopeNope__NonExistent", error!);
        Assert.Contains("GET /commands", error!);
    }

    // ── T02: Entity not found (PatchEntityAttribute) names GET /entities ──────

    /// <summary>
    /// PatchEntityAttribute with a non-existent networkId returns an error that names GET /entities.
    /// </summary>
    [Fact]
    public void PatchEntityAttribute_UnknownEntity_ErrorNamesGetEntities()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var (result, error) = svc.PatchEntityAttribute(999_999_001L, "{\"Name\":\"x\"}");

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("999999001", error!);
        Assert.Contains("GET /entities", error!);
    }

    // ── T03: Entity not found (EditEntityComponent) names GET /entities ───────

    /// <summary>
    /// EditEntityComponent with a non-existent networkId returns an error that names GET /entities.
    /// </summary>
    [Fact]
    public void EditEntityComponent_UnknownEntity_ErrorNamesGetEntities()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var (result, error) = svc.EditEntityComponent(999_999_002L, "SimTransform", new JsonObject());

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("999999002", error!);
        Assert.Contains("GET /entities", error!);
    }

    // ── T04: Unknown component type names GET /components ─────────────────────

    /// <summary>
    /// EditEntityComponent with an unknown componentType returns an error that names GET /components.
    /// The entity must exist so the entity-lookup passes and we reach the component-type lookup.
    /// </summary>
    [Fact]
    public void EditEntityComponent_UnknownComponentType_ErrorNamesGetComponents()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn the entity so the entity lookup succeeds.
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = TestNetworkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform(),
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(TestNetworkId, out _), 5000),
            "Entity did not spawn within timeout.");

        var (result, error) = svc.EditEntityComponent(TestNetworkId, "NopeyMcNopeFace", new JsonObject());

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("NopeyMcNopeFace", error!);
        Assert.Contains("GET /components", error!);
    }

    // ── T05: filterNetworkId not found names GET /entities ────────────────────

    /// <summary>
    /// AddBreakpoint with an unknown filterNetworkId throws ArgumentException that names GET /entities.
    /// </summary>
    [Fact]
    public void AddBreakpoint_UnknownFilterNetworkId_ErrorNamesGetEntities()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Build a valid condition + an unknown filterNetworkId.
        var body = JsonNode.Parse("""
        {
            "condition": {
                "$type": "Lifecycle",
                "IdentifierType": "NameSubstring",
                "TargetValue": "Test",
                "NamePropertyPath": "Name"
            },
            "filterNetworkId": 999999003
        }
        """)!;

        var ex = Assert.Throws<ArgumentException>(() => svc.AddBreakpoint(body));
        Assert.Contains("999999003", ex.Message);
        Assert.Contains("GET /entities", ex.Message);
    }

    // ── T06: Breakpoint not found names GET /breakpoints ─────────────────────

    /// <summary>
    /// RemoveBreakpoint with a non-existent id throws ArgumentException that names GET /breakpoints.
    /// </summary>
    [Fact]
    public void RemoveBreakpoint_UnknownId_ErrorNamesGetBreakpoints()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var ex = Assert.Throws<ArgumentException>(() => svc.RemoveBreakpoint("BP#99999"));
        Assert.Contains("BP#99999", ex.Message);
        Assert.Contains("GET /breakpoints", ex.Message);
    }

    // ── T07: Unknown baselineId names POST /diff/capture ─────────────────────

    /// <summary>
    /// CompareBaseline with an unknown baselineId throws ArgumentException that names POST /diff/capture.
    /// </summary>
    [Fact]
    public void CompareBaseline_UnknownBaselineId_ErrorNamesPostDiffCapture()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var ex = Assert.Throws<ArgumentException>(() => svc.CompareBaseline("BL#99999"));
        Assert.Contains("BL#99999", ex.Message);
        Assert.Contains("POST /diff/capture", ex.Message);
    }

    // ── T08: Wait-gating reason mentions preview/step ──────────────────────────

    /// <summary>
    /// SendCommand with wait:false while not in preview returns reason that mentions
    /// /preview/enter, /sim/play, and /sim/step so the agent knows what to call.
    /// Uses CenterOnEntityCommand (an unmanaged struct registered via [EventId]) so the
    /// reflection-based PublishEventObject path succeeds in the harness.
    /// </summary>
    [Fact]
    public void SendCommand_NotInPreview_WaitReasonMentionsPreviewAndStep()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Force registration of CenterOnEntityCommand (unmanaged struct with [EventId]).
        _ = EventType<CenterOnEntityCommand>.Id;

        // Send via the service — the event publishes successfully, but wait=false while
        // not in preview → awaited:false with the enriched reason.
        var (result, error) = svc.SendCommand("CenterOnEntityCommand", null, wait: false);

        // Should succeed (event published) but with awaited:false and a reason.
        Assert.Null(error);
        Assert.NotNull(result);

        var reason = result!["reason"]?.GetValue<string>();
        Assert.NotNull(reason);
        Assert.Contains("preview", reason!);
        Assert.Contains("POST /sim/step", reason!);
    }

    /// <summary>
    /// SpawnEntity while not in preview returns a reason field that mentions preview/step guidance.
    /// </summary>
    [Fact]
    public void SpawnEntity_NotInPreview_ReasonMentionsPreview()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.SpawnEntity(tkbType: 1L);

        Assert.NotNull(result);
        var reason = result["reason"]?.GetValue<string>();
        Assert.NotNull(reason);
        Assert.Contains("preview", reason!);
        Assert.Contains("POST /sim/step", reason!);
    }
}
