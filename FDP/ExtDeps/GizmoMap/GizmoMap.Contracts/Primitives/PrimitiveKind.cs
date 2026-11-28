using System;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>⭐ CC-14 — what KIND of primitive a <see cref="DebugPrimitive"/> is, as every reader of the frame needs to ask it.</summary>
    public enum PrimitiveClass : byte
    {
        /// <summary>Drawn in the world (or a screen-space shape): line, sphere, box, arrow, text, badge, icon, semantic / MIL-STD shape, filled triangle.</summary>
        Geometry,
        /// <summary>Never drawn: a spatial anchor, a menu / input-capture binding, the layer mask. Routed by shape, not by layer.</summary>
        Meta,
        /// <summary>A screen PANEL (<see cref="DebugPrimitiveShape.StructInspector"/>): drawn by ImGui, no position, exempt from the layer mask.</summary>
        Panel,
        /// <summary>An invisible <see cref="DebugPrimitiveShape.Box2D"/> carrying an anchor id, there only so the hit-test can find the entity.</summary>
        PickTarget,
    }

    /// <summary>⭐ CC-14 / CE-1071 — THE one classifier of "what kind of primitive is this" (DESIGN_Capability_Components §4.4). Lives in the lowest
    /// assembly every reader references, so the buffer, the 2-D renderer, the 3-D triage, the menu pick and the tests ask ONE place; a new meta shape
    /// is ONE line here. Zero allocation. <see cref="Of"/> names every shape explicitly — a new <see cref="DebugPrimitiveShape"/> with no case throws
    /// (and the table rail fails) instead of silently becoming geometry.</summary>
    public static class PrimitiveKind
    {
        /// <summary>The class of <paramref name="p"/>. A pick target is a Box2D, so it is checked before the shape switch.</summary>
        public static PrimitiveClass Of(in DebugPrimitive p)
        {
            if (IsPickTarget(in p)) return PrimitiveClass.PickTarget;
            return ClassOfShape(p.Shape);
        }

        /// <summary>The class of a shape alone (never <see cref="PrimitiveClass.PickTarget"/> — that needs the primitive's fields).</summary>
        public static PrimitiveClass ClassOfShape(DebugPrimitiveShape s)
        {
            switch (s)
            {
                case DebugPrimitiveShape.Line:
                case DebugPrimitiveShape.Sphere:
                case DebugPrimitiveShape.Box2D:
                case DebugPrimitiveShape.Arrow:
                case DebugPrimitiveShape.Text:
                case DebugPrimitiveShape.EntityBadge:
                case DebugPrimitiveShape.Icon:
                case DebugPrimitiveShape.SemanticShape:
                case DebugPrimitiveShape.MilStd2525:
                case DebugPrimitiveShape.FilledTriangle:
                    return PrimitiveClass.Geometry;
                case DebugPrimitiveShape.StructInspector:
                    return PrimitiveClass.Panel;
                case DebugPrimitiveShape.SpatialAnchor:
                case DebugPrimitiveShape.ContextMenuBinding:
                case DebugPrimitiveShape.InputCaptureBinding:
                case DebugPrimitiveShape.MainMenuBinding:
                case DebugPrimitiveShape.LayerControlMask:
                case DebugPrimitiveShape.HudScope:   // ⭐ simulated devices S1d-1 (D-41) — a HUD run's marker: never drawn, protected
                case DebugPrimitiveShape.PanelButton:   // ⭐ SD-S5v (D-122) — a HUD button's hit zone: never drawn, protected
                    return PrimitiveClass.Meta;
                default:
                    throw new ArgumentOutOfRangeException(nameof(s), s, "PrimitiveKind has no class for this shape — add it to ClassOfShape.");
            }
        }

        /// <summary>A shape that is never drawn: the anchor, the three bindings, the layer mask, a HUD scope and a HUD button's hit zone.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMeta(DebugPrimitiveShape s) => s is DebugPrimitiveShape.SpatialAnchor or DebugPrimitiveShape.ContextMenuBinding or DebugPrimitiveShape.InputCaptureBinding
                  or DebugPrimitiveShape.MainMenuBinding or DebugPrimitiveShape.LayerControlMask or DebugPrimitiveShape.HudScope
                  or DebugPrimitiveShape.PanelButton;

        /// <summary>A screen panel (drawn by ImGui, not a map layer).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPanel(DebugPrimitiveShape s) => s == DebugPrimitiveShape.StructInspector;

        /// <summary>⭐ CE-1160 — an invisible <see cref="DebugPrimitiveShape.Box2D"/> with an anchor id, emitted purely so the hit-test finds the entity
        /// (<c>EmitPickBox</c>, <c>DebugPrimitive.MakePickSegment</c>). Never refused by layer: 🔒 user 2026-10-16 — selection and right click must work
        /// whatever debug layers are on.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPickTarget(in DebugPrimitive p)
            => p.Shape == DebugPrimitiveShape.Box2D && p.BoxAnchorId != 0 && p.Color.A == 0 && p.FillColor.A == 0;

        /// <summary>⭐ CE-1071 / CC-16 — a primitive a frame cannot lose: meta (anchors included) and panels. Never refused by layer; on overflow it
        /// evicts a plain primitive.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsProtected(DebugPrimitiveShape s) => IsMeta(s) || IsPanel(s);

        /// <inheritdoc cref="IsProtected(DebugPrimitiveShape)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsProtected(in DebugPrimitive p) => IsProtected(p.Shape);

        /// <summary>Not subject to the layer mask and never the one evicted: <see cref="IsProtected(in DebugPrimitive)"/> or a pick target.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLayerExempt(in DebugPrimitive p) => IsProtected(p.Shape) || IsPickTarget(in p);
    }
}
