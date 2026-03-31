using System;
using System.IO;
using Hrot.SimHost;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests verifying that <see cref="NodeConfiguration"/> correctly handles
    /// the fields absorbed from the deleted <c>SimHostConfig</c> type (DB-MOD1-09):
    /// <see cref="NodeConfiguration.SimulationRateHz"/> and
    /// <see cref="NodeConfiguration.GeodeticOrigin"/>.
    /// </summary>
    public class SimHostConfigTests : IDisposable
    {
        private readonly string _tempDir;

        public SimHostConfigTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"SimHostConfigTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void NodeConfiguration_Parse_SimulationRateHz_And_GeodeticOrigin()
        {
            var json = """
                {
                  "SimulationRateHz": 30,
                  "GeodeticOrigin": {
                    "Latitude": 51.5074,
                    "Longitude": -0.1278,
                    "Altitude": 15.0
                  }
                }
                """;

            var config = NodeConfiguration.Parse(json);

            Assert.Equal(30, config.SimulationRateHz);
            Assert.Equal(51.5074, config.GeodeticOrigin.Latitude,  precision: 4);
            Assert.Equal(-0.1278, config.GeodeticOrigin.Longitude, precision: 4);
            Assert.Equal(15.0,    config.GeodeticOrigin.Altitude,  precision: 2);
        }

        [Fact]
        public void NodeConfiguration_Defaults_SimulationRateHz_Is60()
        {
            var config = new NodeConfiguration();
            Assert.Equal(60, config.SimulationRateHz);
        }

        [Fact]
        public void NodeConfiguration_Defaults_GeodeticOrigin_IsNonZero()
        {
            var config = new NodeConfiguration();
            Assert.NotEqual(0.0, config.GeodeticOrigin.Latitude);
            Assert.NotEqual(0.0, config.GeodeticOrigin.Longitude);
        }
    }
}
