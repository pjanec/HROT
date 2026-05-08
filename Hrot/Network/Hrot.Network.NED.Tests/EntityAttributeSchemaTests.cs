using System.Reflection;
using System.Text.Json;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Replication.Patching;
using Hrot.NED.Messages;
using Hrot.Network.NED.Attributes;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// Contract and behaviour tests for the EntityAttributeSchema DDS topic,
    /// EntityAttributeSchemaPublisherSystem, and JsonAttributeCompiler.ExportSchema()
    /// introduced by task GZ052.
    /// </summary>
    public class EntityAttributeSchemaTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public int WriteCount;
            public T?  LastWritten;
            public void Write(T sample) { WriteCount++; LastWritten = sample; }
        }

        // A minimal JsonAttributeCompiler with one registered path.
        private static JsonAttributeCompiler BuildMinimalCompiler()
        {
            return new AttributeCompilerBuilder()
                .RegisterReferencePath<string>("Name",
                    (string? s, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader reader) => { },
                    descriptorOrdinal: 0)
                .Build();
        }

        // ── SC-GZ052-1: EntityAttributeSchema struct shape ───────────────────

        /// <summary>
        /// The EntityAttributeSchema struct must carry a [DdsKey] NodeId (int)
        /// and a SchemaJson (string) field, matching the DDS topic definition.
        /// </summary>
        [Fact]
        public void SC_GZ052_1_EntityAttributeSchema_HasExpectedFields()
        {
            var type = typeof(EntityAttributeSchema);

            // NodeId must be an int field
            var nodeIdField = type.GetField("NodeId");
            Assert.NotNull(nodeIdField);
            Assert.Equal(typeof(int), nodeIdField!.FieldType);

            // NodeId must carry [DdsKey]
            var ddsKeyAttr = nodeIdField.GetCustomAttribute<CycloneDDS.Schema.DdsKeyAttribute>();
            Assert.NotNull(ddsKeyAttr);

            // SchemaJson must be a string field
            var schemaField = type.GetField("SchemaJson");
            Assert.NotNull(schemaField);
            Assert.Equal(typeof(string), schemaField!.FieldType);
        }

        // ── SC-GZ052-2: Publisher writes exactly once ────────────────────────

        /// <summary>
        /// When Execute is called multiple times, only the first call triggers a DDS write.
        /// </summary>
        [Fact]
        public void SC_GZ052_2_PublisherSystem_WritesExactlyOnce()
        {
            var writer   = new CapturingWriter<EntityAttributeSchema>();
            var compiler = BuildMinimalCompiler();
            var system   = new EntityAttributeSchemaPublisherSystem(
                nodeId:             1,
                compiler:           compiler,
                writer:             writer,
                isDefaultProcessor: true);

            for (int i = 0; i < 10; i++)
                system.Execute(null!, deltaTime: 0f);

            Assert.Equal(1, writer.WriteCount);
        }

        // ── SC-GZ052-3: Non-default processor never writes ───────────────────

        /// <summary>
        /// When isDefaultProcessor is false, no DDS writes occur.
        /// </summary>
        [Fact]
        public void SC_GZ052_3_NonDefaultProcessor_NeverWrites()
        {
            var writer   = new CapturingWriter<EntityAttributeSchema>();
            var compiler = BuildMinimalCompiler();
            var system   = new EntityAttributeSchemaPublisherSystem(
                nodeId:             2,
                compiler:           compiler,
                writer:             writer,
                isDefaultProcessor: false);

            for (int i = 0; i < 10; i++)
                system.Execute(null!, deltaTime: 0f);

            Assert.Equal(0, writer.WriteCount);
        }

        // ── SC-GZ052-4: ExportSchema returns valid JSON ──────────────────────

        /// <summary>
        /// ExportSchema must return a string parseable by JsonDocument.Parse.
        /// </summary>
        [Fact]
        public void SC_GZ052_4_ExportSchema_ReturnsValidJson()
        {
            var compiler = BuildMinimalCompiler();
            string schema = compiler.ExportSchema();

            Assert.False(string.IsNullOrEmpty(schema));
            // Must be parseable without throwing.
            using var doc = JsonDocument.Parse(schema);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }

        // ── SC-GZ052-5: ExportSchema contains at least one property ──────────

        /// <summary>
        /// The exported schema must contain at least one entry under "properties"
        /// for every path registered in AttributeCompilerFactory.Build(null).
        /// </summary>
        [Fact]
        public void SC_GZ052_5_ExportSchema_ContainsAtLeastOneProperty()
        {
            var compiler = Hrot.SimHost.AttributeCompilerFactory.Build(geoTransform: null);
            string schema = compiler.ExportSchema();

            using var doc = JsonDocument.Parse(schema);
            Assert.True(doc.RootElement.TryGetProperty("properties", out var props),
                "Expected 'properties' object in exported schema.");
            int count = 0;
            foreach (var _ in props.EnumerateObject()) count++;
            Assert.True(count >= 1,
                $"Expected at least 1 property in schema, but found {count}.");
        }
    }
}
