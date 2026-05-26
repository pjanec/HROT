using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Scenario.Tests;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// Tests for <see cref="TransientMasterBuilder"/> (RBF-P3T5, RBF-P3T7).
    /// </summary>
    public sealed class TransientMasterBuilderTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ScenarioSerializer _serializer;
        private readonly Guid _exerciseId = Guid.NewGuid();

        public TransientMasterBuilderTests()
        {
            ComponentTypeRegistry.Clear();
            _tempDir = Path.Combine(Path.GetTempPath(), $"TMBTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            // Prime the ComponentTypeRegistry with the component types used in these tests
            // BEFORE building the serializer so AutoSerializer delegates are compiled for
            // them.  We register into a throw-away repo to avoid keeping a live table.
            using var primeRepo = new EntityRepository();
            primeRepo.RegisterComponent<NetworkIdentity>();
            primeRepo.RegisterComponent<NetworkAuthority>();
            primeRepo.RegisterComponent<DummyPosition>();
            primeRepo.RegisterComponent<GuidedTarget>();
            _serializer = new ScenarioSerializerBuilder("TestSubsystem").Build();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── Recording helper ──────────────────────────────────────────────────

        /// <summary>
        /// Captures a single keyframe at wall-clock tick 1_000_000 using the given setup
        /// lambda.  Returns the path to the .fdp file.
        /// </summary>
        private string MakeNetworkRecording(int nodeId, Action<EntityRepository> setup)
        {
            var path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
            var meta = new RecordingMetadata { ExerciseId = _exerciseId, NodeId = nodeId };

            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<DummyPosition>();
            repo.RegisterComponent<GuidedTarget>();
            setup(repo);

            using (var recorder = new AsyncRecorder(path, meta))
                recorder.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);

            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }

        // ── Entity-search helpers ─────────────────────────────────────────────

        private static Entity GetSingleAliveEntity(EntityRepository repo)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, repo.GetMetadata(i).Generation);
                if (repo.IsAlive(e)) return e;
            }
            throw new InvalidOperationException("No alive entity found.");
        }

        private static Entity FindEntityWithNetId(EntityRepository repo, long netVal)
        {
            int typeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
            if (typeId < 0)
                throw new InvalidOperationException("NetworkIdentity not registered.");
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, repo.GetMetadata(i).Generation);
                if (!repo.IsAlive(e)) continue;
                if (!repo.GetComponentMask(i).IsSet(typeId)) continue;
                if (repo.GetComponent<NetworkIdentity>(e).Value == netVal) return e;
            }
            throw new InvalidOperationException($"No entity with NetworkIdentity.Value={netVal}.");
        }

        // ── RBF-P3T5: consensus extraction ───────────────────────────────────

        [Fact]
        public void RBF_P3T5_Build_TwoNodes_SplitAuthority()
        {
            // Node 1: authoritative DummyPosition; Node 2: authoritative GuidedTarget.
            // Master must have 1 entity with both components and X == 1.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 42L });
                repo.AddComponent(e, new DummyPosition { X = 1f, Y = 0f, Z = 0f });
                repo.SetAuthority<NetworkIdentity>(e, true);
                repo.SetAuthority<DummyPosition>(e, true);
            });
            var path2 = MakeNetworkRecording(2, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 42L });
                repo.AddComponent(e, new GuidedTarget { TargetId = Entity.Null });
                repo.SetAuthority<NetworkIdentity>(e, true);
                repo.SetAuthority<GuidedTarget>(e, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            Assert.Equal(1, master.EntityCount);
            var masterEntity = GetSingleAliveEntity(master);
            Assert.True(master.HasComponent<DummyPosition>(masterEntity));
            Assert.True(master.HasComponent<GuidedTarget>(masterEntity));
            Assert.Equal(1f, master.GetComponent<DummyPosition>(masterEntity).X);
        }

        [Fact]
        public void RBF_P3T5_Build_GhostExcluded()
        {
            // Node 1: authoritative DummyPosition{X=10}.
            // Node 2: same entity, DummyPosition{X=99} NOT authoritative (ghost).
            // Master must use X=10 only.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 99L });
                repo.AddComponent(e, new DummyPosition { X = 10f, Y = 0f, Z = 0f });
                repo.SetAuthority<NetworkIdentity>(e, true);
                repo.SetAuthority<DummyPosition>(e, true);
            });
            var path2 = MakeNetworkRecording(2, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 99L });
                repo.AddComponent(e, new DummyPosition { X = 99f, Y = 0f, Z = 0f });
                // No authority set: both bits remain 0 (ghost).
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            Assert.Equal(1, master.EntityCount);
            var masterEntity = GetSingleAliveEntity(master);
            Assert.True(master.HasComponent<DummyPosition>(masterEntity));
            Assert.Equal(10f, master.GetComponent<DummyPosition>(masterEntity).X);
            Assert.NotEqual(99f, master.GetComponent<DummyPosition>(masterEntity).X);
        }

        [Fact]
        public void RBF_P3T5_Build_RelationalHandleRemapped()
        {
            // Single node: entityA (NetworkIdentity 100) holds a GuidedTarget pointing to
            // entityB (NetworkIdentity 101).  The transient master must remap the handle.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var entityA = repo.CreateEntity();
                var entityB = repo.CreateEntity();
                repo.AddComponent(entityB, new NetworkIdentity { Value = 101L });
                repo.AddComponent(entityB, new DummyPosition { X = 5f, Y = 0f, Z = 0f });
                repo.SetAuthority<NetworkIdentity>(entityB, true);
                repo.SetAuthority<DummyPosition>(entityB, true);
                repo.AddComponent(entityA, new NetworkIdentity { Value = 100L });
                repo.AddComponent(entityA, new GuidedTarget { TargetId = entityB });
                repo.SetAuthority<NetworkIdentity>(entityA, true);
                repo.SetAuthority<GuidedTarget>(entityA, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            var masterA = FindEntityWithNetId(master, 100L);
            var masterB = FindEntityWithNetId(master, 101L);
            Assert.True(master.HasComponent<GuidedTarget>(masterA));
            var gt = master.GetComponent<GuidedTarget>(masterA);
            Assert.NotEqual(Entity.Null, gt.TargetId);
            Assert.Equal(masterB, gt.TargetId);
        }

        [Fact]
        public void RBF_P3T5_Build_MissingTargetResolvesToEntityNull()
        {
            // GuidedTarget points to a dead entity handle (index 999, generation 0).
            // That handle is never alive in any SandboxRepo, so it is absent from every
            // save-map.  The resolver returns "null" and deserialization yields Entity.Null.
            var deadHandle = new Entity(999, 0);
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var entityA = repo.CreateEntity();
                repo.AddComponent(entityA, new NetworkIdentity { Value = 200L });
                repo.AddComponent(entityA, new GuidedTarget { TargetId = deadHandle });
                repo.SetAuthority<NetworkIdentity>(entityA, true);
                repo.SetAuthority<GuidedTarget>(entityA, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            var masterA = FindEntityWithNetId(master, 200L);
            Assert.True(master.HasComponent<GuidedTarget>(masterA));
            Assert.Equal(Entity.Null, master.GetComponent<GuidedTarget>(masterA).TargetId);
        }

        [Fact]
        public void RBF_P3T5_Build_SplitBrainConflict_PrimaryOwnerWins()
        {
            // Both nodes claim authority over DummyPosition.  Node 1 is the primary owner
            // (PrimaryOwnerId == 1).  §7.3 sorts primary first, so X=1 must win over X=99.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 300L });
                repo.AddComponent(e, new DummyPosition { X = 1f, Y = 0f, Z = 0f });
                repo.AddComponent(e, new NetworkAuthority { PrimaryOwnerId = 1, LocalNodeId = 1 });
                repo.SetAuthority<NetworkIdentity>(e, true);
                repo.SetAuthority<DummyPosition>(e, true);
                repo.SetAuthority<NetworkAuthority>(e, true);
            });
            var path2 = MakeNetworkRecording(2, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 300L });
                repo.AddComponent(e, new DummyPosition { X = 99f, Y = 0f, Z = 0f });
                repo.AddComponent(e, new NetworkAuthority { PrimaryOwnerId = 1, LocalNodeId = 2 });
                repo.SetAuthority<NetworkIdentity>(e, true);
                repo.SetAuthority<DummyPosition>(e, true);
                repo.SetAuthority<NetworkAuthority>(e, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            var masterE = FindEntityWithNetId(master, 300L);
            Assert.Equal(1f, master.GetComponent<DummyPosition>(masterE).X);
            Assert.NotEqual(99f, master.GetComponent<DummyPosition>(masterE).X);
        }

        [Fact]
        public void RBF_P3T5_Build_RebuildableCheaply()
        {
            // Calling Build twice on the same manager must produce consistent results.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 400L });
                repo.AddComponent(e, new DummyPosition { X = 4f, Y = 0f, Z = 0f });
                repo.SetAuthority<NetworkIdentity>(e, true);
                repo.SetAuthority<DummyPosition>(e, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);

            using var master1 = builder.Build(manager);
            using var master2 = builder.Build(manager);

            Assert.Equal(master1.EntityCount, master2.EntityCount);
            var e1 = GetSingleAliveEntity(master1);
            var e2 = GetSingleAliveEntity(master2);
            Assert.Equal(
                master1.GetComponent<DummyPosition>(e1).X,
                master2.GetComponent<DummyPosition>(e2).X);
        }

        // ── RBF-P3T7: local-entities provider injection ───────────────────────

        [Fact]
        public void RBF_P3T7_LocalEntities_ProviderEntitiesAppearInMaster()
        {
            // Provider = node 1 (only node loaded).  Local entity with DummyPosition{X=7}
            // must appear in the master.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var local = repo.CreateEntity();
                repo.AddComponent(local, new DummyPosition { X = 7f, Y = 0f, Z = 0f });
                repo.SetAuthority<DummyPosition>(local, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            bool found = false;
            for (int i = 0; i <= master.MaxEntityIndex; i++)
            {
                var e = new Entity(i, master.GetMetadata(i).Generation);
                if (!master.IsAlive(e)) continue;
                if (!master.HasComponent<DummyPosition>(e)) continue;
                if (master.GetComponent<DummyPosition>(e).X == 7f) { found = true; break; }
            }
            Assert.True(found, "Local entity from provider must appear in the master.");
        }

        [Fact]
        public void RBF_P3T7_LocalEntities_NonProviderLocalsExcluded()
        {
            // Provider = node 1 (lowest).  Node 2's local entity (X=99) must NOT appear.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                // A global entity so both nodes share the same exercise.
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = 700L });
                repo.SetAuthority<NetworkIdentity>(e, true);
            });
            var path2 = MakeNetworkRecording(2, repo =>
            {
                // Local entity on node 2 — must be excluded from the master.
                var local = repo.CreateEntity();
                repo.AddComponent(local, new DummyPosition { X = 99f, Y = 0f, Z = 0f });
                repo.SetAuthority<DummyPosition>(local, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            for (int i = 0; i <= master.MaxEntityIndex; i++)
            {
                var e = new Entity(i, master.GetMetadata(i).Generation);
                if (!master.IsAlive(e)) continue;
                if (!master.HasComponent<DummyPosition>(e)) continue;
                Assert.NotEqual(99f, master.GetComponent<DummyPosition>(e).X);
            }
        }

        [Fact]
        public void RBF_P3T7_LocalEntities_UseFullPresenceMask_NotAuthorityMask()
        {
            // Local entity: DummyPosition present but authority bit NOT set.
            // §7.8 uses the full presence mask for local entities, so the component must
            // still appear in the master.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var local = repo.CreateEntity();
                repo.AddComponent(local, new DummyPosition { X = 3f, Y = 0f, Z = 0f });
                // Explicitly leave authority unset (already 0 by default, shown for clarity).
                repo.SetAuthority<DummyPosition>(local, false);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            bool found = false;
            for (int i = 0; i <= master.MaxEntityIndex; i++)
            {
                var e = new Entity(i, master.GetMetadata(i).Generation);
                if (!master.IsAlive(e)) continue;
                if (!master.HasComponent<DummyPosition>(e)) continue;
                if (master.GetComponent<DummyPosition>(e).X == 3f) { found = true; break; }
            }
            Assert.True(found,
                "Local entity must appear in master via full presence mask even without authority.");
        }

        [Fact]
        public void RBF_P3T7_LocalEntities_GlobalHandleToLocalResolves()
        {
            // Global entity on node 1 holds a GuidedTarget pointing at a local entity.
            // The local entity must be pre-allocated in the master and the handle must resolve.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var globalE = repo.CreateEntity();
                var localE  = repo.CreateEntity();

                repo.AddComponent(localE, new DummyPosition { X = 8f, Y = 0f, Z = 0f });

                repo.AddComponent(globalE, new NetworkIdentity { Value = 500L });
                repo.AddComponent(globalE, new GuidedTarget { TargetId = localE });
                repo.SetAuthority<NetworkIdentity>(globalE, true);
                repo.SetAuthority<GuidedTarget>(globalE, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);
            using var master = builder.Build(manager);

            var masterGlobal = FindEntityWithNetId(master, 500L);
            Assert.True(master.HasComponent<GuidedTarget>(masterGlobal));
            var gt = master.GetComponent<GuidedTarget>(masterGlobal);
            Assert.NotEqual(Entity.Null, gt.TargetId);
            Assert.True(master.HasComponent<DummyPosition>(gt.TargetId));
            Assert.Equal(8f, master.GetComponent<DummyPosition>(gt.TargetId).X);
        }

        [Fact]
        public void RBF_P3T7_LocalEntities_SwitchProviderRebuilds()
        {
            // Two nodes, each with a local entity at a distinct X value.
            // Switching the provider changes which local entity appears in the master.
            var path1 = MakeNetworkRecording(1, repo =>
            {
                var local = repo.CreateEntity();
                repo.AddComponent(local, new DummyPosition { X = 7f, Y = 0f, Z = 0f });
                repo.SetAuthority<DummyPosition>(local, true);
            });
            var path2 = MakeNetworkRecording(2, repo =>
            {
                var local = repo.CreateEntity();
                repo.AddComponent(local, new DummyPosition { X = 9f, Y = 0f, Z = 0f });
                repo.SetAuthority<DummyPosition>(local, true);
            });

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            manager.SetBaseWallTicks(1_000_000L);
            var builder = new TransientMasterBuilder(_serializer);

            // Provider = node 1 (default): X=7 must be present, X=9 must be absent.
            using (var master = builder.Build(manager))
            {
                bool foundSeven = false;
                for (int i = 0; i <= master.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, master.GetMetadata(i).Generation);
                    if (!master.IsAlive(e)) continue;
                    if (!master.HasComponent<DummyPosition>(e)) continue;
                    Assert.NotEqual(9f, master.GetComponent<DummyPosition>(e).X);
                    if (master.GetComponent<DummyPosition>(e).X == 7f) foundSeven = true;
                }
                Assert.True(foundSeven, "Provider=node1 must include X=7.");
            }

            // Switch to node 2: X=9 must be present, X=7 must be absent.
            manager.SetLocalEntitiesProvider(2);
            using (var master = builder.Build(manager))
            {
                bool foundNine = false;
                for (int i = 0; i <= master.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, master.GetMetadata(i).Generation);
                    if (!master.IsAlive(e)) continue;
                    if (!master.HasComponent<DummyPosition>(e)) continue;
                    Assert.NotEqual(7f, master.GetComponent<DummyPosition>(e).X);
                    if (master.GetComponent<DummyPosition>(e).X == 9f) foundNine = true;
                }
                Assert.True(foundNine, "Provider=node2 must include X=9.");
            }
        }

        [Fact]
        public void RBF_P3T7_SyntheticGuid_ParseableAndDeterministic()
        {
            // Same inputs must produce the same key; the key must be a valid Guid string;
            // different inputs must produce distinct keys.
            string key1 = TransientMasterBuilder.MakeSyntheticKey(1, 5, 2);
            string key2 = TransientMasterBuilder.MakeSyntheticKey(1, 5, 2);

            Assert.Equal(key1, key2);
            Assert.True(Guid.TryParse(key1, out _), $"MakeSyntheticKey result '{key1}' is not a valid Guid.");

            string key3 = TransientMasterBuilder.MakeSyntheticKey(2, 5, 2);  // different providerNodeId
            Assert.NotEqual(key1, key3);
        }
    }
}
