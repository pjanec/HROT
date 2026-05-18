using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for BTreeSchemaExporter (FBT-007).</summary>
    public class BTreeSchemaExporterTests
    {
        // ---- Schema scan targets defined in this test class ----
        // The scanner will find these when given Assembly.GetExecutingAssembly().

        [BTreeAction]
        private static NodeStatus SchemaAction1(
            ref float value, ref BehaviorTreeState state, ref MockContext ctx)
            => NodeStatus.Success;

        [BTreeAction]
        private static NodeStatus SchemaAction2(
            ref int value, ref BehaviorTreeState state, ref MockContext ctx)
            => NodeStatus.Success;

        [BTreeCondition]
        private static NodeStatus SchemaCondition1(
            ref int value, ref BehaviorTreeState state, ref MockContext ctx)
            => value > 0 ? NodeStatus.Success : NodeStatus.Failure;

        // ---- FBT-007: SC1 ----

        /// <summary>Scanner finds all [BTreeAction] and [BTreeCondition] methods in the test assembly.</summary>
        [Fact]
        public void Export_FindsAllMarkedMethods_InTestAssembly()
        {
            var schema = BTreeSchemaExporter.Export(new[] { Assembly.GetExecutingAssembly() });

            // The test assembly contains at least the 2 actions and 1 condition declared above.
            // (Other test classes may add more, so we use >= rather than ==.)
            Assert.True(schema.Actions.Length >= 2,
                $"Expected at least 2 actions but found {schema.Actions.Length}");
            Assert.True(schema.Conditions.Length >= 1,
                $"Expected at least 1 condition but found {schema.Conditions.Length}");
        }

        // ---- FBT-007: SC2 (simplification: FieldOffset is always -1) ----

        /// <summary>FieldOffset is -1 for all scanned methods (Roslyn generator resolves real offsets).</summary>
        [Fact]
        public void Export_FieldOffset_IsNegativeOne_ForAllMethods()
        {
            var schema = BTreeSchemaExporter.Export(new[] { Assembly.GetExecutingAssembly() });

            foreach (var action in schema.Actions)
                Assert.Equal(-1, action.FieldOffset);

            foreach (var condition in schema.Conditions)
                Assert.Equal(-1, condition.FieldOffset);
        }

        // ---- FBT-007: SC3 ----

        /// <summary>ExportToJson produces valid JSON that round-trips through System.Text.Json.</summary>
        [Fact]
        public void ExportToJson_ProducesValidJson_ThatRoundTrips()
        {
            var schema = BTreeSchemaExporter.Export(new[] { Assembly.GetExecutingAssembly() });
            string tempPath = Path.GetTempFileName();
            try
            {
                BTreeSchemaExporter.ExportToJson(schema, tempPath);

                string json = File.ReadAllText(tempPath);
                var roundTripped = JsonSerializer.Deserialize<BTreeSchema>(json);

                Assert.NotNull(roundTripped);
                Assert.Equal(schema.Actions.Length, roundTripped!.Actions.Length);
                Assert.Equal(schema.Conditions.Length, roundTripped.Conditions.Length);
                Assert.Equal(schema.BlackboardDtoTypes.Length, roundTripped.BlackboardDtoTypes.Length);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        // ---- FBT-007: SC4 ----

        /// <summary>Passing an empty assembly list produces an empty schema without throwing.</summary>
        [Fact]
        public void ExportToJson_EmptyAssembly_DoesNotThrow()
        {
            var schema = BTreeSchemaExporter.Export(Array.Empty<Assembly>());

            Assert.NotNull(schema);
            Assert.Empty(schema.Actions);
            Assert.Empty(schema.Conditions);
            Assert.Empty(schema.BlackboardDtoTypes);

            string tempPath = Path.GetTempFileName();
            try
            {
                // ExportToJson must not throw for empty schema.
                BTreeSchemaExporter.ExportToJson(schema, tempPath);
                Assert.True(File.Exists(tempPath));
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        // ---- FBT-007: SC (robustness) ----

        /// <summary>Scanning assemblies that contain no marked methods does not throw.</summary>
        [Fact]
        public void Export_WithUnmarkedAssembly_DoesNotThrow()
        {
            // typeof(string).Assembly is a system assembly with no [BTreeAction]/[BTreeCondition].
            var schema = BTreeSchemaExporter.Export(new[] { typeof(string).Assembly });

            Assert.NotNull(schema);
            // System assembly has no BTree attributes; schema should have no entries from it.
            Assert.Empty(schema.Actions);
            Assert.Empty(schema.Conditions);
        }
    }
}
