using System;
using System.Linq;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Example;
using Xunit;

namespace GizmoMap.Example.Tests
{
    public class GizmoExampleTests
    {
        // SC-GZ056-1: Local mode: produces and transports at least one primitive.
        [Fact]
        public void SC_GZ056_1_LocalModeRunsOneFrame()
        {
            var producer = new DebugPrimitiveBuffer();
            var consumer = new DebugPrimitiveBuffer();
            using var transport = new LocalGizmoTransport();
            var gen     = new DemoSceneGenerator();
            var builder = new LocalDrawBuilder(producer);

            gen.Emit(0.016f, builder);
            transport.PublishPrimitives(producer.GetFrame());
            transport.PollAndApply(consumer);

            Assert.True(consumer.GetFrame().Length > 0,
                "Expected at least one primitive in consumer buffer.");
        }

        // SC-GZ056-2: Demo emits SpatialAnchor.
        [Fact]
        public void SC_GZ056_2_EmitsSpatialAnchor()
        {
            var producer = new DebugPrimitiveBuffer();
            var gen      = new DemoSceneGenerator();
            var builder  = new LocalDrawBuilder(producer);

            gen.Emit(0f, builder);

            var prims = producer.GetFrame().ToArray();
            Assert.Contains(prims, p => p.Shape == DebugPrimitiveShape.SpatialAnchor);
        }

        // SC-GZ056-3: GizmoMap.Example has no Fdp.* / Hrot.* references.
        [Fact]
        public void SC_GZ056_3_NoForbiddenAssemblyReferences()
        {
            var asm      = typeof(DemoSceneGenerator).Assembly;
            var refNames = asm.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();

            Assert.DoesNotContain(refNames, n => n.StartsWith("Fdp.",  StringComparison.Ordinal));
            Assert.DoesNotContain(refNames, n => n.StartsWith("Hrot.", StringComparison.Ordinal));
        }

        // SC-GZ056-4: IGizmoTransport is defined in GizmoMap.Contracts assembly.
        [Fact]
        public void SC_GZ056_4_IGizmoTransportInContracts()
        {
            var contractsAsm = typeof(IGizmoTransport).Assembly;
            Assert.Equal("GizmoMap.Contracts", contractsAsm.GetName().Name);
        }

        // SC-GZ056-5: All required shape types emitted in one frame.
        [Fact]
        public void SC_GZ056_5_AllRequiredShapesEmitted()
        {
            var producer = new DebugPrimitiveBuffer();
            var gen      = new DemoSceneGenerator();
            var builder  = new LocalDrawBuilder(producer);

            gen.Emit(1f, builder); // t=1s for deterministic state

            var shapes = producer.GetFrame().ToArray().Select(p => p.Shape).ToHashSet();

            Assert.Contains(DebugPrimitiveShape.SpatialAnchor,      shapes);
            Assert.Contains(DebugPrimitiveShape.SemanticShape,       shapes);
            Assert.Contains(DebugPrimitiveShape.MilStd2525,          shapes);
            Assert.Contains(DebugPrimitiveShape.Line,                shapes);
            Assert.Contains(DebugPrimitiveShape.Sphere,              shapes);
            Assert.Contains(DebugPrimitiveShape.Arrow,               shapes);
        }

        // SC-GZ056-6: Damaged bit toggles on SemanticShape between frames.
        [Fact]
        public void SC_GZ056_6_DamagedBitToggles()
        {
            // Frame at t=0.5s (not yet toggled => not damaged)
            var buf1  = new DebugPrimitiveBuffer();
            var gen   = new DemoSceneGenerator();
            gen.Emit(0.5f, new LocalDrawBuilder(buf1));

            // Frame at t=0.5+2.0=2.5s (toggles at 2s boundary => damaged)
            var buf2  = new DebugPrimitiveBuffer();
            gen.Emit(2.0f, new LocalDrawBuilder(buf2));

            var sem1 = buf1.GetFrame().ToArray()
                           .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.SemanticShape);
            var sem2 = buf2.GetFrame().ToArray()
                           .FirstOrDefault(p => p.Shape == DebugPrimitiveShape.SemanticShape);

            // One should have bit 0 set (Damaged) and the other not.
            Assert.NotEqual(sem1.ConditionMask & 1u, sem2.ConditionMask & 1u);
        }
    }
}
