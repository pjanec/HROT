using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using StructEdit.Core;

namespace GizmoMap.Example
{
    /// <summary>
    /// Demo scene generator. Implements IGizmoSource.
    /// Every Emit call produces a deterministic set of primitives covering
    /// all required shape types for GZ056 validation.
    /// </summary>
    public sealed class DemoSceneGenerator : IGizmoSource
    {
        // Accumulated time for oscillation and toggle logic.
        private float _elapsedTime;

        // APC profile ID (arbitrary well-known value for tests).
        private const ulong ApcProfileId = 0x0001_0002_0003_0004UL;

        // Static NATO symbol world position.
        private const float NatoX = 500f;
        private const float NatoY = 300f;

        // Moving entity network ID.
        private const long EntityNetworkId = 100L;

        // Context menu binding key: matches the interactive box SubElementId (cast to long).
        private const long BoxMenuEntityId = 1L;

        // Three menu definition JSON strings that cycle every 3 seconds.
        // Each represents a different tactical state for the orange box entity.
        private static readonly string MenuJsonIdle =
            "[" +
            "{\"id\":1,\"label\":\"Center View\",\"shortcut\":\"C\"}," +
            "{\"separator\":true}," +
            "{\"id\":10,\"label\":\"Order: Move\",\"shortcut\":\"M\"}," +
            "{\"id\":11,\"label\":\"Order: Engage\",\"shortcut\":\"E\"}," +
            "{\"separator\":true}," +
            "{\"label\":\"Logistics\",\"children\":[" +
            "{\"id\":20,\"label\":\"Resupply\"}," +
            "{\"id\":21,\"label\":\"Repair\"}" +
            "]}," +
            "{\"id\":99,\"label\":\"DELETE\",\"style\":\"destructive\"}" +
            "]";

        private static readonly string MenuJsonMoving =
            "[" +
            "{\"id\":1,\"label\":\"Center View\",\"shortcut\":\"C\"}," +
            "{\"separator\":true}," +
            "{\"id\":10,\"label\":\"Order: Move\",\"enabled\":false}," +
            "{\"id\":11,\"label\":\"Order: Engage\",\"shortcut\":\"E\"}," +
            "{\"id\":12,\"label\":\"Order: Stop\",\"shortcut\":\"S\"}," +
            "{\"separator\":true}," +
            "{\"label\":\"Logistics\",\"children\":[" +
            "{\"id\":20,\"label\":\"Resupply\",\"enabled\":false,\"tooltip\":\"Cannot resupply: Unit is moving\"}," +
            "{\"id\":21,\"label\":\"Repair\",\"enabled\":false}" +
            "]}," +
            "{\"id\":99,\"label\":\"DELETE\",\"style\":\"destructive\"}" +
            "]";

        private static readonly string MenuJsonEngaging =
            "[" +
            "{\"id\":1,\"label\":\"Center View\",\"shortcut\":\"C\"}," +
            "{\"separator\":true}," +
            "{\"id\":10,\"label\":\"Order: Move\",\"enabled\":false}," +
            "{\"id\":11,\"label\":\"Order: Engage\",\"enabled\":false}," +
            "{\"id\":13,\"label\":\"Order: Cease Fire\",\"shortcut\":\"F\"}," +
            "{\"separator\":true}," +
            "{\"label\":\"Logistics\",\"children\":[" +
            "{\"id\":20,\"label\":\"Resupply\",\"enabled\":false}," +
            "{\"id\":21,\"label\":\"Repair\",\"enabled\":false}" +
            "]}," +
            "{\"id\":99,\"label\":\"DELETE\",\"style\":\"destructive\"}" +
            "]";

        // Menu cycle period in seconds (one menu per 3 seconds, 3 menus => 9-second cycle).
        private const float MenuCyclePeriod = 3f;

        // ---- Interactive box drag state -------------------------------------
        // Current committed position of the draggable Box2D (item 7).
        // Starts near world origin so it is visible with the default camera.
        private float _interactiveBoxX = 0f;
        private float _interactiveBoxY = 0f;

