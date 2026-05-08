using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate: FixedString32 used in DrawText/DrawEntityBadge calls refers to Fdp.Core type.
using FixedString32 = Fdp.Core.FixedString32;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // --------------------------------------------------------------------------
    // SC-GZ001 — Color and primitive type definitions
    // --------------------------------------------------------------------------

    public class Rgba32Tests
    {
        [Fact]
        public void Rgba32_HasSize4()
        {
            Assert.Equal(4, Marshal.SizeOf<Rgba32>());
        }

        [Fact]
        public void Rgba32_Constructor_SetsChannels()
        {
            var c = new Rgba32(10, 20, 30, 200);
            Assert.Equal(10,  c.R);
            Assert.Equal(20,  c.G);
            Assert.Equal(30,  c.B);
            Assert.Equal(200, c.A);
        }

        [Fact]
        public void Rgba32_Constructor_DefaultAlpha255()
        {
            var c = new Rgba32(1, 2, 3);
            Assert.Equal(255, c.A);
        }

        [Fact]
        public void Rgba32_Red_IsCorrect()
        {
            var c = Rgba32.Red;
            Assert.Equal(255, c.R);
            Assert.Equal(0,   c.G);
            Assert.Equal(0,   c.B);
            Assert.Equal(255, c.A);
        }

        [Fact]
        public void Rgba32_Green_IsCorrect()
        {
            var c = Rgba32.Green;
            Assert.Equal(0,   c.R);
            Assert.Equal(255, c.G);
            Assert.Equal(0,   c.B);
            Assert.Equal(255, c.A);
        }

        [Fact]
        public void Rgba32_Yellow_IsCorrect()
        {
            var c = Rgba32.Yellow;
            Assert.Equal(255, c.R);
            Assert.Equal(255, c.G);
            Assert.Equal(0,   c.B);
            Assert.Equal(255, c.A);
        }

        [Fact]
        public void Rgba32_Transparent_HasZeroAlpha()
        {
            var c = Rgba32.Transparent;
            Assert.Equal(0, c.A);
        }

        [Fact]
        public void Rgba32_Equality_SameValuesAreEqual()
        {
            var a = new Rgba32(1, 2, 3, 4);
            var b = new Rgba32(1, 2, 3, 4);
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Rgba32_Inequality_DifferentValuesNotEqual()
        {
            var a = new Rgba32(1, 2, 3, 4);
            var b = new Rgba32(1, 2, 3, 5);
            Assert.NotEqual(a, b);
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void PipelineTarget_None_IsZero()
        {
            Assert.Equal(0, (int)PipelineTarget.None);
        }

        [Fact]
        public void PipelineTarget_All_CombinesMap2DViewport3DAndNodeGraph()
        {
            Assert.Equal(PipelineTarget.All, PipelineTarget.Map2D | PipelineTarget.Viewport3D | PipelineTarget.NodeGraph);
        }

        [Fact]
        public void DebugPrimitiveShape_ValuesAreCorrect()
        {
            Assert.Equal(0, (int)DebugPrimitiveShape.Line);
            Assert.Equal(4, (int)DebugPrimitiveShape.Text);
            Assert.Equal(7, (int)DebugPrimitiveShape.ComponentInspector);
        }

        [Fact]
        public void ScreenAnchor_ValuesAreCorrect()
        {
            Assert.Equal(0, (int)ScreenAnchor.TopLeft);
            Assert.Equal(3, (int)ScreenAnchor.Center);
            Assert.Equal(6, (int)ScreenAnchor.BottomRight);
        }

        [Fact]
        public void CoordinateSpace_ValuesAreCorrect()
        {
            Assert.Equal(0, (int)CoordinateSpace.World);
            Assert.Equal(1, (int)CoordinateSpace.Screen);
            Assert.Equal(2, (int)CoordinateSpace.EntityLocal);
        }

        [Fact]
        public void SizeMode_ValuesAreCorrect()
        {
            Assert.Equal(0, (int)SizeMode.WorldMeters);
            Assert.Equal(1, (int)SizeMode.ScreenPixels);
        }

        [Fact]
        public void PickToken_IsValid_FalseWhenTargetIsNull()
        {
            var t = default(PickToken);
            Assert.False(t.IsValid);
        }

        [Fact]
        public void PickToken_IsValid_TrueForNonNullEntity()
        {
            var t = new PickToken { Target = new Entity(1, 1), SubElementId = 42 };
            Assert.True(t.IsValid);
        }
    }

    // --------------------------------------------------------------------------
    // SC-GZ002 — DebugPrimitive layout and factories
    // --------------------------------------------------------------------------

    public class DebugPrimitiveTests
    {
        [Fact]
        public void DebugPrimitive_HasSize64()
        {
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        [Fact]
        public void MakeLine_SetsShapeAndColor()
        {
            var p = DebugPrimitive.MakeLine(
                new Vector3(1, 2, 3), new Vector3(4, 5, 6), Rgba32.Red);

            Assert.Equal(DebugPrimitiveShape.Line, p.Shape);
            Assert.Equal(Rgba32.Red, p.Color);
        }

        [Fact]
        public void MakeLine_DefaultSolidColor_StartEqualsEndColor()
        {
            var p = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Rgba32.Green);
            Assert.Equal(Rgba32.Green, p.EndColor);
        }

        [Fact]
        public void MakeLine_Thickness_RoundTrip()
        {
            var p = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Rgba32.White, thickness: 2.5f);
            Assert.Equal(25, p.ThicknessU16);
            Assert.Equal(2.5f, p.Thickness, precision: 4);
        }

        [Fact]
        public void MakeLine_SetsLineStartAndEnd()
        {
            var from = new Vector3(1, 0, 0);
            var to   = new Vector3(0, 1, 0);
            var p    = DebugPrimitive.MakeLine(from, to, Rgba32.White);

            Assert.Equal(from, p.LineStart);
            Assert.Equal(to,   p.LineEnd);
        }

        [Fact]
        public void MakeSphere_SetsShapeAndPayload()
        {
            var center = new Vector3(10, 20, 30);
            var p      = DebugPrimitive.MakeSphere(center, 5f, Rgba32.Yellow);

            Assert.Equal(DebugPrimitiveShape.Sphere, p.Shape);
            Assert.Equal(center, p.SphereCenter);
            Assert.Equal(5f, p.SphereRadius);
        }

        [Fact]
        public void MakeArrow_SetsShapeAndPayload()
        {
            var from = new Vector3(0, 0, 0);
            var to   = new Vector3(1, 0, 0);
            var p    = DebugPrimitive.MakeArrow(from, to, Rgba32.Red, headSize: 2f);

            Assert.Equal(DebugPrimitiveShape.Arrow, p.Shape);
            Assert.Equal(from, p.ArrowFrom);
            Assert.Equal(to,   p.ArrowTo);
            Assert.Equal(2f,   p.ArrowHeadSize);
        }

        [Fact]
        public void MakeText_SetsShapeAndPosition()
        {
            var text = new FixedString32("hello");
            var p    = DebugPrimitive.MakeText(10f, 20f, text, Rgba32.White);

            Assert.Equal(DebugPrimitiveShape.Text, p.Shape);
            Assert.Equal(10f, p.TextX);
            Assert.Equal(20f, p.TextY);
            Assert.Equal("hello", p.TextContent.ToString());
        }

        [Fact]
        public void MakeText_InlineMode_StringHashIsZero()
        {
            var p = DebugPrimitive.MakeText(0, 0, new FixedString32("hi"), Rgba32.White);
            Assert.Equal(0u, p.StringHash);
        }

        [Fact]
        public void AnchorProperty_ReconstructsEntity()
        {
            var p = default(DebugPrimitive);
            p.AnchorIndex      = 7;
            p.AnchorGeneration = 3;
            var anchor = p.GetAnchor();
            Assert.Equal(7, anchor.Index);
            Assert.Equal(3, anchor.Generation);
        }

        [Fact]
        public void BadgeRichText_AliasesTextContent()
        {
            var p         = default(DebugPrimitive);
            var badge     = new FixedString32("badge");
            p.BadgeRichText = badge;
            Assert.Equal("badge", p.TextContent.ToString());
            Assert.Equal("badge", p.BadgeRichText.ToString());
        }

        [Fact]
        public void IconAtlasCoord_AliasesTextContent()
        {
            var p       = default(DebugPrimitive);
            var coord   = new FixedString32("atlas:1,2");
            p.IconAtlasCoord = coord;
            Assert.Equal("atlas:1,2", p.TextContent.ToString());
            Assert.Equal("atlas:1,2", p.IconAtlasCoord.ToString());
        }

        // Offset isolation: verify that writing to the Line payload does not
        // corrupt the header, and vice versa.
        [Fact]
        public void PayloadIsolation_LineDoesNotCorruptHeader()
        {
            var p = DebugPrimitive.MakeLine(
                new Vector3(1, 2, 3), new Vector3(4, 5, 6),
                Rgba32.Red, thickness: 1.5f, target: PipelineTarget.Map2D, layer: 3);

            // Header checks
            Assert.Equal(DebugPrimitiveShape.Line, p.Shape);
            Assert.Equal(Rgba32.Red,             p.Color);
            Assert.Equal(PipelineTarget.Map2D,   p.TargetView);
            Assert.Equal(3,                      p.DebugLayer);
            Assert.Equal(15,                     p.ThicknessU16);  // 1.5 * 10

            // Payload checks
            Assert.Equal(new Vector3(1, 2, 3), p.LineStart);
            Assert.Equal(new Vector3(4, 5, 6), p.LineEnd);
        }

        [Fact]
        public void PayloadIsolation_SphereDoesNotCorruptHeader()
        {
            var p = DebugPrimitive.MakeSphere(
                new Vector3(5, 6, 7), 3.0f, Rgba32.Green, PipelineTarget.Viewport3D, layer: 1);

            Assert.Equal(DebugPrimitiveShape.Sphere,  p.Shape);
            Assert.Equal(Rgba32.Green,               p.Color);
            Assert.Equal(PipelineTarget.Viewport3D,  p.TargetView);
            Assert.Equal(1,                          p.DebugLayer);
            Assert.Equal(new Vector3(5, 6, 7),       p.SphereCenter);
            Assert.Equal(3.0f,                       p.SphereRadius);
        }
    }

    // --------------------------------------------------------------------------
    // SC-GZ003 — DebugPrimitiveBuffer
    // --------------------------------------------------------------------------

    public class DebugPrimitiveBufferTests
    {
        [Fact]
        public void Buffer_Empty_GetFrameReturnsEmpty()
        {
            var buf = new DebugPrimitiveBuffer(16);
            Assert.Equal(0, buf.GetFrame().Length);
        }

        [Fact]
        public void Buffer_DrawLine_AppearsInFrame()
        {
            var buf = new DebugPrimitiveBuffer(16);
            buf.DrawLine(Vector3.Zero, Vector3.UnitX, Rgba32.Red);

            var frame = buf.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(DebugPrimitiveShape.Line, frame[0].Shape);
            Assert.Equal(Rgba32.Red,               frame[0].Color);
        }

        [Fact]
        public void Buffer_DrawLineGradient_HasDifferentEndColor()
        {
            var buf = new DebugPrimitiveBuffer(16);
            buf.DrawLineGradient(Vector3.Zero, Vector3.UnitX, Rgba32.Red, Rgba32.Green);

            var frame = buf.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(Rgba32.Red,   frame[0].Color);
            Assert.Equal(Rgba32.Green, frame[0].EndColor);
        }

        [Fact]
        public void Buffer_DrawSphere_AppearsInFrame()
        {
            var buf = new DebugPrimitiveBuffer(16);
            buf.DrawSphere(new Vector3(1, 2, 3), 5f, Rgba32.Yellow);

            var frame = buf.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(DebugPrimitiveShape.Sphere, frame[0].Shape);
            Assert.Equal(5f,                         frame[0].SphereRadius);
        }

        [Fact]
        public void Buffer_DrawArrow_AppearsInFrame()
        {
            var buf = new DebugPrimitiveBuffer(16);
            buf.DrawArrow(Vector3.Zero, Vector3.UnitY, Rgba32.White, headSize: 2f);

            var frame = buf.GetFrame();
            Assert.Equal(DebugPrimitiveShape.Arrow, frame[0].Shape);
            Assert.Equal(2f,                        frame[0].ArrowHeadSize);
        }

        [Fact]
        public void Buffer_DrawText_AppearsInFrame()
        {
            var buf = new DebugPrimitiveBuffer(16);
            buf.DrawText(5f, 10f, new Fdp.Core.FixedString32("test"), Rgba32.White);

            var frame = buf.GetFrame();
            Assert.Equal(DebugPrimitiveShape.Text, frame[0].Shape);
            Assert.Equal(5f,                       frame[0].TextX);
            Assert.Equal(10f,                      frame[0].TextY);
            Assert.Equal("test",                   frame[0].TextContent.ToString());
        }

        [Fact]
        public void Buffer_DrawEntityLocal_SetsAnchorAndSpace()
        {
            var buf    = new DebugPrimitiveBuffer(16);
            var anchor = new Entity(3, 2);
            buf.DrawEntityLocal(anchor, Vector3.Zero, Vector3.UnitZ, Rgba32.Red);

            var frame = buf.GetFrame();
            Assert.Equal(CoordinateSpace.EntityLocal, frame[0].Space);
            Assert.Equal(3, frame[0].AnchorIndex);
            Assert.Equal(2, (int)frame[0].AnchorGeneration);
        }

        [Fact]
        public void Buffer_Clear_ResetsFrameAndDropCount()
        {
            var buf = new DebugPrimitiveBuffer(4);
            buf.DrawLine(Vector3.Zero, Vector3.UnitX, Rgba32.Red);
            buf.Clear();

            Assert.Equal(0, buf.GetFrame().Length);
            Assert.Equal(0, buf.DroppedCount);
        }

        [Fact]
        public void Buffer_CapacityOverflow_DropsExtraPrimitives()
        {
            const int cap = 4;
            var buf = new DebugPrimitiveBuffer(cap);

            for (int i = 0; i < cap + 2; i++)
                buf.DrawLine(Vector3.Zero, Vector3.UnitX, Rgba32.White);

            Assert.Equal(cap, buf.GetFrame().Length);
            Assert.Equal(2,   buf.DroppedCount);
        }

        [Fact]
        public void Buffer_ExposesInternMap()
        {
            var map = new StringInternMap();
            var buf = new DebugPrimitiveBuffer(16, map);
            Assert.Same(map, buf.InternMap);
        }

        [Fact]
        public void Buffer_ImplementsIDebugDrawBuilder()
        {
            var buf = new DebugPrimitiveBuffer(16);
            Assert.IsAssignableFrom<IDebugDrawBuilder>(buf);
        }
    }

    // --------------------------------------------------------------------------
    // SC-GZ019 — StringInternMap and DrawTextLong
    // --------------------------------------------------------------------------

    public class StringInternMapTests
    {
        [Fact]
        public void Fnv1a32_SameInput_SameHash()
        {
            uint h1 = StringInternMap.Fnv1a32("hello world");
            uint h2 = StringInternMap.Fnv1a32("hello world");
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void Fnv1a32_DifferentInputs_DifferentHashes()
        {
            uint h1 = StringInternMap.Fnv1a32("foo");
            uint h2 = StringInternMap.Fnv1a32("bar");
            Assert.NotEqual(h1, h2);
        }

        [Fact]
        public void Fnv1a32_EmptyString_ReturnsOffsetBasis()
        {
            // FNV-1a offset basis for empty input is 2166136261.
            uint h = StringInternMap.Fnv1a32("");
            Assert.Equal(2166136261u, h);
        }

        [Fact]
        public void Intern_TryResolve_ReturnsSameString()
        {
            var map  = new StringInternMap();
            uint h   = StringInternMap.Fnv1a32("long string text");
            map.Intern(h, "long string text");

            Assert.Equal("long string text", map.TryResolve(h));
        }

        [Fact]
        public void TryResolve_UnknownHash_ReturnsNull()
        {
            var map = new StringInternMap();
            Assert.Null(map.TryResolve(0xDEADBEEFu));
        }

        [Fact]
        public void Intern_Idempotent_DoesNotOverwriteExisting()
        {
            var map = new StringInternMap();
            uint h  = StringInternMap.Fnv1a32("text");
            map.Intern(h, "text");
            map.Intern(h, "other");  // same hash, different value (collision simulation)
            // First registration wins.
            Assert.Equal("text", map.TryResolve(h));
        }

        [Fact]
        public void Flush_ClearsAllEntries()
        {
            var map = new StringInternMap();
            uint h  = StringInternMap.Fnv1a32("x");
            map.Intern(h, "x");
            map.Flush();

            Assert.Null(map.TryResolve(h));
            Assert.Empty(map.Entries);
        }

        [Fact]
        public void Entries_ExposesInternedValues()
        {
            var map = new StringInternMap();
            uint h  = StringInternMap.Fnv1a32("entry");
            map.Intern(h, "entry");

            Assert.Contains(h, map.Entries.Keys);
            Assert.Equal("entry", map.Entries[h]);
        }

        [Fact]
        public void DrawTextLong_SetsStringHash_And_InlinePreview()
        {
            var map = new StringInternMap();
            var buf = new DebugPrimitiveBuffer(16, map);
            // Use a string longer than 31 chars to exercise the intern path meaningfully.
            const string longText = "This is a long debug label that exceeds 31 chars";
            buf.DrawTextLong(1f, 2f, longText, Rgba32.White);

            var frame = buf.GetFrame();
            Assert.Equal(1, frame.Length);

            uint expectedHash = StringInternMap.Fnv1a32(longText);
            Assert.Equal(expectedHash, frame[0].StringHash);
            Assert.Equal(DebugPrimitiveShape.Text, frame[0].Shape);
            Assert.Equal(1f, frame[0].TextX);
            Assert.Equal(2f, frame[0].TextY);

            // Full text accessible from map.
            Assert.Equal(longText, map.TryResolve(expectedHash));
        }

        [Fact]
        public void DrawTextLong_Idempotent_InternMapHasOneEntry()
        {
            var buf = new DebugPrimitiveBuffer(16);
            const string text = "same text";
            buf.DrawTextLong(0, 0, text, Rgba32.White);
            buf.DrawTextLong(0, 0, text, Rgba32.Red);   // same text, second call

            Assert.Equal(1, buf.InternMap.Entries.Count);
        }
    }
}
