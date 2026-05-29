using System.Numerics;
using Fdp.Toolkit.Utility;
using ImGuiNET;

namespace Hrot.Utility.Editor.Curve
{
    /// <summary>
    /// Host-agnostic ImGui widget for editing a UtilityCurve.
    /// Build once; use in both the Utility Editor and the Tuning Console.
    /// No StructEdit dependency (Curve Editor Guide §3 Step 2).
    /// </summary>
    public static class CurveWidget
    {
        private const int SampleCount = 16;

        // ── Public entry point ───────────────────────────────────────────────────

        /// <summary>
        /// Draw the curve editor widget. Returns true if any field in <paramref name="curve"/>
        /// changed this frame.
        /// </summary>
        /// <param name="id">Unique ImGui id string (used with PushID).</param>
        /// <param name="curve">The curve being edited — modified in place on user interaction.</param>
        /// <param name="opts">Layout and feature options.</param>
        public static bool Draw(string id, ref UtilityCurve curve, in CurveWidgetOptions opts)
        {
            bool changed = false;

            ImGui.PushID(id);
            ImGui.BeginGroup();

            // Kind dropdown
            int kindIndex = (int)curve.Kind;
            string[] kindNames = Enum.GetNames<CurveKind>();
            if (ImGui.Combo("Kind", ref kindIndex, kindNames, kindNames.Length))
            {
                curve.Kind = (CurveKind)kindIndex;
                changed = true;
            }

            // Plot canvas
            float plotW = opts.PlotWidth > 0f ? opts.PlotWidth : ImGui.GetContentRegionAvail().X;
            float plotH = opts.PlotHeight;
            Vector2 canvasPos = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##plot", new Vector2(plotW, plotH));
            bool plotHovered = ImGui.IsItemHovered();

            // Collect samples for polyline
            Span<float> samples = stackalloc float[SampleCount];
            ComputeSamples(in curve, SampleCount, samples);

            // Draw background
            var drawList = ImGui.GetWindowDrawList();
            uint bgColor    = ImGui.GetColorU32(ImGuiCol.FrameBg);
            uint lineColor  = ImGui.GetColorU32(ImGuiCol.PlotLines);
            uint handleColor = ImGui.GetColorU32(ImGuiCol.PlotLinesHovered);
            uint cmpColor   = 0xFF606060u; // grey for comparison overlay

            drawList.AddRectFilled(canvasPos, canvasPos + new Vector2(plotW, plotH), bgColor);

            // Draw primary curve polyline
            for (int i = 0; i < SampleCount - 1; i++)
            {
                float x0 = canvasPos.X + (i / (float)(SampleCount - 1)) * plotW;
                float x1 = canvasPos.X + ((i + 1) / (float)(SampleCount - 1)) * plotW;
                float y0 = canvasPos.Y + (1f - samples[i]) * plotH;
                float y1 = canvasPos.Y + (1f - samples[i + 1]) * plotH;
                drawList.AddLine(new Vector2(x0, y0), new Vector2(x1, y1), lineColor, 2f);
            }

            // Draw comparison overlay if requested
            if (opts.ShowComparisonOverlay && opts.ComparisonCurve.HasValue)
            {
                Span<float> cmpSamples = stackalloc float[SampleCount];
                var cmpCurve = opts.ComparisonCurve.Value;
                ComputeSamples(in cmpCurve, SampleCount, cmpSamples);
                for (int i = 0; i < SampleCount - 1; i++)
                {
                    float x0 = canvasPos.X + (i / (float)(SampleCount - 1)) * plotW;
                    float x1 = canvasPos.X + ((i + 1) / (float)(SampleCount - 1)) * plotW;
                    float y0 = canvasPos.Y + (1f - cmpSamples[i]) * plotH;
                    float y1 = canvasPos.Y + (1f - cmpSamples[i + 1]) * plotH;
                    drawList.AddLine(new Vector2(x0, y0), new Vector2(x1, y1), cmpColor, 1f);
                }
            }

            // Draw fixture input marker
            if (opts.FixtureInputX >= 0f)
            {
                float markerX = canvasPos.X + Math.Clamp(opts.FixtureInputX, 0f, 1f) * plotW;
                uint markerColor = 0xFF00FF00u; // green marker
                drawList.AddLine(new Vector2(markerX, canvasPos.Y),
                                 new Vector2(markerX, canvasPos.Y + plotH),
                                 markerColor, 1f);
                float outputY = Evaluate(in curve, opts.FixtureInputX);
                drawList.AddText(new Vector2(markerX + 2f, canvasPos.Y + 2f),
                                 markerColor,
                                 $"y={outputY:F2}");
            }

            // PiecewiseLinear point editor (left-click to add, right-click to delete)
            if (curve.Kind == CurveKind.PiecewiseLinear)
            {
                // Draw existing points as handles
                var pts = curve.Points ?? Array.Empty<PiecewisePoint>();
                for (int i = 0; i < pts.Length; i++)
                {
                    var pt = pts[i];
                    float hx = canvasPos.X + pt.X * plotW;
                    float hy = canvasPos.Y + (1f - pt.Y) * plotH;
                    drawList.AddCircleFilled(new Vector2(hx, hy), 4f, handleColor);

                    // Right-click to delete
                    var hMin = new Vector2(hx - 5f, hy - 5f);
                    var hMax = new Vector2(hx + 5f, hy + 5f);
                    if (ImGui.IsMouseHoveringRect(hMin, hMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    {
                        curve.Points = RemovePiecewisePoint(pts, i);
                        changed = true;
                        break;
                    }
                }

                // Left-click on plot area to add a point
                if (plotHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var mousePos = ImGui.GetMousePos();
                    float nx = Math.Clamp((mousePos.X - canvasPos.X) / plotW, 0f, 1f);
                    float ny = Math.Clamp(1f - (mousePos.Y - canvasPos.Y) / plotH, 0f, 1f);
                    curve.Points = AddPiecewisePoint(curve.Points, nx, ny);
                    changed = true;
                }

                // Show point list as text below plot
                ImGui.Text($"Points: {(curve.Points?.Length ?? 0)}");
            }
            else
            {
                // Numeric fields for m, k, b, c (locked ones are shown disabled)
                changed |= DrawParamField("m", ref curve.M, IsParamEditable(curve.Kind, "m"));
                changed |= DrawParamField("k", ref curve.K, IsParamEditable(curve.Kind, "k"));
                changed |= DrawParamField("b", ref curve.B, IsParamEditable(curve.Kind, "b"));
                changed |= DrawParamField("c", ref curve.C, IsParamEditable(curve.Kind, "c"));
            }

            ImGui.EndGroup();
            ImGui.PopID();

            return changed;
        }

        // ── Internal helpers (testable without an ImGui frame) ───────────────────

        /// <summary>
        /// Evaluate the curve at x.
        /// For non-piecewise kinds: delegates to ResponseCurve.Evaluate, then adds C and clamps.
        /// For PiecewiseLinear: interpolates directly from curve.Points (no catalog side-effect).
        /// </summary>
        internal static float Evaluate(in UtilityCurve curve, float x)
        {
            float raw;

            if (curve.Kind == CurveKind.PiecewiseLinear)
            {
                raw = EvaluatePiecewise(curve.Points, x);
            }
            else
            {
                // Use the actual runtime curve function (architecture §5.3).
                var rc = new ResponseCurve(curve.Kind, curve.M, curve.K, curve.B);
                raw = rc.Evaluate(x);
            }

            // Add C before the final clamp so C acts as a uniform y-shift.
            return Math.Clamp(raw + curve.C, 0f, 1f);
        }

        /// <summary>
        /// Fills <paramref name="output"/> with Evaluate(curve, i/(count-1)) for i in [0, count-1].
        /// </summary>
        internal static void ComputeSamples(in UtilityCurve curve, int count, Span<float> output)
        {
            for (int i = 0; i < count; i++)
            {
                float x = count > 1 ? i / (float)(count - 1) : 0f;
                output[i] = Evaluate(in curve, x);
            }
        }

        /// <summary>
        /// Returns true if the parameter is user-editable for the given CurveKind.
        /// Follows the locked-params column in Editor DD §5.2 exactly.
        /// </summary>
        /// <param name="param">One of "m", "k", "b", "c".</param>
        internal static bool IsParamEditable(CurveKind kind, string param)
        {
            // Editor DD §5.2 handle<->param mapping table:
            // Linear/InverseLinear : handles->m,c; locked: k=1, b from left endpoint  -> m=yes k=no b=NO c=YES
            // Threshold/Step       : handles->b,c; locked: m, k                       -> m=no  k=no b=yes c=yes
            // Bell                 : handles->b,k,c; locked: m                        -> m=no  k=yes b=yes c=yes
            // Logistic             : handles->b,k; locked: m, c                       -> m=no  k=yes b=yes c=no
            // Quadratic/InverseQ   : handles->k,b; locked: m, c                       -> m=no  k=yes b=yes c=no
            // PiecewiseLinear      : none locked (points are the data)                 -> all yes
            return param switch
            {
                "m" => kind is CurveKind.Linear
                              or CurveKind.InverseLinear
                              or CurveKind.PiecewiseLinear,
                "k" => kind is CurveKind.Bell
                              or CurveKind.Logistic
                              or CurveKind.Quadratic
                              or CurveKind.InverseQuadratic
                              or CurveKind.PiecewiseLinear,
                "b" => kind is not (CurveKind.Linear or CurveKind.InverseLinear),
                "c" => kind is CurveKind.Linear or CurveKind.InverseLinear
                              or CurveKind.Threshold or CurveKind.Bell or CurveKind.Step
                              or CurveKind.PiecewiseLinear,
                _   => false,
            };
        }

        /// <summary>
        /// Adds a PiecewisePoint at (x,y), both clamped to [0,1], then x-sorts the array.
        /// Returns the new points array. Input array may be null (creates a new one).
        /// </summary>
        internal static PiecewisePoint[] AddPiecewisePoint(PiecewisePoint[]? existing, float x, float y)
        {
            float cx = Math.Clamp(x, 0f, 1f);
            float cy = Math.Clamp(y, 0f, 1f);
            var newPt = new PiecewisePoint(cx, cy);

            PiecewisePoint[] result;
            if (existing == null || existing.Length == 0)
            {
                result = new[] { newPt };
            }
            else
            {
                result = new PiecewisePoint[existing.Length + 1];
                existing.CopyTo(result, 0);
                result[^1] = newPt;
                Array.Sort(result, static (a, b) => a.X.CompareTo(b.X));
            }

            return result;
        }

        /// <summary>
        /// Removes the point at <paramref name="index"/>. Returns new sorted array.
        /// </summary>
        internal static PiecewisePoint[] RemovePiecewisePoint(PiecewisePoint[] points, int index)
        {
            if (index < 0 || index >= points.Length)
                return points;

            var result = new PiecewisePoint[points.Length - 1];
            for (int i = 0, j = 0; i < points.Length; i++)
            {
                if (i != index)
                    result[j++] = points[i];
            }
            return result;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static float EvaluatePiecewise(PiecewisePoint[]? points, float x)
        {
            if (points == null || points.Length == 0) return 0f;
            if (points.Length == 1) return Math.Clamp(points[0].Y, 0f, 1f);

            // Clamp to endpoints
            if (x <= points[0].X) return points[0].Y;
            if (x >= points[^1].X) return points[^1].Y;

            // Binary search for enclosing segment
            int lo = 0, hi = points.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (points[mid].X <= x) lo = mid; else hi = mid;
            }

            // Linear interpolation within segment
            float t = (x - points[lo].X) / (points[hi].X - points[lo].X);
            return points[lo].Y + t * (points[hi].Y - points[lo].Y);
        }

        private static bool DrawParamField(string label, ref float value, bool editable)
        {
            if (!editable) ImGui.BeginDisabled();
            bool changed = ImGui.DragFloat(label, ref value, 0.01f);
            if (!editable) ImGui.EndDisabled();
            return changed && editable;
        }
    }
}
