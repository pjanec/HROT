using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EntityStateExtractionService"/> (DD-P2-T04).
    /// </summary>
    public sealed class EntityStateExtractionServiceTests : IDisposable
    {
        // ── Test-only components (IDs 220-221 reserved for this test class) ──

        [StructLayout(LayoutKind.Sequential)]
        [ComponentId(220)]
        private struct TestPosition { public float X; public float Y; }

        [StructLayout(LayoutKind.Sequential)]
        [ComponentId(221)]
        private struct TestHealth { public int Max; }

        // ── Fixture ───────────────────────────────────────────────────────────

        private readonly EntityRepository _repo;

        public EntityStateExtractionServiceTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<TestPosition>();
            _repo.RegisterComponent<TestHealth>();
        }

        public void Dispose() => _repo.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity CreateLiveEntity(
            long?         networkId = null,
            TestPosition? pos       = null,
            TestHealth?   hp        = null)
        {
            var e = _repo.CreateEntity();
            if (networkId.HasValue)
                _repo.AddComponent(e, new NetworkIdentity(networkId.Value));
            if (pos.HasValue)
                _repo.AddComponent(e, pos.Value);
            if (hp.HasValue)
                _repo.AddComponent(e, hp.Value);
            return e;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_NullRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EntityStateExtractionService(null!));
        }

        [Fact]
        public void ExtractEntities_EmptyRepo_ReturnsEmpty()
        {
            var svc = new EntityStateExtractionService(_repo);
            Assert.Empty(svc.ExtractEntities());
        }

        [Fact]
        public void ExtractEntities_NoFilter_ReturnsAllAliveEntities()
        {
            CreateLiveEntity(networkId: 1);
            CreateLiveEntity(networkId: 2);
            var svc = new EntityStateExtractionService(_repo);
            Assert.Equal(2, svc.ExtractEntities().Count);
        }

        [Fact]
        public void ExtractEntities_DeadEntityExcluded()
        {
            var alive = CreateLiveEntity(networkId: 100);
            var dead  = CreateLiveEntity(networkId: 200);
            _repo.DestroyEntity(dead);

            var svc    = new EntityStateExtractionService(_repo);
            var result = svc.ExtractEntities();

            Assert.Single(result);
            Assert.Equal(100L, result[0].NetworkId);
        }

        [Fact]
        public void ExtractEntities_NetworkIdFilter_OnlyReturnsMatching()
        {
            CreateLiveEntity(networkId: 1);
            CreateLiveEntity(networkId: 2);
            CreateLiveEntity(networkId: 3);

            var svc    = new EntityStateExtractionService(_repo);
            var result = svc.ExtractEntities(new[] { 1L, 3L });

            Assert.Equal(2, result.Count);
            var ids = result.Select(r => r.NetworkId).ToHashSet();
            Assert.Contains(1L, ids);
            Assert.Contains(3L, ids);
        }

        [Fact]
        public void ExtractEntities_EntityWithComponents_PopulatesComponentDict()
        {
            CreateLiveEntity(
                networkId: 42,
                pos: new TestPosition { X = 1, Y = 2 },
                hp:  new TestHealth   { Max = 100 });

            var svc    = new EntityStateExtractionService(_repo);
            var result = svc.ExtractEntities();

            Assert.Single(result);
            var dto = result[0];
            Assert.Equal(42L, dto.NetworkId);
            Assert.True(dto.Components.ContainsKey(nameof(TestPosition)), "TestPosition should be in Components");
            Assert.True(dto.Components.ContainsKey(nameof(TestHealth)),   "TestHealth should be in Components");
        }

        [Fact]
        public void ExtractEntities_LocalIndexAndGenerationPopulated()
        {
            var e      = CreateLiveEntity(networkId: 7);
            var svc    = new EntityStateExtractionService(_repo);
            var result = svc.ExtractEntities();

            Assert.Single(result);
            Assert.Equal(e.Index,      result[0].LocalIndex);
            Assert.Equal(e.Generation, result[0].LocalGeneration);
        }

        [Fact]
        public void ExtractEntities_EmptyNetworkIdFilter_ReturnsAll()
        {
            CreateLiveEntity(networkId: 1);
            CreateLiveEntity(networkId: 2);
            var svc = new EntityStateExtractionService(_repo);

            // Passing an empty list is treated as "no filter" — all entities returned.
            var result = svc.ExtractEntities(new List<long>());
            Assert.Equal(2, result.Count);
        }
    }
}
