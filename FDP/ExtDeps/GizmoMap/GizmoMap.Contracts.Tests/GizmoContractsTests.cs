using System;
using System.Linq;
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

        // SC-GZ053-4b: ⭐⭐ §6.7 — the CANVAS SENTINEL is not a valid anchor. Both token types agree.
        // ⛔ RED-PROOF SHAPE: change IsValid back to `AnchorId != 0` and this reddens.
        [Fact]
        public void SC_GZ053_4b_GizmoPickTokenIsInvalidForTheCanvasSentinel()
        {
            Assert.False(new GizmoPickToken { AnchorId = -1L }.IsValid);
        }

        // ⭐⭐⭐ §6.8 — THE IDENTITY INVARIANT THROWS, AND IT THROWS AT THE BUFFER.
        // 🔒 User ruling 2026-09-11: the zero-identity case must throw "on some suitable (central?) place
        //   where gizmos are processed so it is easy to catch the case soon after it happens in a new code".
        // ⭐ These rail the ECS-FREE buffer, which is the one this assembly can see; the ECS-aware
        //   DebugPrimitiveBuffer calls the SAME DebugPrimitive.AssertHasIdentity.
        // ⛔ RED-PROOF SHAPE: make Fail() assert instead of throw and all three of these redden.
        // 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.8.
        [Fact]
        public void SC_GZ053_6_AnInteractivePrimitiveWithNoIdentity_Throws()
        {
            var buf  = new GizmoPrimitiveBuffer(8);
            var prim = default(DebugPrimitive);
            prim.Shape        = DebugPrimitiveShape.Box2D;
            prim.SubElementId = 3;          // claims interaction...
            // ...and carries no BoxAnchorId.

            var ex = Assert.Throws<GizmoAnchorIdentityException>(() => buf.AppendRaw(in prim));
            Assert.Contains("NO IDENTITY", ex.Message, StringComparison.Ordinal);
        }

        // ⭐⭐ The counter-cases, so the throw cannot over-fire: a real id passes, and a DECORATIVE
        //   hit-testable shape (no anchor, no sub-element) is not a pick target and needs no identity.
        [Fact]
        public void SC_GZ053_6b_ARealIdentity_AndAPurelyDecorativeShape_BothPass()
        {
            var buf = new GizmoPrimitiveBuffer(8);

            var identified = default(DebugPrimitive);
            identified.Shape        = DebugPrimitiveShape.Box2D;
            identified.SubElementId = 3;
            identified.BoxAnchorId  = 7041L;

            var decorative = default(DebugPrimitive);
            decorative.Shape = DebugPrimitiveShape.Box2D;

            buf.AppendRaw(in identified);
            buf.AppendRaw(in decorative);
            Assert.Equal(2, buf.GetFrame().Length);
        }

        // ⭐⭐ A binding with no StructNetworkId throws too — and -1 (the CANVAS sentinel) does NOT,
        //   which is why the test is "!= 0" and never "> 0".
        [Fact]
        public void SC_GZ053_6c_ABindingNeedsAnId_ButTheCanvasSentinelIsOne()
        {
            var buf = new GizmoPrimitiveBuffer(8);

            var orphan = default(DebugPrimitive);
            orphan.Shape = DebugPrimitiveShape.ContextMenuBinding;
            Assert.Throws<GizmoAnchorIdentityException>(() => buf.AppendRaw(in orphan));

            var canvas = default(DebugPrimitive);
            canvas.Shape           = DebugPrimitiveShape.ContextMenuBinding;
            canvas.StructNetworkId = -1L;          // CanvasContextMenuGizmo.CanvasAnchorId
            buf.AppendRaw(in canvas);              // must NOT throw
            Assert.Equal(1, buf.GetFrame().Length);
        }

        // ⭐ The documented escape hatch: no THROW, and the primitive is still APPENDED (degrading to
        //   "drop it silently" would trade a loud failure for an invisible one).
        // ⚠⚠ The Debug.Assert listener is suppressed for the duration, and that is the POINT rather than
        //   a workaround: on the relaxed path a violation still fails a debug build, so this rail cannot
        //   observe "no throw" without silencing it. ⛔ Do not read the suppression as the relaxed path
        //   being quiet — it is quiet only in RELEASE.
        [Fact]
        public void SC_GZ053_6d_TurningStrictnessOff_AppendsInsteadOfThrowing()
        {
            bool previous = GizmoIdentityEnforcement.Strict;
            var listeners = new System.Diagnostics.TraceListener[System.Diagnostics.Trace.Listeners.Count];
            System.Diagnostics.Trace.Listeners.CopyTo(listeners, 0);
            try
            {
                GizmoIdentityEnforcement.Strict = false;
                System.Diagnostics.Trace.Listeners.Clear();

                var buf  = new GizmoPrimitiveBuffer(8);
                var prim = default(DebugPrimitive);
                prim.Shape        = DebugPrimitiveShape.Box2D;
                prim.SubElementId = 3;

                buf.AppendRaw(in prim);
                Assert.Equal(1, buf.GetFrame().Length);
            }
            finally
            {
                GizmoIdentityEnforcement.Strict = previous;
                System.Diagnostics.Trace.Listeners.Clear();
                System.Diagnostics.Trace.Listeners.AddRange(listeners);
            }
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
            Assert.Equal((DebugPrimitiveShape)7,  DebugPrimitiveShape.StructInspector);
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
            Assert.Equal(networkId, prim.StructNetworkId);
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

    // ==========================================================================
    // SC-GZ067: GizmoTypeId propagation into pick token
    // ==========================================================================
    //
    // 🔴 SC-GZ067-1 MOVED 2026-09-10 to GizmoMap.Presentation.Tests
    //    (GizmoPresentationTests.GizmoPickTokenConstructionTests).
    //
    //    ⛔ It lived here as a RE-IMPLEMENTATION of DebugGizmoLayer.HandleInput's token construction --
    //      this project deliberately references ONLY GizmoMap.Contracts, so it structurally cannot call
    //      the production code (see the csproj comment). ⇒ it asserted a COPY of the logic.
    //    📌 That blindness was measured: S5 (DESIGN_Gizmo_Anchor_Identity.md §6) changed the real
    //      construction to `AnchorId = BoxAnchorId` and this test stayed GREEN asserting the old
    //      `AnchorGeneration != 0 ? AnchorIndex : BoxAnchorId` -- the exact R-142 ③ shape.
    //    ⭐ The production code now exposes ONE seam, DebugGizmoLayer.MakePickToken, and the moved rail
    //      calls it. ⛔ Do not re-add a mirror here.

    // ==========================================================================

    public class GizmoPickTokenFieldContractTests
    {
        // SC-GZ067-2: the token carries ONE identity and no ECS handle -- §6.7, testable from the
        // contracts assembly alone because it is a property of the STRUCT, not of the terminal.
        // ⭐⭐ REWRITTEN 2026-09-11. It used to assert that IDENTITY and a PROCESS-LOCAL ECS PAYLOAD
        //   "coexist without aliasing" (AnchorId 90210 beside AnchorIndex 5). ⛔ That payload is
        //   deleted, so the rail now pins the stronger property: the ONLY identity-bearing field is
        //   AnchorId, and `StreamId` is a reserved publisher discriminator that production leaves 0.
        // ⛔ RED-PROOF SHAPE: re-add an `AnchorIndex` field and this stops compiling -- which is the
        //   point. A deleted field is best railed by the type system.
        // 📄 GizmoPickToken.cs field notes; DESIGN_Gizmo_Anchor_Identity.md §6.7.
        [Fact]
        public void SC_GZ067_2_PickToken_CarriesOneIdentityAndNoEcsHandle()
        {
            var token = new GizmoPickToken
            {
                AnchorId     = 90210L,   // identity: the network id -- the only one
                SubElementId = 3u,
                GizmoTypeId  = 77u,
            };

            Assert.Equal(90210L, token.AnchorId);
            Assert.Equal(3u,     token.SubElementId);
            Assert.Equal(77u,    token.GizmoTypeId);
            Assert.Equal(0u,     token.StreamId);   // reserved, never an ECS generation

            // ⭐ The struct has exactly these four settable fields plus StreamId: no ECS handle hides
            //   in it. Enumerating them is how this stays true if someone adds one back.
            var names = typeof(GizmoPickToken).GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(f => f.Name).OrderBy(n => n).ToArray();
            Assert.Equal(
                new[] { "AnchorId", "GizmoTypeId", "StreamId", "SubElementId" },
                names);
        }
    }
}
