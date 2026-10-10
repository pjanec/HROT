using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using GizmoMap.Network;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.SimHost;
using StructEdit.Core;
using StructEdit.Json;
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

        // ⭐ CE-3117 — each debug-trace layer has its own toggle; the first untoggled bit and above stay always on.
        [Fact]
        public void CE3117_TheDebugTraceLayers_AreToggledByTheirOwnBits()
        {
            var all = new LayerControlDto().ToMask();
            foreach (var bit in new[] { DebugTraceLayers.Doors, DebugTraceLayers.Paths, DebugTraceLayers.Blast, DebugTraceLayers.Hearing })
                Assert.True(all.IsSet(bit), $"layer {bit} defaults on");

            var mask = new LayerControlDto { Doors = false, Hearing = false }.ToMask();
            Assert.False(mask.IsSet(DebugTraceLayers.Doors));
            Assert.False(mask.IsSet(DebugTraceLayers.Hearing));
            Assert.True(mask.IsSet(DebugTraceLayers.Paths));
            Assert.True(mask.IsSet(DebugTraceLayers.Blast));
            Assert.True(mask.IsSet(LayerControlDto.FirstUntoggledLayer));
            // ⭐ CE-3124 — the road network has its own toggle too
            Assert.True(all.IsSet(DebugTraceLayers.Roads));
            Assert.False(new LayerControlDto { Roads = false }.ToMask().IsSet(DebugTraceLayers.Roads));
            // ⭐ CE-3134 — the cover layer has its own toggle, OFF by default
            Assert.False(all.IsSet(DebugTraceLayers.Cover));
            Assert.True(new LayerControlDto { Cover = true }.ToMask().IsSet(DebugTraceLayers.Cover));
            // ⭐ CE-3133 — so has the navmesh layer, OFF by default
            Assert.False(all.IsSet(DebugTraceLayers.Navmesh));
            Assert.True(new LayerControlDto { Navmesh = true }.ToMask().IsSet(DebugTraceLayers.Navmesh));
            // ⭐ CE-1033 S5b — the entity cards (Labels) have their own toggle, ON by default (U19: "switchable on/off")
            Assert.True(all.IsSet(DebugTraceLayers.Labels));
            Assert.False(new LayerControlDto { Labels = false }.ToMask().IsSet(DebugTraceLayers.Labels));
            Assert.True(DebugTraceLayers.Labels < LayerControlDto.FirstUntoggledLayer, "a toggled layer lies below the always-on range");
        }

        // ⭐ CE-3133 — the navmesh layer choice: Infantry by default, kept in the settings registry by the panel, and an enum that
        // survives the panel's JSON round trip (the StructEdit session the projector opens).
        [Fact]
        public void CE3133_TheNavmeshLayerChoice_RoundTripsThroughThePanelAndTheRegistry()
        {
            Assert.Equal(NavmeshDrawLayers.Infantry, new LayerControlDto().NavmeshLayers);

            var settings = new GizmoSettingsRegistry();
            new LayerControlDto { NavmeshLayers = NavmeshDrawLayers.Vehicle }.WriteScopes(settings);
            Assert.Equal(NavmeshDrawLayers.Vehicle, NavmeshLayerSetting.Of(settings));
            var read = new LayerControlDto();
            read.ReadScopes(settings);
            Assert.Equal(NavmeshDrawLayers.Vehicle, read.NavmeshLayers);

            var edit = MakeEditService();
            string json;
            using (var session = edit.Open(new LayerControlDto { NavmeshLayers = NavmeshDrawLayers.All, Navmesh = true }, typeof(LayerControlDto)))
                json = session.ToJson();
            using var back = edit.Open(new LayerControlDto(), typeof(LayerControlDto));
            back.LoadJson(json);
            var dto = (LayerControlDto)back.Commit();
            Assert.Equal(NavmeshDrawLayers.All, dto.NavmeshLayers);
            Assert.True(dto.Navmesh);
        }

        // ⭐ CE-3120 — the layer panel shows each family's scope from the settings registry and writes an edit back to it, so the
        // visibility policy (which reads the registry) follows the panel on the next frame.
        [Fact]
        public void CE3120_TheLayerPanel_ReadsAndWritesTheFamilyScopes()
        {
            var settings = new GizmoSettingsRegistry();
            var edit = MakeEditService();
            var gizmo = new LayerControlGizmo(7, new FdpEventBus(), edit, settings: settings);
            Assert.Equal(GizmoScope.SelectedOrPinned,
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path));
            Assert.Equal(GizmoScope.All,
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory));

            var edited = new LayerControlDto { PathSelectedOnly = false, ContactsSelectedOnly = true };
            string json;
            using (var s = edit.Open(edited, typeof(LayerControlDto))) json = StructEdit.Json.EditSessionJsonExtensions.ToJson(s);
            gizmo.OnStructUpdate(json);

            Assert.Equal(GizmoScope.All,
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path));
            Assert.Equal(GizmoScope.SelectedOrPinned,
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory));

            var reread = new LayerControlDto();
            reread.ReadScopes(settings);
            Assert.False(reread.PathSelectedOnly);
            Assert.True(reread.ContactsSelectedOnly);
            Assert.True(reread.SquadSelectedOnly);   // untouched family keeps its default
        }

        // ⭐ CE-3149 — Apply in the layer panel must REACH the gizmo through the arbiter that owns it. 🔒 User, 2026-10-10:
        // "the LayerControlDto dialog … has no effect". 📐 The route is GizmoStructUpdateEvent → GlobalGizmoManager.Execute →
        // OnStructUpdate, and Execute returned early when nothing held the exclusive focus — which the layer control (a
        // permanent, non-exclusive gizmo) never does. ⛔ Every earlier rail called OnStructUpdate directly, skipping the route.
        [Fact]
        public void CE3149_ApplyInTheLayerPanel_ReachesTheGizmo_WithNoFocusHolder()
        {
            var bus = new FdpEventBus();
            Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(bus);
            var edit = MakeEditService();
            var buffer = new DebugPrimitiveBuffer();
            var manager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(buffer, bus);
            long id = Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId();
            var gizmo = new LayerControlGizmo(id, bus, edit);
            manager.Register(id, gizmo);
            Assert.Null(manager.Focus.Holder);   // the premise: nothing holds focus

            string json;
            using (var s = edit.Open(new LayerControlDto { FireTraces = false, Doors = false }, typeof(LayerControlDto)))
                json = StructEdit.Json.EditSessionJsonExtensions.ToJson(s);
            bus.PublishManaged(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoStructUpdateEvent { AnchorId = id, PayloadJson = json });
            bus.SwapBuffers();

            var repo = new EntityRepository();
            manager.Execute(repo, 0f);   // frame 1 routes the Apply (the mask is emitted BEFORE the routing step) …
            buffer.Clear();
            manager.Execute(repo, 0f);   // … frame 2 emits the mask the Apply produced

            var emitted = new LayerMask256();
            bool found = false;
            foreach (var p in buffer.GetFrame())
            {
                if (p.Shape == DebugPrimitiveShape.LayerControlMask) { emitted = p.ActiveLayers; found = true; }
            }
            Assert.True(found, "no LayerControlMask primitive was emitted");
            Assert.False(emitted.IsSet(DebugTraceLayers.FireTraces), "FireTraces was unchecked and applied, yet its layer is still on");
            Assert.False(emitted.IsSet(DebugTraceLayers.Doors));
            Assert.True(emitted.IsSet(DebugTraceLayers.Paths));
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
