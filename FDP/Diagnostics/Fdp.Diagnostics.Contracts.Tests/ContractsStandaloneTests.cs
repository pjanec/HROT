using System.Runtime.InteropServices;
using System.Linq;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests
{
    public class ContractsStandaloneTests
    {
        // SC-GZ041-3: standalone usage of DebugPrimitiveBuffer without Fdp.Toolkits reference.
        [Fact]
        public void SC_GZ041_3_DebugPrimitiveBuffer_StandaloneUsage()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            buffer.DrawLine(
                System.Numerics.Vector3.Zero,
                System.Numerics.Vector3.UnitX,
                new Rgba32(255, 0, 0, 255));
            Assert.Equal(1, buffer.GetFrame().Length);
        }

        // SC-GZ043-1: All == Map2D | Viewport3D | NodeGraph
        [Fact]
        public void SC_GZ043_1_All_EqualsOrOfAllTargets()
        {
            Assert.Equal(PipelineTarget.All,
                PipelineTarget.Map2D | PipelineTarget.Viewport3D | PipelineTarget.NodeGraph);
        }

        // SC-GZ043-2: NodeGraph has byte value 4
        [Fact]
        public void SC_GZ043_2_NodeGraph_HasByteValue4()
        {
            Assert.Equal((byte)4, (byte)PipelineTarget.NodeGraph);
        }

        // SC-GZ043-3: All & NodeGraph != 0
        [Fact]
        public void SC_GZ043_3_All_IncludesNodeGraph()
        {
            Assert.NotEqual(0, (int)(PipelineTarget.All & PipelineTarget.NodeGraph));
        }

        // SC-GZ043-4: All still covers Map2D and Viewport3D
        [Fact]
        public void SC_GZ043_4_All_IncludesMap2D_And_Viewport3D()
        {
            Assert.NotEqual(0, (int)(PipelineTarget.All & PipelineTarget.Map2D));
            Assert.NotEqual(0, (int)(PipelineTarget.All & PipelineTarget.Viewport3D));
        }

        // SC-GZ043-5: DebugPrimitive with TargetView = All has byte pattern 0b00000111
        [Fact]
        public void SC_GZ043_5_DebugPrimitive_All_BitPattern_Is_0x07()
        {
            var prim = new DebugPrimitive();
            prim.TargetView = PipelineTarget.All;
            // TargetView is at FieldOffset(6); PipelineTarget.All = 7 = 0b00000111
            Assert.Equal((byte)0b00000111, (byte)prim.TargetView);
        }

        // ---- GZ050 tests -------------------------------------------------------

        // SC-GZ050-1: New shape enum values have correct integer values.
        [Fact]
        public void SC_GZ050_1_NewShapeValues_HaveCorrectOrdinals()
        {
            Assert.Equal(8,  (int)DebugPrimitiveShape.SemanticShape);
            Assert.Equal(9,  (int)DebugPrimitiveShape.MilStd2525);
            Assert.Equal(10, (int)DebugPrimitiveShape.SpatialAnchor);
        }

        // SC-GZ050-2: DebugPrimitive size is exactly 64 bytes.
        [Fact]
        public void SC_GZ050_2_DebugPrimitive_SizeIs64()
        {
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // SC-GZ050-3: SpatialAnchor payload fields round-trip correctly.
        [Fact]
        public void SC_GZ050_3_SpatialAnchor_FieldsRoundTrip()
        {
            var prim = new DebugPrimitive();
            prim.Shape        = DebugPrimitiveShape.SpatialAnchor;
            prim.NetworkId    = 42L;
            prim.AnchorWorldX = 100f;
            prim.AnchorWorldY = 200f;
            prim.AnchorWorldZ = 10f;
            prim.Heading      = 45f;
            prim.Pitch        = 0f;
            prim.Roll         = 0f;

            Assert.Equal(DebugPrimitiveShape.SpatialAnchor, prim.Shape);
            Assert.Equal(42L,   prim.NetworkId);
            Assert.Equal(100f,  prim.AnchorWorldX);
            Assert.Equal(200f,  prim.AnchorWorldY);
            Assert.Equal(10f,   prim.AnchorWorldZ);
            Assert.Equal(45f,   prim.Heading);
            Assert.Equal(0f,    prim.Pitch);
            Assert.Equal(0f,    prim.Roll);
        }

        // SC-GZ050-4: SemanticShape payload fields round-trip correctly.
        [Fact]
        public void SC_GZ050_4_SemanticShape_FieldsRoundTrip()
        {
            var prim = new DebugPrimitive();
            prim.Shape         = DebugPrimitiveShape.SemanticShape;
            prim.ProfileId     = 0x3400010001000000UL;
            prim.LengthMeters  = 12.5f;
            prim.WidthMeters   = 4.5f;
            prim.ConditionMask = 3u;

            Assert.Equal(DebugPrimitiveShape.SemanticShape, prim.Shape);
            Assert.Equal(0x3400010001000000UL, prim.ProfileId);
            Assert.Equal(12.5f, prim.LengthMeters);
            Assert.Equal(4.5f,  prim.WidthMeters);
            Assert.Equal(3u,    prim.ConditionMask);
        }

        // SC-GZ050-5: Unknown shape value is silently skipped in a renderer loop.
        [Fact]
        public void SC_GZ050_5_UnknownShape_SilentlySkipped()
        {
            var prims = new DebugPrimitive[3];
            prims[0].Shape = DebugPrimitiveShape.Line;
            prims[1].Shape = (DebugPrimitiveShape)11; // future/unknown value
            prims[2].Shape = DebugPrimitiveShape.Sphere;

            int processedCount = 0;
            foreach (var prim in prims)
            {
                switch (prim.Shape)
                {
                    case DebugPrimitiveShape.Line:
                    case DebugPrimitiveShape.Sphere:
                        processedCount++;
                        break;
                    default:
                        continue; // silently skip unrecognized shapes
                }
            }

            Assert.Equal(2, processedCount);
        }

        // SC-GZ050-6 (regression): Existing shape values and size invariant still hold.
        [Fact]
        public void SC_GZ050_6_Regression_ExistingShapesAndSizeUnchanged()
        {
            Assert.Equal(0, (int)DebugPrimitiveShape.Line);
            Assert.Equal(1, (int)DebugPrimitiveShape.Sphere);
            Assert.Equal(2, (int)DebugPrimitiveShape.Box2D);
            Assert.Equal(3, (int)DebugPrimitiveShape.Arrow);
            Assert.Equal(4, (int)DebugPrimitiveShape.Text);
            Assert.Equal(5, (int)DebugPrimitiveShape.EntityBadge);
            Assert.Equal(6, (int)DebugPrimitiveShape.Icon);
            Assert.Equal(7, (int)DebugPrimitiveShape.StructInspector);
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // ---- GZ051 tests -------------------------------------------------------

        // SC-GZ051-1: InspNetworkId field round-trips correctly.
        [Fact]
        public void SC_GZ051_1_InspNetworkId_FieldRoundTrips()
        {
            var prim = new DebugPrimitive();
            prim.Shape         = DebugPrimitiveShape.StructInspector;
            prim.InspNetworkId = 12345L;

            Assert.Equal(12345L, prim.InspNetworkId);
        }

        // SC-GZ051-2: Verified by the build succeeding (InspTargetIndex and InspComponentTypeId
        // no longer exist on DebugPrimitive -- any reference would be a compile error).

        // SC-GZ051-3: StructSchemaHash matches GizmoSettingsRegistry.ComputeHash for a sample type name.
        [Fact]
        public void SC_GZ051_3_StructSchemaHash_MatchesComputeHash()
        {
            uint expected = ComputeHash("MyNamespace.MyType");
            var prim = new DebugPrimitive();
            prim.StructSchemaHash = expected;
            Assert.Equal(expected, prim.StructSchemaHash);
        }

        // SC-GZ051-4: Marshal.SizeOf<DebugPrimitive>() == 64 after field relayout.
        [Fact]
        public void SC_GZ051_4_StructSizeStillIs64()
        {
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // SC-GZ051-5: Remote viewer can reconstruct display label from InspNetworkId and StructSchemaHash
        // without any ECS dependency.
        [Fact]
        public void SC_GZ051_5_DisplayLabel_ConstructableFromStructFields()
        {
            var prim = new DebugPrimitive();
            prim.InspNetworkId  = 99L;
            prim.StructSchemaHash = 0xABCD1234u;

            string label = $"Entity:{prim.InspNetworkId} Schema:{prim.StructSchemaHash:X8}";

            Assert.Equal("Entity:99 Schema:ABCD1234", label);
        }

        // SC-GZ051-6: InspNetworkId is at FieldOffset(24) and StructSchemaHash is at FieldOffset(32).
        [Fact]
        public void SC_GZ051_6_FieldOffsets_AreCorrect()
        {
            int networkIdOffset   = (int)Marshal.OffsetOf<DebugPrimitive>(nameof(DebugPrimitive.InspNetworkId));
            int schemaHashOffset  = (int)Marshal.OffsetOf<DebugPrimitive>(nameof(DebugPrimitive.StructSchemaHash));

            Assert.Equal(24, networkIdOffset);
            Assert.Equal(32, schemaHashOffset);
        }

        // ---- GZ057 tests -------------------------------------------------------

        // SC-GZ057-A: DrawSpatialAnchor emits correct SpatialAnchor primitive.
        [Fact]
        public void DrawSpatialAnchor_EmitsCorrectShape()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 16);
            buffer.DrawSpatialAnchor(networkId: 42L, worldX: 100f, worldY: 200f, worldZ: 5f, headingDeg: 45f);

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);

            var prim = frame[0];
            Assert.Equal(DebugPrimitiveShape.SpatialAnchor, prim.Shape);
            Assert.Equal(42L,  prim.NetworkId);
            Assert.Equal(100f, prim.AnchorWorldX);
            Assert.Equal(200f, prim.AnchorWorldY);
            Assert.Equal(45f,  prim.Heading);
        }

        // SC-GZ057-B: DrawSemanticShape emits correct SemanticShape primitive.
        [Fact]
        public void DrawSemanticShape_EmitsCorrectShape()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 16);
            buffer.DrawSemanticShape(
                networkId:      42L,
                profileId:      0xCAFEUL,
                lengthMeters:   8f,
                widthMeters:    3f,
                conditionMask:  1u);

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);

            var prim = frame[0];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, prim.Shape);
            Assert.Equal(CoordinateSpace.EntityLocal, prim.Space);
            Assert.Equal(42, prim.AnchorIndex);
            Assert.Equal(0xCAFEUL, prim.ProfileId);
            Assert.Equal(8f, prim.LengthMeters);
            Assert.Equal(1u, prim.ConditionMask);
        }

        // SC-PHASE5-A: DrawEntitySphere emits sphere primitive with entity anchor.
        [Fact]
        public void DrawEntitySphere_SetsAnchorAndShape()
        {
            var buffer = new DebugPrimitiveBuffer(4);
            var entity = new Fdp.Core.Entity(7, 3);
            buffer.DrawEntitySphere(entity, System.Numerics.Vector3.Zero, 5f, new Rgba32(255, 0, 0, 255));
            var frames = buffer.GetFrame();
            Assert.Equal(1, frames.Length);
            Assert.Equal(DebugPrimitiveShape.Sphere, frames[0].Shape);
            var token = frames[0].GetPickToken();
            Assert.True(token.IsValid);
            Assert.Equal(entity, token.Target);
        }

        // ---- SC-FONT-TEXT tests (font size in Text primitive) -------------------

        // SC-FONT-TEXT-1: DrawText with fontSizePx=9 produces ThicknessU16==9 and Shape==Text.
        [Fact]
        public void DrawText_WithFontSizePx_StoresSizeInThicknessU16()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawText(0f, 0f, new Fdp.Core.FixedString32("hi"), new Rgba32(255, 255, 255, 255),
                fontSizePx: 9f);

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);
            var prim = frame[0];
            Assert.Equal(DebugPrimitiveShape.Text, prim.Shape);
            Assert.Equal((ushort)9, prim.ThicknessU16);
        }

        // SC-FONT-TEXT-2: DrawText without fontSizePx leaves ThicknessU16==0 (renderer uses default 13px).
        [Fact]
        public void DrawText_WithoutFontSizePx_LeavesThicknessU16Zero()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawText(0f, 0f, new Fdp.Core.FixedString32("hi"), new Rgba32(255, 255, 255, 255));

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal((ushort)0, frame[0].ThicknessU16);
        }

        // SC-FONT-TEXT-3: DrawTextLong with fontSizePx=9 stores size in ThicknessU16.
        [Fact]
        public void DrawTextLong_WithFontSizePx_StoresSizeInThicknessU16()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawTextLong(0f, 0f, "hello world long text", new Rgba32(255, 255, 255, 255),
                fontSizePx: 9f);

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);
            var prim = frame[0];
            Assert.Equal(DebugPrimitiveShape.Text, prim.Shape);
            Assert.Equal((ushort)9, prim.ThicknessU16);
        }

        // SC-FONT-TEXT-4: DrawTextLong without fontSizePx leaves ThicknessU16==0.
        [Fact]
        public void DrawTextLong_WithoutFontSizePx_LeavesThicknessU16Zero()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawTextLong(0f, 0f, "hello", new Rgba32(255, 255, 255, 255));

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal((ushort)0, frame[0].ThicknessU16);
        }

        // SC-FONT-TEXT-5: MakeText with fontSizePx=9 stores value in ThicknessU16.
        [Fact]
        public void MakeText_WithFontSizePx_StoresSizeInThicknessU16()
        {
            var prim = DebugPrimitive.MakeText(1f, 2f,
                new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32(),
                new Rgba32(255, 0, 0, 255),
                fontSizePx: 9f);

            Assert.Equal(DebugPrimitiveShape.Text, prim.Shape);
            Assert.Equal((ushort)9, prim.ThicknessU16);
        }

        // ---- S6: LineOffsetPx, the signed alias at offset 12 --------------------
        // 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6 (S6). Added into this suite rather than a new
        //    class -- SC-FONT-TEXT-* already owns the text primitive (R-142 ④).

        // SC-FONT-TEXT-6: the editor's 3-line entity label stacking survives S6. The four live
        // offsets are EntityEditorLabelGizmo.cs:57/:77/:82/:96 => -16, -30, -30, -44 px.
        // ⛔ RED-PROOF SHAPE: reintroduce `unchecked((ushort)(short)lineOffsetPx)` on write while the
        //    reader uses the signed alias and these come back as 65520 / 65506 / 65492.
        [Theory]
        [InlineData(-16f)]
        [InlineData(-30f)]
        [InlineData(-44f)]
        [InlineData(0f)]
        [InlineData(12f)]
        public void SC_FONT_TEXT_6_MakeText_LineOffsetPx_RoundTripsSigned(float offsetPx)
        {
            var prim = DebugPrimitive.MakeText(0f, 0f,
                new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("L"),
                new Rgba32(255, 255, 255, 255),
                lineOffsetPx: offsetPx);

            Assert.Equal((short)offsetPx, prim.LineOffsetPx);
        }

        // SC-FONT-TEXT-6b: DrawText goes through the same slot, so a buffered line stacks identically.
        [Fact]
        public void SC_FONT_TEXT_6b_DrawText_LineOffsetPx_RoundTripsSigned()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawText(0f, 0f, "line2", new Rgba32(255, 255, 255, 255), lineOffsetPx: -30f);
            buffer.DrawTextLong(0f, 0f, "a longer interned line", new Rgba32(255, 255, 255, 255),
                lineOffsetPx: -44f);

            var frame = buffer.GetFrame();
            Assert.Equal(2, frame.Length);
            Assert.Equal((short)-30, frame[0].LineOffsetPx);
            Assert.Equal((short)-44, frame[1].LineOffsetPx);
        }

        // SC-FONT-TEXT-6c: the alias IS AnchorGeneration -- the same two bytes at offset 12, read
        // signed vs unsigned. ⛔ If these ever stop aliasing, every Text primitive silently loses its
        // stacking and every ECS-anchored primitive silently loses its generation.
        [Fact]
        public void SC_FONT_TEXT_6c_LineOffsetPx_AliasesAnchorGeneration_AtOffset12()
        {
            var p = default(DebugPrimitive);
            p.LineOffsetPx = -16;
            Assert.Equal(unchecked((ushort)(short)-16), p.AnchorGeneration);

            p.AnchorGeneration = 7;              // an ECS generation
            Assert.Equal((short)7, p.LineOffsetPx);

            // ...and neither may grow the struct: 64 bytes is a DDS-marshalled invariant (C1).
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // ---- S7: the EntityLocal anchor key is 32 bits wide ---------------------
        // 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6 (S7); CE-259z.

        // SC-GZ-ANCHOR32-1: DrawSemanticShape puts the NETWORK id in the anchor-key slot, which is
        // what DebugPrimitiveRenderer2D:105 probes the SpatialAnchor cache with.
        [Fact]
        public void SC_GZ_ANCHOR32_1_DrawSemanticShape_AnchorKeyIsTheNetworkId()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawSemanticShape(networkId: 90210L, profileId: 7UL);

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(DebugPrimitiveShape.SemanticShape, frame[0].Shape);
            Assert.Equal(CoordinateSpace.EntityLocal, frame[0].Space);
            Assert.Equal(90210, frame[0].AnchorIndex);
        }

        // SC-GZ-ANCHOR32-2: THE HARD LIMIT, pinned. The key slot is `int`, so int.MaxValue is the
        // largest id that survives; the disjoint TOOL range sits ABOVE it, which is why no tool may
        // emit an EntityLocal primitive. ⛔ This is a CONSTRAINT rail, not a bug rail: the struct is
        // 64 DDS-marshalled bytes and SemanticShape's payload union is full, so there is no wider slot
        // to move to. If someone ever finds one, this rail is the thing that should change.
        [Fact]
        public void SC_GZ_ANCHOR32_2_EntityLocalAnchorKey_IsThirtyTwoBits()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 4);
            buffer.DrawSemanticShape(networkId: int.MaxValue, profileId: 1UL);
            Assert.Equal(int.MaxValue, buffer.GetFrame()[0].AnchorIndex);

            // GlobalGizmoManager.ToolAnchorIdBase, restated here because Fdp.Toolkits is deliberately
            // not referenced by this project (see the csproj).
            const long toolAnchorIdBase = 1L << 40;
            Assert.True(toolAnchorIdBase > int.MaxValue,
                "the tool range must stay OUTSIDE the 32-bit EntityLocal anchor key");
        }

        // ---- Helpers -----------------------------------------------------------

        // FNV-1a 32-bit hash -- mirrors GizmoSettingsRegistry.ComputeHash.
        private static uint ComputeHash(string name)
        {
            uint h = 2166136261u;
            foreach (char c in name)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        // ---- GZ064 tests -------------------------------------------------------

        // SC-GZ064-1: DebugPrimitive remains exactly 64 bytes after adding GizmoTypeId.
        [Fact]
        public void SC_GZ064_1_DebugPrimitive_SizeIs64()
        {
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // SC-GZ064-2: GizmoPickToken has GizmoTypeId field; default is 0.
        [Fact]
        public void SC_GZ064_2_GizmoPickToken_HasGizmoTypeIdField_DefaultIsZero()
        {
            var token = default(GizmoPickToken);
            Assert.Equal(0u, token.GizmoTypeId);
        }

        // SC-GZ064-4: PickToken has GizmoTypeId field of uint.
        [Fact]
        public void SC_GZ064_4_PickToken_HasGizmoTypeIdField()
        {
            var token = new PickToken { GizmoTypeId = 42u };
            Assert.Equal(42u, token.GizmoTypeId);
        }
    }
}
