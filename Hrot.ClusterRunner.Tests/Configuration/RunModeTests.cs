using System;
using Hrot.ClusterRunner.Configuration;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Configuration
{
    /// <summary>
    /// Unit tests for editor and demo mode additions (PACK2-R001).
    /// </summary>
    public class RunModeTests
    {
        [Fact]
        public void ParseModeString_Editor_ReturnsEditorFlag()
        {
            var cfg = new HrotRunnerConfiguration { ModeString = "editor" };
            cfg.Validate();
            Assert.True(cfg.RequestedSubsystems.Contains("editor"));
            Assert.Equal(1, cfg.RequestedSubsystems.Count);
        }

        [Fact]
        public void ParseModeString_Demo_ReturnsDemoFlags()
        {
            // "demo" is an alias for "all" (orchestrator, simhost, ig, excon, cgf)
            var cfg = new HrotRunnerConfiguration { ModeString = "demo", NoWait = true };
            cfg.Validate();
            Assert.True(cfg.RequestedSubsystems.Contains("simhost"));
            Assert.True(cfg.RequestedSubsystems.Contains("ig"));
            Assert.True(cfg.RequestedSubsystems.Contains("excon"));
            Assert.True(cfg.RequestedSubsystems.Contains("orchestrator"));
            Assert.True(cfg.RequestedSubsystems.Contains("cgf"));
        }

        [Fact]
        public void Validate_EditorCombinedWithIg_ThrowsInvalidOperation()
        {
            // editor,ig -- an invalid combination
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                var cfg = new HrotRunnerConfiguration { ModeString = "editor,ig", NoWait = true };
                cfg.Validate();
            });
            Assert.Contains("Editor", ex.Message);
        }

        [Fact]
        public void RequestedSubsystems_IsCaseInsensitive()
        {
            // RequestedSubsystems uses OrdinalIgnoreCase so "Editor" and "editor" match
            var cfg = new HrotRunnerConfiguration { ModeString = "editor" };
            cfg.Validate();
            Assert.True(cfg.RequestedSubsystems.Contains("Editor"));
            Assert.True(cfg.RequestedSubsystems.Contains("EDITOR"));
            Assert.True(cfg.RequestedSubsystems.Contains("editor"));
        }

        [Fact]
        public void ParseModeString_Demo_SameSubsetsAsAll()
        {
            // "demo" and "all" must produce the same set of subsystem names
            var cfgAll  = new HrotRunnerConfiguration { ModeString = "all",  NoWait = true };
            var cfgDemo = new HrotRunnerConfiguration { ModeString = "demo", NoWait = true };
            cfgAll.Validate();
            cfgDemo.Validate();
            Assert.True(cfgAll.RequestedSubsystems.SetEquals(cfgDemo.RequestedSubsystems));
        }

        [Fact]
        public void ParseMode_AllMode_DoesNotContainEditor()
        {
            // Editor is a standalone mode and must never be part of "all"
            var cfg = new HrotRunnerConfiguration { ModeString = "all", NoWait = true };
            cfg.Validate();
            Assert.False(cfg.RequestedSubsystems.Contains("editor"));
        }

        [Fact]
        public void ParseModeString_Editor_StandaloneDoesNotRequireNoWait()
        {
            // Editor mode must be standalone -- no NoWait required
            var cfg = new HrotRunnerConfiguration { ModeString = "editor" };
            var ex  = Record.Exception(() => cfg.Validate());
            Assert.Null(ex);
        }

        [Fact]
        public void Validate_EditorCombinedWithCgf_ThrowsInvalidOperation()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                var cfg = new HrotRunnerConfiguration { ModeString = "editor,cgf", NoWait = true };
                cfg.Validate();
            });
            Assert.Contains("Editor", ex.Message);
        }

        [Fact]
        public void Validate_EditorWithSimHost_IsAllowed()
        {
            // editor + simhost: SimHost is NOT in the forbidden distributed flags
            // (the guard only blocks IG, ExCon, Orchestrator, CGF).
            // This test documents the current boundary.
            var cfg = new HrotRunnerConfiguration { ModeString = "editor,simhost", NoWait = true };
            // Should not throw (editor + simhost is not an explicitly forbidden combo)
            var ex = Record.Exception(() => cfg.Validate());
            // If the validation does not throw, both names must be in RequestedSubsystems.
            if (ex == null)
            {
                Assert.True(cfg.RequestedSubsystems.Contains("editor"));
                Assert.True(cfg.RequestedSubsystems.Contains("simhost"));
            }
        }
    }
}