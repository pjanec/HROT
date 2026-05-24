using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for the core EQS query template interfaces and types (TASK-EQS-008).
    /// Pure unit tests: no ECS, no harness, no DDS.
    /// </summary>
    public class EqsQueryTemplateTests
    {
        // ── T-EQS-008-1: EqsTestPhase values ─────────────────────────────────────

        [Fact]
        public void EqsTestPhase_ValuesAreCorrect()
        {
            Assert.Equal(0, (int)EqsTestPhase.FilterCheap);
            Assert.Equal(1, (int)EqsTestPhase.FilterExpensive);
            Assert.Equal(2, (int)EqsTestPhase.ScoreCheap);
            Assert.Equal(3, (int)EqsTestPhase.ScoreExpensive);
        }

        // ── T-EQS-008-2: Compose a trivial template and exercise it ──────────────

        [Fact]
        public void EqsQueryTemplate_CanBeComposedWithTrivialGeneratorAndTest()
        {
            // Trivial generator: hardcodes 2 positional candidates at X=10 and X=20.
            var generator = new HardcodedTwoPointGenerator();

            // Trivial filter: rejects the first candidate (index 0, X=10) by setting EntityId = -1L.
            var filter = new RejectFirstCandidateFilter();

            var template = new EqsQueryTemplate
            {
                BlueprintId   = 42u,
                Generator     = generator,
                FilterCheap   = new IEqsTest[] { filter },
                MaxCandidates = 8,
            };

            // Allocate candidate buffer and run generate + filter manually.
            var candidates = new EqsResult[template.MaxCandidates];
            var span       = candidates.AsSpan();

            var sensor   = new EqsSensor { BlueprintId = 42u, Epoch = 1, SearchRadius = 50f };
            var observer = Entity.Null;

            int count = template.Generator.Generate(observer, ref sensor, null!, span);
            Assert.Equal(2, count);

            var activeSpan = span.Slice(0, count);
            foreach (var test in template.FilterCheap!)
                test.ExecuteBatch(observer, ref sensor, null!, activeSpan);

            // Count surviving candidates (EntityId != -1L).
            int survivors = 0;
            for (int i = 0; i < count; i++)
                if (candidates[i].EntityId != -1L) survivors++;

            Assert.Equal(1, survivors);
            // The surviving candidate must be the second one (X = 20).
            Assert.Equal(20f, candidates[1].PositionX);
        }

        // ── T-EQS-008-3: IEqsTemplateRegistry returns false for unknown ID ────────

        [Fact]
        public void IEqsTemplateRegistry_TryGetTemplate_ReturnsFalseForUnknownId()
        {
            IEqsTemplateRegistry registry = new DictionaryBackedRegistry();

            bool found = registry.TryGetTemplate(99999u, out _);

            Assert.False(found);
        }

        // ── T-EQS-008-4: EqsTemplateAttribute stores AssetId ─────────────────────

        [Fact]
        public void EqsTemplateAttribute_StoresAssetId()
        {
            var attr = new EqsTemplateAttribute("test-guid");

            Assert.Equal("test-guid", attr.AssetId);
        }

        // ── Minimal in-test implementations ───────────────────────────────────────

        private sealed class HardcodedTwoPointGenerator : IEqsGenerator
        {
            public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
            {
                if (candidates.Length < 2) return 0;
                candidates[0] = new EqsResult { EntityId = 0, PositionX = 10f, PositionY = 0f };
                candidates[1] = new EqsResult { EntityId = 0, PositionX = 20f, PositionY = 0f };
                return 2;
            }
        }

        private sealed class RejectFirstCandidateFilter : IEqsTest
        {
            public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

            public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
            {
                if (candidates.Length > 0)
                    candidates[0].EntityId = -1L;
            }
        }

        private sealed class DictionaryBackedRegistry : IEqsTemplateRegistry
        {
            private readonly Dictionary<uint, EqsQueryTemplate> _map = new();

            public bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template)
                => _map.TryGetValue(blueprintId, out template);
        }
    }
}
