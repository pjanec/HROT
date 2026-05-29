using System;
using System.IO;
using CarKinem.Road;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Xunit;

namespace CarKinem.Tests.Road
{
    public class RoadNetworkLoaderTests
    {
        private readonly string _testDataPath;
        
        public RoadNetworkLoaderTests()
        {
            // Assume test data is in TestData subfolder
            _testDataPath = Path.Combine(
                Path.GetDirectoryName(typeof(RoadNetworkLoaderTests).Assembly.Location)!,
                "Road", "TestData", "sample_road.json"
            );
        }
        
        [Fact]
        public void LoadFromJson_ValidFile_LoadsSuccessfully()
        {
            if (!File.Exists(_testDataPath))
            {
                // Create test file dynamically if missing
                Directory.CreateDirectory(Path.GetDirectoryName(_testDataPath)!);
                File.WriteAllText(_testDataPath, GetSampleJson());
            }
            
            var blob = RoadNetworkLoader.LoadFromJson(_testDataPath);
            
            Assert.Equal(3, blob.Nodes.Length);
            Assert.Equal(2, blob.Segments.Length);
            
            blob.Dispose();
        }
        
        [Fact]
        public void LoadFromJson_MissingFile_ThrowsException()
        {
            Assert.Throws<FileNotFoundException>(() =>
                RoadNetworkLoader.LoadFromJson("nonexistent.json")
            );
        }
        
        [Fact]
        public void LoadFromJson_LoadsSegmentProperties()
        {
            if (!File.Exists(_testDataPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_testDataPath)!);
                File.WriteAllText(_testDataPath, GetSampleJson());
            }
            
            var blob = RoadNetworkLoader.LoadFromJson(_testDataPath);
            
            // First segment id 0
            Assert.Equal(25f, blob.Segments[0].SpeedLimit);
            Assert.Equal(3.5f, blob.Segments[0].LaneWidth);
            Assert.Equal(2, blob.Segments[0].LaneCount);
            
            blob.Dispose();
        }

        [Fact]
        public void LoadFromJson_Phase2Format_WithAdapter_LoadsCorrectly()
        {
            string phase2Json = @"{
  ""$meta"": { ""docType"": ""Fdp.RoadNetwork"", ""schemaVersion"": 1 },
  ""nodes"": [
    { ""id"": 0, ""position"": { ""x"": 0, ""y"": 0 } },
    { ""id"": 1, ""position"": { ""x"": 100, ""y"": 0 } },
    { ""id"": 2, ""position"": { ""x"": 100, ""y"": 100 } }
  ],
  ""segments"": [
    {
      ""id"": 0,
      ""startNodeId"": 0,
      ""endNodeId"": 1,
      ""controlPoints"": {
        ""p0"": { ""x"": 0, ""y"": 0 },
        ""t0"": { ""x"": 50, ""y"": 0 },
        ""p1"": { ""x"": 100, ""y"": 0 },
        ""t1"": { ""x"": 50, ""y"": 0 }
      },
      ""speedLimit"": 25.0,
      ""laneWidth"": 3.5,
      ""laneCount"": 2
    },
    {
      ""id"": 1,
      ""startNodeId"": 1,
      ""endNodeId"": 2,
      ""controlPoints"": {
        ""p0"": { ""x"": 100, ""y"": 0 },
        ""t0"": { ""x"": 0, ""y"": 50 },
        ""p1"": { ""x"": 100, ""y"": 100 },
        ""t1"": { ""x"": 0, ""y"": 50 }
      },
      ""speedLimit"": 20.0,
      ""laneWidth"": 3.5,
      ""laneCount"": 1
    }
  ],
  ""metadata"": {
    ""worldBounds"": {
      ""min"": { ""x"": -10, ""y"": -10 },
      ""max"": { ""x"": 110, ""y"": 110 }
    },
    ""gridCellSize"": 5.0
  }
}";
            string tempPath = Path.GetTempFileName();
            File.WriteAllText(tempPath, phase2Json);
            try
            {
                var registry = new MigrationRegistry();
                registry.RegisterPassthroughDocType("Fdp.RoadNetwork", 1);
                var adapter = new ReadOnlyMigrationAdapter(new MigrationPipeline(registry));

                var blob = RoadNetworkLoader.LoadFromJson(tempPath, adapter);

                Assert.Equal(3, blob.Nodes.Length);
                Assert.Equal(2, blob.Segments.Length);

                blob.Dispose();
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        [Fact]
        public void LoadFromJson_LegacyFormat_NoAdapter_LoadsCorrectly()
        {
            string tempPath = Path.GetTempFileName();
            File.WriteAllText(tempPath, GetSampleJson());
            try
            {
                var blob = RoadNetworkLoader.LoadFromJson(tempPath);

                Assert.Equal(3, blob.Nodes.Length);
                Assert.Equal(2, blob.Segments.Length);

                blob.Dispose();
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        private string GetSampleJson()
        {
            return @"{
  ""nodes"": [
    { ""id"": 0, ""position"": { ""x"": 0, ""y"": 0 } },
    { ""id"": 1, ""position"": { ""x"": 100, ""y"": 0 } },
    { ""id"": 2, ""position"": { ""x"": 100, ""y"": 100 } }
  ],
  ""segments"": [
    {
      ""id"": 0,
      ""startNodeId"": 0,
      ""endNodeId"": 1,
      ""controlPoints"": {
        ""p0"": { ""x"": 0, ""y"": 0 },
        ""t0"": { ""x"": 50, ""y"": 0 },
        ""p1"": { ""x"": 100, ""y"": 0 },
        ""t1"": { ""x"": 50, ""y"": 0 }
      },
      ""speedLimit"": 25.0,
      ""laneWidth"": 3.5,
      ""laneCount"": 2
    },
    {
      ""id"": 1,
      ""startNodeId"": 1,
      ""endNodeId"": 2,
      ""controlPoints"": {
        ""p0"": { ""x"": 100, ""y"": 0 },
        ""t0"": { ""x"": 0, ""y"": 50 },
        ""p1"": { ""x"": 100, ""y"": 100 },
        ""t1"": { ""x"": 0, ""y"": 50 }
      },
      ""speedLimit"": 20.0,
      ""laneWidth"": 3.5,
      ""laneCount"": 1
    }
  ],
  ""metadata"": {
    ""worldBounds"": {
      ""min"": { ""x"": -10, ""y"": -10 },
      ""max"": { ""x"": 110, ""y"": 110 }
    },
    ""gridCellSize"": 5.0
  }
}";
        }
    }
}
