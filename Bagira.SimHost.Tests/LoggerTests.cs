using System;
using System.IO;
using Bagira.SimHost.Utilities;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Logger"/> (TASK-S5.3).
    /// </summary>
    public class LoggerTests : IDisposable
    {
        // Each test gets a fresh StringWriter so logger output is captured without
        // interfering with other tests.
        private readonly StringWriter _consoleCapture;
        private readonly TextWriter   _originalOut;
        private readonly LogLevel     _originalLevel;

        public LoggerTests()
        {
            _originalOut    = Console.Out;
            _originalLevel  = Logger.MinimumLevel;
            _consoleCapture = new StringWriter();
            Console.SetOut(_consoleCapture);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Logger.MinimumLevel = _originalLevel;
            _consoleCapture.Dispose();
        }

        // ── S5.3 Test 1: minimum level filtering ─────────────────────────────────

        /// <summary>
        /// Messages below <see cref="Logger.MinimumLevel"/> must produce no output.
        /// Messages at or above the minimum must produce output.
        /// </summary>
        [Fact]
        public void Logger_MinimumLevel_FiltersLowerPriorityMessages()
        {
            // Arrange: suppress Debug and Info
            Logger.MinimumLevel = LogLevel.Warning;

            // Act
            Logger.Debug("should be suppressed");
            Logger.Info("also suppressed");
            Logger.Warning("this must appear");

            // Assert
            var output = _consoleCapture.ToString();
            Assert.DoesNotContain("should be suppressed", output);
            Assert.DoesNotContain("also suppressed",      output);
            Assert.Contains("this must appear",           output);
        }

        /// <summary>
        /// All levels pass when <see cref="Logger.MinimumLevel"/> is <see cref="LogLevel.Debug"/>.
        /// </summary>
        [Fact]
        public void Logger_MinimumLevelDebug_AllMessagesAppear()
        {
            Logger.MinimumLevel = LogLevel.Debug;

            Logger.Debug("debug message");
            Logger.Info("info message");
            Logger.Warning("warning message");

            var output = _consoleCapture.ToString();
            Assert.Contains("debug message",   output);
            Assert.Contains("info message",    output);
            Assert.Contains("warning message", output);
        }

        // ── S5.3 Test 2: output string format ────────────────────────────────────

        /// <summary>
        /// Each log line must contain a timestamp in HH:mm:ss.fff format,
        /// a level tag (e.g. "INFO "), and the message text.
        /// </summary>
        [Fact]
        public void Logger_Info_OutputContainsTimestampAndLevelAndMessage()
        {
            Logger.MinimumLevel = LogLevel.Info;
            Logger.Info("hello world");

            var output = _consoleCapture.ToString();

            // Timestamp bracket: [HH:mm:ss.fff]
            Assert.Matches(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\]", output);
            // Level tag
            Assert.Contains("[INFO ]", output);
            // Message
            Assert.Contains("hello world", output);
        }

        [Fact]
        public void Logger_Warning_OutputContainsWarnTag()
        {
            Logger.MinimumLevel = LogLevel.Warning;
            Logger.Warning("something bad");

            var output = _consoleCapture.ToString();
            Assert.Contains("[WARN ]", output);
            Assert.Contains("something bad", output);
        }

        [Fact]
        public void Logger_Debug_OutputContainsDebugTag()
        {
            Logger.MinimumLevel = LogLevel.Debug;
            Logger.Debug("trace info");

            var output = _consoleCapture.ToString();
            Assert.Contains("[DEBUG]", output);
            Assert.Contains("trace info", output);
        }

        // ── S5.3 Test 3: Error goes to stderr, not stdout ─────────────────────────

        /// <summary>
        /// <see cref="LogLevel.Error"/> messages must be written to
        /// <see cref="Console.Error"/>, not <see cref="Console.Out"/>.
        /// </summary>
        [Fact]
        public void Logger_Error_WritesToStdErr()
        {
            var errCapture   = new StringWriter();
            var originalErr  = Console.Error;
            Console.SetError(errCapture);

            try
            {
                Logger.MinimumLevel = LogLevel.Error;
                Logger.Error("critical failure");

                // Should NOT appear on stdout
                var stdOut = _consoleCapture.ToString();
                Assert.DoesNotContain("critical failure", stdOut);

                // SHOULD appear on stderr
                var stdErr = errCapture.ToString();
                Assert.Contains("critical failure", stdErr);
                Assert.Contains("[ERROR]",          stdErr);
            }
            finally
            {
                Console.SetError(originalErr);
                errCapture.Dispose();
            }
        }
    }
}
