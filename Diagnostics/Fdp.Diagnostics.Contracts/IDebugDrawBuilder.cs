extern alias GizmoMapContracts;

using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // ECS-extended draw builder interface. Contains all non-ECS methods from
    // GizmoMapContracts::IGizmoDrawBuilder plus FDP-specific entity-coupled draw methods.
    // NOTE: Does not inherit IGizmoDrawBuilder directly to avoid FixedString32 type conflict
    // (Fdp.Core.FixedString32 vs GizmoMap.Contracts.FixedString32). DebugPrimitiveBuffer
    // satisfies both interfaces at the class level via explicit IGizmoDrawBuilder implementation.
    public interface IDebugDrawBuilder
    {
        void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid);

        void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid);

        void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid);

        void DrawBox2D(
            Vector2 center, Vector2 extents, Rgba32 color,
            float angleDeg = 0f,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid,
            long anchorId = 0,
            ushort subElementId = 0)
        {
        }

        void DrawArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0);

        void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0);

        // Interns full managed string for text exceeding 31 chars; emits StringHash != 0.
        // The first 31 chars are stored inline as a preview fallback.
        // NOTE: allocates on the intern map registration path (cold path only);
        // subsequent calls with the same text hit the map and allocate nothing.
        void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0);

        void DrawEntityBadge(
            Entity target, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All);

        void DrawEntityLocal(
            Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0);

        void DrawEntityLocalInteractive(
            Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, ushort subElementId,
            float thickness = 1f, byte layer = 0);

        /// <summary>
        /// Called once per frame before gizmo systems execute. Advances the persistence clock
        /// by <paramref name="deltaTime"/>, evicts expired persistent primitives, clears the
        /// transient buffer, and re-injects surviving persistent primitives.
        /// Default no-op for implementations that do not support persistence.
        /// </summary>
        void EndFrame(float deltaTime) { }

        // GZ057: entity presentation primitives.

        /// <summary>
        /// Emits a <see cref="DebugPrimitiveShape.SpatialAnchor"/> primitive carrying the
        /// pre-resolved world position and heading for a networked entity.
        /// Must be emitted BEFORE the corresponding <see cref="DrawSemanticShape"/> call
        /// with the same <paramref name="networkId"/>.
        /// Default no-op so existing stub implementations compile without changes.
        /// </summary>
        void DrawSpatialAnchor(
            long  networkId,
            float worldX,
            float worldY,
            float worldZ,
            float headingDeg,
            float pitchDeg = 0f,
            float rollDeg = 0f,
            byte  layer = 0) { }

        /// <summary>
        /// Emits a <see cref="DebugPrimitiveShape.SemanticShape"/> primitive in
        /// <see cref="CoordinateSpace.EntityLocal"/>, linked to a SpatialAnchor via
        /// <c>AnchorIndex = (int)networkId</c>.
        /// Default no-op so existing stub implementations compile without changes.
        /// </summary>
        void DrawSemanticShape(
            long   networkId,
            ulong  profileId,
            float  lengthMeters  = 0f,
            float  widthMeters   = 0f,
            uint   conditionMask = 0,
            byte   layer         = 0) { }

        /// <summary>
        /// Emits a <see cref="DebugPrimitiveShape.ContextMenuBinding"/> meta-primitive that
        /// associates a context-menu JSON string with a networked entity.
        /// The JSON is interned in the buffer's <c>StringInternMap</c> (cold-path only);
        /// subsequent calls with identical JSON allocate nothing.
        /// The string hash is transmitted to the IG terminal via the gizmo-stream and
        /// resolved back to JSON via the shared <c>StringInternBatch</c> DDS topic.
        /// Default no-op so existing stub implementations compile without changes.
        /// </summary>
        void DrawContextMenuBinding(long networkId, string menuJson) { }

        /// <summary>
        /// Emits a raw <see cref="DebugPrimitive"/> directly into the buffer without
        /// any shape-specific translation. Used by the interaction manager to inject
        /// <see cref="DebugPrimitiveShape.InputCaptureBinding"/> meta-primitives.
        /// Default no-op so existing stub implementations compile without changes.
        /// </summary>
        void EmitRaw(in DebugPrimitive prim) { }

        /// <summary>
        /// Emits a world-space sphere primitive anchored to <paramref name="anchor"/>.
        /// The sphere is hit-testable by <c>DebugGizmoLayer</c> -- clicking it triggers
        /// <c>GizmoInteractionStartedEvent { Token.Target = anchor }</c>.
        /// Default no-op so existing stub implementations compile without changes.
        /// </summary>
        void DrawEntitySphere(
            Entity anchor,
            Vector3 worldCenter,
            float   radius,
            Rgba32  color,
            byte    layer = 0) { }

        /// <summary>
        /// Emits a <see cref="DebugPrimitiveShape.MainMenuBinding"/> meta-primitive that
        /// injects items into the host application's main menu bar.
        /// The JSON is interned in the buffer's <c>StringInternMap</c> (cold-path only).
        /// Default no-op so existing stub implementations compile without changes.
        /// </summary>
        void DrawMainMenuBinding(string menuJson) { }
    }
}
