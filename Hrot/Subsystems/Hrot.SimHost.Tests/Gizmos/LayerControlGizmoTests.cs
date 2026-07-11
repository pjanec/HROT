using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using GizmoMap.Network;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.SimHost;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.SimHost.Tests.Gizmos
{
    // GZH-011 unit tests for LayerControlGizmo and related schema hash logic.

    // IGizmoUiStatePublisher stub that records all Publish calls.
    internal sealed class LayerControlPublisherStub : IGizmoUiStatePublisher
    {
        public List<GizmoUiState> Published { get; } = new();
        public void Publish(GizmoUiState state) { Published.Add(state); }
    }

    public class GZH011_Tests
    {
        private static IComponentEditService MakeEditService()
            => new ComponentEditServiceBuilder().Build();

        // GZH011_1: LayerControlGizmo.SchemaHash equals the FNV-1a hash of the DTO's full type name.
        [Fact]
        public void GZH011_1_SchemaHash_MatchesComputedHash()
        {
            uint expected = GizmoSettingsRegistry.ComputeHash("Hrot.Common.Diagnostics.Gizmos.LayerControlDto");
            Assert.Equal(expected, LayerControlGizmo.SchemaHash);
        }

        // GZH011_2: When _isEditing is toggled by an OpenLayerEditorEvent, UpdateAndDraw calls
        //           the publisher exactly once. A second UpdateAndDraw with the same DTO state
        //           does NOT echo the state (StructInspectorProjector suppresses duplicates).
        // B (Fixture Gap TH-3): OpenLayerEditorEvent is an unmanaged struct; gizmo reads it via
        // _interactionBus.Read<OpenLayerEditorEvent>() (unmanaged ring). Test must use bus.Publish
        // (not bus.PublishManaged) so it reaches the correct ring buffer.
        [Fact]
        public void GZH011_2_UpdateAndDraw_WithEditing_PublishesOnce_NoDuplicateEcho()
        {
            var bus       = new FdpEventBus();
            var editSvc   = MakeEditService();
            var publisher = new LayerControlPublisherStub();
            var gizmo     = new LayerControlGizmo(anchorId: 1L, bus, editSvc, publisher);

            // Trigger _isEditing by publishing the toggle event (unmanaged), then swap buffers.
            bus.Publish(new OpenLayerEditorEvent());
            bus.SwapBuffers();

            // First UpdateAndDraw: editing is active, expect one Publish call.
            var draw1 = new DebugPrimitiveBuffer();
            gizmo.UpdateAndDraw(new EntityRepository(), 0f, draw1);
            Assert.Equal(1, publisher.Published.Count);

            // Second UpdateAndDraw: same DTO state, no event — StructInspectorProjector suppresses echo.
            bus.SwapBuffers(); // drain (no new events)
            var draw2 = new DebugPrimitiveBuffer();
            gizmo.UpdateAndDraw(new EntityRepository(), 0f, draw2);
            Assert.Equal(1, publisher.Published.Count);
        }
    }

    // ==========================================================================
    // DEBT-002: GizmoUiStateHub wired in composition roots
    // ==========================================================================

    public class DEBT002_Tests
    {
        // DEBT002_SimHost: SimHostApp.GizmoUiHub is non-null after construction.
        // The field is initialised in the field declaration, so it does not require
        // InitializeEmbedded() to be called.
        [Fact]
        public void DEBT002_SimHost_GizmoUiHub_IsNonNull_AfterConstruction()
        {
            var app = new SimHostApp();
            Assert.NotNull(app.GizmoUiHub);
        }
    }
}
