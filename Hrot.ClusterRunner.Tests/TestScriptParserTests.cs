using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// Unit tests for R3.2: TestScript JSON parser and repeat-expansion logic.
    /// Covers SC-3 of the R3.2 task specification.
    /// </summary>
    public class TestScriptParserTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Writes JSON to a temporary file and returns the path.</summary>
        private static string WriteTemp(string json)
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, json);
            return path;
        }

        // ── SC-3 Test_ParseScript_Valid ───────────────────────────────────────

        /// <summary>
        /// A well-formed script JSON must deserialise into a <see cref="TestScript"/>
        /// with the expected field values. (R3.2 SC-3)
        /// </summary>
        [Fact]
        public void Test_ParseScript_Valid()
        {
            const string json = """
                {
                  "TestName": "Basic Test",
                  "Duration": 5.0,
                  "Steps": [
                    { "Time": 0.0, "Action": "wait", "Args": { "seconds": 1.0 } },
                    { "Time": 1.0, "Action": "assert_all", "Assert": { "duration": { "Min": 1.0 } } }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var script = HeadlessTestExecutor.LoadScript(path);

                Assert.Equal("Basic Test", script.TestName);
                Assert.Equal(5.0, script.Duration);
                Assert.Equal(2, script.Steps.Count);
                Assert.Equal("wait",       script.Steps[0].Action);
                Assert.Equal("assert_all", script.Steps[1].Action);
                Assert.Equal(0.0, script.Steps[0].Time);
                Assert.Equal(1.0, script.Steps[1].Time);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Step arguments must be parsed and accessible as a dictionary. (R3.2 SC-1)
        /// </summary>
        [Fact]
        public void Test_ParseScript_StepArgs_Accessible()
        {
            const string json = """
                {
                  "TestName": "Args Test",
                  "Duration": 2.0,
                  "Steps": [
                    { "Time": 0.0, "Action": "wait", "Args": { "seconds": 0.5 } }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var script = HeadlessTestExecutor.LoadScript(path);

                var args = script.Steps[0].Args;
                Assert.True(args.ContainsKey("seconds"));
                Assert.Equal(0.5, Convert.ToDouble(args["seconds"]));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Assertion rules must be parsed correctly from JSON. (R3.2 SC-1)
        /// </summary>
        [Fact]
        public void Test_ParseScript_AssertionRules_Parsed()
        {
            const string json = """
                {
                  "TestName": "Assert Test",
                  "Duration": 3.0,
                  "Steps": [
                    {
                      "Time": 1.0,
                      "Action": "assert_all",
                      "Assert": {
                        "fps":     { "Min": 30.0, "Max": 120.0 },
                        "latency": { "Max": 50.0 }
                      }
                    }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var script = HeadlessTestExecutor.LoadScript(path);

                var assertions = script.Steps[0].Assert;
                Assert.NotNull(assertions);
                Assert.True(assertions!.ContainsKey("fps"));
                Assert.Equal(30.0,  assertions["fps"].Min);
                Assert.Equal(120.0, assertions["fps"].Max);
                Assert.Null(assertions!["fps"].Exactly);
                Assert.Equal(50.0,  assertions["latency"].Max);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ── SC-3 Test_ParseScript_InvalidDuration ────────────────────────────

        /// <summary>
        /// A script with <c>Duration &lt;= 0</c> must throw <see cref="InvalidOperationException"/>. (R3.2 SC-2/SC-3)
        /// </summary>
        [Fact]
        public void Test_ParseScript_InvalidDuration_ThrowsOnZero()
        {
            const string json = """
                {
                  "TestName": "Bad Duration",
                  "Duration": 0,
                  "Steps": [
                    { "Time": 0.0, "Action": "wait" }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => HeadlessTestExecutor.LoadScript(path));
                Assert.Contains("Duration", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// A script with a negative <c>Duration</c> must also throw. (R3.2 SC-2/SC-3)
        /// </summary>
        [Fact]
        public void Test_ParseScript_InvalidDuration_ThrowsOnNegative()
        {
            const string json = """
                {
                  "TestName": "Negative Duration",
                  "Duration": -1.0,
                  "Steps": [
                    { "Time": 0.0, "Action": "wait" }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => HeadlessTestExecutor.LoadScript(path));
                Assert.Contains("Duration", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// A script with an empty <c>Steps</c> array must throw. (R3.2 SC-2/SC-3)
        /// </summary>
        [Fact]
        public void Test_ParseScript_EmptySteps_Throws()
        {
            const string json = """
                {
                  "TestName": "Empty Steps",
                  "Duration": 5.0,
                  "Steps": []
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => HeadlessTestExecutor.LoadScript(path));
                Assert.Contains("step", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ── SC-3 Test_ParseScript_RepeatExpansion ────────────────────────────

        /// <summary>
        /// A step with <c>Repeat=3</c> and <c>Interval=1.0</c> must expand to three
        /// individual steps at <c>t=0</c>, <c>t=1</c>, and <c>t=2</c>. (R3.2 SC-3)
        /// </summary>
        [Fact]
        public void Test_ParseScript_RepeatExpansion_CreatesCorrectTimes()
        {
            const string json = """
                {
                  "TestName": "Repeat Test",
                  "Duration": 10.0,
                  "Steps": [
                    { "Time": 0.0, "Action": "wait", "Repeat": 3, "Interval": 1.0 }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var script = HeadlessTestExecutor.LoadScript(path);

                Assert.Equal(3, script.Steps.Count);
                Assert.Equal(0.0, script.Steps[0].Time, precision: 10);
                Assert.Equal(1.0, script.Steps[1].Time, precision: 10);
                Assert.Equal(2.0, script.Steps[2].Time, precision: 10);

                // All expanded steps must carry the same action.
                Assert.All(script.Steps, s => Assert.Equal("wait", s.Action));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// When <c>Repeat=1</c> (default), the step must not be replicated. (R3.2 SC-2)
        /// </summary>
        [Fact]
        public void Test_ParseScript_RepeatDefaultOne_NoExpansion()
        {
            const string json = """
                {
                  "TestName": "No Repeat Test",
                  "Duration": 3.0,
                  "Steps": [
                    { "Time": 0.0, "Action": "wait" }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var script = HeadlessTestExecutor.LoadScript(path);

                Assert.Single(script.Steps);
                Assert.Equal(0.0, script.Steps[0].Time);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// A repeated step must inherit all original args, not just time. (R3.2 SC-2)
        /// </summary>
        [Fact]
        public void Test_ParseScript_RepeatExpansion_PreservesArgs()
        {
            const string json = """
                {
                  "TestName": "Repeat Args Test",
                  "Duration": 10.0,
                  "Steps": [
                    {
                      "Time": 0.0, "Action": "wait",
                      "Args": { "seconds": 0.25 },
                      "Repeat": 2, "Interval": 0.5
                    }
                  ]
                }
                """;

            var path = WriteTemp(json);
            try
            {
                var script = HeadlessTestExecutor.LoadScript(path);

                Assert.Equal(2, script.Steps.Count);
                foreach (var step in script.Steps)
                {
                    Assert.True(step.Args.ContainsKey("seconds"));
                    Assert.Equal(0.25, Convert.ToDouble(step.Args["seconds"]));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ── ExpandRepeats unit tests (direct) ─────────────────────────────────

        /// <summary>
        /// ExpandRepeats must work correctly when called directly on a list. (R3.2 SC-2)
        /// </summary>
        [Fact]
        public void Test_ExpandRepeats_MultipleSteps_MixedRepeat()
        {
            var steps = new List<TestStep>
            {
                new TestStep { Time = 0.0, Action = "a", Repeat = 1, Interval = 0 },
                new TestStep { Time = 5.0, Action = "b", Repeat = 3, Interval = 1.0 }
            };

            var expanded = HeadlessTestExecutor.ExpandRepeats(steps);

            // Step "a" → 1 copy; step "b" → 3 copies
            Assert.Equal(4, expanded.Count);
            Assert.Equal("a", expanded[0].Action);
            Assert.Equal("b", expanded[1].Action); Assert.Equal(5.0, expanded[1].Time);
            Assert.Equal("b", expanded[2].Action); Assert.Equal(6.0, expanded[2].Time);
            Assert.Equal("b", expanded[3].Action); Assert.Equal(7.0, expanded[3].Time);
        }
    }
}