        // Box position captured at the start of the current drag.
        private float _dragBaseBoxX;
        private float _dragBaseBoxY;

        // World position received on the first DragUpdate of the current drag.
        private float _dragStartWorldX;
        private float _dragStartWorldY;
        private bool  _gotFirstDragUpdate;

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            _elapsedTime += deltaTime;

            // Require the draw builder to be a LocalDrawBuilder for raw emission.
            var local = draw as LocalDrawBuilder
                ?? throw new InvalidOperationException(
                       "DemoSceneGenerator requires a LocalDrawBuilder.");

            EmitScene(local, _elapsedTime);
        }

        /// <summary>
        /// Overload accepting LocalDrawBuilder directly (for tests that bypass IDebugDrawBuilder).
        /// </summary>
        public void Emit(float deltaTime, LocalDrawBuilder builder)
        {
            _elapsedTime += deltaTime;
            EmitScene(builder, _elapsedTime);
        }

        // ---- Interaction handler -------------------------------------------

        /// <summary>
        /// Receives gizmo interaction events from the presentation layer and
        /// updates the interactive box position so the next Emit() tick renders
        /// it at the new world coordinates.
        /// </summary>
        public void OnGizmoInteraction(GizmoPickToken token, GizmoInteractionEventKind kind, Vector3 pos)
        {
            if (token.SubElementId != 1) return;
            Console.WriteLine($"Gizmo interaction: anchor={token.AnchorId} sub={token.SubElementId} {kind} at {pos}");
            switch (kind)
            {
                case GizmoInteractionEventKind.Started:
                    _dragBaseBoxX = _interactiveBoxX;
                    _dragBaseBoxY = _interactiveBoxY;
                    _gotFirstDragUpdate = false;
                    break;

                case GizmoInteractionEventKind.DragUpdate:
                    if (!_gotFirstDragUpdate)
                    {
                        _dragStartWorldX = pos.X;
                        _dragStartWorldY = pos.Y;
                        _gotFirstDragUpdate = true;
                    }
                    _interactiveBoxX = _dragBaseBoxX + (pos.X - _dragStartWorldX);
                    _interactiveBoxY = _dragBaseBoxY + (pos.Y - _dragStartWorldY);
                    break;

                case GizmoInteractionEventKind.Commit:
                    if (_gotFirstDragUpdate)
                    {
                        _interactiveBoxX = _dragBaseBoxX + (pos.X - _dragStartWorldX);
                        _interactiveBoxY = _dragBaseBoxY + (pos.Y - _dragStartWorldY);
                    }
                    _gotFirstDragUpdate = false;
                    break;

                case GizmoInteractionEventKind.Cancel:
                    _interactiveBoxX = _dragBaseBoxX;
                    _interactiveBoxY = _dragBaseBoxY;
                    _gotFirstDragUpdate = false;
                    break;
            }
        }

        /// <summary>
        /// Receives context menu action events from the presentation layer.
        /// Called when the operator clicks a menu item on the interactive box.
        /// </summary>
        public void OnMenuAction(GizmoPickToken token, int actionId)
        {
            string menuJson = GetActiveMenuJson(_elapsedTime);
            string label    = ResolveActionLabel(menuJson, actionId);
            Console.WriteLine($"[ContextMenu] anchor={token.AnchorId} action={actionId} ({label})");
        }

        // ---- Scene emission -------------------------------------------------

