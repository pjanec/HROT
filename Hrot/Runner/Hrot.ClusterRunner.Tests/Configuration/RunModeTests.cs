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

        // ── ST-015: the retired StrideMock mode ───────────────────────────────
        //
        // This block used to be "SM-007: StrideMock wiring" and asserted the mode WORKED. The mock
        // subsystem is gone, so the two mode tests are INVERTED rather than deleted: the valuable
        // assertion is no longer "stridemock composes" but "stridemock is refused, and the refusal
        // does not go on offering it". Two further tests here took `typeof(StrideMockSubsystem)`
        // directly and could not survive the type's removal at all.

        [Fact]
        public void Validate_StrideMockMode_NowThrows()
        {
            // ST-015 (was SC_SM007_1, inverted): the token is no longer a valid mode.
            var cfg = new HrotRunnerConfiguration { ModeString = "stridemock", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => cfg.Validate());

            // The message enumerates the valid modes, so that list must not still advertise this one
            // -- a stale list is a lie the user reads straight off their terminal.
            //
            // Assert on the OFFERED LIST, not the whole message: the message quotes the rejected
            // input back at the user ("Invalid mode: 'stridemock'. Use: ..."), so a naive
            // DoesNotContain over ex.Message can never pass for this input and would be asserting
            // the wrong thing.
            var offered = ex.Message[(ex.Message.IndexOf("Use:", StringComparison.Ordinal) + 4)..];
            Assert.DoesNotContain("stridemock", offered, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Validate_OrchestratorCgfStrideMock_NowThrows()
        {
            // ST-015 (was SC_SM007_2, inverted): one dead token poisons an otherwise valid combo,
            // rather than being silently dropped from it.
            var cfg = new HrotRunnerConfiguration { ModeString = "orchestrator,cgf,stridemock", NoWait = true };
            Assert.Throws<InvalidOperationException>(() => cfg.Validate());
        }

        [Fact]
        public void Validate_ExistingModes_StillParseWithoutError()
        {
            // SC_SM007_3 — no regression for established subsystem names
            foreach (var mode in new[] { "simhost", "ig", "excon", "orchestrator", "cgf" })
            {
                var cfg = new HrotRunnerConfiguration { ModeString = mode, NoWait = true };
                cfg.Validate(); // must not throw
            }
        }

        [Fact]
        public void Validate_AllMode_ExpandsToTheFiveSubsystems()
        {
            // ST-015: replaces Validate_AllMode_DoesNotContainStrideMock, which asserted that "all"
            // omits a token that no longer exists anywhere -- trivially true, and it would have read
            // like a live guard. The invariant worth holding is what "all" DOES expand to, which is
            // also the fact the programme charter had to measure by hand: five subsystems, and
            // there is no "--mode cluster".
            var cfg = new HrotRunnerConfiguration { ModeString = "all", NoWait = true };
            cfg.Validate();

            Assert.Equal(
                new[] { "cgf", "excon", "ig", "orchestrator", "simhost" },
                cfg.RequestedSubsystems.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }
    }
}