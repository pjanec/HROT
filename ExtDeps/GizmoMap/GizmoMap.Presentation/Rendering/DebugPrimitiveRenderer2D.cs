using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Raylib-based 2-D renderer for <see cref="DebugPrimitive"/> spans.
    ///
    /// Two-pass algorithm:
    ///   Pass 1 - Sweep the input span and collect all <see cref="DebugPrimitiveShape.SpatialAnchor"/>
    ///            primitives into a keyed cache.
    ///   Pass 2 - Resolve <see cref="CoordinateSpace.EntityLocal"/> primitives against the cache,
    ///            sort by (DebugLayer, ZIndex), then dispatch to <see cref="DispatchShape"/>.
    ///
    /// <see cref="DispatchShape"/> is virtual so test subclasses can capture dispatched
    /// primitives without invoking Raylib.
    /// </summary>
    public class DebugPrimitiveRenderer2D
    {
        private ushort _activeLayerMask = 0xFFFF;

        private readonly ISemanticShapeProfileRegistry? _semanticRegistry;
        private readonly ImGuiPropertyTreeAdapter _imGuiAdapter;

        private const float DegToRad = MathF.PI / 180f;

        public DebugPrimitiveRenderer2D(
            ISemanticShapeProfileRegistry? semanticRegistry = null,
            ImGuiPropertyTreeAdapter? imGuiAdapter = null)
        {
            _semanticRegistry = semanticRegistry;
            _imGuiAdapter = imGuiAdapter ?? new ImGuiPropertyTreeAdapter();
        }

        /// <summary>Overrides the 16-bit layer visibility mask.</summary>
        public void SetLayerMask(ushort mask) => _activeLayerMask = mask;

        /// <summary>
        /// Filters, resolves coordinate spaces using the two-pass SpatialAnchor cache,
        /// sorts by (DebugLayer, ZIndex) and dispatches each surviving primitive to
        /// <see cref="DispatchShape"/>.
        /// </summary>
        public void Render(ReadOnlySpan<DebugPrimitive> primitives, Camera2D camera, float zoom)
        {
            if (zoom <= 0f) zoom = 1f;

            // ---- Pass 1: build SpatialAnchor cache -------------------------
            var anchors = new Dictionary<long, SpatialAnchorEntry>();
            foreach (ref readonly var prim in primitives)
            {
                if (prim.Shape == DebugPrimitiveShape.SpatialAnchor)
                {
                    anchors[prim.NetworkId] = new SpatialAnchorEntry
                    {
                        X      = prim.AnchorWorldX,
                        Y      = prim.AnchorWorldY,
                        Z      = prim.AnchorWorldZ,
                        YawRad = prim.Heading * DegToRad,
                    };
                }
            }

            // ---- Pass 2: resolve, filter, sort, dispatch -------------------
            var sortBuffer = new List<DebugPrimitive>(primitives.Length);

            foreach (ref readonly var prim in primitives)
            {
                // SpatialAnchor and ContextMenuBinding are meta-primitives; never render them directly.
                if (prim.Shape == DebugPrimitiveShape.SpatialAnchor) continue;
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding) continue;

                // Filter: must target the 2-D map pipeline.
                if ((prim.TargetView & PipelineTarget.Map2D) == 0) continue;

                // Filter: layer mask.
                if (prim.DebugLayer >= 16 || (_activeLayerMask & (1u << prim.DebugLayer)) == 0)
                    continue;

                // Filter: LOD zoom culling.
                if (prim.MinZoomLod != 0 && zoom < prim.MinZoomLod * 0.25f) continue;
                if (prim.MaxZoomLod != 0 && zoom > prim.MaxZoomLod * 0.25f) continue;

                if (prim.Space == CoordinateSpace.EntityLocal)
                {
                    // Resolve against SpatialAnchor cache keyed by AnchorIndex (used as network ID).
                    long anchorKey = (long)prim.AnchorIndex;
                    if (!anchors.TryGetValue(anchorKey, out var entry))
                        continue; // No anchor found; skip.

                    float cos = MathF.Cos(entry.YawRad);
                    float sin = MathF.Sin(entry.YawRad);

                    DebugPrimitive resolved = prim;
                    resolved.Space = CoordinateSpace.World;

                    switch (prim.Shape)
                    {
                        case DebugPrimitiveShape.Line:
                        {
                            resolved.LineStart = ApplyAnchor2D(in entry, cos, sin, prim.LineStart);
                            resolved.LineEnd   = ApplyAnchor2D(in entry, cos, sin, prim.LineEnd);
                            break;
                        }
                        case DebugPrimitiveShape.Arrow:
                        {
                            resolved.ArrowFrom = ApplyAnchor2D(in entry, cos, sin, prim.ArrowFrom);
                            resolved.ArrowTo   = ApplyAnchor2D(in entry, cos, sin, prim.ArrowTo);
                            break;
                        }
                        case DebugPrimitiveShape.Sphere:
                        {
                            resolved.SphereCenter = ApplyAnchor2D(in entry, cos, sin, prim.SphereCenter);
                            break;
                        }
                        case DebugPrimitiveShape.Box2D:
                        {
                            (float wx, float wy) = ApplyAnchor2D_XY(in entry, cos, sin, prim.BoxCenterX, prim.BoxCenterY);
                            resolved.BoxCenterX  = wx;
                            resolved.BoxCenterY  = wy;
                            resolved.BoxAngleDeg = prim.BoxAngleDeg + entry.YawRad * (180f / MathF.PI);
                            break;
                        }
                        case DebugPrimitiveShape.Text:
                        {
                            (float wx, float wy) = ApplyAnchor2D_XY(in entry, cos, sin, prim.TextX, prim.TextY);
                            resolved.TextX = wx;
                            resolved.TextY = wy;
                            break;
                        }
                        case DebugPrimitiveShape.SemanticShape:
                        {
                            // Encode resolved world position in spare fields (Pitch=X, InspOffsetY=Y).
                            // ProfileId, LengthMeters, WidthMeters, ConditionMask remain intact.
                            resolved.Pitch       = entry.X;
                            resolved.InspOffsetY = entry.Y;
                            break;
                        }
                        default:
                        {
                            // Icon and other shapes: transform via IconWorldPosX/Y.
                            (float wx, float wy) = ApplyAnchor2D_XY(in entry, cos, sin, prim.IconWorldPosX, prim.IconWorldPosY);
                            resolved.IconWorldPosX = wx;
                            resolved.IconWorldPosY = wy;
                            break;
                        }
                    }

                    sortBuffer.Add(resolved);
                    continue;
                }

                sortBuffer.Add(prim);
            }

            // Stable painter's-algorithm sort: DebugLayer ascending, ZIndex ascending.
            sortBuffer.Sort(static (a, b) =>
            {
                int cmp = a.DebugLayer.CompareTo(b.DebugLayer);
                return cmp != 0 ? cmp : a.ZIndex.CompareTo(b.ZIndex);
            });

            foreach (var prim in sortBuffer)
                DispatchShape(in prim, camera, zoom);
        }

        /// <summary>
        /// Issues the actual Raylib draw call(s) for one primitive.
        /// Override in test subclasses to capture dispatches without Raylib.
        /// </summary>
        protected virtual void DispatchShape(in DebugPrimitive prim, Camera2D camera, float zoom)
        {
            var color = ToRaylibColor(prim.Color);

            float geomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;
            float thickness = prim.SizeMode == SizeMode.ScreenPixels
                ? prim.Thickness / zoom
                : prim.Thickness;

            bool screenSpace = prim.Space == CoordinateSpace.Screen;
            if (screenSpace) Raylib.EndMode2D();

            switch (prim.Shape)
            {
                case DebugPrimitiveShape.Line:
                {
                    var start = new Vector2(prim.LineStart.X, prim.LineStart.Y);
                    var end   = new Vector2(prim.LineEnd.X,   prim.LineEnd.Y);
                    bool gradient = prim.EndColor.R != prim.Color.R
                                 || prim.EndColor.G != prim.Color.G
                                 || prim.EndColor.B != prim.Color.B
                                 || prim.EndColor.A != prim.Color.A;
                    if (gradient)
                        DrawGradientLine(start, end, thickness, color, ToRaylibColor(prim.EndColor));
                    else
                        Raylib.DrawLineEx(start, end, thickness, color);
                    break;
                }

                case DebugPrimitiveShape.Sphere:
                {
                    var center = new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y);
                    float scaledRadius = prim.SphereRadius * geomScale;
                    if (prim.Thickness > 0f)
                    {
                        float scaledThickness = prim.SizeMode == SizeMode.ScreenPixels
                            ? prim.Thickness / zoom
                            : prim.Thickness;
                        float innerRadius = Math.Max(0f, scaledRadius - scaledThickness);
                        Raylib.DrawRing(center, innerRadius, scaledRadius, 0f, 360f, 32, color);
                    }
                    else
                    {
                        Raylib.DrawCircleV(center, scaledRadius, color);
                    }
                    break;
                }

                case DebugPrimitiveShape.Arrow:
                {
                    var from = new Vector2(prim.ArrowFrom.X, prim.ArrowFrom.Y);
                    var to   = new Vector2(prim.ArrowTo.X,   prim.ArrowTo.Y);
                    DrawArrow(from, to, prim.ArrowHeadSize * geomScale, color, thickness);
                    break;
                }

                case DebugPrimitiveShape.Box2D:
                {
                    // Raylib.DrawRectanglePro uses the Rectangle's X/Y as the placement target for the Origin.
                    // Since our Origin is the center of the extents, X/Y must be the exact BoxCenter.
                    var rect = new Rectangle(
                        prim.BoxCenterX, 
                        prim.BoxCenterY, 
                        prim.BoxExtentX * 2f * geomScale,
                        prim.BoxExtentY * 2f * geomScale);
        
                    var origin = new Vector2(prim.BoxExtentX * geomScale, prim.BoxExtentY * geomScale);
                    Raylib.DrawRectanglePro(rect, origin, prim.BoxAngleDeg, color);
                    break;
                }

                case DebugPrimitiveShape.Text:
                {
                    int tx = (int)prim.TextX;
                    int ty = (int)prim.TextY;
                    string str = prim.TextContent.ToString();
                    Raylib.DrawText(str, tx, ty, 12, color);
                    break;
                }

                case DebugPrimitiveShape.EntityBadge:
                {
                    // Position comes from the anchor cache (already resolved to World space in Render).
                    // BadgeTargetIndex holds the anchor network ID in the decoupled scenario.
                    float worldX = prim.BoxCenterX; // world position stored in BoxCenterX/Y for badge
                    float worldY = prim.BoxCenterY;
                    var screenPos = Raylib.GetWorldToScreen2D(new Vector2(worldX, worldY), camera);
                    var richText = prim.BadgeRichText;
                    RichTextRenderer.DrawRichTextBadge(ref richText, (int)screenPos.X, (int)screenPos.Y, 12);
                    break;
                }

                case DebugPrimitiveShape.Icon:
                {
                    // Fallback: draw yellow dot at world position.
                    var center = new Vector2(prim.IconWorldPosX, prim.IconWorldPosY);
                    Raylib.DrawCircleV(center, 4f, Color.Yellow);
                    break;
                }

                case DebugPrimitiveShape.SemanticShape:
                {
                    // Resolved world position stored in Pitch (X) and InspOffsetY (Y).
                    float worldX = prim.Pitch;
                    float worldY = prim.InspOffsetY;
                    float radius = prim.LengthMeters > 0f ? prim.LengthMeters : 5f;

                    if (_semanticRegistry != null
                        && _semanticRegistry.TryGetProfile(prim.ProfileId, out var profile))
                    {
                        float len = profile.LengthMeters > 0f ? profile.LengthMeters : radius;
                        float wid = profile.WidthMeters  > 0f ? profile.WidthMeters  : radius * 0.5f;
                        var rect = new Rectangle(
                            worldX - len * geomScale * 0.5f,
                            worldY - wid * geomScale * 0.5f,
                            len * geomScale,
                            wid * geomScale);
                        Raylib.DrawRectangleLinesEx(rect, 1f, color);

                        // Bit 0 of ConditionMask = Damaged: draw a red X overlay.
                        if ((prim.ConditionMask & 1u) != 0)
                        {
                            Raylib.DrawLineEx(
                                new Vector2(rect.X, rect.Y),
                                new Vector2(rect.X + rect.Width, rect.Y + rect.Height),
                                1.5f, Color.Red);
                            Raylib.DrawLineEx(
                                new Vector2(rect.X + rect.Width, rect.Y),
                                new Vector2(rect.X, rect.Y + rect.Height),
                                1.5f, Color.Red);
                        }
                    }
                    else
                    {
                        // Fallback: magenta outline circle.
                        Raylib.DrawCircleLines((int)worldX, (int)worldY, radius * geomScale, Color.Magenta);
                    }
                    break;
                }

                case DebugPrimitiveShape.MilStd2525:
                {
                    MilStd2525Renderer.Draw(
                        prim.SidcCode.ToString(),
                        prim.MilWorldPosX,
                        prim.MilWorldPosY,
                        camera,
                        zoom);
                    break;
                }

                case DebugPrimitiveShape.ComponentInspector:
                {
                    _imGuiAdapter.Schedule(
                        prim.InspNetworkId,
                        prim.InspSchemaHash,
                        prim.InspOffsetX,
                        prim.InspOffsetY,
                        prim.InspIsReadOnly != 0);
                    break;
                }

                default:
                    break;
            }

            if (screenSpace) Raylib.BeginMode2D(camera);
        }

        // ---- Protected static helpers used by production DispatchShape ----------

        protected static Color ToRaylibColor(Rgba32 c) => new Color(c.R, c.G, c.B, c.A);

        // ---- Private helpers ------------------------------------------------

        private static void DrawGradientLine(
            Vector2 from, Vector2 to, float thickness, Color startColor, Color endColor)
        {
            var dir = to - from;
            float len = dir.Length();
            if (len < float.Epsilon) return;

            var unit = dir / len;
            var perp = new Vector2(-unit.Y, unit.X) * (thickness * 0.5f);

            var v0 = from + perp;
            var v1 = from - perp;
            var v2 = to   - perp;
            var v3 = to   + perp;

            Rlgl.Begin((int)DrawMode.Quads);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v0.X, v0.Y);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v1.X, v1.Y);
            Rlgl.Color4ub(endColor.R,   endColor.G,   endColor.B,   endColor.A);   Rlgl.Vertex2f(v2.X, v2.Y);
            Rlgl.Color4ub(endColor.R,   endColor.G,   endColor.B,   endColor.A);   Rlgl.Vertex2f(v3.X, v3.Y);
            Rlgl.End();
        }

        private static void DrawArrow(Vector2 from, Vector2 to, float headSize, Color color, float thickness)
        {
            Raylib.DrawLineEx(from, to, thickness, color);

            var dir = to - from;
            float len = dir.Length();
            if (len < float.Epsilon) return;

            var unit = dir / len;
            var perp = new Vector2(-unit.Y, unit.X);

            var tip   = to;
            var baseL = to - unit * headSize + perp * (headSize * 0.4f);
            var baseR = to - unit * headSize - perp * (headSize * 0.4f);

            Raylib.DrawTriangle(tip, baseL, baseR, color);
        }

        // ---- SpatialAnchor transform helpers --------------------------------

        private static Vector3 ApplyAnchor2D(
            in SpatialAnchorEntry entry, float cos, float sin, Vector3 local)
        {
            float wx = entry.X + cos * local.X - sin * local.Y;
            float wy = entry.Y + sin * local.X + cos * local.Y;
            return new Vector3(wx, wy, local.Z);
        }

        private static (float wx, float wy) ApplyAnchor2D_XY(
            in SpatialAnchorEntry entry, float cos, float sin, float localX, float localY)
        {
            float wx = entry.X + cos * localX - sin * localY;
            float wy = entry.Y + sin * localX + cos * localY;
            return (wx, wy);
        }
    }

    /// <summary>Cache entry populated from a SpatialAnchor primitive.</summary>
    public struct SpatialAnchorEntry
    {
        public float X;
        public float Y;
        public float Z;
        public float YawRad;
    }
}
