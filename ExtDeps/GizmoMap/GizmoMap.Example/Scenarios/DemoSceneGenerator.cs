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
