using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replay;
using Xunit;

namespace Fdp.Tests
{
    public class MetadataTests
    {
        [Fact]
        public void Metadata_Serialization_RoundTrip()
        {
            var meta = new RecordingMetadata
            {
                ProtocolVersion = 2,
                AppVersion = "2.5.0",
                Description = "Test Description",
                Timestamp = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                TotalFrames = 100,
                Duration = TimeSpan.FromMinutes(2)
            };
            meta.CustomTags["Level"] = "Map1";
            meta.CustomTags["User"] = "Tester";

            var json = MetadataSerializer.Serialize(meta);
            var deserialized = MetadataSerializer.Deserialize(json);

            Assert.Equal(meta.ProtocolVersion, deserialized.ProtocolVersion);
            Assert.Equal(meta.AppVersion, deserialized.AppVersion);
            Assert.Equal(meta.Description, deserialized.Description);
            Assert.Equal(meta.TotalFrames, deserialized.TotalFrames);
            Assert.Equal(meta.Duration, deserialized.Duration);
            Assert.Equal(meta.CustomTags["Level"], deserialized.CustomTags["Level"]);
        }

        // ── RBF-P1T1 tests ───────────────────────────────────────────────────────

        [Fact]
        public void RBF_P1T1_Metadata_RoundTripsExerciseId()
        {
            var expected = Guid.NewGuid();
            var meta = new RecordingMetadata { ExerciseId = expected };
            var json = MetadataSerializer.Serialize(meta);
            var result = MetadataSerializer.Deserialize(json);
            Assert.Equal(expected, result.ExerciseId);
        }

        [Fact]
        public void RBF_P1T1_Metadata_RoundTripsNodeId()
        {
            var meta = new RecordingMetadata { NodeId = 7 };
            var json = MetadataSerializer.Serialize(meta);
            var result = MetadataSerializer.Deserialize(json);
            Assert.Equal(7, result.NodeId);
        }

        [Fact]
        public void RBF_P1T1_Metadata_LegacyJsonDeserializes()
        {
            // JSON written before federation support — no ExerciseId or NodeId fields.
            const string legacyJson = "{\"ProtocolVersion\":1,\"Timestamp\":\"2024-01-01T00:00:00Z\",\"AppVersion\":\"1.0.0\",\"Description\":\"\",\"TotalFrames\":0,\"Duration\":\"00:00:00\",\"CustomTags\":{},\"SchemaManifest\":null,\"EventManifest\":null,\"MaxNetworkId\":0}";
            var result = MetadataSerializer.Deserialize(legacyJson);
            Assert.Equal(Guid.Empty, result.ExerciseId);
            Assert.Equal(0, result.NodeId);
        }

        [Fact]
        public void AsyncRecorder_WritesSidecarFile()
        {
            var filePath = Path.GetTempFileName();
            var metaPath = filePath + ".meta.json";
            
            try 
            {
                var inputMeta = new RecordingMetadata { Description = "Sidecar Test" };
                
                // Initialize with explicit MinRecordableId to match our fix pattern, though not strictly required for this test
                using (var recorder = new AsyncRecorder(filePath, inputMeta) { MinRecordableId = 0 })
                {
                    // Just open and close to trigger Dispose logic
                }

                Assert.True(File.Exists(metaPath), "Metadata file should exist");
                
                var content = File.ReadAllText(metaPath);
                var readMeta = MetadataSerializer.Deserialize(content);
                
                Assert.Equal("Sidecar Test", readMeta.Description);
                Assert.Equal(0, readMeta.TotalFrames);
                Assert.True(readMeta.Duration.TotalSeconds >= 0);
            }
            finally
            {
                try 
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                }
                catch { /* Ignore */ }
            }
        }

        // ── RBF-P1T3 tests ───────────────────────────────────────────────────────

