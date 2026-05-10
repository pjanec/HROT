using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Stateless gizmo projector that renders entities as rectangular polyline silhouettes
    /// with exaggerated perspective in the scenario editor.
    ///
    /// The outline uses a fixed footprint (in screen pixels) aligned to the entity's
    /// facing direction and stretched forward by <see cref="PerspectiveExaggeration"/> so
    /// that the heading of even small entities is immediately apparent at typical editor
    /// zoom levels.
    /// </summary>
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
    public sealed class EntityEditorPolylineGizmo : IStatelessGizmo
    {
        // Outline dimensions in screen pixels.
        private const float HalfWidth  = 6f;
        private const float HalfLength = 10f;

        // Length multiplier applied along the entity's forward direction.
        private const float PerspectiveExaggeration = 2.5f;

        private static readonly Rgba32 OutlineColor = new Rgba32(100, 220, 255, 200);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SimTransform>(entity)) return;

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var pos = tf.Position;

            float yawRad = SimMath.ExtractYaw(tf.Rotation);

            float cosA = MathF.Cos(yawRad);
            float sinA = MathF.Sin(yawRad);

            // Forward and right unit vectors in the XY plane.
            var fwd   = new Vector2(cosA,  sinA);
            var right = new Vector2(-sinA, cosA);

            // Corner offsets: forward half uses exaggerated length; rear half uses normal.
            float fwdLen  = HalfLength * PerspectiveExaggeration;
            float rearLen = HalfLength;

            var fl = new Vector2(pos.X, pos.Y) + fwd * fwdLen  - right * HalfWidth;  // front-left
            var fr = new Vector2(pos.X, pos.Y) + fwd * fwdLen  + right * HalfWidth;  // front-right
            var rl = new Vector2(pos.X, pos.Y) - fwd * rearLen - right * HalfWidth;  // rear-left
            var rr = new Vector2(pos.X, pos.Y) - fwd * rearLen + right * HalfWidth;  // rear-right

            // Emit the four sides of the silhouette rectangle.
            draw.DrawLine(ToVec3(fl), ToVec3(fr), OutlineColor, 1f, SizeMode.ScreenPixels);
            draw.DrawLine(ToVec3(fr), ToVec3(rr), OutlineColor, 1f, SizeMode.ScreenPixels);
            draw.DrawLine(ToVec3(rr), ToVec3(rl), OutlineColor, 1f, SizeMode.ScreenPixels);
            draw.DrawLine(ToVec3(rl), ToVec3(fl), OutlineColor, 1f, SizeMode.ScreenPixels);

            // Heading tick: short line from centre to front midpoint.
            var centre   = new Vector2(pos.X, pos.Y);
            var frontMid = new Vector2(pos.X, pos.Y) + fwd * fwdLen;
            draw.DrawLine(ToVec3(centre), ToVec3(frontMid), OutlineColor, 1f, SizeMode.ScreenPixels);
        }

        private static Vector3 ToVec3(Vector2 v) => new Vector3(v.X, v.Y, 0f);
    }
}
