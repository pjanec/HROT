using System.Collections.Generic;
using System.Linq;
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

        // ⭐ CE-1160 — 🔒 User, 2026-10-16: "I need the entity selection and right click work always independently of what i pick in the
        //   debug layers menu." With every toggle off the selection ring is still drawn: it sits on the first bit no toggle owns.
        [Fact]
        public void CE1160_EveryLayerToggleOff_TheSelectionRing_IsStillEmittedAndKept()
        {
            var dto = new LayerControlDto { Units = { Entities = false, Labels = false }, Command = { Paths = false }, Ai = { VisionCones = false }, Map = { Doors = false, Roads = false } };
            var mask = dto.ToMask();
            Assert.False(mask.IsSet(0));
            Assert.True(DebugTraceLayers.Selection >= LayerControlDto.FirstUntoggledLayer);
            Assert.True(mask.IsSet(DebugTraceLayers.Selection));

            using var w = new EntityRepository();
            w.RegisterComponent<Hrot.IG.Components.SelectionState>();
            w.RegisterComponent<SimTransform>();
            var e = w.CreateEntity();
            w.AddComponent(e, new Hrot.IG.Components.SelectionState { IsSelected = true, IsPrimarySelection = true });
            w.AddComponent(e, new SimTransform { Position = new System.Numerics.Vector3(10f, 20f, 0f) });
            var buffer = new DebugPrimitiveBuffer();
            buffer.SetActiveLayers(mask);
            new SelectionHighlightGizmo().Draw(w, e, buffer);

            var ring = Assert.Single(buffer.GetFrame().ToArray());
            Assert.Equal(DebugPrimitiveShape.Sphere, ring.Shape);
            Assert.Equal(DebugTraceLayers.Selection, ring.DebugLayer);
        }

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
            foreach (var bit in new[] { DebugTraceLayers.Doors, DebugTraceLayers.Paths })
                Assert.True(all.IsSet(bit), $"layer {bit} defaults on");
            // ⭐ CE-1065 — 🔒 user, 2026-10-14: "turn most of them off by default": AiHelpers, FireTraces, Blast, Hearing start off
            foreach (var bit in new byte[] { 2, DebugTraceLayers.FireTraces, DebugTraceLayers.Blast, DebugTraceLayers.Hearing })
                Assert.False(all.IsSet(bit), $"layer {bit} defaults off");

            var mask = new LayerControlDto { Fire = { Hearing = true }, Map = { Doors = false } }.ToMask();
            Assert.False(mask.IsSet(DebugTraceLayers.Doors));
            Assert.True(mask.IsSet(DebugTraceLayers.Hearing));
            Assert.True(mask.IsSet(DebugTraceLayers.Paths));
            Assert.False(mask.IsSet(DebugTraceLayers.Blast));
            Assert.True(mask.IsSet(LayerControlDto.FirstUntoggledLayer));
            // ⭐ CE-3124 — the road network has its own toggle too
            Assert.True(all.IsSet(DebugTraceLayers.Roads));
            Assert.False(new LayerControlDto { Map = { Roads = false } }.ToMask().IsSet(DebugTraceLayers.Roads));
            // ⭐ CE-3134 — the cover layer has its own toggle, OFF by default
            Assert.False(all.IsSet(DebugTraceLayers.Cover));
            Assert.True(new LayerControlDto { Map = { Cover = true } }.ToMask().IsSet(DebugTraceLayers.Cover));
            // ⭐ CE-3133 — so has the navmesh layer, OFF by default
            Assert.False(all.IsSet(DebugTraceLayers.Navmesh));
            Assert.True(new LayerControlDto { Map = { Navmesh = true } }.ToMask().IsSet(DebugTraceLayers.Navmesh));
            // ⭐ CE-1033 S5b — the entity cards (Labels) have their own toggle, ON by default (U19: "switchable on/off")
            Assert.True(all.IsSet(DebugTraceLayers.Labels));
            Assert.False(new LayerControlDto { Units = { Labels = false } }.ToMask().IsSet(DebugTraceLayers.Labels));
            Assert.True(DebugTraceLayers.Labels < LayerControlDto.FirstUntoggledLayer, "a toggled layer lies below the always-on range");
            // ⭐ CE-1065 — the heading arrows / stance labels and the spatial grid have their own toggles, OFF by default
            foreach (var bit in new[] { DebugTraceLayers.UnitDetails, DebugTraceLayers.SpatialGrid, DebugTraceLayers.Concealed })   // ⭐ CE-1169 — the ExCon layer too
            {
                Assert.False(all.IsSet(bit), $"layer {bit} defaults off");
                Assert.True(bit < LayerControlDto.FirstUntoggledLayer, $"layer {bit} lies below the always-on range");
            }
            Assert.True(new LayerControlDto { Units = { UnitDetails = true } }.ToMask().IsSet(DebugTraceLayers.UnitDetails));
            Assert.True(new LayerControlDto { Map = { SpatialGrid = true } }.ToMask().IsSet(DebugTraceLayers.SpatialGrid));
            // ⭐ CE-1225 — the sectors have their own toggle, OFF by default, inside the toggled range (the selection ring stays above it)
            Assert.False(all.IsSet(DebugTraceLayers.Sectors), "the sectors default off");
            Assert.True(new LayerControlDto { Command = { Sectors = true } }.ToMask().IsSet(DebugTraceLayers.Sectors));
            Assert.True(DebugTraceLayers.Sectors < LayerControlDto.FirstUntoggledLayer, "the sectors lie below the always-on range");
            // ⭐ CE-1227 — the 2-D terrain has its own toggle (no longer the Entities bit), ON by default, inside the toggled range
            Assert.True(all.IsSet(DebugTraceLayers.Terrain2D), "the 2-D terrain defaults on");
            Assert.False(new LayerControlDto { Map = { Terrain2D = false } }.ToMask().IsSet(DebugTraceLayers.Terrain2D));
            Assert.True(new LayerControlDto { Units = { Entities = false } }.ToMask().IsSet(DebugTraceLayers.Terrain2D), "unticking Entities keeps the city");
            Assert.True(DebugTraceLayers.Terrain2D < LayerControlDto.FirstUntoggledLayer, "the terrain lies below the always-on range");
            // ⭐ CE-1203 (§5e FL-1/FL-2) — the finer rows: each its own bit, ON by default, inside the toggled range, and unticking one
            //   clears only its own bit. Contacts is split from Perception: hiding the vision cones keeps the contact lines.
            var rows = new (byte Bit, LayerControlDto Off)[]
            {
                (DebugTraceLayers.Rounds,           new LayerControlDto { Units = { Rounds = false } }),
                (DebugTraceLayers.SquadCommand,     new LayerControlDto { Command = { SquadCommand = false } }),
                (DebugTraceLayers.Actions,          new LayerControlDto { Ai = { Actions = false } }),
                (DebugTraceLayers.Utility,          new LayerControlDto { Ai = { Utility = false } }),
                (DebugTraceLayers.Eqs,              new LayerControlDto { Ai = { Eqs = false } }),
                (DebugTraceLayers.Routes,           new LayerControlDto { Command = { Routes = false } }),
                (DebugTraceLayers.TacticalGraphics, new LayerControlDto { Map = { TacticalGraphics = false } }),
                (DebugTraceLayers.TerrainZones,     new LayerControlDto { Map = { TerrainZones = false } }),
                (DebugTraceLayers.Traffic,          new LayerControlDto { Map = { Traffic = false } }),
                (DebugTraceLayers.Contacts,         new LayerControlDto { Ai = { Contacts = false } }),
            };
            Assert.Equal(rows.Length, rows.Select(r => r.Bit).Distinct().Count());
            foreach (var (bit, off) in rows)
            {
                Assert.True(all.IsSet(bit), $"layer {bit} defaults on");
                Assert.True(bit < LayerControlDto.FirstUntoggledLayer, $"layer {bit} lies below the always-on range");
                var rowMask = off.ToMask();
                Assert.False(rowMask.IsSet(bit), $"unticking layer {bit} clears it");
                for (int other = 0; other < LayerControlDto.FirstUntoggledLayer; other++)
                    if (other != bit) Assert.Equal(all.IsSet(other), rowMask.IsSet(other));
            }
            Assert.True(new LayerControlDto { Ai = { VisionCones = false } }.ToMask().IsSet(DebugTraceLayers.Contacts), "hiding the vision cones keeps the contact lines");
        }

        // ⭐ CE-1203 (§5e FL-4/FL-5) — the panel's document (what the generic inspector draws): five groups in order, and every row a
        // display name and a hover line ([EditDisplayName], [EditDoc]). 🔴 Red-proof: a row added without them shows its C# name and
        // no meaning — the user's complaint ("i do not even know what they mean").
        [Fact]
        public void CE1203_ThePanel_HasFiveGroups_AndEveryRowANameAndAMeaning()
        {
            using var session = MakeEditService().Open(new LayerControlDto(), typeof(LayerControlDto));
            var groups = session.Document.Root.Children.Where(n => n.Children.Count > 0).ToList();   // the groups (the first line, "Showing", is a text row)
            Assert.Equal(new[] { "Units", "Command", "AI", "Fire", "Map" }, groups.Select(g => g.Metadata.DisplayName).ToArray());
            var rows = groups.SelectMany(g => g.Children).ToList();
            Assert.True(rows.Count >= 26, $"the five groups hold {rows.Count} rows");
            foreach (var node in groups.Concat(rows))
            {
                Assert.False(string.IsNullOrWhiteSpace(node.Metadata.DisplayName), $"{node.JsonPath} has no display name");
                Assert.Contains(node.Metadata.CustomAttributes, a => a is StructEdit.Core.Attributes.EditDocAttribute d && d.Summary.Length > 0);
            }
            Assert.Equal(rows.Count, rows.Select(r => r.Metadata.DisplayName).Distinct().Count());
        }

        /// <summary>
        /// ⭐ <c>CE-1288</c> — the 2-D helper grid is a row of the layer panel ("Grid (2-D)", Map group): its own bit, ON by default, inside the
        /// toggled range (the selection ring stays above it), and unticking it clears only its own bit — the terrain beside it stays.
        /// ⛔ Red-proof: leave <c>FirstUntoggledLayer</c> at 27 and the bit is "always on" (the first assertion fails); drop the
        /// <c>mask.SetBit</c> line and the default-on assertion fails.
        /// </summary>
        [Fact]
        public void CE1288_TheGridIsALayerPanelRow_OnByDefault_WithItsOwnBit()
        {
            var all = new LayerControlDto().ToMask();
            Assert.True(all.IsSet(DebugTraceLayers.Grid), "the grid defaults on");
            Assert.True(DebugTraceLayers.Grid < LayerControlDto.FirstUntoggledLayer, "the grid lies below the always-on range");
            Assert.True(DebugTraceLayers.Selection >= LayerControlDto.FirstUntoggledLayer, "the selection ring stays above every toggle");

            var off = new LayerControlDto { Map = { Grid = false } }.ToMask();
            Assert.False(off.IsSet(DebugTraceLayers.Grid), "unticking the row clears the bit");
            for (int other = 0; other < 256; other++)
                if (other != DebugTraceLayers.Grid) Assert.Equal(all.IsSet(other), off.IsSet(other));   // and nothing else
            Assert.True(new LayerControlDto { Map = { Terrain2D = false } }.ToMask().IsSet(DebugTraceLayers.Grid), "the terrain's box does not hide the grid");

            // its bit is nobody else's
            var bits = typeof(DebugTraceLayers).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(byte) && f.Name != nameof(DebugTraceLayers.Selection))
                .Select(f => (byte)f.GetRawConstantValue()!).ToList();
            Assert.Equal(bits.Count, bits.Distinct().Count());

            // the panel shows it: a display name and a hover line, in the Map group
            using var session = MakeEditService().Open(new LayerControlDto(), typeof(LayerControlDto));
            var map = session.Document.Root.Children.Single(n => n.Metadata.DisplayName == "Map");
            Assert.Contains(map.Children, n => n.Metadata.DisplayName == "Grid (2-D)");
            // and the user's file remembers it per view like every other row
            Assert.Contains(LayerControlSets.RowNames, r => r.Name == "Map.Grid" && r.Display == "Grid (2-D)" && r.Doc.Length > 0);
        }

        /// <summary>
        /// ⭐ <c>CE-1309</c> (docs/DESIGN_People_Display.md §7e U20e) — the mind-state bar is a row of the layer panel ("Mind state", Units
        /// group): its own bit, OFF by default, inside the toggled range (the selection ring stays above it); ticking it sets only its bit.
        /// 🔴 Red-proof: drop the <c>mask.SetBit</c> line ⇒ red.
        /// </summary>
        [Fact]
        public void CE1309_MindStateIsALayerPanelRow_OffByDefault_WithItsOwnBit()
        {
            Assert.False(new LayerControlDto().ToMask().IsSet(DebugTraceLayers.MindState), "off by default");
            Assert.True(DebugTraceLayers.MindState < LayerControlDto.FirstUntoggledLayer, "it lies below the always-on range");
            Assert.True(DebugTraceLayers.Selection >= LayerControlDto.FirstUntoggledLayer, "the selection ring stays above every toggle");
            var all = new LayerControlDto().ToMask();
            var on = new LayerControlDto { Units = { MindState = true } }.ToMask();
            Assert.True(on.IsSet(DebugTraceLayers.MindState), "ticking the row sets the bit");
            for (int other = 0; other < 256; other++)
                if (other != DebugTraceLayers.MindState) Assert.Equal(all.IsSet(other), on.IsSet(other));   // and nothing else
            var bits = typeof(DebugTraceLayers).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(byte) && f.Name != nameof(DebugTraceLayers.Selection))
                .Select(f => (byte)f.GetRawConstantValue()!).ToList();
            Assert.Equal(bits.Count, bits.Distinct().Count());

            using var session = MakeEditService().Open(new LayerControlDto(), typeof(LayerControlDto));
            var units = session.Document.Root.Children.Single(n => n.Metadata.DisplayName == "Units");
            Assert.Contains(units.Children, n => n.Metadata.DisplayName == "Mind state");
            Assert.Contains(LayerControlSets.RowNames, r => r.Name == "Units.MindState" && r.Display == "Mind state" && r.Doc.Length > 0);
        }

        private static string JsonOf(LayerControlDto dto)
        {
            using var s = MakeEditService().Open(dto, typeof(LayerControlDto));
            return StructEdit.Json.EditSessionJsonExtensions.ToJson(s);
        }

        // ⭐ CE-1203 FL-8 (§5e) — a set per view: the view switch picks the set the gizmo applies and shows; an edit changes only the
        // set on screen; an edit of the OTHER view's set (a click in the frame the view switched) is dropped. 🔴 Red-proof: one set for
        // both views — hiding the 2-D round streaks hid them in 3-D too (where the 3-D round makes them a duplicate anyway).
        [Fact]
        public void CE1203_EachViewHasItsOwnSet_AndTheViewPicksIt()
        {
            bool is3D = false;
            var gizmo = new LayerControlGizmo(7, new FdpEventBus(), MakeEditService(), settings: new GizmoSettingsRegistry()) { Is3D = () => is3D };
            var draw = new DebugPrimitiveBuffer();

            var off2D = new LayerControlDto { View = LayerControlSets.View2D, Units = { Rounds = false } };
            gizmo.OnStructUpdate(JsonOf(off2D));
            Assert.False(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Rounds), "2-D: Rounds unticked");
            Assert.True(gizmo.SetOf(is3D: true).Units.Rounds, "the 3-D set is untouched");

            is3D = true;
            gizmo.UpdateAndDraw(new EntityRepository(), 0f, draw);
            Assert.True(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Rounds), "3-D: its own set, Rounds on");
            gizmo.OnStructUpdate(JsonOf(off2D));   // the 2-D panel's click arriving after the switch
            Assert.True(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Rounds), "an edit of the 2-D set is not applied to the 3-D set");

            gizmo.OnStructUpdate(JsonOf(new LayerControlDto { View = LayerControlSets.View3D, Map = { Doors = false } }));
            Assert.False(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Doors));
            is3D = false;
            gizmo.UpdateAndDraw(new EntityRepository(), 0f, draw);
            Assert.True(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Doors), "back in 2-D: the 2-D set (Doors on, Rounds off)");
            Assert.False(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Rounds));
        }

        // ⭐ CE-1203 FL-9 (§5e) — remembered: both sets, the family scopes and the navmesh choice go through the shared settings to one
        // file, saved on every change, and a gizmo built over a registry loaded from that file starts where the last one stopped.
        // 🔴 Red-proof: nothing was saved — every start was the defaults.
        [Fact]
        public void CE1203_BothSetsAndTheScopes_AreRemembered_InTheUsersFile()
        {
            string file = System.IO.Path.GetTempFileName();
            try
            {
                var settings = new GizmoSettingsRegistry();
                int saves = 0;
                bool is3D = false;
                var gizmo = new LayerControlGizmo(7, new FdpEventBus(), MakeEditService(), settings: settings,
                    saveSettings: () => { saves++; settings.SaveToDisk(file); }) { Is3D = () => is3D };
                gizmo.OnStructUpdate(JsonOf(new LayerControlDto { View = LayerControlSets.View2D, Units = { Rounds = false }, Command = { PathsSelectedOnly = false } }));
                is3D = true;
                gizmo.UpdateAndDraw(new EntityRepository(), 0f, new DebugPrimitiveBuffer());
                // the panel sends the WHOLE shown set — the 3-D set, which re-read the scope the 2-D edit wrote
                Assert.False(gizmo.SetOf(is3D: true).Command.PathsSelectedOnly, "the 3-D set shows the scope the 2-D edit wrote");
                gizmo.OnStructUpdate(JsonOf(new LayerControlDto { View = LayerControlSets.View3D, Command = { PathsSelectedOnly = false }, Map = { Navmesh = true, NavmeshLayers = NavmeshDrawLayers.Vehicle } }));
                Assert.Equal(2, saves);

                var loaded = new GizmoSettingsRegistry();
                loaded.LoadFromDisk(file);
                var next = new LayerControlGizmo(8, new FdpEventBus(), MakeEditService(), settings: loaded);
                Assert.False(next.SetOf(is3D: false).Units.Rounds);
                Assert.True(next.SetOf(is3D: false).Map.Doors);
                Assert.True(next.SetOf(is3D: true).Units.Rounds);
                Assert.True(next.SetOf(is3D: true).Map.Navmesh);
                Assert.Equal(NavmeshDrawLayers.Vehicle, next.SetOf(is3D: true).Map.NavmeshLayers);
                Assert.False(next.SetOf(is3D: false).Command.PathsSelectedOnly, "the family scope is one per family, for both views");
                Assert.False(next.SetOf(is3D: true).Command.PathsSelectedOnly);
                Assert.False(next.ActiveLayers.IsSet(DebugTraceLayers.Rounds), "it starts in 2-D with the remembered 2-D set");
                Assert.True(LayerControlSets.RowCount >= 26, $"{LayerControlSets.RowCount} rows remembered per set");
            }
            finally { System.IO.File.Delete(file); }
        }

        // ⭐ CE-4038 B10 (docs/DESIGN_Film_Director.md §4b, R-4060) — a film take's layers are a TRANSIENT override: the mask follows it (the
        // take starts clean — overlays off, the scene kept as the user has it — then its own layers on), but ⛔ NOTHING reaches the user's
        // settings or file; clearing it gives the user's set back. And POST /map/layers IS the user's click: written and saved.
        // 🔴 Red-proof: SetOverride writing through LayerControlSets.Write + saveSettings ⇒ the save count and the registry move.
        [Fact]
        public void CE4038_ATakesLayers_AreNeverWritten_AndTheRouteIsTheUsersClick()
        {
            var settings = new GizmoSettingsRegistry();
            int saves = 0;
            var gizmo = new LayerControlGizmo(7, new FdpEventBus(), MakeEditService(), settings: settings, saveSettings: () => saves++);
            uint labels = GizmoSettingsRegistry.ComputeHash(LayerControlSets.Key(false, "Units", "Labels"));
            uint cones = GizmoSettingsRegistry.ComputeHash(LayerControlSets.Key(false, "Ai", "VisionCones"));
            bool labelsBefore = settings.Read(labels).BoolValue, conesBefore = settings.Read(cones).BoolValue;
            Assert.True(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Labels), "the user's set shows the cards");

            var unknown = gizmo.SetOverride(new[] { "Ai.VisionCones" }, new[] { "Map.Roads" });
            Assert.Empty(unknown);
            Assert.False(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Labels), "a take starts clean: the cards are off");
            Assert.True(gizmo.ActiveLayers.IsSet(1), "the take's own layer is on (vision cones)");
            Assert.True(gizmo.ActiveLayers.IsSet(0), "the scene is kept: the units");
            Assert.False(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Roads), "and the take can turn a scene layer off");
            Assert.Equal(new[] { "No.Such" }, gizmo.SetOverride(new[] { "No.Such" }, System.Array.Empty<string>()));

            gizmo.UpdateAndDraw(new EntityRepository(), 0f, new DebugPrimitiveBuffer());   // a frame with the override: still nothing written
            Assert.Equal(0, saves);
            Assert.Equal((labelsBefore, conesBefore), (settings.Read(labels).BoolValue, settings.Read(cones).BoolValue));

            gizmo.ClearOverride();
            Assert.True(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Labels), "the user's set again");

            Assert.Empty(gizmo.SetRows(is3D: false, new[] { "Fire.Blast" }, new[] { "Units.Labels" }));
            Assert.Equal(1, saves);
            Assert.False(settings.Read(labels).BoolValue, "the route's change is the user's: in the registry");
            Assert.False(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Labels));
            Assert.True(gizmo.ActiveLayers.IsSet(DebugTraceLayers.Blast));
            Assert.Contains(LayerControlSets.RowNames, r => r.Name == "Ai.VisionCones" && r.Display.Length > 0 && r.Doc.Length > 0);
        }

        // ⭐ CE-3133 — the navmesh layer choice: Infantry by default, kept in the settings registry by the panel, and an enum that
        // survives the panel's JSON round trip (the StructEdit session the projector opens).
        [Fact]
        public void CE3133_TheNavmeshLayerChoice_RoundTripsThroughThePanelAndTheRegistry()
        {
            Assert.Equal(NavmeshDrawLayers.Infantry, new LayerControlDto().Map.NavmeshLayers);

            var settings = new GizmoSettingsRegistry();
            new LayerControlDto { Map = { NavmeshLayers = NavmeshDrawLayers.Vehicle } }.WriteScopes(settings);
            Assert.Equal(NavmeshDrawLayers.Vehicle, NavmeshLayerSetting.Of(settings));
            var read = new LayerControlDto();
            read.ReadScopes(settings);
            Assert.Equal(NavmeshDrawLayers.Vehicle, read.Map.NavmeshLayers);

            var edit = MakeEditService();
            string json;
            using (var session = edit.Open(new LayerControlDto { Map = { NavmeshLayers = NavmeshDrawLayers.All, Navmesh = true } }, typeof(LayerControlDto)))
                json = session.ToJson();
            using var back = edit.Open(new LayerControlDto(), typeof(LayerControlDto));
            back.LoadJson(json);
            var dto = (LayerControlDto)back.Commit();
            Assert.Equal(NavmeshDrawLayers.All, dto.Map.NavmeshLayers);
            Assert.True(dto.Map.Navmesh);
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
            Assert.Equal(GizmoScope.PinnedOnly,   // ⭐ CE-1329 — a brain family draws for pinned units only, and has no scope row
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory));

            var edited = new LayerControlDto { Command = { PathsSelectedOnly = false } };
            string json;
            using (var s = edit.Open(edited, typeof(LayerControlDto))) json = StructEdit.Json.EditSessionJsonExtensions.ToJson(s);
            gizmo.OnStructUpdate(json);

            Assert.Equal(GizmoScope.All,
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path));
            Assert.Equal(GizmoScope.PinnedOnly,
                GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory));

            var reread = new LayerControlDto();
            reread.ReadScopes(settings);
            Assert.False(reread.Command.PathsSelectedOnly);
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
            using (var s = edit.Open(new LayerControlDto { Fire = { FireTraces = false }, Map = { Doors = false } }, typeof(LayerControlDto)))
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

    public class CE1065_LayerPanelStaysOpen_Tests
    {
        // ⭐ CE-1065 — 🔒 user, 2026-10-14: "the layer dialog … responding to each checkbox click immediately, ie not needing Apply
        // button which closes the dialog". A struct update (now sent on every checkbox click) applies the mask AND leaves the panel
        // open — the panel is still emitted the frame after; only the View menu entry toggles it shut.
        [Fact]
        public void CE1065_AnUpdate_AppliesTheMask_AndThePanelStaysOpen()
        {
            var bus = new FdpEventBus();
            Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(bus);
            var edit = new ComponentEditServiceBuilder().Build();
            var buffer = new DebugPrimitiveBuffer();
            var manager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(buffer, bus);
            long id = Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId();
            var gizmo = new LayerControlGizmo(id, bus, edit);
            manager.Register(id, gizmo);
            var repo = new EntityRepository();

            bus.Publish(new OpenLayerEditorEvent());   // the menu entry opens the panel
            bus.SwapBuffers();
            manager.Execute(repo, 0f);
            Assert.True(HasPanel(buffer), "the menu entry did not open the panel");

            string json;
            using (var s = edit.Open(new LayerControlDto { Map = { Doors = false } }, typeof(LayerControlDto)))
                json = StructEdit.Json.EditSessionJsonExtensions.ToJson(s);
            bus.PublishManaged(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoStructUpdateEvent { AnchorId = id, PayloadJson = json });
            bus.SwapBuffers();
            buffer.Clear();
            manager.Execute(repo, 0f);   // routes the click
            buffer.Clear();
            manager.Execute(repo, 0f);   // the frame after

            Assert.True(HasPanel(buffer), "the panel closed on the update — a second checkbox would need the menu again");
            foreach (var p in buffer.GetFrame())
                if (p.Shape == DebugPrimitiveShape.LayerControlMask) Assert.False(p.ActiveLayers.IsSet(DebugTraceLayers.Doors));

            bus.Publish(new OpenLayerEditorEvent());   // the menu entry again closes it
            bus.SwapBuffers();
            buffer.Clear();
            manager.Execute(repo, 0f);
            Assert.False(HasPanel(buffer), "the menu entry did not close the panel");
        }

        // ⭐ CE-1066 — the panel's [X]: the terminal publishes GizmoInspectorClosedEvent, the manager routes it by anchor (no focus
        // holder needed), the panel stops; the menu entry then OPENS it again with one click (it does not need two).
        [Fact]
        public void CE1066_ThePanelsClose_ReachesTheGizmo_AndOneMenuClickReopensIt()
        {
            var bus = new FdpEventBus();
            Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(bus);
            var edit = new ComponentEditServiceBuilder().Build();
            var buffer = new DebugPrimitiveBuffer();
            var manager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(buffer, bus);
            long id = Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId();
            manager.Register(id, new LayerControlGizmo(id, bus, edit));
            var repo = new EntityRepository();
            void Frame() { bus.SwapBuffers(); buffer.Clear(); manager.Execute(repo, 0f); }

            bus.Publish(new OpenLayerEditorEvent());
            Frame();
            Assert.True(HasPanel(buffer));

            bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoInspectorClosedEvent { AnchorId = id });
            Frame();   // routes the close (the panel is emitted before the routing step)…
            Frame();   // …the frame after has none
            Assert.False(HasPanel(buffer), "the [X] did not close the panel");

            bus.Publish(new OpenLayerEditorEvent());
            Frame();
            Assert.True(HasPanel(buffer), "one menu click did not reopen the panel after its [X]");
        }

        /// <summary>
        /// ⭐⭐ CE-1071 (docs/designs/gizmos-1/DESIGN.md §11.11) — the layer control hands the buffer the SAME mask it emits, so a hidden
        /// layer (the navmesh, off by default since CE-1065) is refused at emit and a visible one is kept; the mask primitive is still in
        /// the frame for the terminals. 📐 Without it, hidden layers filled the frame and pushed the mask, menus and tool bindings out.
        /// </summary>
        [Fact]
        public void CE1071_TheLayerControl_HandsItsMaskToTheBuffer_HiddenLayersAreRefusedAtEmit()
        {
            var buffer = new DebugPrimitiveBuffer();
            var gizmo = new LayerControlGizmo(3L, new FdpEventBus(), new ComponentEditServiceBuilder().Build());
            gizmo.UpdateAndDraw(new EntityRepository(), 0f, buffer);
            Assert.False(buffer.IsLayerActive(DebugTraceLayers.Navmesh));
            Assert.True(buffer.IsLayerActive(0));

            buffer.DrawLine(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, new Rgba32(1, 2, 3, 255), layer: DebugTraceLayers.Navmesh);
            buffer.DrawLine(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, new Rgba32(1, 2, 3, 255), layer: 0);
            var frame = buffer.GetFrame().ToArray();
            Assert.Equal(1, buffer.SuppressedCount);
            Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.Line);
            Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.LayerControlMask);
        }

        private static bool HasPanel(DebugPrimitiveBuffer buffer)
        {
            foreach (var p in buffer.GetFrame())
                if (p.Shape == DebugPrimitiveShape.StructInspector) return true;
            return false;
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