        /// <summary>
        /// Verifies that RecordingModule stamps the ExerciseId from RecordingConfiguration
        /// into the .meta.json sidecar when the recording is disposed.
        /// </summary>
        [Fact]
        public void RBF_P1T3_RecordingModule_WritesExerciseIdToSidecar()
        {
            var exerciseId = Guid.NewGuid();
            var fdpPath = Path.Combine(Path.GetTempPath(), $"rbf_p1t3_ex_{Guid.NewGuid():N}.fdp");
            var metaPath = fdpPath + ".meta.json";
            try
            {
                var config = new RecordingConfiguration
                {
                    FilePath   = fdpPath,
                    ExerciseId = exerciseId,
                    NodeId     = 0,
                };
                var module = new RecordingModule(config);
                var registry = new CapturingRegistry();
                module.RegisterSystems(registry);

                using var world = new EntityRepository();
                world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
                foreach (var sys in registry.Systems)
                    sys.Execute(world, 0.016f);

                module.Dispose();

                Assert.True(File.Exists(metaPath), ".meta.json must be written");
                var meta = MetadataSerializer.Deserialize(File.ReadAllText(metaPath));
                Assert.Equal(exerciseId, meta.ExerciseId);
            }
            finally
            {
                try { if (File.Exists(fdpPath)) File.Delete(fdpPath); } catch { }
                try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
            }
        }

        /// <summary>
        /// Verifies that RecordingModule stamps the NodeId from RecordingConfiguration
        /// into the .meta.json sidecar when the recording is disposed.
        /// </summary>
        [Fact]
        public void RBF_P1T3_RecordingModule_WritesNodeIdToSidecar()
        {
            var fdpPath = Path.Combine(Path.GetTempPath(), $"rbf_p1t3_nid_{Guid.NewGuid():N}.fdp");
            var metaPath = fdpPath + ".meta.json";
            try
            {
                var config = new RecordingConfiguration
                {
                    FilePath   = fdpPath,
                    ExerciseId = Guid.NewGuid(),
                    NodeId     = 7,
                };
                var module = new RecordingModule(config);
                var registry = new CapturingRegistry();
                module.RegisterSystems(registry);

                using var world = new EntityRepository();
                world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
                foreach (var sys in registry.Systems)
                    sys.Execute(world, 0.016f);

                module.Dispose();

                Assert.True(File.Exists(metaPath), ".meta.json must be written");
                var meta = MetadataSerializer.Deserialize(File.ReadAllText(metaPath));
                Assert.Equal(7, meta.NodeId);
            }
            finally
            {
                try { if (File.Exists(fdpPath)) File.Delete(fdpPath); } catch { }
                try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
            }
        }

        /// <summary>
        /// Verifies that AsyncRecorder's constructor signature accepts (string, RecordingMetadata?)
        /// and has not gained additional required parameters.
        /// </summary>
        [Fact]
        public void RBF_P1T3_AsyncRecorder_NoCtorChangeRequired()
        {
            var ctors = typeof(AsyncRecorder).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            // There must be at least one public ctor
            Assert.NotEmpty(ctors);

            // Find the (string, RecordingMetadata?) overload
            bool found = false;
            foreach (var ctor in ctors)
            {
                var parms = ctor.GetParameters();
                if (parms.Length == 2
                    && parms[0].ParameterType == typeof(string)
                    && parms[1].ParameterType == typeof(RecordingMetadata))
                {
                    found = true;
                    // The second parameter must be optional (has default null)
                    Assert.True(parms[1].HasDefaultValue, "RecordingMetadata? parameter must have a default value");
                    break;
                }
            }
            Assert.True(found, "AsyncRecorder must expose a public ctor(string, RecordingMetadata?)");
        }

        // ── Helper ────────────────────────────────────────────────────────────────

        private sealed class CapturingRegistry : ISystemRegistry
        {
            public List<IEcsModuleSystem> Systems { get; } = new();
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);
            public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
            {
                Systems.Add(system);
                return system;
            }
        }
    }
}
