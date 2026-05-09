using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="EntityPlacementGizmo"/> (EPG-001..EPG-006).
///
/// Tests exercise the gizmo through <see cref="PlacementCanvasBridge"/> to match
/// real production usage. No Raylib window context is required -- bridge.HandleClick
/// is pure in-memory state.
/// </summary>
public class EntityPlacementGizmoTests
{
    // -- Test constants ---------------------------------------------------

    private const long  TestTkbType = 202L;
    private const float ClickX      = 1234.5f;
    private const float ClickY      = 5678.9f;

    // -- Helper -----------------------------------------------------------

    private static (List<SpawnEntityCommand> captured, PlacementCanvasBridge bridge)
        CreateBridge(long tkbType = 202L, string? initialPropertiesJson = null)
    {
        var captured = new List<SpawnEntityCommand>();
        PlacementCanvasBridge? bridge = null;
        var gizmo = new EntityPlacementGizmo(
            onEntityCreated:       cmd => captured.Add(cmd),
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson,
            onRemove:              () => bridge?.RequestPop());
        bridge = new PlacementCanvasBridge(gizmo);
        return (captured, bridge);
    }

    // -- EPG-001: left-click publishes exactly one command ----------------

    [Fact]
    public void LeftClick_WritesExactlyOneCommand()
    {
        var (captured, bridge) = CreateBridge();

        bridge.HandleClick(new Vector2(ClickX, ClickY), MapMouseButton.Left);

        Assert.Single(captured);
    }

    // -- EPG-002: left-click command carries correct TKB type -------------

    [Fact]
    public void LeftClick_CommandHasCorrectTkbType()
    {
        var (captured, bridge) = CreateBridge(tkbType: TestTkbType);

        bridge.HandleClick(new Vector2(ClickX, ClickY), MapMouseButton.Left);

        Assert.Equal(TestTkbType, captured[0].TkbType);
    }

    // -- EPG-003: left-click command carries click position ---------------

    [Fact]
    public void LeftClick_CommandHasInitialTransformMatchingClickPosition()
    {
        var (captured, bridge) = CreateBridge();

        bridge.HandleClick(new Vector2(ClickX, ClickY), MapMouseButton.Left);

        Assert.True(captured[0].InitialTransform.HasValue);
        Assert.Equal(ClickX, captured[0].InitialTransform!.Value.Position.X, precision: 2);
        Assert.Equal(ClickY, captured[0].InitialTransform!.Value.Position.Y, precision: 2);
    }

    // -- EPG-004: left-click command has non-empty RequestId --------------

    [Fact]
    public void LeftClick_CommandHasNonEmptyRequestId()
    {
        var (captured, bridge) = CreateBridge();

        bridge.HandleClick(new Vector2(ClickX, ClickY), MapMouseButton.Left);

        Assert.NotEqual(Guid.Empty, captured[0].RequestId);
    }

    // -- EPG-005: right-click does not publish any command ----------------

    [Fact]
    public void RightClick_DoesNotPublish()
    {
        var (captured, bridge) = CreateBridge();

        bridge.HandleClick(new Vector2(ClickX, ClickY), MapMouseButton.Right);

        Assert.Empty(captured);
    }

    // -- EPG-006: initialAttributesJson is forwarded verbatim -------------

    [Fact]
    public void InitialAttributesJson_ForwardedVerbatim()
    {
        const string json = "{\"affiliation\":\"FORCE_FRIENDLY\"}";
        var (captured, bridge) = CreateBridge(initialPropertiesJson: json);

        bridge.HandleClick(new Vector2(ClickX, ClickY), MapMouseButton.Left);

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }
}
