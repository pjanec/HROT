using System.Numerics;
using Hrot.IG.Components;
using Hrot.Map.Common.Events;
using Fdp.Kernel;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests for Shift+Right-Click personal waypoint appending (ROUTES1-T009).
///
/// Verifies that a shift+right-click on the canvas publishes
/// <see cref="CmdAppendPersonalWaypoint"/> for every selected vehicle, and that
/// plain right-click does not emit this event.
/// </summary>
public class ShiftRightClickTests : System.IDisposable
{
    private readonly IgApplication _app;

    public ShiftRightClickTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 230);
    }

    public void Dispose() => _app.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates an entity with a <see cref="SelectionState"/> marked as selected.</summary>
    private Entity CreateSelectedVehicle(Vector3 position)
    {
        var entity = _app.World.CreateEntity();
        _app.World.AddComponent(entity, new SimTransform { Position = position });
        _app.World.AddComponent(entity, new SelectionState { IsSelected = true });
        return entity;
    }

    /// <summary>Creates an entity with a <see cref="SelectionState"/> marked as primary selection.</summary>
    private Entity CreatePrimarySelectedVehicle(Vector3 position)
    {
        var entity = _app.World.CreateEntity();
        _app.World.AddComponent(entity, new SimTransform { Position = position });
        _app.World.AddComponent(entity, new SelectionState { IsPrimarySelection = true });
        return entity;
    }

    /// <summary>Swaps event bus buffers and drains all pending waypoint events.</summary>
    private CmdAppendPersonalWaypoint[] ConsumeWaypointEvents()
    {
        _app.World.Bus.SwapBuffers();
        var span = _app.World.Bus.Consume<CmdAppendPersonalWaypoint>();
        var results = new CmdAppendPersonalWaypoint[span.Length];
        for (int i = 0; i < span.Length; i++)
            results[i] = span[i];
        return results;
    }

    // ── Core behaviour ────────────────────────────────────────────────────────

    /// <summary>
    /// One selected vehicle → exactly one <see cref="CmdAppendPersonalWaypoint"/> published.
    /// </summary>
    [Fact]
    public void ShiftRightClick_OneSelectedVehicle_EmitsOneEvent()
    {
        CreateSelectedVehicle(new Vector3(10f, 5f, 20f));

        _app.TestHook_SimulateShiftRightClick(new Vector2(50f, 60f));

        Assert.Single(ConsumeWaypointEvents());
    }

    /// <summary>
    /// Two selected vehicles → exactly two events published, one per vehicle.
    /// </summary>
    [Fact]
    public void ShiftRightClick_TwoSelectedVehicles_EmitsTwoEvents()
    {
        CreateSelectedVehicle(new Vector3(10f, 5f, 20f));
        CreateSelectedVehicle(new Vector3(30f, 5f, 40f));

        _app.TestHook_SimulateShiftRightClick(new Vector2(50f, 60f));

        Assert.Equal(2, ConsumeWaypointEvents().Length);
    }

    /// <summary>
    /// A plain right-click (no shift) must NOT publish any waypoint event.
    /// </summary>
    [Fact]
    public void PlainRightClick_DoesNotEmitWaypointEvent()
    {
        CreateSelectedVehicle(new Vector3(10f, 5f, 20f));

        _app.TestHook_SimulatePlainRightClick(new Vector2(50f, 60f));

        Assert.Empty(ConsumeWaypointEvents());
    }

    /// <summary>
    /// Shift+right-click with no selected entities must produce zero events and not throw.
    /// </summary>
    [Fact]
    public void ShiftRightClick_NoSelectedVehicles_EmitsNoEvents()
    {
        var ex = Record.Exception(() => _app.TestHook_SimulateShiftRightClick(new Vector2(50f, 60f)));

        Assert.Null(ex);
        Assert.Empty(ConsumeWaypointEvents());
    }

    // ── Event content ─────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="CmdAppendPersonalWaypoint.VehicleEntity"/> must reference the
    /// entity that has a matching <see cref="SelectionState"/>.
    /// </summary>
    [Fact]
    public void ShiftRightClick_EventHasCorrectVehicleEntity()
    {
        var vehicle = CreateSelectedVehicle(new Vector3(10f, 5f, 20f));

        _app.TestHook_SimulateShiftRightClick(new Vector2(50f, 60f));

        var events = ConsumeWaypointEvents();
        Assert.Single(events);
        Assert.Equal(vehicle, events[0].VehicleEntity);
    }

    /// <summary>
    /// The <see cref="CmdAppendPersonalWaypoint.WorldPosition"/> X and Z must match the
    /// canvas click position, and Y must come from <see cref="SimTransform.Position.Y"/>.
    /// </summary>
    [Fact]
    public void ShiftRightClick_WorldPosition_XZ_FromClick_Y_FromSimTransform()
    {
        const float altitude = 7.5f;
        CreateSelectedVehicle(new Vector3(10f, altitude, 20f));
        var clickPos = new Vector2(55f, 77f);

        _app.TestHook_SimulateShiftRightClick(clickPos);

        var events = ConsumeWaypointEvents();
        Assert.Single(events);
        var pos = events[0].WorldPosition;
        Assert.Equal(clickPos.X, pos.X, precision: 3);
        Assert.Equal(altitude,   pos.Y, precision: 3);
        Assert.Equal(clickPos.Y, pos.Z, precision: 3);
    }

    /// <summary>
    /// A vehicle with <see cref="SelectionState.IsPrimarySelection"/> set (but not
    /// <see cref="SelectionState.IsSelected"/>) must also receive a waypoint event.
    /// </summary>
    [Fact]
    public void ShiftRightClick_PrimarySelectedVehicle_AlsoReceivesEvent()
    {
        var vehicle = CreatePrimarySelectedVehicle(new Vector3(5f, 3f, 8f));

        _app.TestHook_SimulateShiftRightClick(new Vector2(10f, 20f));

        var events = ConsumeWaypointEvents();
        Assert.Single(events);
        Assert.Equal(vehicle, events[0].VehicleEntity);
    }

    /// <summary>
    /// A vehicle without a <see cref="SimTransform"/> must use altitude 0 as fallback.
    /// </summary>
    [Fact]
    public void ShiftRightClick_VehicleWithoutSimTransform_UsesZeroAltitude()
    {
        var entity = _app.World.CreateEntity();
        _app.World.AddComponent(entity, new SelectionState { IsSelected = true });
        // No SimTransform added intentionally.

        _app.TestHook_SimulateShiftRightClick(new Vector2(100f, 200f));

        var events = ConsumeWaypointEvents();
        Assert.Single(events);
        Assert.Equal(0f, events[0].WorldPosition.Y, precision: 3);
    }
}
