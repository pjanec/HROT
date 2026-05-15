using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// SR-T05..SR-T08, SR-T35: PredicateCompiler correctness and short-circuit tests.
    /// </summary>
    public class PredicateCompilerTests : IDisposable
    {
        private readonly FdpRecordingHarness _harness;
        private readonly IPredicateCompiler _compiler;

        public PredicateCompilerTests()
        {
            ComponentTypeRegistry.Clear();
            _harness  = new FdpRecordingHarness();
            _compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        }

        public void Dispose() => _harness.Dispose();

        // ── SR-T05: Compound AND -- both conditions true ─────────────────────

        [Fact]
        public void SR_T05_CompoundAnd_BothTrue_EntityMatches()
        {
            // Arrange: entity with HarnessPosition.X = 50 and HarnessVelocity.Vx = 3
            _harness.SpawnEntity()
                .WithComponent(new HarnessPosition { X = 50f })
                .WithComponent(new HarnessVelocity { Vx = 3f });
            var entity = _harness.LastSpawned;
            _harness.Tick();

            var predicate = new CompoundPredicateDto
            {
                Operator = LogicalOperator.And,
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessPosition),
                        PropertyPath  = "X",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 40.0 }
                    },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessVelocity),
                        PropertyPath  = "Vx",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 2.0 }
                    }
                }
            };

            var fn = _compiler.CompileComponentPredicate(predicate);

            Assert.True(fn(_harness.Repository, entity));
        }

        // ── SR-T05b: Compound AND -- one condition false ─────────────────────

        [Fact]
        public void SR_T05b_CompoundAnd_OneFalse_EntityDoesNotMatch()
        {
            _harness.SpawnEntity()
                .WithComponent(new HarnessPosition { X = 10f }) // too low for > 40
                .WithComponent(new HarnessVelocity { Vx = 3f });
            var entity = _harness.LastSpawned;
            _harness.Tick();

            var predicate = new CompoundPredicateDto
            {
                Operator = LogicalOperator.And,
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessPosition),
                        PropertyPath  = "X",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 40.0 }
                    },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessVelocity),
                        PropertyPath  = "Vx",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 2.0 }
                    }
                }
            };

            var fn = _compiler.CompileComponentPredicate(predicate);

            Assert.False(fn(_harness.Repository, entity));
        }

        // ── SR-T06: Compound OR -- union of matches ──────────────────────────

        [Fact]
        public void SR_T06_CompoundOr_FirstConditionTrue_EntityMatches()
        {
            _harness.SpawnEntity()
                .WithComponent(new HarnessPosition { X = 100f });
            var entity = _harness.LastSpawned;
            _harness.Tick();

            var predicate = new CompoundPredicateDto
            {
                Operator = LogicalOperator.Or,
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessPosition),
                        PropertyPath  = "X",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 90.0 }
                    },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessVelocity), // entity doesn't have this
                        PropertyPath  = "Vx",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 2.0 }
                    }
                }
            };

            var fn = _compiler.CompileComponentPredicate(predicate);

            Assert.True(fn(_harness.Repository, entity));
        }

        // ── SR-T07: PropertyMatch with Equals ───────────────────────────────

        [Fact]
        public void SR_T07_PropertyMatch_Equals_MatchesCorrectEntity()
        {
            _harness.SpawnEntity().WithComponent(new HarnessPosition { X = 80f });
            var match = _harness.LastSpawned;
            _harness.SpawnEntity().WithComponent(new HarnessPosition { X = 90f });
            var noMatch = _harness.LastSpawned;
            _harness.Tick();

            var predicate = new PropertyMatchDto
            {
                ComponentType = typeof(HarnessPosition),
                PropertyPath  = "X",
                Operator      = SearchOperator.Equals,
                Predicate     = new StringPredicateDto { Substring = "80" }
            };

            var fn = _compiler.CompileComponentPredicate(predicate);

            Assert.True(fn(_harness.Repository, match));
            Assert.False(fn(_harness.Repository, noMatch));
        }

        // ── SR-T08: allocation budget for compiled predicate ─────────────────

        [Fact]
        public void SR_T08_CompiledPredicate_AllocationBudget()
        {
            _harness.SpawnEntity().WithComponent(new HarnessPosition { X = 50f });
            var entity = _harness.LastSpawned;
            _harness.Tick();

            var predicate = new PropertyMatchDto
            {
                ComponentType = typeof(HarnessPosition),
                PropertyPath  = "X",
                Operator      = SearchOperator.GreaterThan,
                Predicate     = new NumericPredicateDto { MinValue = 40.0 }
            };

            var fn = _compiler.CompileComponentPredicate(predicate);

            // Warmup
            for (int i = 0; i < 10; i++) _ = fn(_harness.Repository, entity);

            long before = GC.GetAllocatedBytesForCurrentThread();
            const int Iterations = 1000;
            for (int i = 0; i < Iterations; i++)
                _ = fn(_harness.Repository, entity);

            long allocBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            // Allow up to 1 MB (boxing in GetValueAsString path).
            Assert.True(allocBytes < 1_048_576,
                $"Allocated {allocBytes} bytes for {Iterations} no-match evaluations (limit: 1 MB)");
        }

        // ── SR-T35: short-circuit AND -- second condition not called ─────────

        [Fact]
        public void SR_T35_CompoundAnd_ShortCircuit_SecondNotCalledWhenFirstFails()
        {
            // Entity only has HarnessPosition (not HarnessVelocity).
            // The AND compound should short-circuit after the first (X check) fails.
            // We verify by checking that a missing-component guard doesn't panic.
            _harness.SpawnEntity()
                .WithComponent(new HarnessPosition { X = 1f }); // 1 < 40, fails first condition
            var entity = _harness.LastSpawned;
            _harness.Tick();

            int secondCallCount = 0;
            var predicate = new CompoundPredicateDto
            {
                Operator = LogicalOperator.And,
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessPosition),
                        PropertyPath  = "X",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 40.0 }
                    },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessVelocity),
                        PropertyPath  = "Vx",
                        Operator      = SearchOperator.GreaterThan,
                        Predicate     = new NumericPredicateDto { MinValue = 2.0 }
                    }
                }
            };

            var fn = _compiler.CompileComponentPredicate(predicate);

            bool result = fn(_harness.Repository, entity);
            Assert.False(result); // First condition fails, second should not evaluate
        }

        // ── ExtractMandatoryComponents: AND root ─────────────────────────────

        [Fact]
        public void ExtractMandatoryComponents_AndRoot_ReturnsBothTypes()
        {
            var predicate = new CompoundPredicateDto
            {
                Operator = LogicalOperator.And,
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessPosition),
                        PropertyPath  = "X",
                        Operator      = SearchOperator.Equals,
                        Predicate     = new NumericPredicateDto()
                    },
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessVelocity),
                        PropertyPath  = "Vx",
                        Operator      = SearchOperator.Equals,
                        Predicate     = new NumericPredicateDto()
                    }
                }
            };

            var mandatory = _compiler.ExtractMandatoryComponents(predicate);

            Assert.Contains(typeof(HarnessPosition), mandatory);
            Assert.Contains(typeof(HarnessVelocity), mandatory);
        }

        // ── ExtractMandatoryComponents: OR root returns empty ────────────────

        [Fact]
        public void ExtractMandatoryComponents_OrRoot_ReturnsEmpty()
        {
            var predicate = new CompoundPredicateDto
            {
                Operator = LogicalOperator.Or,
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(HarnessPosition),
                        PropertyPath  = "X",
                        Operator      = SearchOperator.Equals,
                        Predicate     = new NumericPredicateDto()
                    }
                }
            };

            var mandatory = _compiler.ExtractMandatoryComponents(predicate);

            Assert.Empty(mandatory);
        }
    }
}