        private void EmitScene(LocalDrawBuilder builder, float t)
        {
            // ---- 1. SpatialAnchor: moving entity at circular orbit ---------------
            float orbitRadius = 200f;
            float orbitPeriod = 10f;
            float angle       = (t / orbitPeriod) * (2f * MathF.PI);
            float anchorX     = MathF.Cos(angle) * orbitRadius;
            float anchorY     = MathF.Sin(angle) * orbitRadius;
            float yawDeg      = angle * (180f / MathF.PI); // heading in degrees

            var anchorPrim = default(DebugPrimitive);
            anchorPrim.Shape        = DebugPrimitiveShape.SpatialAnchor;
            anchorPrim.TargetView   = PipelineTarget.Map2D;
            anchorPrim.NetworkId    = EntityNetworkId;
            anchorPrim.AnchorWorldX = anchorX;
            anchorPrim.AnchorWorldY = anchorY;
            anchorPrim.AnchorWorldZ = 0f;
            anchorPrim.Heading      = yawDeg;
            builder.EmitRaw(in anchorPrim);

            // ---- 2. SemanticShape: APC, EntityLocal, toggles Damaged every 2s ---
            bool isDamaged = ((int)(t / 2f) & 1) != 0;
            var semPrim = default(DebugPrimitive);
            semPrim.Shape         = DebugPrimitiveShape.SemanticShape;
            semPrim.Space         = CoordinateSpace.EntityLocal;
            semPrim.TargetView    = PipelineTarget.Map2D;
            semPrim.AnchorIndex   = (int)EntityNetworkId;
            semPrim.ProfileId     = ApcProfileId;
            semPrim.LengthMeters  = 8f;
            semPrim.WidthMeters   = 4f;
            semPrim.ConditionMask = isDamaged ? 1u : 0u;
            semPrim.Color         = Rgba32.Green;
            builder.EmitRaw(in semPrim);

            // ---- 3. Sphere: sensor ring, EntityLocal, WorldMeters ---------------
            var sensorPrim = default(DebugPrimitive);
            sensorPrim.Shape        = DebugPrimitiveShape.Sphere;
            sensorPrim.Space        = CoordinateSpace.EntityLocal;
            sensorPrim.SizeMode     = SizeMode.WorldMeters;
            sensorPrim.TargetView   = PipelineTarget.Map2D;
            sensorPrim.AnchorIndex  = (int)EntityNetworkId;
            sensorPrim.SphereCenter = Vector3.Zero;
            sensorPrim.SphereRadius = 50f;
            sensorPrim.Color        = new Rgba32(0, 200, 255, 128);
            builder.EmitRaw(in sensorPrim);

            // ---- 4. ComponentInspector: mock schema hash 0xDEADBEEF ---------------
            var inspPrim = default(DebugPrimitive);
            inspPrim.Shape         = DebugPrimitiveShape.ComponentInspector;
            inspPrim.TargetView    = PipelineTarget.Map2D;
            inspPrim.InspNetworkId = EntityNetworkId;
            inspPrim.InspSchemaHash = 0xDEADBEEF;
            inspPrim.InspOffsetX   = 20f;
            inspPrim.InspOffsetY   = 20f;
            builder.EmitRaw(in inspPrim);

            // ---- 5. MilStd2525: hostile infantry at static world position ---------
            var milPrim = default(DebugPrimitive);
            milPrim.Shape        = DebugPrimitiveShape.MilStd2525;
            milPrim.Space        = CoordinateSpace.World;
            milPrim.TargetView   = PipelineTarget.Map2D;
            milPrim.MilWorldPosX = NatoX;
            milPrim.MilWorldPosY = NatoY;
            milPrim.SidcCode     = new FixedString32("SHGPE----------");
            milPrim.Color        = Rgba32.Red;
            builder.EmitRaw(in milPrim);

            // ---- 6. EntityBadge: rich text at NATO symbol position ----------------
            var badgePrim = default(DebugPrimitive);
            badgePrim.Shape           = DebugPrimitiveShape.EntityBadge;
            badgePrim.Space           = CoordinateSpace.World;
            badgePrim.TargetView      = PipelineTarget.Map2D;
            // Store world position in BoxCenterX/Y (read by renderer for badge position).
            badgePrim.BoxCenterX      = NatoX;
            badgePrim.BoxCenterY      = NatoY + 25f;
            badgePrim.BadgeRichText   = new FixedString32("\x01Hostile\x04 - \x02Target");
            builder.EmitRaw(in badgePrim);

            // ---- 7. Interactive Box2D: draggable box at current live position ----
            var boxPrim = default(DebugPrimitive);
            boxPrim.Shape        = DebugPrimitiveShape.Box2D;
            boxPrim.Space        = CoordinateSpace.World;
            boxPrim.TargetView   = PipelineTarget.Map2D;
            boxPrim.BoxCenterX   = _interactiveBoxX;
            boxPrim.BoxCenterY   = _interactiveBoxY;
            boxPrim.BoxExtentX   = 30f;
            boxPrim.BoxExtentY   = 30f;
            boxPrim.BoxAngleDeg  = 0f;
            boxPrim.Color        = new Rgba32(255, 100, 0, 200);
            boxPrim.SubElementId = 1;
            boxPrim.ZIndex       = 2;
            builder.EmitRaw(in boxPrim);

            // ---- 8. Gradient Line: moving entity -> static NATO symbol -------------
            builder.DrawLineGradient(
                new Vector3(anchorX, anchorY, 0f),
                new Vector3(NatoX,   NatoY,   0f),
                Rgba32.Yellow,
                Rgba32.Red,
                thickness: 2f,
                sizeMode: SizeMode.ScreenPixels);

            // ---- 9. Arrow: velocity vector, EntityLocal, ScreenPixels -------------
            var arrowPrim = default(DebugPrimitive);
            arrowPrim.Shape       = DebugPrimitiveShape.Arrow;
            arrowPrim.Space       = CoordinateSpace.EntityLocal;
            arrowPrim.SizeMode    = SizeMode.ScreenPixels;
            arrowPrim.TargetView  = PipelineTarget.Map2D;
            arrowPrim.AnchorIndex = (int)EntityNetworkId;
            arrowPrim.ArrowFrom   = Vector3.Zero;
            arrowPrim.ArrowTo     = new Vector3(MathF.Cos(angle) * 30f, MathF.Sin(angle) * 30f, 0f);
            arrowPrim.ArrowHeadSize = 8f;
            arrowPrim.Color       = Rgba32.White;
            builder.EmitRaw(in arrowPrim);

            // ---- 10. Icon: HUD icon at screen (50, 50) -------------------------
            var iconPrim = default(DebugPrimitive);
            iconPrim.Shape        = DebugPrimitiveShape.Icon;
            iconPrim.Space        = CoordinateSpace.Screen;
            iconPrim.TargetView   = PipelineTarget.Map2D;
            iconPrim.IconWorldPosX = 50f;
            iconPrim.IconWorldPosY = 50f;
            iconPrim.IconAtlasCoord = new FixedString32("b12");
            iconPrim.Color        = Rgba32.White;
            builder.EmitRaw(in iconPrim);

            // ---- 11. DrawTextLong: 200-char diagnostic string ------------------
            string diagnostic = new string('D', 200); // 200-char string
            builder.DrawTextLong(10f, 10f, diagnostic, Rgba32.White, CoordinateSpace.Screen);

            // ---- 12. Z-index test: two overlapping Box2D at same world pos -----
            var boxGray = default(DebugPrimitive);
            boxGray.Shape      = DebugPrimitiveShape.Box2D;
            boxGray.Space      = CoordinateSpace.World;
            boxGray.TargetView = PipelineTarget.Map2D;
            boxGray.BoxCenterX = 0f;
            boxGray.BoxCenterY = 0f;
            boxGray.BoxExtentX = 50f;
            boxGray.BoxExtentY = 50f;
            boxGray.ZIndex     = 0;
            boxGray.Color      = new Rgba32(128, 128, 128, 255);
            builder.EmitRaw(in boxGray);

            var boxWhite = default(DebugPrimitive);
            boxWhite.Shape      = DebugPrimitiveShape.Box2D;
            boxWhite.Space      = CoordinateSpace.World;
            boxWhite.TargetView = PipelineTarget.Map2D;
            boxWhite.BoxCenterX = 0f;
            boxWhite.BoxCenterY = 0f;
            boxWhite.BoxExtentX = 50f;
            boxWhite.BoxExtentY = 50f;
            boxWhite.ZIndex     = 1;
            boxWhite.Color      = Rgba32.White;
            builder.EmitRaw(in boxWhite);

            // ---- 13. LOD text: visible only between zoom 1.0 and 3.0 -----------
            // MinZoomLod=4 => 4*0.25=1.0 min zoom; MaxZoomLod=12 => 12*0.25=3.0 max zoom.
            builder.DrawText(
                100f, 100f,
                new FixedString32("LOD test text"),
                Rgba32.Yellow,
                CoordinateSpace.World);
            // Note: LOD fields are set on the raw primitive; builder.DrawText doesn't expose them.
            // Emit the LOD-constrained text as a raw primitive instead.
            var lodPrim = default(DebugPrimitive);
            lodPrim.Shape      = DebugPrimitiveShape.Text;
            lodPrim.Space      = CoordinateSpace.World;
            lodPrim.TargetView = PipelineTarget.Map2D;
            lodPrim.TextX      = 100f;
            lodPrim.TextY      = 130f;
            lodPrim.TextContent = new FixedString32("LOD-cull text");
            lodPrim.Color      = Rgba32.Yellow;
            lodPrim.MinZoomLod = 4;   // 1.0x zoom minimum
            lodPrim.MaxZoomLod = 12;  // 3.0x zoom maximum
            builder.EmitRaw(in lodPrim);

            // ---- 14. ContextMenuBinding: cycle through 3 menu definitions for the orange box ----
            // Pick one of the three menu JSON strings based on elapsed time.
            string activeMenu = GetActiveMenuJson(t);
            uint   menuHash   = StringInternMap.Fnv1a32(activeMenu);
            builder.Buffer.InternMap.Intern(menuHash, activeMenu); // idempotent; rarely allocates
            var menuBinding = DebugPrimitive.MakeContextMenuBinding(BoxMenuEntityId, menuHash);
            builder.EmitRaw(in menuBinding);
        }

