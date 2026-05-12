using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Presentation.Shapes;
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
        private readonly IEntityShapeLibrary _shapeLibrary;
        private readonly ImGuiPropertyTreeAdapter _imGuiAdapter;

        private const float DegToRad = MathF.PI / 180f;

        public DebugPrimitiveRenderer2D(
            IEntityShapeLibrary? shapeLibrary = null,
            ImGuiPropertyTreeAdapter? imGuiAdapter = null)
        {
            _shapeLibrary = shapeLibrary ?? new DefaultEntityShapeLibrary();
            _imGuiAdapter = imGuiAdapter ?? new ImGuiPropertyTreeAdapter();
        }

        /// <summary>
        /// Filters, resolves coordinate spaces using the two-pass SpatialAnchor cache,
        /// sorts by (DebugLayer, ZIndex) and dispatches each surviving primitive to
        /// <see cref="DispatchShape"/>.
        /// </summary>
        public void Render(ReadOnlySpan<DebugPrimitive> primitives, Camera2D camera, float zoom)
        {
            if (zoom <= 0f) zoom = 1f;

            // Enforce stateless default: fully visible unless overridden by the backend this frame.
            var activeLayers = new LayerMask256();
            activeLayers.SetAll();

            // ---- Pass 1: build SpatialAnchor cache & extract global frame state ----
            var anchors = new Dictionary<long, SpatialAnchorEntry>();
            foreach (ref readonly var prim in primitives)
            {
                if (prim.Shape == DebugPrimitiveShape.SpatialAnchor)
                {
                    anchors[prim.NetworkId] = new SpatialAnchorEntry
                    {
                        X        = prim.AnchorWorldX,
                        Y        = prim.AnchorWorldY,
                        Z        = prim.AnchorWorldZ,
                        YawRad   = prim.Heading * DegToRad,
                        PitchRad = prim.Pitch * DegToRad,
                        RollRad  = prim.Roll * DegToRad,
                    };
                }
                else if (prim.Shape == DebugPrimitiveShape.LayerControlMask)
                {
                    // Backend asserts authority over the layer visibility mask for this frame.
                    activeLayers = prim.ActiveLayers;
                }
            }

            // ---- Pass 2: resolve, filter, sort, dispatch -------------------
            var sortBuffer = new List<DebugPrimitive>(primitives.Length);

            foreach (ref readonly var prim in primitives)
            {
                // SpatialAnchor, meta-binding and control primitives are never dispatched directly.
                if (prim.Shape == DebugPrimitiveShape.SpatialAnchor) continue;
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding) continue;
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding) continue;
                if (prim.Shape == DebugPrimitiveShape.MainMenuBinding) continue;
                if (prim.Shape == DebugPrimitiveShape.LayerControlMask) continue;

                // Filter: must target the 2-D map pipeline.
                if ((prim.TargetView & PipelineTarget.Map2D) == 0) continue;

                // Filter: robust 256-bit layer mask evaluation.
                if (!activeLayers.IsSet(prim.DebugLayer)) continue;

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
                            resolved.ResolvedWorldX = entry.X;
                            resolved.ResolvedWorldY = entry.Y;
                            resolved.ResolvedYawRad = entry.YawRad;
                            resolved.ResolvedPitchRad = entry.PitchRad;
                            resolved.ResolvedRollRad = entry.RollRad;
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

        public void DrawStructInspector(Action<long, uint, string>? onStructUpdate = null)
        {
            _imGuiAdapter.DrawScheduled(onStructUpdate);
        }

        /// <summary>
        /// Issues the actual Raylib draw call(s) for one primitive.
        /// Override in test subclasses to capture dispatches without Raylib.
        /// </summary>
        protected virtual void DispatchShape(in DebugPrimitive prim, Camera2D camera, float zoom)
        {
            var color = ToRaylibColor(prim.Color);

            float geomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;
            // Interpret zero payload thickness as a standard 1-unit stroke so
            // default(DebugPrimitive) or EmitRaw paths still render visibly.
            float baseThickness = prim.ThicknessU16 > 0 ? prim.ThicknessU16 / 10f : 1f;
            float thickness = prim.SizeMode == SizeMode.ScreenPixels
                ? baseThickness / zoom
                : baseThickness;

            bool screenSpace = prim.Space == CoordinateSpace.Screen;
            if (screenSpace) Raylib.EndMode2D();

            switch (prim.Shape)
            {
                case DebugPrimitiveShape.Line:
                {
                    var start = new Vector2(prim.LineStart.X, prim.LineStart.Y);
                    var end   = new Vector2(prim.LineEnd.X,   prim.LineEnd.Y);
                    var endColor = ToRaylibColor(prim.EndColor);
                    bool gradient = prim.EndColor.R != prim.Color.R
                                 || prim.EndColor.G != prim.Color.G
                                 || prim.EndColor.B != prim.Color.B
                                 || prim.EndColor.A != prim.Color.A;
                    if (prim.LineStyle == LineStyle.Dashed || prim.LineStyle == LineStyle.Dotted)
                        DrawStyledLineGradient(start, end, thickness, color, endColor, prim.LineStyle, geomScale);
                    else if (gradient)
                        DrawGradientLine(start, end, thickness, color, endColor);
                    else
                        Raylib.DrawLineEx(start, end, thickness, color);
                    break;
                }

                case DebugPrimitiveShape.Sphere:
                {
                    var center = new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y);
                    float scaledRadius = prim.SphereRadius * geomScale;
                    bool hasExplicitFill = prim.FillColor.A > 0;
                    bool legacyFill = prim.FillColor.A == 0 && prim.ThicknessU16 == 0 && color.A > 0;
                    if (hasExplicitFill || legacyFill)
                    {
                        var fillColor = hasExplicitFill ? ToRaylibColor(prim.FillColor) : color;
                        Raylib.DrawCircleV(center, scaledRadius, fillColor);
                    }
                    if (baseThickness > 0f)
                    {
                        float scaledThickness = thickness;
                        if (prim.LineStyle == LineStyle.Solid)
                        {
                            float innerRadius = Math.Max(0f, scaledRadius - scaledThickness);
                            Raylib.DrawRing(center, innerRadius, scaledRadius, 0f, 360f, 32, color);
                        }
                        else
                        {
                            int segments = Math.Max(16, (int)(scaledRadius * 2f));
                            float step = (MathF.PI * 2f) / segments;
                            for (int i = 0; i < segments; i++)
                            {
                                float a0 = i * step;
                                float a1 = (i + 1) * step;
                                var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * scaledRadius;
                                var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * scaledRadius;
                                DrawStyledLine(p0, p1, scaledThickness, color, prim.LineStyle, geomScale);
                            }
                        }
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
                    bool hasExplicitFill = prim.FillColor.A > 0;
                    bool legacyFill = prim.FillColor.A == 0 && prim.ThicknessU16 == 0 && color.A > 0;
                    if (hasExplicitFill || legacyFill)
                    {
                        var fillColor = hasExplicitFill ? ToRaylibColor(prim.FillColor) : color;
                        Raylib.DrawRectanglePro(rect, origin, prim.BoxAngleDeg, fillColor);
                    }

                    if (thickness > 0f && color.A > 0)
                    {
                        float ex = prim.BoxExtentX * geomScale;
                        float ey = prim.BoxExtentY * geomScale;
                        float angleRad = prim.BoxAngleDeg * DegToRad;
                        float cos = MathF.Cos(angleRad);
                        float sin = MathF.Sin(angleRad);
                        var c = new Vector2(prim.BoxCenterX, prim.BoxCenterY);
                        var tl = c + new Vector2(-ex * cos + ey * sin, -ex * sin - ey * cos);
                        var tr = c + new Vector2( ex * cos + ey * sin,  ex * sin - ey * cos);
                        var br = c + new Vector2( ex * cos - ey * sin,  ex * sin + ey * cos);
                        var bl = c + new Vector2(-ex * cos - ey * sin, -ex * sin + ey * cos);
                        DrawStyledLine(tl, tr, thickness, color, prim.LineStyle, geomScale);
                        DrawStyledLine(tr, br, thickness, color, prim.LineStyle, geomScale);
                        DrawStyledLine(br, bl, thickness, color, prim.LineStyle, geomScale);
                        DrawStyledLine(bl, tl, thickness, color, prim.LineStyle, geomScale);
                    }
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
                    float worldX = prim.ResolvedWorldX;
                    float worldY = prim.ResolvedWorldY;
                    float yawRad = prim.ResolvedYawRad;
                    float pitchRad = prim.ResolvedPitchRad;
                    float rollRad = prim.ResolvedRollRad;
                    float len = prim.LengthMeters > 0f ? prim.LengthMeters : 5f;
                    float wid = prim.WidthMeters > 0f ? prim.WidthMeters : len * 0.5f;

                    var profile = _shapeLibrary.GetShape(null, prim.ProfileId);
                    if (profile != null && profile.Name != "_fallback")
                    {
                        var rotation = PresentationMath.FromYawPitchRoll(yawRad, pitchRad, rollRad);

                        PerspectiveShapeRenderer.RenderShape(
                            profile,
                            new Vector2(worldX, worldY),
                            rotation,
                            len,
                            wid,
                            color,
                            exaggerationCoefficient: 0.05f,
                            visualScaleMultiplier: 1.0f,
                            currentCondition: (EntityShapeCondition)prim.ConditionMask,
                            zoom: zoom);
                    }
                    else
                    {
                        var rect = new Rectangle(
                            worldX - len * geomScale * 0.5f,
                            worldY - wid * geomScale * 0.5f,
                            len * geomScale,
                            wid * geomScale);
                        Raylib.DrawRectangleLinesEx(rect, 1f, new Color((byte)255, (byte)0, (byte)255, color.A));
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

                case DebugPrimitiveShape.StructInspector:
                {
                    _imGuiAdapter.Schedule(
                        prim.StructNetworkId,
                        prim.StructSchemaHash,
                        prim.GizmoTypeId,
                        prim.StructAnchor,
                        prim.StructOffsetX,
                        prim.StructOffsetY,
                        prim.SizeMode,
                        prim.StructIsReadOnly != 0);
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

            var v0 = from - perp;
            var v1 = from + perp;
            var v2 = to + perp;
            var v3 = to - perp;

            Rlgl.CheckRenderBatchLimit(6);
            Rlgl.SetTexture(Rlgl.GetTextureIdDefault());
            Rlgl.Begin((int)DrawMode.Triangles);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v0.X, v0.Y);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v1.X, v1.Y);
            Rlgl.Color4ub(endColor.R,   endColor.G,   endColor.B,   endColor.A);   Rlgl.Vertex2f(v2.X, v2.Y);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v0.X, v0.Y);
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

        private static void DrawStyledLine(
            Vector2 start, Vector2 end, float thickness, Color color, LineStyle style, float geomScale)
        {
            if (style == LineStyle.Solid)
            {
                Raylib.DrawLineEx(start, end, thickness, color);
                return;
            }

            float dashLen = style == LineStyle.Dashed ? 8f * geomScale : 3f * geomScale;
            float gapLen  = style == LineStyle.Dashed ? 8f * geomScale : 4f * geomScale;
            var dir = end - start;
            float totalDist = dir.Length();
            if (totalDist < float.Epsilon) return;
            var normDir = dir / totalDist;
            float currentDist = 0f;
            while (currentDist < totalDist)
            {
                float segEndDist = Math.Min(currentDist + dashLen, totalDist);
                var segStart = start + normDir * currentDist;
                var segEnd = start + normDir * segEndDist;
                Raylib.DrawLineEx(segStart, segEnd, thickness, color);
                currentDist += dashLen + gapLen;
            }
        }

        private static void DrawStyledLineGradient(
            Vector2 start, Vector2 end, float thickness, Color startColor, Color endColor, LineStyle style, float geomScale)
        {
            if (style == LineStyle.Solid)
            {
                DrawGradientLine(start, end, thickness, startColor, endColor);
                return;
            }

            float dashLen = style == LineStyle.Dashed ? 8f * geomScale : 3f * geomScale;
            float gapLen  = style == LineStyle.Dashed ? 8f * geomScale : 4f * geomScale;
            var dir = end - start;
            float totalDist = dir.Length();
            if (totalDist < float.Epsilon) return;
            var normDir = dir / totalDist;
            float currentDist = 0f;
            while (currentDist < totalDist)
            {
                float segEndDist = Math.Min(currentDist + dashLen, totalDist);
                float t0 = currentDist / totalDist;
                var segStart = start + normDir * currentDist;
                var segEnd = start + normDir * segEndDist;
                var segCol = new Color(
                    (byte)(startColor.R + (endColor.R - startColor.R) * t0),
                    (byte)(startColor.G + (endColor.G - startColor.G) * t0),
                    (byte)(startColor.B + (endColor.B - startColor.B) * t0),
                    (byte)(startColor.A + (endColor.A - startColor.A) * t0));
                Raylib.DrawLineEx(segStart, segEnd, thickness, segCol);
                currentDist += dashLen + gapLen;
            }
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
        public float PitchRad;
        public float RollRad;
    }
}
