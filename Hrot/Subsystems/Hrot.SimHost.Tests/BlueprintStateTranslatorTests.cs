using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// BSA-202: Tests for BlueprintStateTranslator — Extract, Inject,
    /// GetOutputDomKeys, CanTranslate, and legacy black-hole.
    /// </summary>
    public sealed class BlueprintStateTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly BlueprintRegistry _registry;

        public BlueprintStateTranslatorTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();

            // Register all three blackboard tier components.
            _repo.RegisterComponent<BlueprintBlackboard1024>();
            _repo.RegisterComponent<BlueprintBlackboard4096>();
            _repo.RegisterComponent<BlueprintBlackboard16384>();
            _repo.RegisterManagedComponent<InitialBlueprintsIntent>();

            _registry = new BlueprintRegistry();
        }

        public void Dispose() => _repo.Dispose();

        // ── helpers ──────────────────────────────────────────────────────────

        private BlueprintStateTranslator CreateTranslator()
            => new BlueprintStateTranslator(_registry);

        private BlueprintStateTranslator CreateTranslatorWithoutRegistry()
            => new BlueprintStateTranslator(null);

        /// <summary>
        /// Registers a minimal Instance blueprint in the registry and returns its id.
        /// Uses a stub InitDefault that writes a sentinel byte.
        /// </summary>
        private int RegisterTestBlueprint(string name, Guid assetId, int stateSize = 16)
        {
            int blueprintId = unchecked((int)BlueprintIdHash.Compute(assetId));

            var def = new BlueprintDefinition
            {
                Name = name,
                Kind = BlueprintDispatchKind.Instance,
                StructureHash = (ulong)blueprintId,
                StateSize = stateSize,
                AssetId = assetId,
                InitDefault = span =>
                {
                    if (span.Length > 0)
                        span[0] = 0xAB; // sentinel
                },
            };

            _registry.RegisterInstance(blueprintId, def);
            return blueprintId;
        }

        private static Entity CreateEntityWithBlackboard(
            EntityRepository repo, bool add2048 = true)
        {
            var entity = repo.CreateEntity();
            if (add2048)
                repo.AddComponent(entity, default(BlueprintBlackboard1024));
            return entity;
        }

        // ── Test 7: Extract round-trip ───────────────────────────────────────

        [Fact]
        public void Extract_TwoBlueprintsAttached_ReturnsCorrectAssignments()
        {
            var assetId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var assetId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
            int bpId1 = RegisterTestBlueprint("TestBp1", assetId1, stateSize: 16);
            int bpId2 = RegisterTestBlueprint("TestBp2", assetId2, stateSize: 16);

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, default(BlueprintBlackboard1024));

            var result1 = BlueprintInstanceService.AttachToEntity(_repo, _registry, bpId1, entity);
            Assert.Equal(BlueprintAttachStatus.Attached, result1.Status);

            var result2 = BlueprintInstanceService.AttachToEntity(_repo, _registry, bpId2, entity);
            Assert.Equal(BlueprintAttachStatus.Attached, result2.Status);

            var translator = CreateTranslator();
            var data = translator.Extract(_repo, entity, new StubGuidResolver());

            Assert.True(data.ContainsKey("BlueprintAssignments"));
            var assignments = data["BlueprintAssignments"] as List<Dictionary<string, object>>;
            Assert.NotNull(assignments);
            Assert.Equal(2, assignments!.Count);

            var assetIds = assignments.Select(a => a["AssetId"] as string).ToArray();
            Assert.Contains(assetId1.ToString(), assetIds);
            Assert.Contains(assetId2.ToString(), assetIds);

            // Result must NOT contain any BlueprintBlackboard* keys.
            Assert.DoesNotContain("BlueprintBlackboard1024", data.Keys);
            Assert.DoesNotContain("BlueprintBlackboard4096", data.Keys);
            Assert.DoesNotContain("BlueprintBlackboard16384", data.Keys);
        }

        // ── Test 8: Inject → Intent ─────────────────────────────────────────

        [Fact]
        public void Inject_WithAssignmentsData_SetsInitialBlueprintsIntent()
        {
            var assetId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var entity = _repo.CreateEntity();

            var scenarioData = new Dictionary<string, object>
            {
                ["BlueprintAssignments"] = System.Text.Json.JsonSerializer.SerializeToElement(
                    new[]
                    {
                        new { AssetId = assetId.ToString() },
                    }),
            };

            var translator = CreateTranslatorWithoutRegistry();
            translator.Inject(_repo, entity, scenarioData, new StubGuidResolver());

            Assert.True(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity));
            var intent = ((Fdp.ModuleHost.Abstractions.ISimulationView)_repo).GetManagedComponentRO<InitialBlueprintsIntent>(entity);
            Assert.NotNull(intent);
            Assert.Single(intent!.Blueprints);
            Assert.Equal(assetId, intent.Blueprints[0].AssetId);
        }

        [Fact]
        public void Inject_WithMultipleAssignments_SetsAllEntries()
        {
            var assetId1 = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var assetId2 = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var entity = _repo.CreateEntity();

            var scenarioData = new Dictionary<string, object>
            {
                ["BlueprintAssignments"] = System.Text.Json.JsonSerializer.SerializeToElement(
                    new[]
                    {
                        new { AssetId = assetId1.ToString() },
                        new { AssetId = assetId2.ToString() },
                    }),
            };

            var translator = CreateTranslatorWithoutRegistry();
            translator.Inject(_repo, entity, scenarioData, new StubGuidResolver());

            var intent = ((Fdp.ModuleHost.Abstractions.ISimulationView)_repo).GetManagedComponentRO<InitialBlueprintsIntent>(entity);
            Assert.NotNull(intent);
            Assert.Equal(2, intent!.Blueprints.Count);
        }

        // ── Test 9: Legacy black-hole ────────────────────────────────────────

        [Fact]
        public void Inject_LegacyBlackboardKey_DoesNotThrow()
        {
            var entity = _repo.CreateEntity();

            var scenarioData = new Dictionary<string, object>
            {
                ["BlueprintBlackboard1024"] = new Dictionary<string, object>
                {
                    ["dummy"] = "data",
                },
            };

            var translator = CreateTranslatorWithoutRegistry();

            // Must not throw.
            var ex = Record.Exception(() =>
                translator.Inject(_repo, entity, scenarioData, new StubGuidResolver()));
            Assert.Null(ex);
        }

        [Fact]
        public void Inject_LegacyBlackboardKey_DoesNotAddAnyBlackboardComponent()
        {
            var entity = _repo.CreateEntity();

            var scenarioData = new Dictionary<string, object>
            {
                ["BlueprintBlackboard1024"] = new Dictionary<string, object>(),
            };

            var translator = CreateTranslatorWithoutRegistry();
            translator.Inject(_repo, entity, scenarioData, new StubGuidResolver());

            Assert.False(_repo.HasComponent<BlueprintBlackboard1024>(entity));
            Assert.False(_repo.HasComponent<BlueprintBlackboard4096>(entity));
            Assert.False(_repo.HasComponent<BlueprintBlackboard16384>(entity));
        }

        // ── Test 10: GetOutputDomKeys returns all 4 keys ─────────────────────

        [Fact]
        public void GetOutputDomKeys_ReturnsAllFourKeys()
        {
            var translator = CreateTranslatorWithoutRegistry();
            var keys = translator.GetOutputDomKeys().ToList();

            Assert.Equal(4, keys.Count);
            Assert.Contains("BlueprintAssignments", keys);
            Assert.Contains("BlueprintBlackboard1024", keys);
            Assert.Contains("BlueprintBlackboard4096", keys);
            Assert.Contains("BlueprintBlackboard16384", keys);
        }

        // ── Test 11: CanTranslate ────────────────────────────────────────────

        [Fact]
        public void CanTranslate_EntityWithBlackboard1024_ReturnsTrue()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, default(BlueprintBlackboard1024));

            var translator = CreateTranslatorWithoutRegistry();
            Assert.True(translator.CanTranslate(_repo, entity));
        }

        [Fact]
        public void CanTranslate_EntityWithoutBlackboard_ReturnsFalse()
        {
            var entity = _repo.CreateEntity();

            var translator = CreateTranslatorWithoutRegistry();
            Assert.False(translator.CanTranslate(_repo, entity));
        }

        [Fact]
        public void CanTranslate_EntityWithBlackboard4096_ReturnsTrue()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, default(BlueprintBlackboard4096));

            var translator = CreateTranslatorWithoutRegistry();
            Assert.True(translator.CanTranslate(_repo, entity));
        }

        // ── Test 12: AssetId emit fix (cross-check) ──────────────────────────

        [Fact]
        public void AssetId_RegisteredInstanceBlueprint_HasNonEmptyAssetId()
        {
            var assetId = Guid.Parse("66666666-6666-6666-6666-666666666666");
            int bpId = RegisterTestBlueprint("AssetIdTest", assetId);

            Assert.True(_registry.TryGetById(bpId, out var def));
            Assert.NotNull(def);
            Assert.NotEqual(Guid.Empty, def!.AssetId);
            Assert.Equal(assetId, def.AssetId);
        }
    }

    /// <summary>Minimal stub resolver for tests that don't need GUID resolution.</summary>
    internal sealed class StubGuidResolver : IGuidResolver
    {
        public string Resolve(Entity entity) => entity.ToString();
        public Entity Resolve(string guidStr) => Entity.Null;
    }
}
