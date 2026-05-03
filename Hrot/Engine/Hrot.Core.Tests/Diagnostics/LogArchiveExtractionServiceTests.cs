using System;
using System.IO;
using System.Threading.Tasks;
using Hrot.Core.Diagnostics;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.Core.Tests.Diagnostics
{
    /// <summary>
    /// Unit tests for <see cref="LogArchiveExtractionService"/> (DD-P2-T05).
    /// Uses a temporary directory that is cleaned up after each test.
    /// </summary>
    public sealed class LogArchiveExtractionServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public LogArchiveExtractionServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"fdp_log_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string WriteLogFile(string subsystem, int nodeId, string[] lines, string suffix = "")
        {
            var path = Path.Combine(_tempDir, $"{subsystem}_{nodeId}{suffix}.log");
            File.WriteAllLines(path, lines);
            return path;
        }

        private string TargetPath() => Path.Combine(_tempDir, "output.log");

        // ── Constructor ───────────────────────────────────────────────────────

        [Fact]
        public void Constructor_NullLogDirectory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LogArchiveExtractionService(null!, "Sub", 1));
        }

        [Fact]
        public void Constructor_NullSubsystemName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LogArchiveExtractionService(_tempDir, null!, 1));
        }

        // ── ExtractLogsAsync — empty / missing directory ───────────────────────

        [Fact]
        public async Task ExtractLogsAsync_NonExistentDirectory_ReturnsZero()
        {
            var svc = new LogArchiveExtractionService(
                Path.Combine(_tempDir, "doesNotExist"), "Sub", 1);

            int written = await svc.ExtractLogsAsync(TargetPath(), 0, float.MaxValue);
            Assert.Equal(0, written);
        }

        [Fact]
        public async Task ExtractLogsAsync_NoMatchingFiles_ReturnsZero()
        {
            // File doesn't match the pattern "Sub_1*.log"
            File.WriteAllLines(Path.Combine(_tempDir, "Other_2.log"), new[] { "line1" });

            var svc = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), 0, float.MaxValue);
            Assert.Equal(0, written);
        }

        // ── ExtractLogsAsync — basic line copy ────────────────────────────────

        [Fact]
        public async Task ExtractLogsAsync_AllLinesPass_WritesAllLines()
        {
            WriteLogFile("Sub", 1, new[] { "line1", "line2", "line3" });

            var svc    = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), 0, float.MaxValue);

            Assert.Equal(3, written);
            var output = File.ReadAllLines(TargetPath());
            Assert.Equal(3, output.Length);
        }

        // ── ExtractLogsAsync — severity filtering (NLog pipe format) ──────────

        [Fact]
        public async Task ExtractLogsAsync_SeverityFilter_ExcludesLowSeverityLines()
        {
            // NLog pipe format: "HH:mm:ss.fff | LEVEL | Logger | Message"
            var lines = new[]
            {
                "12:00:00.000 | TRACE | TestLogger | trace message",   // severity 0
                "12:00:01.000 | DEBUG | TestLogger | debug message",   // severity 1
                "12:00:02.000 | WARN  | TestLogger | warn message",    // severity 3
                "12:00:03.000 | ERROR | TestLogger | error message",   // severity 4
            };
            WriteLogFile("Sub", 1, lines);

            var svc    = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), severityThreshold: 3, float.MaxValue);

            // Only WARN and ERROR should pass.
            Assert.Equal(2, written);
            var output = File.ReadAllLines(TargetPath());
            Assert.All(output, l => Assert.True(l.Contains("WARN") || l.Contains("ERROR"), $"Unexpected: {l}"));
        }

        // ── ExtractLogsAsync — severity filtering (bracket format) ────────────

        [Fact]
        public async Task ExtractLogsAsync_BracketSeverityFilter_WorksCorrectly()
        {
            // Bracket format: "[N] message"
            var lines = new[]
            {
                "[1] debug line",   // severity 1 — below threshold
                "[3] warn line",    // severity 3 — passes
                "[5] fatal line",   // severity 5 — passes
            };
            WriteLogFile("Sub", 1, lines);

            var svc    = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), severityThreshold: 3, float.MaxValue);

            Assert.Equal(2, written);
        }

        // ── ExtractLogsAsync — multiple matching files ─────────────────────────

        [Fact]
        public async Task ExtractLogsAsync_MultipleMatchingFiles_CollectsAllLines()
        {
            WriteLogFile("Sub", 1, new[] { "a", "b" }, suffix: "_run1");
            WriteLogFile("Sub", 1, new[] { "c", "d" }, suffix: "_run2");

            var svc    = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), 0, float.MaxValue);

            Assert.Equal(4, written);
        }

        // ── ExtractLogsAsync — file age filter ────────────────────────────────

        [Fact]
        public async Task ExtractLogsAsync_FileOlderThanMaxAge_IsSkipped()
        {
            var logPath = WriteLogFile("Sub", 1, new[] { "old line" });
            // Back-date the file so it appears very old.
            File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddHours(-48));

            var svc    = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), 0, maxAgeHours: 1);

            Assert.Equal(0, written);
        }

        [Fact]
        public async Task ExtractLogsAsync_FileWithinMaxAge_IsIncluded()
        {
            WriteLogFile("Sub", 1, new[] { "recent line" });
            // File last-write time defaults to now, so maxAgeHours=2 should include it.

            var svc    = new LogArchiveExtractionService(_tempDir, "Sub", 1);
            int written = await svc.ExtractLogsAsync(TargetPath(), 0, maxAgeHours: 2);

            Assert.Equal(1, written);
        }

        // ── HrotNodeConfig.LogDirectory ───────────────────────────────────────

        [Fact]
        public void HrotNodeConfig_LogDirectory_DefaultsToEmptyString()
        {
            var cfg = new HrotNodeConfig();
            Assert.Equal(string.Empty, cfg.LogDirectory);
        }

        [Fact]
        public void HrotNodeConfig_LogDirectory_CanBeSet()
        {
            var cfg = new HrotNodeConfig { LogDirectory = @"C:\logs" };
            Assert.Equal(@"C:\logs", cfg.LogDirectory);
        }
    }
}
