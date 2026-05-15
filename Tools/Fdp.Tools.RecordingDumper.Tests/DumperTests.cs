using System;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Support;
using Xunit;

namespace Fdp.Tools.RecordingDumper.Tests
{
    /// <summary>
    /// Integration and unit tests for the fdp-recording-dumper CLI (EX-T30, EX-T31, EX-T32).
    /// </summary>
    public class DumperTests : IDisposable
    {
        public DumperTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        // ── EX-T30: switch round-trip ─────────────────────────────────────────

        [Fact]
        public void EX_T30_AllSwitches_MappedToCorrectOptions()
        {
            // We cannot inspect the internal JsonExportOptions directly from
            // RunMain without intercepting the service call, so instead we
            // exercise a real run with every flag and verify the JSON output
            // reflects each option.

            string fdpPath = BuildFiveFrameRecording();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var stdout = new StringWriter();
                var stderr = new StringWriter();

                int code = Program.RunMain(
                    new[]
                    {
                        "-i", fdpPath,
                        "-o", outPath,
                        "-s", "2",
                        "-e", "3",
                        "--no-events",
                        "--no-entities",
                        "--minified",
                        "--epsilon", "0.005",
                    },
                    stdout,
                    stderr);

                Assert.Equal(0, code);
                Assert.True(File.Exists(outPath), "Output file should have been created.");

                string text = File.ReadAllText(outPath);
                // Minified: no newlines
                Assert.DoesNotContain("\n", text);

                var root = JsonNode.Parse(text)!.AsObject();
                // --start-frame 2 --end-frame 3 -> ByFrame window -> 2 frames
                Assert.Equal(2, root["Frames"]!.AsArray().Count);

                // --no-entities: no Entities block
                foreach (var frame in root["Frames"]!.AsArray())
                    Assert.Null(frame!["Entities"]);

                // --no-events: no Events block
                foreach (var frame in root["Frames"]!.AsArray())
                    Assert.Null(frame!["Events"]);
            }
            finally { TryDelete(outPath); TryDelete(fdpPath); }
        }

        // ── EX-T31: conflicting frame/time options returns exit code 1 ─────────

        [Fact]
        public void EX_T31_ConflictingFrameAndTimeOptions_ReturnsExitCode1()
        {
            string fdpPath = BuildFiveFrameRecording();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var stdout = new StringWriter();
                var stderr = new StringWriter();

                int code = Program.RunMain(
                    new[]
                    {
                        "-i", fdpPath,
                        "-o", outPath,
                        "--start-frame", "2",
                        "--start-time", "0.5",  // conflict
                    },
                    stdout,
                    stderr);

                Assert.Equal(1, code);
                string errText = stderr.ToString();
                Assert.Contains("mutually exclusive", errText, StringComparison.OrdinalIgnoreCase);
            }
            finally { TryDelete(outPath); TryDelete(fdpPath); }
        }

        // ── EX-T32: CLI integration — same output as direct service call ───────

        [Fact]
        public void EX_T32_CliIntegration_MatchesDirectServiceOutput()
        {
            string fdpPath = BuildFixtureRecording();
            string cliOut = Path.GetTempFileName() + ".json";
            string svcOut = Path.GetTempFileName() + ".json";
            try
            {
                // Run via CLI
                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int code = Program.RunMain(
                    new[]
                    {
                        "-i", fdpPath,
                        "-o", cliOut,
                        "--minified",
                        "--no-events",
                    },
                    stdout,
                    stderr);

                Assert.Equal(0, code);

                // Run via service directly with same options.
                // Note: ComponentTypeRegistry.Clear() must NOT be called here because
                // SchemaValidator requires component types from the recording to remain
                // registered; the harness already registered HarnessPosition in this process.
                new RecordingExportService().ExportToJson(fdpPath, svcOut, new JsonExportOptions
                {
                    IncludeEvents = false,
                    Minified = true,
                });

                // Both outputs should parse to equivalent JSON
                var cliRoot = JsonNode.Parse(File.ReadAllText(cliOut))!.AsObject();
                var svcRoot = JsonNode.Parse(File.ReadAllText(svcOut))!.AsObject();

                // Check same frame count
                Assert.Equal(
                    svcRoot["Frames"]!.AsArray().Count,
                    cliRoot["Frames"]!.AsArray().Count);

                // Check same header magic
                Assert.Equal(
                    svcRoot["Header"]!["Magic"]!.GetValue<string>(),
                    cliRoot["Header"]!["Magic"]!.GetValue<string>());
            }
            finally
            {
                TryDelete(cliOut);
                TryDelete(svcOut);
                TryDelete(fdpPath);
            }
        }

        // ── EX-T33: missing input file returns exit code 2 ────────────────────

        [Fact]
        public void EX_T33_MissingInputFile_ReturnsExitCode2()
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = Program.RunMain(
                new[] { "-i", "does_not_exist_abc123.fdp", "-o", "out.json" },
                stdout,
                stderr);

            Assert.Equal(2, code);
        }

        // ── Fixture builders ──────────────────────────────────────────────────

        private static string BuildFixtureRecording()
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            h.Tick().RecordKeyframe(100_000L);
            h.Tick().RecordDelta(200_000L);
            h.Tick().RecordDelta(300_000L);
            return h.BuildToTempFile();
        }

        private static string BuildFiveFrameRecording()
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            h.Tick().RecordKeyframe(100_000L);
            h.Tick().RecordDelta(200_000L);
            h.Tick().RecordDelta(300_000L);
            h.Tick().RecordDelta(400_000L);
            h.Tick().RecordDelta(500_000L);
            return h.BuildToTempFile();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
