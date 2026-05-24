using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using System;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for EqsQueryTemplate.ComputeStructureHash() (TASK-EQS-021).
    /// </summary>
    public class EqsStructureHashTests
    {
        // ---- Stub types used to produce different type names ----

        private sealed class GeneratorA : IEqsGenerator
        {
            public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
                => 0;
        }

        private sealed class GeneratorB : IEqsGenerator
        {
            public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
                => 0;
        }

        private sealed class TestX : IEqsTest
        {
            public EqsTestPhase Phase => EqsTestPhase.FilterCheap;
            public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates) { }
        }

        private sealed class TestY : IEqsTest
        {
            public EqsTestPhase Phase => EqsTestPhase.FilterCheap;
            public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates) { }
        }

        // T-SH1: two templates with different generator types must produce different hashes.
        [Fact]
        public void ComputeStructureHash_DifferentGenerators_ProduceDifferentHashes()
        {
            var templateA = new EqsQueryTemplate { Generator = new GeneratorA(), MaxCandidates = 8 };
            var templateB = new EqsQueryTemplate { Generator = new GeneratorB(), MaxCandidates = 8 };

            ulong hashA = templateA.ComputeStructureHash();
            ulong hashB = templateB.ComputeStructureHash();

            Assert.NotEqual(hashA, hashB);
        }

        // T-SH2: two structurally identical templates must produce the same hash.
        [Fact]
        public void ComputeStructureHash_SameStructure_ProducesSameHash()
        {
            var templateA = new EqsQueryTemplate
            {
                Generator    = new GeneratorA(),
                FilterCheap  = new IEqsTest[] { new TestX() },
                MaxCandidates = 8,
            };
            var templateB = new EqsQueryTemplate
            {
                Generator    = new GeneratorA(),
                FilterCheap  = new IEqsTest[] { new TestX() },
                MaxCandidates = 8,
            };

            ulong hashA = templateA.ComputeStructureHash();
            ulong hashB = templateB.ComputeStructureHash();

            Assert.Equal(hashA, hashB);
        }

        // T-SH3: same generator but different FilterCheap tests must produce different hashes.
        [Fact]
        public void ComputeStructureHash_DifferentTests_ProduceDifferentHashes()
        {
            var templateA = new EqsQueryTemplate
            {
                Generator    = new GeneratorA(),
                FilterCheap  = new IEqsTest[] { new TestX() },
                MaxCandidates = 8,
            };
            var templateB = new EqsQueryTemplate
            {
                Generator    = new GeneratorA(),
                FilterCheap  = new IEqsTest[] { new TestY() },
                MaxCandidates = 8,
            };

            ulong hashA = templateA.ComputeStructureHash();
            ulong hashB = templateB.ComputeStructureHash();

            Assert.NotEqual(hashA, hashB);
        }
    }
}