        // ---- Context menu helpers ------------------------------------------

        // Returns the active menu JSON string for the given elapsed time.
        // Cycles through Idle -> Moving -> Engaging every MenuCyclePeriod seconds.
        internal static string GetActiveMenuJson(float t)
        {
            int phase = (int)(t / MenuCyclePeriod) % 3;
            return phase switch
            {
                0 => MenuJsonIdle,
                1 => MenuJsonMoving,
                _ => MenuJsonEngaging,
            };
        }

        // Walks the menu JSON string and resolves the label for the given action id.
        // Used for console logging in OnMenuAction. Returns the id as string if not found.
        internal static string ResolveActionLabel(string menuJson, int actionId)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(menuJson);
                return FindLabel(doc.RootElement, actionId) ?? actionId.ToString();
            }
            catch
            {
                return actionId.ToString();
            }
        }

        private static string? FindLabel(System.Text.Json.JsonElement element, int actionId)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    string? found = FindLabel(item, actionId);
                    if (found != null) return found;
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (element.TryGetProperty("id", out var idProp) && idProp.GetInt32() == actionId
                    && element.TryGetProperty("label", out var lbl))
                    return lbl.GetString();

                if (element.TryGetProperty("children", out var children))
                    return FindLabel(children, actionId);
            }
            return null;
        }

        // ---- StructEdit mock schema for schema hash 0xDEADBEEF ----------------

        /// <summary>
        /// Builds a synthetic <see cref="EditDocument"/> for schema hash <c>0xDEADBEEF</c>.
        /// Used by <see cref="GizmoMap.Presentation.GizmoSchemaRegistry"/> to populate the
        /// StructEdit property panel in the interactive demo.
        /// </summary>
        public static EditDocument BuildMockDocument()
        {
            var nameNode = new EditNode(
                new EditNodeId(1), "Name", "$.Name",
                EditNodeKind.String, typeof(string),
                isReadOnly: true);

            var healthNode = new EditNode(
                new EditNodeId(2), "Health", "$.Health",
                EditNodeKind.Scalar, typeof(float),
                isReadOnly: true);

            var factionNode = new EditNode(
                new EditNodeId(3), "Faction", "$.Faction",
                EditNodeKind.String, typeof(string),
                isReadOnly: true);

            var root = new EditNode(
                new EditNodeId(0), "MockComponent", "$",
                EditNodeKind.Struct, typeof(object),
                children: new[] { nameNode, healthNode, factionNode });

            return new EditDocument(root, typeof(object), EditScope.WholeComponent);
        }
    }
}
