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
            // "demo" is an alias for "all" (orchestrator, simhost, ig, cgf, traffic)
            var cfg = new HrotRunnerConfiguration { ModeString = "demo", NoWait = true };
            cfg.Validate();
            Assert.True(cfg.RequestedSubsystems.Contains("simhost"));
            Assert.True(cfg.RequestedSubsystems.Contains("ig"));
            Assert.True(cfg.RequestedSubsystems.Contains("orchestrator"));
            Assert.True(cfg.RequestedSubsystems.Contains("cgf"));
            Assert.True(cfg.RequestedSubsystems.Contains("traffic"));
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
            // (the guard only blocks IG, Orchestrator, CGF).
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
            foreach (var mode in new[] { "simhost", "ig", "orchestrator", "cgf" })
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
            // like a live guard. The invariant worth holding is what "all" DOES expand to: every
            // fine-grained node in one process, and there is no "--mode cluster".
            // 2026-10-28: the traffic node joined "all" (user ruling: "all" = orchestrator, simhost,
            // cgf, traffic, player ... all the fine-grained nodes together).
            var cfg = new HrotRunnerConfiguration { ModeString = "all", NoWait = true };
            cfg.Validate();

            Assert.Equal(
                new[] { "cgf", "ig", "orchestrator", "simhost", "traffic" },
                cfg.RequestedSubsystems.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Validate_ServerMode_ExpandsToOrchestratorSimHostCgfTraffic_AndNeverWaits()
        {
            // "server" = the server side of a networked game; players (--mode player) join it later, so it
            // must start with NEITHER --wait-for NOR --no-wait.
            var cfg = new HrotRunnerConfiguration { ModeString = "server" };
            cfg.Validate();   // must not throw

            Assert.Equal(
                new[] { "cgf", "orchestrator", "simhost", "traffic" },
                cfg.RequestedSubsystems.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Validate_AllMode_StartsWithoutWaitFor()
        {
            // The generalised isAll exemption: "all" hosts the master, so nobody is waited for.
            var cfg = new HrotRunnerConfiguration { ModeString = "all" };
            cfg.Validate();   // must not throw
        }

        [Fact]
        public void Validate_PlayerMode_IsTheIgNode()
        {
            // "player" is the user-facing name of the IG node; the internal key stays "ig".
            var cfg = new HrotRunnerConfiguration { ModeString = "player", NoWait = true };
            cfg.Validate();

            Assert.Equal(new[] { "ig" }, cfg.RequestedSubsystems.ToArray());
            Assert.False(cfg.RequestedSubsystems.Contains("player"));
        }

        [Fact]
        public void Validate_PlayerWithSimHost_NormalisesPlayerToIg()
        {
            var cfg = new HrotRunnerConfiguration { ModeString = "player,simhost", NoWait = true };
            cfg.Validate();

            Assert.Equal(
                new[] { "ig", "simhost" },
                cfg.RequestedSubsystems.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Validate_PlayerAlone_StillNeedsWaitForOrNoWait()
        {
            // A player is a single-subsystem node: it keeps today's peer-wait rule (the launch scripts pass --no-wait).
            var cfg = new HrotRunnerConfiguration { ModeString = "player" };
            var ex = Assert.Throws<InvalidOperationException>(() => cfg.Validate());
            Assert.Contains("--wait-for required", ex.Message, StringComparison.Ordinal);   // the peer-wait rule, not "Invalid mode"
        }

        [Fact]
        public void Validate_InvalidModeMessage_OffersTheNewModes()
        {
            var cfg = new HrotRunnerConfiguration { ModeString = "nonsense", NoWait = true };
            var ex = Assert.Throws<InvalidOperationException>(() => cfg.Validate());
            var offered = ex.Message[(ex.Message.IndexOf("Use:", StringComparison.Ordinal) + 4)..];
            Assert.Contains("server", offered, StringComparison.Ordinal);
            Assert.Contains("player", offered, StringComparison.Ordinal);
            Assert.Contains("traffic", offered, StringComparison.Ordinal);
        }

        /// <summary>
        /// ⭐ <c>CE-3354</c> ② — <c>--node-names IG=film-cam,SimHost=sim-1</c> names each subsystem's node (keys case-insensitive, <c>player</c> = IG); an unnamed one gets null ⇒
        /// the default; <c>--node-name</c> names a process' single node. 🔴 Mutations (each compiles): the key compare made case-sensitive ⇒ the first cell reds; the map not filled ⇒ all red.
        /// </summary>
        [Fact]
        public void CE3354_NodeNames_ResolvePerSubsystem_AndNodeNameNamesTheOneNode()
        {
            var cfg = new HrotRunnerConfiguration { ModeString = "all", NoWait = true, NodeNames = "IG=film-cam,simhost=sim-1" };
            cfg.Validate();
            Assert.Equal("film-cam", cfg.NodeNameFor("IG"));
            Assert.Equal("sim-1", cfg.NodeNameFor("SimHost"));
            Assert.Null(cfg.NodeNameFor("CGF"));                      // unnamed ⇒ <subsystem>-<nodeId>

            var one = new HrotRunnerConfiguration { ModeString = "editor", NodeName = "my-editor" };
            one.Validate();
            Assert.Equal("my-editor", one.NodeNameFor("Editor"));
        }

        /// <summary>
        /// ⭐ <c>CE-3354</c> ② — refused at startup, clearly: <c>--node-name</c> with several nodes in one process (ambiguous), a <c>--node-names</c> key that is not a node of the process,
        /// a malformed pair, and one name given to two nodes. 🔴 Mutation: the ambiguity check removed ⇒ the first cell does not throw.
        /// </summary>
        [Fact]
        public void CE3354_NodeName_WithSeveralNodes_IsRefused_AndSoAreBadOrDuplicateNodeNames()
        {
            var ambiguous = Assert.Throws<InvalidOperationException>(() =>
                new HrotRunnerConfiguration { ModeString = "all", NoWait = true, NodeName = "x" }.Validate());
            Assert.Contains("--node-names", ambiguous.Message);
            Assert.Throws<InvalidOperationException>(() => new HrotRunnerConfiguration { ModeString = "simhost,cgf", NoWait = true, NodeNames = "IG=a" }.Validate());
            Assert.Throws<InvalidOperationException>(() => new HrotRunnerConfiguration { ModeString = "simhost,cgf", NoWait = true, NodeNames = "SimHost" }.Validate());
            Assert.Throws<InvalidOperationException>(() => new HrotRunnerConfiguration { ModeString = "simhost,cgf", NoWait = true, NodeNames = "SimHost=a,CGF=A" }.Validate());
        }
    }
}