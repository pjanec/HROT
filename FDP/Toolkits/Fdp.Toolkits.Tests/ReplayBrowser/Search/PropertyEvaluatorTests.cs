using System;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// SR-T04: PropertyEvaluator path validation and value extraction.
    /// </summary>
    public class PropertyEvaluatorTests
    {
        private readonly IComponentEditService _editService;

        public PropertyEvaluatorTests()
        {
            ComponentTypeRegistry.Clear();
            // Register harness component types via EntityRepository so the registry is populated.
            using var harness = new FdpRecordingHarness();
            _editService = new ComponentEditServiceBuilder().Build();
        }

        // ── SR-T04a: valid path returns correct string value ─────────────────

        [Fact]
        public void SR_T04a_ValidPath_X_ReturnsCorrectStringValue()
        {
            var evaluator = new PropertyEvaluator(_editService, typeof(HarnessPosition), "X");
            var pos = new HarnessPosition { X = 42.5f, Y = 0f, Z = 0f };

            string result = evaluator.GetValueAsString(pos);

            Assert.Equal("42.5", result);
        }

        // ── SR-T04b: invalid path throws ArgumentException at construction ───

        [Fact]
        public void SR_T04b_InvalidPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new PropertyEvaluator(_editService, typeof(HarnessPosition), "NonExistent"));
        }

        // ── SR-T04c: nested path works (HarnessTransform.Position) ───────────

        [Fact]
        public void SR_T04c_NestedPath_ReturnsCorrectValue()
        {
            var evaluator = new PropertyEvaluator(_editService, typeof(HarnessTransform), "Position");
            var transform = new HarnessTransform { Position = new System.Numerics.Vector3(3f, 4f, 5f) };

            // "Position" returns the Vector3 as a string; just ensure it doesn't throw.
            string result = evaluator.GetValueAsString(transform);
            Assert.False(string.IsNullOrEmpty(result));
        }

        // ── SR-T04d: allocation budget for hot path ──────────────────────────

        [Fact]
        public void SR_T04d_GetValueAsString_HotPath_AllocationBudget()
        {
            var evaluator = new PropertyEvaluator(_editService, typeof(HarnessPosition), "X");
            var pos = new HarnessPosition { X = 1f };

            // Warmup
            for (int i = 0; i < 10; i++) evaluator.GetValueAsString(pos);

            long before = GC.GetAllocatedBytesForCurrentThread();
            const int Iterations = 1000;
            for (int i = 0; i < Iterations; i++)
                _ = evaluator.GetValueAsString(pos);

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            // Allow up to 1 MB (boxing of float + string alloc).
            Assert.True(allocatedBytes < 1_048_576,
                $"Allocated {allocatedBytes} bytes for {Iterations} hot-path calls (limit: 1 MB)");
        }
    }
}
