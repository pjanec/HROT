using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="EntityPlacementGizmo"/> (EPG-001..EPG-006).
///
/// Tests exercise the gizmo directly via <see cref="IEntityStatefulGizmo"/> method calls
/// (PlacementCanvasBridge was deleted in Phase 3, BATCH-29).
/// No Raylib window context is required.
/// </summary>
public class EntityPlacementGizmoTests
{
    // -- Test constants ---------------------------------------------------

    private const long  TestTkbType = 202L;
    private const float ClickX      = 1234.5f;
    private const float ClickY      = 5678.9f;

    // -- Helper -----------------------------------------------------------

    private static (List<SpawnEntityCommand> captured, EntityPlacementGizmo gizmo)
        CreateGizmo(long tkbType = 202L, string? initialPropertiesJson = null)
    {
        var captured = new List<SpawnEntityCommand>();
        var gizmo = new EntityPlacementGizmo(
            onEntityCreated:       cmd => captured.Add(cmd),
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson);
        return (captured, gizmo);
    }

    // -- EPG-001: left-click publishes exactly one command ----------------

    [Fact]
    public void LeftClick_WritesExactlyOneCommand()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Single(captured);
    }

    // -- EPG-002: left-click command carries correct TKB type -------------

    [Fact]
    public void LeftClick_CommandHasCorrectTkbType()
    {
        var (captured, gizmo) = CreateGizmo(tkbType: TestTkbType);

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Equal(TestTkbType, captured[0].TkbType);
    }

    // -- EPG-003: left-click command carries click position ---------------

    [Fact]
    public void LeftClick_CommandHasInitialTransformMatchingClickPosition()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.True(captured[0].InitialTransform.HasValue);
        Assert.Equal(ClickX, captured[0].InitialTransform!.Value.Position.X, precision: 2);
        Assert.Equal(ClickY, captured[0].InitialTransform!.Value.Position.Y, precision: 2);
    }

    // -- EPG-004: left-click command has non-empty RequestId --------------

    [Fact]
    public void LeftClick_CommandHasNonEmptyRequestId()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.NotEqual(Guid.Empty, captured[0].RequestId);
    }

    // -- EPG-005: right-click does not publish any command ----------------

    [Fact]
    public void RightClick_DoesNotPublish()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: true, new Vector3(ClickX, ClickY, 0f));

        Assert.Empty(captured);
    }

    // -- EPG-006: initialAttributesJson is forwarded verbatim -------------

    [Fact]
    public void InitialAttributesJson_ForwardedVerbatim()
    {
        const string json = "{\"affiliation\":\"FORCE_FRIENDLY\"}";
        var (captured, gizmo) = CreateGizmo(initialPropertiesJson: json);

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }

    // -- CE-1017 S2 (Add Entity, the UI half of the placement tool) ---------

    [Fact]
    public void CE1017_APlacedEntity_FacesNorth()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        // yaw +90° about Z turns the transform's forward (+X, east) to +Y (north)
        var forward = Vector3.Transform(Vector3.UnitX, captured[0].InitialTransform!.Value.Rotation);
        Assert.Equal(0f, forward.X, precision: 4);
        Assert.Equal(1f, forward.Y, precision: 4);
    }

    [Fact]
    public void CE1017_APlacedEntity_AsksForTheGroundLevel()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Equal(Fdp.Toolkit.NetworkSpawning.SpawnHeight.OnGround, captured[0].SpawnHeight);
    }

    [Fact]
    public void CE1017_AShiftClick_PlacesLikeAClick()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left | MapMouseButton.ShiftMask, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Single(captured);
    }

    [Fact]
    public void CE1017_AlwaysMulti_EachClickPlaces_AndOnlyRightClickOrEscEnds()
    {
        var captured = new List<SpawnEntityCommand>();
        int removed = 0;
        var gizmo = new EntityPlacementGizmo(cmd => captured.Add(cmd), TestTkbType,
            autoPopOnPlace: false, onRemove: () => removed++);

        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(1, 1, 0));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(2, 2, 0));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(3, 3, 0));
        Assert.Equal(3, captured.Count);
        Assert.Equal(0, removed);

        gizmo.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Escape, isPressed: true);
        Assert.Equal(1, removed);
    }
}
