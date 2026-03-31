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
        // ── Mode parsing ──────────────────────────────────────────────────────

        [Fact]
        public void ParseMode_All_ReturnsAllFlags()
        {
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.All, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_SimHost_ReturnsSingleFlag()
        {
            var config = new RunnerConfiguration { ModeString = "simhost", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.SimHost, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_Ig_ReturnsSingleFlag()
        {
            var config = new RunnerConfiguration { ModeString = "ig", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.IG, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_Ios_ReturnsSingleFlag()
        {
            var config = new RunnerConfiguration { ModeString = "ios", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.ExCon, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_ComboSimHostIg_ReturnsCorrectFlags()
        {
            var config = new RunnerConfiguration { ModeString = "simhost,ig", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.SimHost | RunMode.IG, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_ComboAllFour_EqualsAllFlag()
        {
            // RunMode.All = Orchestrator | SimHost | IG | ExCon; all four tokens are required.
            var config = new RunnerConfiguration { ModeString = "simhost,ig,ios,orchestrator", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.All, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_Cgf_ReturnsCgfFlag()
        {
            var config = new RunnerConfiguration { ModeString = "cgf", NoWait = true };
            config.Validate();
            Assert.Equal(RunMode.CGF, config.ParsedMode);
        }

        [Fact]
        public void ParseMode_ComboCgfOrchestrator_ReturnsBothFlags()
        {
            var config = new RunnerConfiguration { ModeString = "orchestrator,cgf", NoWait = true };
            config.Validate();
            Assert.True(config.ParsedMode.HasFlag(RunMode.CGF));
            Assert.True(config.ParsedMode.HasFlag(RunMode.Orchestrator));
        }

        [Fact]
        public void ParseMode_CgfNotInAll_ConfirmedByDirectCheck()
        {
            // CGF is a standalone mode; it is NOT included in RunMode.All by design.
            Assert.False(RunMode.All.HasFlag(RunMode.CGF));
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

        // ── Flags ─────────────────────────────────────────────────────────────

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
            Assert.Equal(RunMode.SimHost, goodConfig.ParsedMode);
        }

        // ── Wait-for peer parsing ─────────────────────────────────────────────

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

        // ── JSON config merge ─────────────────────────────────────────────────

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

        // ── Additional edge cases ─────────────────────────────────────────────

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
        public void ParseMode_AllMode_HasAllFourFlags()
        {
            // RunMode.All includes Orchestrator; CGF is excluded from All by design.
            var config = new RunnerConfiguration { ModeString = "all", NoWait = true };
            config.Validate();
            Assert.True(config.ParsedMode.HasFlag(RunMode.SimHost));
            Assert.True(config.ParsedMode.HasFlag(RunMode.IG));
            Assert.True(config.ParsedMode.HasFlag(RunMode.ExCon));
            Assert.True(config.ParsedMode.HasFlag(RunMode.Orchestrator));
            Assert.False(config.ParsedMode.HasFlag(RunMode.CGF));
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

        // ── BUG1-F002: NodeId property default ───────────────────────────────

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
    }
}
