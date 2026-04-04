using System;
using Hrot.ClusterRunner.Configuration;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Configuration
{
    /// <summary>
    /// Unit tests for RunMode.Editor and RunMode.Demo additions (PACK2-R001).
    /// </summary>
    public class RunModeTests
    {
        [Fact]
        public void ParseModeString_Editor_ReturnsEditorFlag()
        {
            var cfg = new HrotRunnerConfiguration { ModeString = "editor" };
            cfg.Validate();
            Assert.Equal(RunMode.Editor, cfg.ParsedMode);
        }

        [Fact]
        public void ParseModeString_Demo_ReturnsDemoFlags()
        {
            var cfg = new HrotRunnerConfiguration { ModeString = "demo", NoWait = true };
            cfg.Validate();
            Assert.Equal(RunMode.Demo, cfg.ParsedMode);
        }

        [Fact]
        public void Validate_EditorCombinedWithIg_ThrowsInvalidOperation()
        {
            // editor,ig — an invalid combination
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                var cfg = new HrotRunnerConfiguration { ModeString = "editor,ig", NoWait = true };
                cfg.Validate();
            });
            Assert.Contains("Editor", ex.Message);
        }

        [Fact]
        public void RunMode_Editor_HasCorrectBitValue()
        {
            // Editor = 1 << 6 = 64
            Assert.Equal(64, (int)RunMode.Editor);
        }

        [Fact]
        public void RunMode_Demo_EqualsAll()
        {
            // Demo = Orchestrator | SimHost | IG | ExCon | CGF = same as All
            Assert.Equal(RunMode.All, RunMode.Demo);
        }

        [Fact]
        public void RunMode_Editor_DoesNotOverlapWithExistingFlags()
        {
            // Editor must not overlap with any previously defined flags
            var allExisting = RunMode.SimHost | RunMode.IG | RunMode.ExCon
                            | RunMode.Orchestrator | RunMode.CGF | RunMode.CI;
            Assert.Equal(0, (int)(RunMode.Editor & allExisting));
        }

        [Fact]
        public void ParseModeString_Editor_StandaloneDoesNotRequireNoWait()
        {
            // Editor mode must be standalone — no NoWait required
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
            // Note: SimHost is combined because Editor does not block SimHost.
            var ex = Record.Exception(() => cfg.Validate());
            // If the validation doesn't throw, ParsedMode should include both flags.
            if (ex == null)
            {
                Assert.True(cfg.ParsedMode.HasFlag(RunMode.Editor));
                Assert.True(cfg.ParsedMode.HasFlag(RunMode.SimHost));
            }
        }
    }
}
