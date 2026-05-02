using System.IO;
using System.Text.Json;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NodeConfiguration"/> and CLI role-parsing helpers
    /// introduced by MOD1-P3T5.
    /// </summary>
    public class NodeConfigurationTests
    {
        // ── NodeConfiguration defaults ────────────────────────────────────────

        [Fact]
        public void NodeConfiguration_LoadFrom_ReturnsDefaults_WhenFileAbsent()
        {
            // Use a path that definitely does not exist.
            var config = NodeConfiguration.LoadFrom("/nonexistent/path/that-does-not-exist.json");

            Assert.Equal(42u, config.DdsDomainId);
            Assert.Equal(string.Empty, config.CycloneDdsConfigPath);
            Assert.Equal(string.Empty, config.RoadNetworkBlobPath);
            Assert.Equal(string.Empty, config.BehaviorRegistryPath);
            Assert.Equal(string.Empty, config.EntityTemplatePath);
        }

        [Fact]
        public void NodeConfiguration_LoadFrom_DoesNotThrow_WhenFileAbsent()
        {
            var ex = Record.Exception(
                () => NodeConfiguration.LoadFrom("/path/that/does/not/exist/config.json"));
            Assert.Null(ex);
        }

        [Fact]
        public void NodeConfiguration_LoadFrom_ReturnsDefaults_WhenFileInvalid()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "{ this is not json }");
                var config = NodeConfiguration.LoadFrom(tempFile);
                Assert.Equal(42u, config.DdsDomainId);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void NodeConfiguration_LoadFrom_ParsesValidJson()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    DdsDomainId         = 7u,
                    CycloneDdsConfigPath = "Config/dds-node.xml",
                    RoadNetworkBlobPath  = "Assets/roads.json",
                });
                File.WriteAllText(tempFile, json);

                var config = NodeConfiguration.LoadFrom(tempFile);

                Assert.Equal(7u, config.DdsDomainId);
                Assert.Equal("Config/dds-node.xml", config.CycloneDdsConfigPath);
                Assert.Equal("Assets/roads.json", config.RoadNetworkBlobPath);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        // ── NodeConfiguration.Parse ───────────────────────────────────────────

        [Fact]
        public void NodeConfiguration_Parse_ReturnsDefaults_WhenNullInput()
        {
            var config = NodeConfiguration.Parse(null);
            Assert.Equal(42u, config.DdsDomainId);
        }

        [Fact]
        public void NodeConfiguration_Parse_ReturnsDefaults_WhenEmptyString()
        {
            var config = NodeConfiguration.Parse(string.Empty);
            Assert.Equal(42u, config.DdsDomainId);
        }

        // ── SimHostApp.ParseRole ──────────────────────────────────────────────

        [Fact]
        public void SimHostApp_ParsesRole_Brain()
        {
            var role = SimHostApp.ParseRole(new[] { "--role", "Brain" });
            Assert.Equal(NodeRole.Brain, role);
        }

        [Fact]
        public void SimHostApp_ParsesRole_MuscleGround()
        {
            var role = SimHostApp.ParseRole(new[] { "--role", "MuscleGround" });
            Assert.Equal(NodeRole.MuscleGround, role);
        }

        [Fact]
        public void SimHostApp_ParsesRole_CaseInsensitive()
        {
            var role = SimHostApp.ParseRole(new[] { "--role", "musclEground" });
            Assert.Equal(NodeRole.MuscleGround, role);
        }

        [Fact]
        public void SimHostApp_ParsesRole_DefaultsToStandaloneRole()
        {
            var role = SimHostApp.ParseRole(System.Array.Empty<string>());
            Assert.Equal(NodeRole.MuscleGround | NodeRole.Perception, role);
        }

        [Fact]
        public void SimHostApp_ParsesRole_DefaultsToStandaloneRole_WhenFlagAbsent()
        {
            var role = SimHostApp.ParseRole(new[] { "--domain", "42" });
            Assert.Equal(NodeRole.MuscleGround | NodeRole.Perception, role);
        }

        [Fact]
        public void SimHostApp_ParsesRole_DefaultsToStandaloneRole_WhenValueUnrecognised()
        {
            var role = SimHostApp.ParseRole(new[] { "--role", "UnknownRole" });
            Assert.Equal(NodeRole.MuscleGround | NodeRole.Perception, role);
        }

        // ── SimHostApp.ParseNodeConfig ────────────────────────────────────────

        [Fact]
        public void SimHostApp_ParseNodeConfig_ReturnsDefaults_WhenFlagAbsent()
        {
            var config = SimHostApp.ParseNodeConfig(System.Array.Empty<string>());
            Assert.Equal(42u, config.DdsDomainId);
        }

        [Fact]
        public void SimHostApp_ParseNodeConfig_LoadsFromFile_WhenFlagPresent()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var json = JsonSerializer.Serialize(new { DdsDomainId = 99u });
                File.WriteAllText(tempFile, json);

                var config = SimHostApp.ParseNodeConfig(new[] { "--config", tempFile });

                Assert.Equal(99u, config.DdsDomainId);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}
