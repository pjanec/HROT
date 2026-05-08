using System.Linq;
using System.Reflection;
using Xunit;
using GizmoMap.Network;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network.Tests
{
    public class GizmoNetworkTests
    {
        // SC-GZ054-1: GizmoMap.Network references only GizmoMap.Contracts and CycloneDDS.
        // Verified by inspecting the assembly's referenced assemblies.
        [Fact]
        public void SC_GZ054_1_AssemblyReferencesOnlyAllowedAssemblies()
        {
            var assembly = typeof(DebugPrimitivesBatch).Assembly;
            var refs = assembly.GetReferencedAssemblies();
            foreach (var r in refs)
            {
                Assert.False(
                    r.Name != null && (r.Name.StartsWith("Fdp.") || r.Name.StartsWith("Hrot.")),
                    $"GizmoMap.Network must not reference FDP/Hrot assemblies, but found: {r.Name}");
            }
        }

        // SC-GZ054-2: DebugPrimitivesBatch in GizmoMap.Network has the expected public fields.
        [Fact]
        public void SC_GZ054_2_DebugPrimitivesBatchHasExpectedFields()
        {
            var type = typeof(DebugPrimitivesBatch);
            var fieldNames = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                 .Select(f => f.Name)
                                 .ToHashSet();

            Assert.Contains("FrameNumber",   fieldNames);
            Assert.Contains("NodeId",        fieldNames);
            Assert.Contains("PrimitivesData", fieldNames);
        }

        // SC-GZ054-3: EntityAttributeSchema has NodeId (int) and SchemaJson (string).
        [Fact]
        public void SC_GZ054_3_EntityAttributeSchemaHasExpectedFields()
        {
            var type = typeof(EntityAttributeSchema);

            var nodeIdField = type.GetField("NodeId", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(nodeIdField);
            Assert.Equal(typeof(int), nodeIdField!.FieldType);

            var schemaField = type.GetField("SchemaJson", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(schemaField);
            Assert.Equal(typeof(string), schemaField!.FieldType);
        }

        // SC-GZ054-4: GizmoMap.Network does NOT contain any type implementing IEcsModuleSystem.
        [Fact]
        public void SC_GZ054_4_AssemblyContainsNoEcsModuleSystemImplementations()
        {
            var assembly = typeof(DebugPrimitivesBatch).Assembly;
            bool hasEcsImpl = assembly.GetTypes()
                .Any(t => t.GetInterface("IEcsModuleSystem") != null);
            Assert.False(hasEcsImpl,
                "GizmoMap.Network must not contain any IEcsModuleSystem implementation.");
        }

        // SC-GZ054-5: DdsDebugPrimitivePublisher constructor works with an IDdsWriter stub.
        [Fact]
        public void SC_GZ054_5_DdsDebugPrimitivePublisherConstructorDoesNotThrow()
        {
            var stubWriter = new StubDdsWriter();
            var publisher = new DdsDebugPrimitivePublisher(stubWriter);
            Assert.NotNull(publisher);

            // Also exercise Publish to verify it doesn't throw with an empty buffer.
            var buffer = new GizmoPrimitiveBuffer(capacity: 16);
            publisher.Publish(buffer, frameNumber: 1, nodeId: 0);
            Assert.Equal(1, stubWriter.WriteCount);
        }

        // SC-GZ054-6: Publisher/subscriber byte roundtrip preserves primitive count and field values.
        [Fact]
        public void SC_GZ054_6_PublisherSubscriberByteRoundtrip_PreservesPrimitives()
        {
            var capturingWriter = new CapturingDdsWriter();
            var publisher       = new DdsDebugPrimitivePublisher(capturingWriter);
            var subscriber      = new DdsDebugPrimitiveSubscriber(capturingWriter);

            // Emit a primitive with known field values into the source buffer.
            var source = new GizmoPrimitiveBuffer(capacity: 4);
            var prim   = new DebugPrimitive { Shape = DebugPrimitiveShape.Sphere, SphereRadius = 2.5f };
            source.AppendRaw(in prim);

            publisher.Publish(source, frameNumber: 7, nodeId: 3);

            // Verify the batch header was encoded correctly.
            Assert.Equal(1, capturingWriter.WriteCount);
            Assert.Equal(7u, capturingWriter.LastBatch.FrameNumber);
            Assert.Equal(3,  capturingWriter.LastBatch.NodeId);

            // Decode via the subscriber into a target buffer.
            var target = new GizmoPrimitiveBuffer(capacity: 4);
            bool consumed = subscriber.PollAndApply(target);

            Assert.True(consumed);
            var frame = target.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(DebugPrimitiveShape.Sphere, frame[0].Shape);
            Assert.Equal(2.5f, frame[0].SphereRadius);
        }

        private sealed class StubDdsWriter : IDdsWriter<DebugPrimitivesBatch>
        {
            public int WriteCount { get; private set; }
            public void Write(DebugPrimitivesBatch sample) => WriteCount++;
        }

        // Captures the last written batch and acts as its own IDdsReader (returns it once).
        private sealed class CapturingDdsWriter : IDdsWriter<DebugPrimitivesBatch>, IDdsReader<DebugPrimitivesBatch>
        {
            public int WriteCount { get; private set; }
            public DebugPrimitivesBatch LastBatch { get; private set; }
            private bool _pending;

            public void Write(DebugPrimitivesBatch sample)
            {
                LastBatch = sample;
                WriteCount++;
                _pending  = true;
            }

            public bool TryRead(out DebugPrimitivesBatch sample)
            {
                if (_pending)
                {
                    sample   = LastBatch;
                    _pending = false;
                    return true;
                }
                sample = default;
                return false;
            }
        }
    }
}
