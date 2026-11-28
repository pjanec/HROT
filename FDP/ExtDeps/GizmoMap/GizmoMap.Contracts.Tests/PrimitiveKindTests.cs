using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace GizmoMap.Contracts.Tests
{
    /// <summary>⭐ CC-14 (docs/DESIGN_Capability_Components.md §4.4) — the ONE classifier of "what kind of primitive is this".</summary>
    public class PrimitiveKindTests
    {
        // The expected class of every shape, written out by hand: a new DebugPrimitiveShape value has no row here and fails the rail below.
        private static readonly Dictionary<DebugPrimitiveShape, PrimitiveClass> Expected = new()
        {
            [DebugPrimitiveShape.Line]                = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.Sphere]              = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.Box2D]               = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.Arrow]               = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.Text]                = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.EntityBadge]         = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.Icon]                = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.StructInspector]     = PrimitiveClass.Panel,
            [DebugPrimitiveShape.SemanticShape]       = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.MilStd2525]          = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.SpatialAnchor]       = PrimitiveClass.Meta,
            [DebugPrimitiveShape.ContextMenuBinding]  = PrimitiveClass.Meta,
            [DebugPrimitiveShape.InputCaptureBinding] = PrimitiveClass.Meta,
            [DebugPrimitiveShape.MainMenuBinding]     = PrimitiveClass.Meta,
            [DebugPrimitiveShape.LayerControlMask]    = PrimitiveClass.Meta,
            [DebugPrimitiveShape.FilledTriangle]      = PrimitiveClass.Geometry,
            [DebugPrimitiveShape.HudScope]            = PrimitiveClass.Meta,       // ⭐ simulated devices S1d-1 — a HUD run's marker, protected
            [DebugPrimitiveShape.PanelButton]         = PrimitiveClass.Meta,       // ⭐ SD-S5v — a HUD button's hit zone, protected
        };

        [Fact]
        public void EveryShape_HasAnExpectedClass_AndTheClassifierAgrees()
        {
            foreach (DebugPrimitiveShape shape in Enum.GetValues(typeof(DebugPrimitiveShape)))
            {
                Assert.True(Expected.TryGetValue(shape, out var want), $"{shape} has no row in the expected table — a new shape must be classified");
                Assert.Equal(want, PrimitiveKind.ClassOfShape(shape));
                var p = default(DebugPrimitive);
                p.Shape = shape;
                Assert.Equal(want, PrimitiveKind.Of(in p));
                Assert.Equal(want == PrimitiveClass.Meta, PrimitiveKind.IsMeta(shape));
                Assert.Equal(want == PrimitiveClass.Panel, PrimitiveKind.IsPanel(shape));
                Assert.Equal(want is PrimitiveClass.Meta or PrimitiveClass.Panel, PrimitiveKind.IsProtected(shape));
            }
        }

        [Fact]
        public void AShapeWithNoCase_Throws_InsteadOfBecomingGeometry()
            => Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveKind.ClassOfShape((DebugPrimitiveShape)200));

        [Fact]
        public void APickTarget_IsABox2DWithAnAnchorAndBothColoursInvisible_AndIsLayerExemptButNotProtected()
        {
            var pick = DebugPrimitive.MakeBox2D(new Vector2(1f, 1f), new Vector2(2f, 2f), new Rgba32(0, 0, 0, 0), layer: 0, anchorId: 9);
            var drawn = DebugPrimitive.MakeBox2D(new Vector2(1f, 1f), new Vector2(2f, 2f), new Rgba32(255, 0, 0, 255), layer: 0, anchorId: 9);
            var noAnchor = DebugPrimitive.MakeBox2D(new Vector2(1f, 1f), new Vector2(2f, 2f), new Rgba32(0, 0, 0, 0), layer: 0, anchorId: 0);

            Assert.Equal(PrimitiveClass.PickTarget, PrimitiveKind.Of(in pick));
            Assert.True(PrimitiveKind.IsPickTarget(in pick));
            Assert.True(PrimitiveKind.IsLayerExempt(in pick));
            Assert.False(PrimitiveKind.IsProtected(in pick));
            Assert.Equal(PrimitiveClass.Geometry, PrimitiveKind.Of(in drawn));
            Assert.Equal(PrimitiveClass.Geometry, PrimitiveKind.Of(in noAnchor));
            Assert.False(PrimitiveKind.IsLayerExempt(in drawn));
        }

        [Fact]
        public void AnAnchor_IsProtected_CC16()
        {
            var a = DebugPrimitive.MakeSpatialAnchor(1, 0f, 0f, 0f, 0f);
            Assert.True(PrimitiveKind.IsProtected(in a));
            Assert.True(PrimitiveKind.IsLayerExempt(in a));
        }
    }
}
