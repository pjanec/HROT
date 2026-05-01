using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TacticalIntentMapperRegistry"/> (TASK-TI002).
    /// </summary>
    public class TacticalIntentMapperRegistryTests
    {
        // ── Stub ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal stub mapper for testing — returns a fixed doctrine name.
        /// </summary>
        private sealed class StubMapper : ITacticalOrderMapper
        {
            private readonly string _resultDoctrine;

            public string TargetIntentId { get; }

            public StubMapper(string targetIntentId, string resultDoctrine)
            {
                TargetIntentId = targetIntentId;
                _resultDoctrine = resultDoctrine;
            }

            public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                               out AssignDoctrineEvent assignment)
            {
                assignment = new AssignDoctrineEvent
                {
                    Entity       = self,
                    DoctrineName = _resultDoctrine,
                    JsonParams   = jsonParams,
                };
                return true;
            }
        }

        // ── SC-1 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-1: Register two mappers with distinct <c>TargetIntentId</c> values.
        /// <c>TryGetMapper</c> must return the correct mapper for each ID.
        /// </summary>
        [Fact]
        public void TryGetMapper_AfterRegisteringTwoDistinctMappers_ReturnsCorrectMapperForEach()
        {
            var registry = new TacticalIntentMapperRegistry();
            var mapperA  = new StubMapper("DefendArea",    "InfantryDefend");
            var mapperB  = new StubMapper("ConvoyEscort", "ApcEscort");

            registry.Register(mapperA);
            registry.Register(mapperB);

            bool foundA = registry.TryGetMapper("DefendArea",    out var resultA);
            bool foundB = registry.TryGetMapper("ConvoyEscort", out var resultB);

            Assert.True(foundA);
            Assert.Same(mapperA, resultA);

            Assert.True(foundB);
            Assert.Same(mapperB, resultB);
        }

        // ── SC-2 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-2: Registering two mappers with the same <c>TargetIntentId</c> must throw
        /// <see cref="InvalidOperationException"/>.
        /// </summary>
        [Fact]
        public void Register_DuplicateTargetIntentId_ThrowsInvalidOperationException()
        {
            var registry = new TacticalIntentMapperRegistry();
            var mapper1  = new StubMapper("DefendArea", "DoctrineA");
            var mapper2  = new StubMapper("DefendArea", "DoctrineB");

            registry.Register(mapper1);

            Assert.Throws<InvalidOperationException>(() => registry.Register(mapper2));
        }

        // ── SC-3 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-3: <c>TryGetMapper</c> on an empty registry must return <c>false</c>
        /// and set the out parameter to <c>null</c>.
        /// </summary>
        [Fact]
        public void TryGetMapper_EmptyRegistry_ReturnsFalseAndNullMapper()
        {
            var registry = new TacticalIntentMapperRegistry();

            bool found = registry.TryGetMapper("Unknown", out var mapper);

            Assert.False(found);
            Assert.Null(mapper);
        }
    }
}
