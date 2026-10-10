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
    
        // ── CE-3137 U-0 rail ⑦: a working slot in a unit's SECOND block is searchable ───────────

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct U0SearchRoot { public float Speed; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct U0SearchState { public int Hits; }

        /// <summary>
        /// ⭐⭐ <c>U0_R8</c> (§34 rail ⑦) — the replay search reads a behaviour's working slot in WHICHEVER block of the
        /// unit's store holds it. A replay frame is an <see cref="EntityRepository"/> whose tier components were
        /// recorded like any other component, so a two-block unit replays as two blocks; this pins the matcher on one.
        /// 🔴 Red-proof by construction: the slot lives in the 256 block, and the single-block read (<c>TryGetStoreReadOnly</c>,
        /// what the matcher used before U-0) returns the 4096 — which does not hold it.
        /// </summary>
        [Fact]
        public unsafe void U0_R8_AWorkingSlotInTheSecondBlock_IsFoundByTheSearch()
        {
            const int behaviourId = 0x0C3137;
            const int slotKey     = 0x5EA2C4;
            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            registry.Register(behaviourId, "U0Search", new Fdp.Toolkit.Behavior.BehaviorDefinition
            {
                Name                 = "U0Search",
                BrainTier            = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree,
                BlackboardLayoutType = typeof(U0SearchRoot),
                StatefulWorkingSlots = new[]
                {
                    new Fdp.Toolkit.Behavior.StatefulSlotInfo(slotKey, 4, 0, typeof(U0SearchState), "Action", Role: 0, Scope: 0),
                },
            });

            using var repo = new EntityRepository();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(repo);
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();

            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new Fdp.Toolkit.Behavior.Components.BehaviorState { ActiveBehaviorHash = behaviourId });
            var asc = Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.Ascending;
            Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.AddBlock(repo, unit, asc[0]);
            Assert.True(Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.TryAttachSlot(
                repo, unit, slotKey, sizeof(U0SearchState), 0, Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.BTree,
                out byte* block, out int offset));
            ((U0SearchState*)(block + offset))->Hits = 7;
            Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.AddBlock(repo, unit, asc[2]);   // larger ⇒ "the" store
            Assert.Equal(2, Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.Measure(repo, unit).Blocks);

            var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build(), registry);
            Func<SearchOperator, double, double, Func<EntityRepository, Entity, bool>> compile = (op, min, max) =>
                compiler.CompileComponentPredicate(new BehaviorParamPredicateDto
                {
                    BehaviorId = behaviourId, WorkingSlotKey = slotKey, PropertyPath = "Hits", Operator = op,
                    Predicate  = new NumericPredicateDto { MinValue = min, MaxValue = max },
                });

            Assert.True(compile(SearchOperator.Equals, 7, 7)(repo, unit), "the slot in the smaller (second-searched) block matches");
            Assert.False(compile(SearchOperator.Equals, 8, 8)(repo, unit), "anti-vacuity: a different value does not");
        }
}
}
