using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Scenario.Tests;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>A recorded, per-node type that must NEVER merge (DataPolicy.NodeLocal) — the rail's own, so the skip is proved on a RECORDED component.</summary>
    [ComponentId(457)]
    [DataPolicy(DataPolicy.NodeLocal)]
    public struct MergerNodeLocalProbe { public int Value; }

    /// <summary>
    /// ⭐ G-7 / CE-4354 S1 (docs/UX/DESIGN_Review_Perspective.md §6.6, §6.12) — the rails of <see cref="BinaryMerger"/>: the ORACLE (binary equals the JSON merge),
    /// the OWNER, the REMAP (+ the M2 probe), the STABLE HANDLES and the skip of <see cref="DataPolicy.NodeLocal"/>. Fixtures: the same keyframe recordings
    /// <see cref="TransientMasterBuilderTests"/> uses (one file per node, split / disjoint ownership).
    /// </summary>
    public sealed class BinaryMergerTests : IDisposable
    {
        private readonly string _dir;
        private readonly ScenarioSerializer _serializer;
        private readonly Guid _exercise = Guid.NewGuid();

        public BinaryMergerTests()
        {
            ComponentTypeRegistry.Clear();
            _dir = Path.Combine(Path.GetTempPath(), $"BinMerge_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
            using var prime = new EntityRepository();
            prime.RegisterComponent<NetworkIdentity>();
            prime.RegisterComponent<NetworkAuthority>();
            prime.RegisterComponent<DummyPosition>();
            prime.RegisterComponent<GuidedTarget>();
            _serializer = new ScenarioSerializerBuilder("TestSubsystem").Build();
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        // ── fixtures ───────────────────────────────────────────────────────────────────────

        /// <summary>One file for a node: each step mutates the node's world, then a keyframe at wall tick (step+1) × 1 000 000.</summary>
        private string Record(int nodeId, params Action<EntityRepository>[] steps)
        {
            var path = Path.Combine(_dir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
            var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)_exercise, NodeId = (Fdp.Core.NodeId)nodeId };
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<DummyPosition>();
            repo.RegisterComponent<GuidedTarget>();
            repo.RegisterComponent<UnitRoster>();
            repo.RegisterComponent<MergerNodeLocalProbe>();
            repo.RegisterComponent<ChildMap>();
            repo.RegisterComponent<TerrainObjectKey>();
            using (var rec = new AsyncRecorder(path, meta))
                for (int i = 0; i < steps.Length; i++)
                {
                    steps[i](repo);
                    rec.CaptureKeyframe(repo, (i + 1) * 1_000_000L, blocking: true, eventBus: repo.Bus);
                }
            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }

        private static Entity Unit(EntityRepository repo, int net)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity { Value = net });
            repo.SetAuthority<NetworkIdentity>(e, true);
            return e;
        }

        private static void Own<T>(EntityRepository repo, Entity e, T value) where T : unmanaged
        {
            repo.AddComponent(e, value);
            repo.SetAuthority<T>(e, true);
        }

        /// <summary>Entities already in the target before the merge: they make the target's handles DIFFER from the sandboxes' (a remap that forgot to map would otherwise pass by coincidence — both worlds allocate 0, 1, 2 …).</summary>
        private const int Junk = 3;

        private static EntityRepository NewTarget(int junk = Junk)
        {
            var t = new EntityRepository();
            RepositoryPriming.RegisterDiscoveredComponents(t);
            for (int i = 0; i < junk; i++) t.CreateEntity();
            return t;
        }

        private static long NetOf(EntityRepository repo, Entity e)
            => e.IsNull || !repo.IsAlive(e) || !repo.HasComponent<NetworkIdentity>(e) ? -1 : repo.GetComponent<NetworkIdentity>(e).Value;

        private static Entity FindNet(EntityRepository repo, long net)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, repo.GetMetadata(i).Generation);
                if (repo.IsAlive(e) && repo.HasComponent<NetworkIdentity>(e) && repo.GetComponent<NetworkIdentity>(e).Value == net) return e;
            }
            return Entity.Null;
        }

        /// <summary>A component's bytes with every raw Entity replaced by its NETWORK id (so two worlds' values compare), as a hex string; null when absent.</summary>
        private static unsafe string? ValueByNetId(EntityRepository repo, Entity e, int typeId)
        {
            if (!repo.GetComponentMask(e.Index).IsSet(typeId)) return null;
            var type = ComponentTypeRegistry.GetType(typeId)!;
            var plan = EntityRemapPlan.For(type);
            var buf = new byte[plan.Size];
            fixed (byte* b = buf)
            {
                Buffer.MemoryCopy(repo.GetComponentPointer(e, typeId), b, plan.Size, plan.Size);
                plan.RemapAt(b, x => new Entity((int)NetOf(repo, x), 1));
            }
            return Convert.ToHexString(buf);
        }

        // ── the ORACLE rail ────────────────────────────────────────────────────────────────

        [Fact]
        public void Oracle_EveryUnmanagedComponentOfTheJsonMerge_IsInTheBinaryMerge_EqualByNetworkId()
        {
            // 42: node 1 holds a GHOST of it (X 99, not owned) and owns 43's position + a LOCAL entity; node 2 owns 42 (position + a target ref to 43) and holds 43's identity only.
            var p1 = Record(1, r =>
            {
                var a = r.CreateEntity(); r.AddComponent(a, new NetworkIdentity { Value = 42 }); r.AddComponent(a, new DummyPosition { X = 99 });   // ghost: held, not owned
                var b = Unit(r, 43); Own(r, b, new DummyPosition { X = 5 });
                var local = r.CreateEntity(); r.AddComponent(local, new DummyPosition { X = 7 });
            });
            var p2 = Record(2, r =>
            {
                var a = Unit(r, 42); Own(r, a, new DummyPosition { X = 1, Y = 2, Z = 3 });
                var b = r.CreateEntity(); r.AddComponent(b, new NetworkIdentity { Value = 43 });
                Own(r, a, new GuidedTarget { TargetId = b });
            });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            manager.SetBaseWallTicks(1_000_000L);

            using var json = new TransientMasterBuilder(_serializer).Build(manager);
            using var bin = NewTarget();
            var merger = new BinaryMerger();
            merger.Merge(manager, bin);

            Assert.Equal(json.EntityCount + Junk, bin.EntityCount);
            int compared = 0;
            foreach (long net in new long[] { 42, 43 })
            {
                var je = FindNet(json, net); var be = FindNet(bin, net);
                Assert.False(je.IsNull); Assert.False(be.IsNull);
                foreach (int typeId in ComponentTypeRegistry.GetAllIds())
                {
                    var type = ComponentTypeRegistry.GetType(typeId)!;
                    if (!type.IsValueType) continue;                               // the oracle covers UNMANAGED components (managed ones are what the JSON merge loses)
                    var jv = ValueByNetId(json, je, typeId);
                    if (jv == null) continue;
                    Assert.Equal(jv, ValueByNetId(bin, be, typeId));               // ⭐ every component of the JSON merge is in the binary merge, equal
                    compared++;
                }
            }
            Assert.True(compared >= 5, $"compared only {compared} components");
            Assert.Equal(1f, bin.GetComponent<DummyPosition>(FindNet(bin, 42)).X);   // the owner's value, not the ghost's
        }

        // ── the OWNER rail ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Owner_EveryComponentANodeOwns_EqualsItsSandbox_ManagedIncluded()
        {
            var p1 = Record(1, r =>
            {
                var a = Unit(r, 100); Own(r, a, new DummyPosition { X = 11 });
                var kid = Unit(r, 101);
                r.SetAuthority<NetworkIdentity>(kid, false);
                r.AddComponent(a, new ChildMap { InstanceToEntity = new Dictionary<int, Entity> { [3] = kid } });
                r.SetAuthority(a, ComponentTypeRegistry.GetId(typeof(ChildMap)), true);
                r.AddComponent(a, new TerrainObjectKey { Key = "t/b/d1" });
                r.SetAuthority(a, ComponentTypeRegistry.GetId(typeof(TerrainObjectKey)), true);
            });
            var p2 = Record(2, r =>
            {
                var kid = Unit(r, 101); Own(r, kid, new DummyPosition { X = 22 });
            });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            manager.SetBaseWallTicks(1_000_000L);
            using var bin = NewTarget();
            var merger = new BinaryMerger();
            merger.Merge(manager, bin);

            foreach (var (node, ctx) in manager.Contexts)
            {
                var sb = ctx.SandboxRepo;
                for (int i = 0; i <= sb.MaxEntityIndex; i++)
                {
                    var se = new Entity(i, sb.GetMetadata(i).Generation);
                    if (!sb.IsAlive(se) || !sb.HasComponent<NetworkIdentity>(se)) continue;
                    var te = FindNet(bin, sb.GetComponent<NetworkIdentity>(se).Value);
                    var owned = sb.GetComponentMask(i); owned.BitwiseAnd(sb.GetMetadata(i).AuthorityMask);
                    for (int typeId = 0; typeId < 512; typeId++)
                    {
                        if (!owned.IsSet(typeId)) continue;
                        var type = ComponentTypeRegistry.GetType(typeId)!;
                        if (type.IsValueType)
                            Assert.Equal(ValueByNetId(sb, se, typeId), ValueByNetId(bin, te, typeId));
                        else if (type == typeof(ChildMap))
                        {
                            var src = (ChildMap)sb.GetManagedComponentByTypeId(se, typeId);
                            var dst = (ChildMap)bin.GetManagedComponentByTypeId(te, typeId);
                            Assert.Equal(src.InstanceToEntity.Count, dst.InstanceToEntity.Count);
                            foreach (var kv in src.InstanceToEntity)
                                Assert.Equal(NetOf(sb, kv.Value), NetOf(bin, dst.InstanceToEntity[kv.Key]));   // the handle mapped, node 1's own copy of 101 → the merged 101
                        }
                        else
                            Assert.Same(sb.GetManagedComponentByTypeId(se, typeId), bin.GetManagedComponentByTypeId(te, typeId));   // M5: the same object, no copy
                    }
                }
            }
            Assert.Equal(22f, bin.GetComponent<DummyPosition>(FindNet(bin, 101)).X);   // node 2 owns 101's position
            Assert.True(bin.HasManagedComponent<TerrainObjectKey>(FindNet(bin, 100)));
        }

        // ── the REMAP rail (+ the paradox rule) ────────────────────────────────────────────

        [Fact]
        public unsafe void Remap_EveryEntityFieldOfTheMergedWorld_ResolvesToALiveMergedEntity_InlineArrayIncluded()
        {
            var p1 = Record(1, r =>
            {
                var boss = Unit(r, 200);
                var s1 = Unit(r, 201); var s2 = Unit(r, 202);
                var roster = new UnitRoster { Count = 2 };
                roster.SubordinateEntities[0] = s1; roster.SubordinateEntities[1] = s2;   // ⭐ D03 — an [InlineArray] of Entity
                Own(r, boss, roster);
                Own(r, boss, new GuidedTarget { TargetId = s2 });
            });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1 });
            manager.SetBaseWallTicks(1_000_000L);
            using var bin = NewTarget();
            var merger = new BinaryMerger();
            merger.Merge(manager, bin);

            int checkedFields = 0;
            for (int i = 0; i <= bin.MaxEntityIndex; i++)
            {
                var e = new Entity(i, bin.GetMetadata(i).Generation);
                if (!bin.IsAlive(e)) continue;
                for (int typeId = 0; typeId < 512; typeId++)
                {
                    if (!bin.GetComponentMask(i).IsSet(typeId)) continue;
                    var type = ComponentTypeRegistry.GetType(typeId)!;
                    if (!type.IsValueType) continue;
                    var plan = EntityRemapPlan.For(type);
                    if (!plan.HasEntities) continue;
                    byte* p = (byte*)bin.GetComponentPointer(e, typeId);
                    foreach (int off in plan.Offsets)
                    {
                        var h = Unsafe.ReadUnaligned<Entity>(p + off);
                        if (!h.IsNull) { Assert.True(bin.IsAlive(h), $"{type.Name}@{off} -> {h} is not a live merged entity"); checkedFields++; }
                    }
                }
            }
            Assert.True(checkedFields >= 3, $"checked only {checkedFields} entity fields");

            var boss = FindNet(bin, 200);
            var merged = bin.GetComponent<UnitRoster>(boss);
            Assert.Equal(201, NetOf(bin, merged.SubordinateEntities[0]));
            Assert.Equal(202, NetOf(bin, merged.SubordinateEntities[1]));
            Assert.Equal(202, NetOf(bin, bin.GetComponent<GuidedTarget>(boss).TargetId));
            Assert.Empty(merger.Paradoxes);
        }

        [Fact]
        public void Remap_AReferenceToNoMergedEntity_BecomesNull_AndIsFlagged()
        {
            var p1 = Record(1, r =>
            {
                var a = Unit(r, 300);
                var local = r.CreateEntity();                       // a LOCAL entity of node 1 …
                Own(r, a, new GuidedTarget { TargetId = local });
            });
            var p2 = Record(2, r =>
            {
                var other = r.CreateEntity(); r.AddComponent(other, new DummyPosition { X = 1 });
                var a = Unit(r, 301); Own(r, a, new GuidedTarget { TargetId = other });   // … and node 2 points at ITS local (node 1 is the provider: not in the map)
            });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            manager.SetBaseWallTicks(1_000_000L);
            using var bin = NewTarget();
            var merger = new BinaryMerger();
            merger.Merge(manager, bin);

            Assert.True(bin.GetComponent<GuidedTarget>(FindNet(bin, 301)).TargetId.IsNull);   // the paradox rule: Null …
            var flagged = Assert.Single(merger.Paradoxes);                                     // … and FLAGGED, never hidden, never thrown
            Assert.Equal(301, flagged.NetId);
            Assert.Equal(2, flagged.NodeId);
            Assert.Equal(typeof(GuidedTarget), flagged.Component);
            Assert.False(bin.GetComponent<GuidedTarget>(FindNet(bin, 300)).TargetId.IsNull);   // the provider's own local resolves
        }

        // ── the M2 PROBE as a rail ─────────────────────────────────────────────────────────

        [Fact]
        public void M2_EveryComponentWithARawEntity_HasARemapEntry()
        {
            using var prime = new EntityRepository();
            RepositoryPriming.RegisterDiscoveredComponents(prime);   // every [ComponentId] type of the loaded assemblies — the set the merge sees
            int probed = 0, withEntities = 0;
            foreach (var type in ComponentTypeRegistry.GetAllRegistered())
            {
                int expected = CountRawEntities(type);   // an INDEPENDENT walker (every field incl. private; inline arrays × capacity; generic arguments for a managed type)
                if (expected == 0) { Assert.False(type.IsValueType && EntityRemapPlan.For(type).HasEntities, $"{type.Name}: the plan found an Entity the independent walk did not"); continue; }
                probed++;
                int id = ComponentTypeRegistry.GetId(type);
                bool merged = !DataPolicyAttribute.IsNodeLocal(type) && ComponentTypeRegistry.IsRecordable(id);
                if (!merged) continue;               // a node-local / unrecorded type never reaches the merged world
                withEntities++;
                if (type.IsValueType)
                    Assert.True(EntityRemapPlan.For(type).Offsets.Count == expected,
                        $"{type.Name} holds {expected} raw Entity handle(s) but its remap plan has {EntityRemapPlan.For(type).Offsets.Count} ({string.Join(", ", EntityRemapPlan.For(type).FieldPaths)})");
                else
                    Assert.True(ManagedEntityRemap.TryGet(type, out _), $"{type.Name} is a MANAGED component holding a raw Entity with no ManagedEntityRemap entry — it would reach the merged world with a node-local handle");
            }
            Assert.True(withEntities >= 20, $"the probe found only {withEntities} merged types with a raw Entity (probed {probed})");
            var plan = EntityRemapPlan.For(typeof(UnitRoster));
            Assert.Equal(UnitRoster.Capacity, plan.Offsets.Count);                                       // the inline array's every element
            Assert.Contains(plan.FieldPaths, p => p.Contains("SubordinateEntities"));
        }

        private static int CountRawEntities(Type t, int depth = 0)
        {
            if (depth > 6 || t.IsPrimitive || t.IsEnum || t == typeof(string)) return 0;
            if (t == typeof(Entity)) return 1;
            int n = 0;
            if (t.IsValueType)
            {
                if (t.Namespace?.StartsWith("System", StringComparison.Ordinal) ?? false) return 0;
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    int inner = CountRawEntities(f.FieldType, depth + 1);
                    var ia = f.FieldType.GetCustomAttribute<InlineArrayAttribute>();
                    n += ia != null ? 0 : inner;          // an InlineArray struct's own field is counted once below
                }
                var self = t.GetCustomAttribute<InlineArrayAttribute>();
                if (self != null)
                {
                    var elem = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0].FieldType;
                    return CountRawEntities(elem, depth + 1) * self.Length;
                }
                // fields whose type is an InlineArray struct: count its elements × capacity
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (f.FieldType.GetCustomAttribute<InlineArrayAttribute>() != null) n += CountRawEntities(f.FieldType, depth + 1);
                return n;
            }
            // a managed type: any public member (field / property) whose type, or generic argument, holds an Entity
            var seen = new HashSet<Type>();
            return HoldsEntity(t, seen, 0) ? 1 : 0;
        }

        private static bool HoldsEntity(Type t, HashSet<Type> seen, int depth)
        {
            if (depth > 6 || t.IsPrimitive || t.IsEnum || t == typeof(string) || !seen.Add(t)) return false;
            if (t == typeof(Entity)) return true;
            foreach (var a in t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes) if (HoldsEntity(a, seen, depth + 1)) return true;
            if (t.IsArray && HoldsEntity(t.GetElementType()!, seen, depth + 1)) return true;
            if (t.Namespace?.StartsWith("System", StringComparison.Ordinal) ?? false) return false;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) if (HoldsEntity(f.FieldType, seen, depth + 1)) return true;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) if (HoldsEntity(p.PropertyType, seen, depth + 1)) return true;
            return false;
        }

        // ── the STABLE-HANDLE rail ─────────────────────────────────────────────────────────

        [Fact]
        public void StableHandles_TwoSuccessiveMerges_KeepTheTargetEntity_AndFollowTheRecording()
        {
            var p1 = Record(1,
                r =>   // frame 1: units 400 (position + target ref) and 401
                {
                    var a = Unit(r, 400); Own(r, a, new DummyPosition { X = 1 }); Own(r, a, new GuidedTarget { TargetId = a });
                    var b = Unit(r, 401); Own(r, b, new DummyPosition { X = 5 });
                },
                r =>   // frame 2: 400 moved and LOST its target ref; 401 is gone; 402 is born
                {
                    var a = FindNet(r, 400);
                    r.SetComponent(a, new DummyPosition { X = 2 });
                    r.RemoveComponent<GuidedTarget>(a);
                    r.DestroyEntity(FindNet(r, 401));
                    var c = Unit(r, 402); Own(r, c, new DummyPosition { X = 9 });
                });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1 });
            manager.SetBaseWallTicks(1_000_000L);
            using var bin = NewTarget();
            var merger = new BinaryMerger();

            merger.Merge(manager, bin);
            var a1 = FindNet(bin, 400);
            Assert.Equal(2 + Junk, bin.EntityCount);
            Assert.Equal(1f, bin.GetComponent<DummyPosition>(a1).X);
            Assert.True(bin.HasComponent<GuidedTarget>(a1));
            Assert.Equal(2, merger.Target.LastCreated);

            manager.SetBaseWallTicks(2_000_000L);
            merger.Merge(manager, bin);
            var a2 = FindNet(bin, 400);
            Assert.Equal(a1, a2);                                              // ⭐ the same target Entity (handle + generation) for a network id
            Assert.True(merger.TryGetEntity(400, out var viaMap) && viaMap == a1);
            Assert.Equal(2f, bin.GetComponent<DummyPosition>(a2).X);           // follows the recording
            Assert.False(bin.HasComponent<GuidedTarget>(a2));                  // a component no source supplies any more is removed
            Assert.True(FindNet(bin, 401).IsNull);                             // gone from every node ⇒ destroyed
            Assert.False(FindNet(bin, 402).IsNull);                            // born ⇒ created
            Assert.Equal(2 + Junk, bin.EntityCount);
            Assert.Equal(1, merger.Target.LastCreated);
            Assert.Equal(1, merger.Target.LastDestroyed);

            // a world boundary: Reset forgets the map; the next merge into a CLEARED world creates afresh
            bin.SoftClear();
            merger.Reset();
            merger.Merge(manager, bin);
            Assert.Equal(2, bin.EntityCount);
            Assert.Equal(2, merger.Target.LastCreated);
        }

        // ── NodeLocal / unrecorded types never merge ──────────────────────────────────────

        [Fact]
        public void NodeLocalComponents_AreNotCopied_OthersOnTheSameEntityAre()
        {
            var p1 = Record(1, r =>
            {
                var a = Unit(r, 500); Own(r, a, new DummyPosition { X = 3 }); Own(r, a, new MergerNodeLocalProbe { Value = 77 });
            });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1 });
            manager.SetBaseWallTicks(1_000_000L);
            var probeId = ComponentTypeRegistry.GetId(typeof(MergerNodeLocalProbe));
            Assert.True(manager.Contexts[1].SandboxRepo.HasComponent<MergerNodeLocalProbe>(FindNet(manager.Contexts[1].SandboxRepo, 500)),
                "the fixture must RECORD the node-local component, or the skip proves nothing");
            Assert.True(ComponentTypeRegistry.IsRecordable(probeId));

            using var bin = NewTarget();
            new BinaryMerger().Merge(manager, bin);
            var a = FindNet(bin, 500);
            Assert.Equal(3f, bin.GetComponent<DummyPosition>(a).X);
            Assert.False(bin.HasComponent<MergerNodeLocalProbe>(a));           // ⭐ DataPolicy.NodeLocal ⇒ skipped
        }

        [Fact]
        public void NodeLocal_TheMarkedServicesCarryTheFlag()
        {
            foreach (var t in new[] { typeof(NetworkEntityMap), typeof(global::CarKinem.Spatial.SpatialGridData), typeof(TerrainWorld), typeof(EgressPublicationState), typeof(PendingAuthorityGrants), typeof(NetworkAckPeerSet) })
                Assert.True(DataPolicyAttribute.IsNodeLocal(t), t.Name);
            Assert.False(DataPolicyAttribute.IsNodeLocal(typeof(DummyPosition)));
            Assert.Equal(DataPolicy.Default, DataPolicyAttribute.Of(typeof(DummyPosition)));
        }
    }
}
