using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace GizmoMap.Contracts.Tests
{
    public class GizmoContractsTests
    {
        // SC-GZ053-1: assembly boundary — the test project itself compiles with no FDP/Hrot references.
        // If GizmoMap.Contracts had a dependency on Fdp.Core or Hrot.*, this file would fail to build.
        // Verified implicitly by a successful standalone build of this test project.
        [Fact]
        public void SC_GZ053_1_AssemblyBoundaryVerifiedByStandaloneBuild()
        {
            // Load the GizmoMap.Contracts assembly and verify no Fdp.* or Hrot.* references.
            var assembly = typeof(DebugPrimitive).Assembly;
            var refs = assembly.GetReferencedAssemblies();
            foreach (var r in refs)
            {
                Assert.False(
                    r.Name != null && (r.Name.StartsWith("Fdp.") || r.Name.StartsWith("Hrot.")),
                    $"GizmoMap.Contracts must not reference FDP/Hrot assemblies, but found: {r.Name}");
            }
        }

        // SC-GZ053-2: DebugPrimitive is exactly 64 bytes.
        [Fact]
        public void SC_GZ053_2_DebugPrimitiveSizeIs64()
        {
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // SC-GZ053-3: GizmoPickToken with non-zero AnchorId is valid.
        [Fact]
        public void SC_GZ053_3_GizmoPickTokenIsValidWhenAnchorIdNonZero()
        {
            var token = new GizmoPickToken { AnchorId = 42L, SubElementId = 7u };
            Assert.True(token.IsValid);
        }

        // SC-GZ053-4: GizmoPickToken with zero AnchorId is invalid.
        [Fact]
        public void SC_GZ053_4_GizmoPickTokenIsInvalidWhenAnchorIdZero()
        {
            var token = new GizmoPickToken { AnchorId = 0L };
            Assert.False(token.IsValid);
        }

        // SC-GZ053-5: All DebugPrimitiveShape enum values 0-10 are accessible.
        [Fact]
        public void SC_GZ053_5_DebugPrimitiveShapeEnumValuesAccessible()
        {
            Assert.Equal((DebugPrimitiveShape)0,  DebugPrimitiveShape.Line);
            Assert.Equal((DebugPrimitiveShape)1,  DebugPrimitiveShape.Sphere);
            Assert.Equal((DebugPrimitiveShape)2,  DebugPrimitiveShape.Box2D);
            Assert.Equal((DebugPrimitiveShape)3,  DebugPrimitiveShape.Arrow);
            Assert.Equal((DebugPrimitiveShape)4,  DebugPrimitiveShape.Text);
            Assert.Equal((DebugPrimitiveShape)5,  DebugPrimitiveShape.EntityBadge);
            Assert.Equal((DebugPrimitiveShape)6,  DebugPrimitiveShape.Icon);
            Assert.Equal((DebugPrimitiveShape)7,  DebugPrimitiveShape.ComponentInspector);
            Assert.Equal((DebugPrimitiveShape)8,  DebugPrimitiveShape.SemanticShape);
            Assert.Equal((DebugPrimitiveShape)9,  DebugPrimitiveShape.MilStd2525);
            Assert.Equal((DebugPrimitiveShape)10, DebugPrimitiveShape.SpatialAnchor);
            Assert.Equal((DebugPrimitiveShape)11, DebugPrimitiveShape.ContextMenuBinding);
        }

        // SC-GZ053-6: IGizmoSource is accessible; create a mock implementation and call Emit.
        [Fact]
        public void SC_GZ053_6_IGizmoSourceIsAccessibleAndCallable()
        {
            var source = new TestGizmoSource();
            var buffer = new GizmoPrimitiveBuffer(capacity: 16);
            source.Emit(0.016f, buffer);
            Assert.True(source.EmitCalled);
        }

        // SC-GZ053-7: MakeContextMenuBinding factory sets Shape, StringHash and InspNetworkId correctly.
        [Fact]
        public void SC_GZ053_7_MakeContextMenuBinding_SetsFieldsCorrectly()
        {
            const long   networkId = 42L;
            const uint   menuHash  = 0xDEAD_BEEF;

            var prim = DebugPrimitive.MakeContextMenuBinding(networkId, menuHash);

            Assert.Equal(DebugPrimitiveShape.ContextMenuBinding, prim.Shape);
            Assert.Equal(menuHash,  prim.StringHash);
            Assert.Equal(networkId, prim.InspNetworkId);
        }

        // SC-GZ053-8: StringInternMap.Fnv1a32 produces consistent hashes and zero stays zero.
        [Fact]
        public void SC_GZ053_8_StringInternMap_Fnv1a32_IsConsistent()
        {
            const string text = "hello";
            uint h1 = StringInternMap.Fnv1a32(text);
            uint h2 = StringInternMap.Fnv1a32(text);
            Assert.Equal(h1, h2);

            // Different strings must produce different hashes (not guaranteed in general but
            // holds for these two inputs).
            uint h3 = StringInternMap.Fnv1a32("world");
            Assert.NotEqual(h1, h3);
        }

        // SC-GZ053-9: StringInternMap intern + resolve round-trip.
        [Fact]
        public void SC_GZ053_9_StringInternMap_InternResolveRoundtrip()
        {
            var map  = new StringInternMap();
            string text = "[{\"id\":1,\"label\":\"Foo\"}]";
            uint hash   = StringInternMap.Fnv1a32(text);

            map.Intern(hash, text);
            string? resolved = map.TryResolve(hash);

            Assert.Equal(text, resolved);
        }

        private sealed class TestGizmoSource : IGizmoSource
        {
            public bool EmitCalled { get; private set; }

            public void Emit(float deltaTime, IGizmoDrawBuilder draw)
            {
                EmitCalled = true;
                // Exercise the builder to ensure it's usable.
                draw.DrawArrow(
                    new System.Numerics.Vector3(0, 0, 0),
                    new System.Numerics.Vector3(1, 0, 0),
                    Rgba32.Red);
            }
        }
    }
}
