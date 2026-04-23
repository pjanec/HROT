using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Xunit;

// ── Test-only components with System.Numerics fields ────────────────────────
// IDs 230-231 reserved for this test file.

namespace Fdp.Toolkit.Scenario.Tests
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(230)]
    public struct SimTransform
    {
        public Vector3    Position;
        public Quaternion Rotation;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(231)]
    public struct Velocity2D
    {
        public Vector2 Linear;
    }

    // ── Converter unit tests ──────────────────────────────────────────────────

    /// <summary>
    /// Unit tests for <see cref="Vector2ArrayConverter"/>, <see cref="Vector3ArrayConverter"/>
    /// and <see cref="QuaternionArrayConverter"/>.
    /// </summary>
    public sealed class ScenarioJsonConvertersTests
    {
        private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions
        {
            IncludeFields  = true,
            WriteIndented  = true,          // verify single-line output even with indentation enabled
            Converters     =
            {
                new Vector3ArrayConverter(),
                new QuaternionArrayConverter(),
                new Vector2ArrayConverter(),
            },
        };

        // ── Vector3 ───────────────────────────────────────────────────────────

        [Fact]
        public void Vector3_Roundtrip_PreservesComponents()
        {
            var original = new Vector3(1.5f, -2.25f, 3.125f);
            var json     = JsonSerializer.Serialize(original, _opts);
            var result   = JsonSerializer.Deserialize<Vector3>(json, _opts);

            Assert.Equal(original.X, result.X);
            Assert.Equal(original.Y, result.Y);
            Assert.Equal(original.Z, result.Z);
        }

        [Fact]
        public void Vector3_SerializesAsSingleLineArray()
        {
            var value = new Vector3(1f, 2f, 3f);
            var json  = JsonSerializer.Serialize(value, _opts);

            // Must be a compact one-liner regardless of WriteIndented
            Assert.DoesNotContain("\n", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        [Fact]
        public void Vector3_Zero_Roundtrip()
        {
            var json   = JsonSerializer.Serialize(Vector3.Zero, _opts);
            var result = JsonSerializer.Deserialize<Vector3>(json, _opts);
            Assert.Equal(Vector3.Zero, result);
        }

        // ── Quaternion ────────────────────────────────────────────────────────

        [Fact]
        public void Quaternion_Roundtrip_PreservesComponents()
        {
            var original = new Quaternion(0.1f, 0.2f, 0.3f, 0.9274f);
            var json     = JsonSerializer.Serialize(original, _opts);
            var result   = JsonSerializer.Deserialize<Quaternion>(json, _opts);

            Assert.Equal(original.X, result.X);
            Assert.Equal(original.Y, result.Y);
            Assert.Equal(original.Z, result.Z);
            Assert.Equal(original.W, result.W);
        }

        [Fact]
        public void Quaternion_SerializesAsSingleLineArray()
        {
            var q    = Quaternion.Identity;
            var json = JsonSerializer.Serialize(q, _opts);

            Assert.DoesNotContain("\n", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        [Fact]
        public void Quaternion_Identity_Roundtrip()
        {
            var json   = JsonSerializer.Serialize(Quaternion.Identity, _opts);
            var result = JsonSerializer.Deserialize<Quaternion>(json, _opts);
            Assert.Equal(Quaternion.Identity, result);
        }

        // ── Vector2 ───────────────────────────────────────────────────────────

        [Fact]
        public void Vector2_Roundtrip_PreservesComponents()
        {
            var original = new Vector2(-7.5f, 42.0625f);
            var json     = JsonSerializer.Serialize(original, _opts);
            var result   = JsonSerializer.Deserialize<Vector2>(json, _opts);

            Assert.Equal(original.X, result.X);
            Assert.Equal(original.Y, result.Y);
        }

        [Fact]
        public void Vector2_SerializesAsSingleLineArray()
        {
            var value = new Vector2(1f, 2f);
            var json  = JsonSerializer.Serialize(value, _opts);

            Assert.DoesNotContain("\n", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        // ── FdpAutoSerializer integration ─────────────────────────────────────

        /// <summary>
        /// Verifies that Vector3/Quaternion fields on a real ECS component are written
        /// as compact arrays when using FdpAutoSerializer (which uses _fieldAwareOptions
        /// internally).  The output must be single-line per vector, not a verbose object.
        /// </summary>
        [Fact]
        public void FdpAutoSerializer_SimTransform_Position_WrittenAsArray()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();

            var serializer = new ScenarioSerializerBuilder("test").Build();
            var entity     = repo.CreateEntity();
            repo.AddComponent<SimTransform>(entity, new SimTransform
            {
                Position = new Vector3(10f, 20f, 30f),
                Rotation = Quaternion.Identity,
            });

            var dom = serializer.Serialize(repo, new ScenarioHeader("test"));

            // The ScenarioSerializer stores entities under "Entities" (capital E)
            // as a JsonObject keyed by entity ID, not as an array.
            var entitiesObj = dom["Entities"]?.AsObject();
            Assert.NotNull(entitiesObj);
            Assert.NotEmpty(entitiesObj!);

            // First entity node contains component keys directly.
            var entityNode = entitiesObj!.First().Value?.AsObject();
            Assert.NotNull(entityNode);

            // SimTransform must be a key in the entity node.
            Assert.True(entityNode!.ContainsKey("SimTransform"),
                "Entity must contain a 'SimTransform' key");

            // Position value must be a JSON array, not a JSON object.
            var positionNode = entityNode["SimTransform"]?["Position"];
            Assert.NotNull(positionNode);
            Assert.IsType<JsonArray>(positionNode);
        }

        /// <summary>
        /// Verifies full roundtrip: serialize an entity with <see cref="SimTransform"/>,
        /// deserialize back, and confirm component data is preserved.
        /// </summary>
        [Fact]
        public void FdpAutoSerializer_SimTransform_Roundtrip()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();

            var serializer = new ScenarioSerializerBuilder("test").Build();

            var original = new SimTransform
            {
                Position = new Vector3(1f, 2f, 3f),
                Rotation = new Quaternion(0f, 0f, 0f, 1f),
            };

            var entity = repo.CreateEntity();
            repo.AddComponent<SimTransform>(entity, original);

            var dom = serializer.Serialize(repo, new ScenarioHeader("test"));

            using var repo2 = new EntityRepository();
            repo2.RegisterComponent<SimTransform>();
            serializer.Deserialize(repo2, dom);

            // Exactly one entity should have been deserialized.
            var query = repo2.Query().With<SimTransform>().Build();
            var found = false;
            foreach (var e in query)
            {
                var t = repo2.GetComponent<SimTransform>(e);
                Assert.Equal(original.Position.X, t.Position.X);
                Assert.Equal(original.Position.Y, t.Position.Y);
                Assert.Equal(original.Position.Z, t.Position.Z);
                Assert.Equal(original.Rotation.X, t.Rotation.X);
                Assert.Equal(original.Rotation.Y, t.Rotation.Y);
                Assert.Equal(original.Rotation.Z, t.Rotation.Z);
                Assert.Equal(original.Rotation.W, t.Rotation.W);
                found = true;
            }
            Assert.True(found, "No entity with SimTransform was deserialized.");
        }
    }
}
