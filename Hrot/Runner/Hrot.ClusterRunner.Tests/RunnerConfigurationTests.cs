using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Newtonsoft.Json;
using Hrot.ClusterRunner.Configuration;
using RunnerConfiguration = Hrot.ClusterRunner.Configuration.HrotRunnerConfiguration;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RunnerConfiguration"/> covering mode parsing,
    /// flag handling, wait-for peer parsing, JSON merge, and validation errors.
    /// </summary>
    public class RunnerConfigurationTests
    {
        // -- Mode parsing -------------------------------------------------------

        [Fact]
        public void ParseMode_All_ReturnsAllFlags()
        {
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("simhost"));
            Assert.True(config.RequestedSubsystems.Contains("ig"));
            Assert.True(config.RequestedSubsystems.Contains("excon"));
            Assert.True(config.RequestedSubsystems.Contains("orchestrator"));
            Assert.True(config.RequestedSubsystems.Contains("cgf"));
        }

        [Fact]
        public void ParseMode_SimHost_ReturnsSingleFlag()
        {
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("simhost"));
            Assert.Equal(1, config.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseMode_Ig_ReturnsSingleFlag()
        {
            var config = new RunnerConfiguration { ModeString = "ig", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("ig"));
            Assert.Equal(1, config.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseMode_Ios_ReturnsSingleFlag()
        {
            // "ios" is a legacy alias for "excon"
            var config = new RunnerConfiguration { ModeString = "ios", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("excon"));
            Assert.Equal(1, config.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseMode_ComboSimHostIg_ReturnsCorrectFlags()
        {
            var config = new RunnerConfiguration { ModeString = "simhost,ig", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("simhost"));
            Assert.True(config.RequestedSubsystems.Contains("ig"));
            Assert.Equal(2, config.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseMode_ComboAllFive_EqualsAllFlag()
        {
            // All five tokens must produce the same set as "all".
            var config = new RunnerConfiguration { ModeString = "simhost,ig,ios,orchestrator,cgf", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("simhost"));
            Assert.True(config.RequestedSubsystems.Contains("ig"));
            Assert.True(config.RequestedSubsystems.Contains("excon"));
            Assert.True(config.RequestedSubsystems.Contains("orchestrator"));
            Assert.True(config.RequestedSubsystems.Contains("cgf"));
        }

        [Fact]
        public void ParseMode_Cgf_ReturnsCgfFlag()
        {
            var config = new RunnerConfiguration { ModeString = "cgf", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("cgf"));
            Assert.Equal(1, config.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseMode_ComboCgfOrchestrator_ReturnsBothFlags()
        {
            var config = new RunnerConfiguration { ModeString = "orchestrator,cgf", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("cgf"));
            Assert.True(config.RequestedSubsystems.Contains("orchestrator"));
        }

        [Fact]
        public void ParseMode_CgfInAll_ConfirmedByDirectCheck()
        {
            // CGF is included when "all" is specified.
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("cgf"));
        }

        [Fact]
        public void ParseMode_InvalidToken_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "invalid" };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Invalid mode", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseMode_EmptyString_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "" };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Invalid mode", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseMode_PartialInvalidCombo_ThrowsInvalidOperation()
        {
            // "simhost,bad" should fail because "bad" is not a valid token
            var config = new RunnerConfiguration { ModeString = "simhost,bad" };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Invalid mode", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // -- Flags -------------------------------------------------------------

        [Fact]
        public void HeadlessFlag_Default_IsFalse()
        {
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.False(config.Headless);
        }

        [Fact]
        public void HeadlessFlag_Set_IsTrue()
        {
            var config = new RunnerConfiguration { ModeString = "all", Headless = true, NoWait = true };
            config.Validate();
            Assert.True(config.Headless);
        }

        [Fact]
        public void NoWaitFlag_SuppressesWaitForRequirement()
        {
            // Separate subsystem without --wait-for: should fail without --no-wait
            var badConfig = new RunnerConfiguration { ModeString = "simhost" };
            Assert.Throws<InvalidOperationException>(() => badConfig.Validate());

            // Same config with --no-wait: should pass
            var goodConfig = new RunnerConfiguration { ModeString = "simhost", NoWait = true };
            goodConfig.Validate(); // Should not throw
            Assert.True(goodConfig.RequestedSubsystems.Contains("simhost"));
        }

        // -- Wait-for peer parsing ---------------------------------------------

        [Fact]
        public void WaitFor_ParsesCommaSeparatedPeers()
        {
            var config = new RunnerConfiguration
            {
                ModeString    = "simhost",
                WaitForString = "ig,ios"
            };
            config.Validate();
            Assert.Contains("ig",  config.WaitForPeers);
            Assert.Contains("ios", config.WaitForPeers);
            Assert.Equal(2, config.WaitForPeers.Count);
        }

        [Fact]
        public void WaitFor_ThreePeers_AllParsed()
        {
            var config = new RunnerConfiguration
            {
                ModeString    = "simhost",
                WaitForString = "simhost,ig,ios"
            };
            config.Validate();
            Assert.Equal(3, config.WaitForPeers.Count);
        }

        [Fact]
        public void SeparateMode_WithoutWaitFor_AndWithoutNoWait_ThrowsError()
        {
            var config = new RunnerConfiguration { ModeString = "ig" };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("wait-for", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // -- JSON config merge -------------------------------------------------

        [Fact]
        public void MergeFromJsonFile_OverridesModeString()
        {
            var json = JsonConvert.SerializeObject(new { ModeString = "ig" });
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, json);

                var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
                config.MergeFromJsonFile(path);

                Assert.Equal("ig", config.ModeString);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MergeFromJsonFile_OverridesDomainId()
        {
            var json = JsonConvert.SerializeObject(new { DomainId = 42 });
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, json);

                var config = new RunnerConfiguration { ModeString = "all", NoWait = true, DomainId = 0 };
                config.MergeFromJsonFile(path);

                Assert.Equal(42, config.DomainId);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MergeFromJsonFile_MissingFile_ThrowsFileNotFound()
        {
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            var ex = Assert.Throws<FileNotFoundException>(() =>
                config.MergeFromJsonFile("nonexistent_config_12345.json"));
            Assert.Contains("Config file not found", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // -- Additional edge cases ---------------------------------------------

        [Fact]
        public void DomainId_DefaultValue_IsZero()
        {
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.Equal(0, config.DomainId);
        }

        [Fact]
        public void DomainId_CustomValue_IsPreserved()
        {
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true, DomainId = 7 };
            config.Validate();
            Assert.Equal(7, config.DomainId);
        }

        [Fact]
        public void ParseMode_AllMode_HasAllFiveFlags()
        {
            // "all" includes all five subsystems: Orchestrator, SimHost, IG, ExCon, and CGF.
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("simhost"));
            Assert.True(config.RequestedSubsystems.Contains("ig"));
            Assert.True(config.RequestedSubsystems.Contains("excon"));
            Assert.True(config.RequestedSubsystems.Contains("orchestrator"));
            Assert.True(config.RequestedSubsystems.Contains("cgf"));
        }

        [Fact]
        public void WaitFor_InvalidPeerName_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration
            {
                ModeString    = "simhost",
                WaitForString = "badpeer"
            };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("badpeer", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // -- BUG1-F002: NodeId property default --------------------------------

        [Fact]
        public void NodeId_DefaultsToZero()
        {
            // When --node-id is not supplied the property must be 0 (legacy fallback sentinel).
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true };
            config.Validate();
            Assert.Equal(0, config.NodeId);
        }

        [Fact]
        public void NodeId_SetExplicitly_PreservedAfterValidate()
        {
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true, NodeId = 42 };
            config.Validate();
            Assert.Equal(42, config.NodeId);
        }

        // -- Editor mode -------------------------------------------------------

        [Fact]
        public void ParseMode_Editor_ReturnsEditorFlag()
        {
            var config = new RunnerConfiguration { ModeString = "editor", NoWait = true };
            config.Validate();
            Assert.True(config.RequestedSubsystems.Contains("editor"));
            Assert.Equal(1, config.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseMode_AllMode_DoesNotIncludeEditor()
        {
            // Editor is a standalone mode and must never be part of "all".
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.False(config.RequestedSubsystems.Contains("editor"));
        }

        [Fact]
        public void ParseMode_EditorWithIg_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "editor,ig", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Editor", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseMode_EditorWithExCon_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "editor,excon", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Editor", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseMode_EditorWithOrchestrator_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "editor,orchestrator", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Editor", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseMode_EditorWithCgf_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "editor,cgf", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Editor", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseMode_EditorWithAll_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "editor,all", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        // -- --network option --------------------------------------------------

        [Fact]
        public void NetworkProtocol_Default_IsNed()
        {
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true };
            config.Validate();
            Assert.Equal("ned", config.NetworkProtocol, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void NetworkProtocol_Bdc_SetCorrectly()
        {
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true, NetworkProtocol = "bdc" };
            config.Validate();
            Assert.Equal("bdc", config.NetworkProtocol, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void NetworkProtocol_InvalidValue_ThrowsInvalidOperation()
        {
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true, NetworkProtocol = "unknown" };
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("--network", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}