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

            Assert.Contains("FrameNumber", fieldNames);
            Assert.Contains("NodeId",      fieldNames);
            Assert.Contains("Primitives",  fieldNames);
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
            var buffer = new DebugPrimitiveBuffer(capacity: 16);
            publisher.Publish(buffer, frameNumber: 1, nodeId: 0);
            Assert.Equal(1, stubWriter.WriteCount);
        }

        private sealed class StubDdsWriter : IDdsWriter<DebugPrimitivesBatch>
        {
            public int WriteCount { get; private set; }
            public void Write(DebugPrimitivesBatch sample) => WriteCount++;
        }
    }
}
