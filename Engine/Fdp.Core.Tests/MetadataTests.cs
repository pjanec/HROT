using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Kernel.FlightRecorder;
using Fdp.Kernel.FlightRecorder.Metadata;
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
    }
}
