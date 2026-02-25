using System;
using System.IO;
using System.Text.Json;
using Bagira.SimHost.Configuration;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SimHostConfig"/> (TASK-S5.2).
    /// </summary>
    public class SimHostConfigTests : IDisposable
    {
        // Use a temp directory so tests are isolated from the workspace.
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

        // ── S5.2 Test 1: valid payload parses ────────────────────────────────────

        /// <summary>
        /// Loading a well-formed JSON file must return the correct property values.
        /// </summary>
        [Fact]
        public void SimHostConfig_Load_ValidJson_ReturnsCorrectValues()
        {
            // Arrange
            var filePath = Path.Combine(_tempDir, "config.json");
            var json = """
                {
                  "DomainId": 5,
                  "SimulationRateHz": 30,
                  "GeodeticOrigin": {
                    "Latitude": 51.5074,
                    "Longitude": -0.1278,
                    "Altitude": 15.0
                  }
                }
                """;
            File.WriteAllText(filePath, json);

            // Act
            var config = SimHostConfig.Load(filePath);

            // Assert
            Assert.Equal(5,       config.DomainId);
            Assert.Equal(30,      config.SimulationRateHz);
            Assert.Equal(51.5074, config.GeodeticOrigin.Latitude,  precision: 4);
            Assert.Equal(-0.1278, config.GeodeticOrigin.Longitude, precision: 4);
            Assert.Equal(15.0,    config.GeodeticOrigin.Altitude,  precision: 2);
        }

        // ── S5.2 Test 2: missing file creates defaults ────────────────────────────

        /// <summary>
        /// When the config file does not exist, <see cref="SimHostConfig.Load"/> must:
        /// <list type="bullet">
        ///   <item>Return default values.</item>
        ///   <item>Write the defaults to disk so the file exists afterwards.</item>
        /// </list>
        /// </summary>
        [Fact]
        public void SimHostConfig_Load_MissingFile_WritesDefaultsToDisk()
        {
            // Arrange — path that does NOT exist yet
            var filePath = Path.Combine(_tempDir, "missing_config.json");
            Assert.False(File.Exists(filePath), "Pre-condition: file must not exist");

            // Act
            var config = SimHostConfig.Load(filePath);

            // Assert: defaults returned
            Assert.Equal(0,  config.DomainId);
            Assert.Equal(60, config.SimulationRateHz);
            Assert.True(config.GeodeticOrigin.Latitude  != 0);
            Assert.True(config.GeodeticOrigin.Longitude != 0);

            // Assert: file written to disk
            Assert.True(File.Exists(filePath), "Load must create the file when it is missing");

            // Assert: written file is valid JSON that round-trips
            var reloaded = SimHostConfig.Load(filePath);
            Assert.Equal(config.DomainId,           reloaded.DomainId);
            Assert.Equal(config.SimulationRateHz,   reloaded.SimulationRateHz);
            Assert.Equal(config.GeodeticOrigin.Latitude,  reloaded.GeodeticOrigin.Latitude);
            Assert.Equal(config.GeodeticOrigin.Longitude, reloaded.GeodeticOrigin.Longitude);
            Assert.Equal(config.GeodeticOrigin.Altitude,  reloaded.GeodeticOrigin.Altitude);
        }

        // ── Additional: Save round-trip ───────────────────────────────────────────

        /// <summary>
        /// A config written by <see cref="SimHostConfig.Save"/> must be readable back
        /// with the same values.
        /// </summary>
        [Fact]
        public void SimHostConfig_Save_RoundTrip_PreservesAllValues()
        {
            // Arrange
            var filePath = Path.Combine(_tempDir, "saved_config.json");
            var original = new SimHostConfig
            {
                DomainId         = 7,
                SimulationRateHz = 120,
                GeodeticOrigin   = new GeodeticOriginConfig
                {
                    Latitude  = 48.8566,
                    Longitude = 2.3522,
                    Altitude  = 35.0
                }
            };

            // Act
            SimHostConfig.Save(original, filePath);
            var loaded = SimHostConfig.Load(filePath);

            // Assert
            Assert.Equal(original.DomainId,                    loaded.DomainId);
            Assert.Equal(original.SimulationRateHz,            loaded.SimulationRateHz);
            Assert.Equal(original.GeodeticOrigin.Latitude,     loaded.GeodeticOrigin.Latitude);
            Assert.Equal(original.GeodeticOrigin.Longitude,    loaded.GeodeticOrigin.Longitude);
            Assert.Equal(original.GeodeticOrigin.Altitude,     loaded.GeodeticOrigin.Altitude);
        }
    }
}
