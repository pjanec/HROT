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
using Fdp.Toolkit.Replication.Extensions;
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
        private readonly Xunit.Abstractions.ITestOutputHelper _out;

        public BinaryMergerTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            _out = output;
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
            repo.RegisterComponent<DescriptorOwnership>();
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

        // ── S2 · I8 — ownership from the RECORDED components (the HANDOVER rail, §6.6) ───────────────

        private const long HandoverDescriptor = 9001;

        private static OwnershipSource RecordedOwnership()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterFromTranslator(HandoverDescriptor, new[] { ComponentType<DummyPosition>.ID });   // the replication's own component → descriptor map
            return new OwnershipSource(map);
        }

        /// <summary>
        /// One entity (net 50) on BOTH nodes. Each node holds a position (node 1: X 10, node 2: X 20) and BOTH cold masks claim it — the stale-mask case (an older
        /// recording, or any frame the cold table did not stamp). Only the recorded components tell who owns: the primary owner stays node 1 throughout; the
        /// per-descriptor record names node 1 at frame 1 and node 2 from frame 2.
        /// </summary>
        private (string p1, string p2) RecordDescriptorHandover()
        {
            Action<EntityRepository, int, float, int> frame = (r, node, x, owner) =>
            {
                var e = FindNet(r, 50);
                if (e.IsNull)
                {
                    e = Unit(r, 50);
                    r.AddComponent(e, new NetworkAuthority { PrimaryOwnerId = 1, LocalNodeId = (Fdp.Core.NodeId)node });
                    Own(r, e, new DummyPosition { X = x });
                    r.AddComponent(e, new DescriptorOwnership());
                }
                r.SetManagedComponent(e, new DescriptorOwnership().WithOwner(OwnershipExtensions.PackKey(HandoverDescriptor, 0), owner));
            };
            var p1 = Record(1, r => frame(r, 1, 10f, 1), r => frame(r, 1, 10f, 2));
            var p2 = Record(2, r => frame(r, 2, 20f, 1), r => frame(r, 2, 20f, 2));
            return (p1, p2);
        }

        [Fact]
        public void Handover_TheMergedComponentSwitchesToTheNewOwner_AtTheHandoverFrame_AndKeepsItsTargetEntity()
        {
            var (p1, p2) = RecordDescriptorHandover();
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());

            manager.SetBaseWallTicks(1_000_000L);
            merger.Merge(manager, bin);
            var before = FindNet(bin, 50);
            Assert.Equal(10f, bin.GetComponent<DummyPosition>(before).X);                  // before the handover: the OLD owner's sandbox
            Assert.True(merger.OwnershipDisagreements > 0, "the stale masks (both claim) disagree with the record, which names node 1 only");

            manager.SetBaseWallTicks(2_000_000L);
            merger.Merge(manager, bin);
            var after = FindNet(bin, 50);
            Assert.Equal(20f, bin.GetComponent<DummyPosition>(after).X);                   // ⭐ from the handover frame on: the NEW owner's sandbox
            Assert.Equal(before, after);                                                   // ⭐ M-8: the entity keeps its target entity; only the source switched
            Assert.True(merger.OwnershipDisagreements > 0);                                // node 1's mask still claims what the record gave away

            manager.SetBaseWallTicks(1_000_000L);                                          // and back (a seek): the OLD owner again
            merger.Merge(manager, bin);
            Assert.Equal(10f, bin.GetComponent<DummyPosition>(FindNet(bin, 50)).X);
        }

        [Fact]
        public void Handover_OfThePrimaryOwner_MovesEveryComponentTheRecordDoesNotPin()
        {
            // no per-descriptor entries and no map: only NetworkAuthority.PrimaryOwnerId (1 → 2 at frame 2) says who owns; the masks claim on both nodes throughout
            Action<EntityRepository, int, float, int> frame = (r, node, x, primary) =>
            {
                var e = FindNet(r, 60);
                if (e.IsNull) { e = Unit(r, 60); r.AddComponent(e, new NetworkAuthority { PrimaryOwnerId = primary, LocalNodeId = (Fdp.Core.NodeId)node }); Own(r, e, new DummyPosition { X = x }); }
                r.SetComponent(e, new NetworkAuthority { PrimaryOwnerId = primary, LocalNodeId = (Fdp.Core.NodeId)node });
            };
            var p1 = Record(1, r => frame(r, 1, 10f, 1), r => frame(r, 1, 10f, 2));
            var p2 = Record(2, r => frame(r, 2, 20f, 1), r => frame(r, 2, 20f, 2));
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            using var bin = NewTarget();
            var merger = new BinaryMerger(new OwnershipSource());

            manager.SetBaseWallTicks(1_000_000L);
            merger.Merge(manager, bin);
            Assert.Equal(10f, bin.GetComponent<DummyPosition>(FindNet(bin, 60)).X);
            manager.SetBaseWallTicks(2_000_000L);
            merger.Merge(manager, bin);
            Assert.Equal(20f, bin.GetComponent<DummyPosition>(FindNet(bin, 60)).X);
        }

        [Fact]
        public void Ownership_AnEntityWithNoRecordedOwnership_KeepsTheMaskRule()
        {
            // no NetworkAuthority anywhere (local / older recordings): node 2 owns by the mask alone, with a map in hand — the S1 rule, no disagreement counted
            var p1 = Record(1, r => { var e = Unit(r, 70); r.AddComponent(e, new DummyPosition { X = 1 }); });                        // a ghost: held, not owned
            var p2 = Record(2, r => { var e = Unit(r, 70); Own(r, e, new DummyPosition { X = 2 }); });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            manager.SetBaseWallTicks(1_000_000L);
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());
            merger.Merge(manager, bin);
            Assert.Equal(2f, bin.GetComponent<DummyPosition>(FindNet(bin, 70)).X);
            Assert.Equal(0, merger.OwnershipDisagreements);
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

        // ── S4 · I3 — the INCREMENTAL merge (§6.6 "play" rails, §6.12 S4) ───────────────────────────────

        private static long Wall(int frame) => (frame + 1) * 1_000_000L;

        /// <summary>A node's recording as a PLAY: frame 0 a keyframe, then DELTAS (a keyframe every <paramref name="keyEvery"/>), the sim clock ticking once per frame — what a real recording looks like (<see cref="Record"/> is keyframes on a frozen clock).</summary>
        private string RecordPlay(int nodeId, int frames, Action<EntityRepository, int> step, int keyEvery = 8)
        {
            var path = Path.Combine(_dir, $"play{nodeId}_{Guid.NewGuid():N}.fdp");
            var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)_exercise, NodeId = (Fdp.Core.NodeId)nodeId };
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<DummyPosition>();
            repo.RegisterComponent<GuidedTarget>();
            repo.RegisterComponent<UnitRoster>();
            repo.RegisterComponent<MergerNodeLocalProbe>();
            repo.RegisterComponent<DescriptorOwnership>();
            repo.RegisterComponent<ChildMap>();
            uint prevTick = 0;
            using (var rec = new AsyncRecorder(path, meta))
                for (int f = 0; f < frames; f++)
                {
                    repo.Tick();
                    step(repo, f);
                    if (f == 0 || (keyEvery > 0 && f % keyEvery == 0)) rec.CaptureKeyframe(repo, Wall(f), blocking: true, eventBus: repo.Bus);
                    else rec.CaptureFrame(repo, prevTick, Wall(f), blocking: true, eventBus: repo.Bus);
                    prevTick = repo.GlobalVersion > 0 ? repo.GlobalVersion - 1 : 0;   // the harness' convention: a write at the CURRENT version is caught by the next delta
                    repo.Bus.SwapBuffers();
                }
            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }

        /// <summary>A networked unit whose recorded ownership says <paramref name="owner"/> (every merge below reads the RECORD — the delta frames carry no authority masks).</summary>
        private static Entity OwnedUnit(EntityRepository r, int net, int owner, int node)
        {
            var e = r.CreateEntity();
            r.AddComponent(e, new NetworkIdentity { Value = net });
            r.AddComponent(e, new NetworkAuthority { PrimaryOwnerId = owner, LocalNodeId = (Fdp.Core.NodeId)node });
            return e;
        }

        private static void Move(EntityRepository r, int net, float x)
        {
            var e = FindNet(r, net);
            if (!e.IsNull && r.HasComponent<DummyPosition>(e)) r.SetComponent(e, new DummyPosition { X = x });
        }

        private static readonly long HandoverKey = OwnershipExtensions.PackKey(HandoverDescriptor, 0);

        /// <summary>
        /// The play fixture: node 1 owns 40 units (positions), a holder of a raw handle (101 → 102, which dies at frame 10), a ChildMap (managed, remapped), a LOCAL entity, and 300 (whose
        /// position BOTH nodes hold — node 1's 10, node 2's 20 — owned by node 1 until frame 18, by node 2 after: only the DescriptorOwnership moves); node 2 owns 200..209 and holds a
        /// ghost of 100 that its 200 points at. Moves, a birth (150), a death, a removed component and a late handle — every kind of change a merge must follow.
        /// </summary>
        private (string p1, string p2) RecordPlayGroup(int frames = 24)
        {
            var p1 = RecordPlay(1, frames, (r, f) =>
            {
                if (f == 0)
                {
                    for (int i = 100; i < 140; i++) { var u = OwnedUnit(r, i, 1, 1); r.AddComponent(u, new DummyPosition { X = i }); }
                    r.AddComponent(FindNet(r, 101), new GuidedTarget { TargetId = FindNet(r, 102) });
                    r.AddComponent(FindNet(r, 110), new ChildMap { InstanceToEntity = new Dictionary<int, Entity> { [3] = FindNet(r, 111) } });
                    var u300 = OwnedUnit(r, 300, 1, 1); r.AddComponent(u300, new DummyPosition { X = 10 }); r.AddComponent(u300, new DescriptorOwnership().WithOwner(HandoverKey, 1));
                    var local = r.CreateEntity(); r.AddComponent(local, new DummyPosition { X = 7000 });
                    return;
                }
                for (int i = 100; i < 140; i++) if ((i + f) % 7 == 0) Move(r, i, i + f * 0.5f);
                if (f == 8) r.AddComponent(FindNet(r, 105), new GuidedTarget { TargetId = FindNet(r, 106) });
                if (f == 10) r.DestroyEntity(FindNet(r, 102));
                if (f == 12) { var u = OwnedUnit(r, 150, 1, 1); r.AddComponent(u, new DummyPosition { X = 150 }); }
                if (f == 14) r.RemoveComponent<DummyPosition>(FindNet(r, 103));
                if (f == 18) r.SetManagedComponent(FindNet(r, 300), new DescriptorOwnership().WithOwner(HandoverKey, 2));
                if (f == 20) { for (int i = 0; i <= r.MaxEntityIndex; i++) { var e = new Entity(i, r.GetMetadata(i).Generation); if (r.IsAlive(e) && !r.HasComponent<NetworkIdentity>(e) && r.HasComponent<DummyPosition>(e)) r.SetComponent(e, new DummyPosition { X = 7001 }); } }
            });
            var p2 = RecordPlay(2, frames, (r, f) =>
            {
                if (f == 0)
                {
                    for (int i = 200; i < 210; i++) { var u = OwnedUnit(r, i, 2, 2); r.AddComponent(u, new DummyPosition { X = i }); }
                    var ghost = OwnedUnit(r, 100, 1, 2); r.AddComponent(ghost, new DummyPosition { X = -1 });
                    r.AddComponent(FindNet(r, 200), new GuidedTarget { TargetId = ghost });
                    var g300 = OwnedUnit(r, 300, 1, 2); r.AddComponent(g300, new DummyPosition { X = 20 }); r.AddComponent(g300, new DescriptorOwnership().WithOwner(HandoverKey, 1));
                    return;
                }
                for (int i = 200; i < 210; i++) if ((i + f) % 5 == 0) Move(r, i, i + f * 0.25f);
                if (f == 18) r.SetManagedComponent(FindNet(r, 300), new DescriptorOwnership().WithOwner(HandoverKey, 2));
            });
            return (p1, p2);
        }

        /// <summary>The two worlds hold the same merge: every network id has the same components (presence + unmanaged bytes with handles read by network id) and the same locals.</summary>
        private static void AssertSameMergedWorld(EntityRepository inc, EntityRepository full, int frame)
        {
            Assert.Equal(full.EntityCount, inc.EntityCount);
            var nets = new List<long>();
            for (long n = 100; n < 160; n++) nets.Add(n);
            for (long n = 200; n < 210; n++) nets.Add(n);
            nets.Add(300);
            foreach (long n in nets)
            {
                var ie = FindNet(inc, n); var fe = FindNet(full, n);
                Assert.True(ie.IsNull == fe.IsNull, $"frame {frame}: net {n} alive in one world only");
                if (ie.IsNull) continue;
                Assert.True(inc.GetComponentMask(ie.Index).Equals(full.GetComponentMask(fe.Index)), $"frame {frame}: net {n} holds a different set of components");
                foreach (int typeId in ComponentTypeRegistry.GetAllIds())
                {
                    var type = ComponentTypeRegistry.GetType(typeId)!;
                    if (!type.IsValueType) continue;
                    Assert.True(ValueByNetId(full, fe, typeId) == ValueByNetId(inc, ie, typeId), $"frame {frame}: net {n} {type.Name} differs");
                }
                if (inc.HasComponent<ChildMap>(ie))
                {
                    var a = ManagedOf<ChildMap>(inc, ie).InstanceToEntity; var b = ManagedOf<ChildMap>(full, fe).InstanceToEntity;
                    Assert.Equal(b.Count, a.Count);
                    foreach (var kv in b) Assert.Equal(NetOf(full, kv.Value), NetOf(inc, a[kv.Key]));
                }
                if (inc.HasComponent<DescriptorOwnership>(ie))
                    Assert.Equal(ManagedOf<DescriptorOwnership>(full, fe).TryGetOwner(HandoverKey, out int fo) ? fo : -1, ManagedOf<DescriptorOwnership>(inc, ie).TryGetOwner(HandoverKey, out int io) ? io : -1);
            }
            Assert.Equal(LocalXs(full), LocalXs(inc));
        }

        private static T ManagedOf<T>(EntityRepository r, Entity e) where T : class => (T)r.GetManagedComponentByTypeId(e, ComponentTypeRegistry.GetId(typeof(T)));

        private static string LocalXs(EntityRepository r)
        {
            var xs = new List<float>();
            for (int i = 0; i <= r.MaxEntityIndex; i++)
            {
                var e = new Entity(i, r.GetMetadata(i).Generation);
                if (r.IsAlive(e) && !r.HasComponent<NetworkIdentity>(e) && r.HasComponent<DummyPosition>(e)) xs.Add(r.GetComponent<DummyPosition>(e).X);
            }
            xs.Sort();
            return string.Join(",", xs);
        }

        private int MergeAndCompare(FederatedReplayManager manager, BinaryMerger inc, EntityRepository incTarget, int frame)
        {
            inc.Merge(manager, incTarget);
            using var full = NewTarget();
            var oracle = new BinaryMerger(RecordedOwnership());      // a fresh merger: the first pass is the FULL pass
            oracle.Merge(manager, full);
            AssertSameMergedWorld(incTarget, full, frame);
            Assert.Equal(oracle.Paradoxes.Count, inc.Paradoxes.Count);
            return inc.LastComponentsCopied;
        }

        /// <summary>
        /// ⭐ S4 rail (a) — THE ORACLE OVER PLAY: stepping the recording forward frame by frame, then seeking back and playing on, the INCREMENTAL merge's world equals a FULL pass's at every
        /// frame (component set + unmanaged bytes with handles by network id + managed ChildMap / ownership record + locals). And it really is incremental: it copies far less than a full pass.
        /// 🔴 Mutations: <c>Moved</c> returning false ⇒ a moved position is never copied ⇒ red · <c>Moved</c> returning true ⇒ the saving rail below reds.
        /// </summary>
        [Fact]
        public void Incremental_Oracle_OverPlay_EqualsAFullPassAtEveryFrame_ForwardBackAndSeeks()
        {
            var (p1, p2) = RecordPlayGroup();
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());

            manager.SetBaseWallTicks(Wall(0));
            int firstCopied = MergeAndCompare(manager, merger, bin, 0);
            long incrementalCopies = 0, frames = 0;
            for (int f = 1; f < 24; f++)
            {
                manager.StepForwardAll();
                incrementalCopies += MergeAndCompare(manager, merger, bin, f); frames++;
            }
            Assert.True(incrementalCopies < firstCopied * frames / 2, $"the incremental pass copied {incrementalCopies} components over {frames} frames; a full pass copies {firstCopied} each");

            // a seek BACK (versions say nothing then): a full copy for the node, same world
            manager.SetBaseWallTicks(Wall(5));
            MergeAndCompare(manager, merger, bin, 5);
            for (int f = 6; f < 14; f++) { manager.StepForwardAll(); MergeAndCompare(manager, merger, bin, f); }
            // a seek forward across the death (102), the birth (150) and the handover (300), then back one step at a time
            manager.SetBaseWallTicks(Wall(20));
            MergeAndCompare(manager, merger, bin, 20);
            for (int f = 19; f >= 16; f--) { manager.StepBackwardAll(); MergeAndCompare(manager, merger, bin, f); }
            Assert.Equal(10f, bin.GetComponent<DummyPosition>(FindNet(bin, 300)).X);          // before the handover: node 1's
            manager.SetBaseWallTicks(Wall(23));
            MergeAndCompare(manager, merger, bin, 23);
            Assert.Equal(20f, bin.GetComponent<DummyPosition>(FindNet(bin, 300)).X);          // after: node 2's
        }

        /// <summary>
        /// ⭐ S4 rail (b) — 0 B PER STEADY MERGED FRAME (R-2084, the CE-2394 shape): over frames where only UNMANAGED chunks changed, and over frames where NOTHING changed, a merge allocates NOTHING.
        /// The managed component with handles (a remapped <c>ChildMap</c>) and the cross-node handle are in the fixture, so a per-frame remap copy would show here.
        /// 🔴 Mutations: a <c>new List</c> in <c>Merge</c> · <c>Moved</c> returning true ⇒ the ChildMap is rebuilt every frame ⇒ red.
        /// </summary>
        [Fact]
        public void Incremental_Steady_AllocatesZeroBytes_WhetherNothingChangedOrOnlyUnmanagedChunksDid()
        {
            var p1 = RecordPlay(1, 30, (r, f) =>
            {
                if (f == 0)
                {
                    for (int i = 100; i < 160; i++) { var u = OwnedUnit(r, i, 1, 1); r.AddComponent(u, new DummyPosition { X = i }); }
                    r.AddComponent(FindNet(r, 101), new GuidedTarget { TargetId = FindNet(r, 102) });
                    r.AddComponent(FindNet(r, 110), new ChildMap { InstanceToEntity = new Dictionary<int, Entity> { [3] = FindNet(r, 111) } });
                    var local = r.CreateEntity(); r.AddComponent(local, new DummyPosition { X = 7000 });
                    return;
                }
                if (f < 10) for (int i = 100; i < 160; i++) if ((i + f) % 3 == 0) Move(r, i, i + f);   // frames 1..9: only positions move; 10..29: nothing changes
            }, keyEvery: 0);   // ⚠ no mid-recording keyframe: a keyframe RESTORES every chunk (M4) — everything moved, a managed remap legitimately allocates
            var p2 = RecordPlay(2, 30, (r, f) =>
            {
                if (f != 0) return;
                var ghost = OwnedUnit(r, 100, 1, 2); r.AddComponent(ghost, new DummyPosition { X = -1 });
                var u = OwnedUnit(r, 200, 2, 2); r.AddComponent(u, new DummyPosition { X = 200 }); r.AddComponent(u, new GuidedTarget { TargetId = ghost });
            }, keyEvery: 0);
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());

            manager.SetBaseWallTicks(Wall(0));
            merger.Merge(manager, bin);
            int fullCopies = merger.LastComponentsCopied;

            long bytes = 0; int changedFrames = 0; var perFrame = new List<string>();
            for (int f = 1; f < 10; f++)                                                        // ① only unmanaged chunks changed
            {
                manager.StepForwardAll();
                long before = GC.GetAllocatedBytesForCurrentThread();
                merger.Merge(manager, bin);
                long used = GC.GetAllocatedBytesForCurrentThread() - before;
                perFrame.Add($"f{f}:{used}");
                if (f >= 4) { bytes += used; changedFrames++; Assert.True(merger.LastComponentsCopied > 0 && merger.LastComponentsCopied < fullCopies, $"frame {f}: copied {merger.LastComponentsCopied} of {fullCopies}"); }
            }
            Assert.True(bytes == 0L, "bytes per changed frame: " + string.Join(" ", perFrame));

            bytes = 0; int quietFrames = 0; perFrame.Clear();
            for (int f = 10; f < 30; f++)                                                       // ② nothing changed
            {
                manager.StepForwardAll();
                long before = GC.GetAllocatedBytesForCurrentThread();
                merger.Merge(manager, bin);
                long used = GC.GetAllocatedBytesForCurrentThread() - before;
                perFrame.Add($"f{f}:{used}");
                if (f >= 14) { bytes += used; quietFrames++; Assert.Equal(0, merger.LastComponentsCopied); Assert.True(merger.LastComponentsSkipped > 0); }
            }
            Assert.True(bytes == 0L, "bytes per quiet frame: " + string.Join(" ", perFrame));
            Assert.True(changedFrames >= 5 && quietFrames >= 10);
        }

        /// <summary>
        /// ⭐ S4 rail (c) — THE SWEEP HAZARD (§6.12 S1 note): the holder's own chunk did NOT change (its handle is the same bytes in its sandbox), but the entity it names died — the incremental
        /// pass destroys the target entity and must re-remap the holder, as a full pass does in the same call: the handle becomes <c>Null</c> (a paradox, §7.6), not a dead target handle.
        /// 🔴 Mutation: <c>_structural</c> never set ⇒ the holder keeps the destroyed entity's handle ⇒ red.
        /// </summary>
        [Fact]
        public void Sweep_ADestroyedTargetsHolder_IsReRemapped_ByTheIncrementalPass()
        {
            var p1 = RecordPlay(1, 6, (r, f) =>
            {
                if (f == 0)
                {
                    var holder = OwnedUnit(r, 400, 1, 1); r.AddComponent(holder, new DummyPosition { X = 1 });
                    var victim = OwnedUnit(r, 401, 1, 1); r.AddComponent(victim, new DummyPosition { X = 2 });
                    r.AddComponent(holder, new GuidedTarget { TargetId = victim });
                }
                if (f == 3) r.DestroyEntity(FindNet(r, 401));          // the holder is not touched: its GuidedTarget chunk stays as it was
            });
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1 });
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());

            manager.SetBaseWallTicks(Wall(0));
            merger.Merge(manager, bin);
            var holderE = FindNet(bin, 400);
            var victimE = FindNet(bin, 401);
            Assert.Equal(victimE, bin.GetComponent<GuidedTarget>(holderE).TargetId);       // alive: the holder names the target entity of 401
            Assert.Equal(0, merger.Paradoxes.Count);

            for (int f = 1; f < 6; f++)
            {
                manager.StepForwardAll();
                merger.Merge(manager, bin);
                if (f < 3) { Assert.False(FindNet(bin, 401).IsNull); continue; }
                Assert.True(FindNet(bin, 401).IsNull, $"frame {f}: 401 is gone from every node");
                Assert.Equal(Entity.Null, bin.GetComponent<GuidedTarget>(holderE).TargetId);   // ⭐ re-remapped: not the dead target's handle
                Assert.Equal(1, merger.Paradoxes.Count);                                       // … and said (§7.6)
            }
            Assert.Equal(holderE, FindNet(bin, 400));                                          // the holder kept its entity throughout
        }

        /// <summary>
        /// ⭐ S4 — an OWNER CHANGE re-copies even when no source chunk moved: both nodes hold node-constant positions (10, 20); only the ownership RECORD moves at frame 1 (a managed chunk), and the
        /// merged position switches from node 1's to node 2's.
        /// 🔴 Mutation: the <c>last.IsSet(typeId)</c> test dropped from the skip ⇒ the old owner's value stays ⇒ red.
        /// </summary>
        [Fact]
        public void Owner_AHandoverWhoseComponentChunkDidNotMove_StillSwitchesTheSource()
        {
            Action<EntityRepository, int, int, float> step = (r, node, f, x) =>
            {
                if (f == 0) { var u = OwnedUnit(r, 50, 1, node); r.AddComponent(u, new DummyPosition { X = x }); r.AddComponent(u, new DescriptorOwnership().WithOwner(HandoverKey, 1)); }
                if (f == 1) r.SetManagedComponent(FindNet(r, 50), new DescriptorOwnership().WithOwner(HandoverKey, 2));
            };
            var p1 = RecordPlay(1, 4, (r, f) => step(r, 1, f, 10f));
            var p2 = RecordPlay(2, 4, (r, f) => step(r, 2, f, 20f));
            using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());

            manager.SetBaseWallTicks(Wall(0));
            merger.Merge(manager, bin);
            Assert.Equal(10f, bin.GetComponent<DummyPosition>(FindNet(bin, 50)).X);
            manager.StepForwardAll();
            merger.Merge(manager, bin);
            Assert.Equal(20f, bin.GetComponent<DummyPosition>(FindNet(bin, 50)).X);            // ⭐ the record moved, the position chunk did not
            manager.StepForwardAll();
            merger.Merge(manager, bin);
            Assert.Equal(20f, bin.GetComponent<DummyPosition>(FindNet(bin, 50)).X);
        }

        /// <summary>
        /// ⭐ S4 — THE COST, measured (design §6.12 S4 row): the largest exercise reachable from a test — 3 nodes, 3 000 units each (9 000 entities, one position each, a handle holder per 10th), 10 % of the
        /// positions moving per frame. Prints the FULL pass and the INCREMENTAL step (ms per merged frame, merge only — the playback's own step is not counted) and asserts only generous ceilings.
        /// </summary>
        [Fact]
        public void Incremental_Cost_OnA9000EntityExercise_IsMeasured()
        {
            const int perNode = 3000, frames = 24;
            string P(int node) => RecordPlay(node, frames, (r, f) =>
            {
                int baseNet = node * 10_000;
                if (f == 0)
                {
                    for (int i = 0; i < perNode; i++)
                    {
                        var u = OwnedUnit(r, baseNet + i, node, node); r.AddComponent(u, new DummyPosition { X = i });
                        if (i % 10 == 0 && i > 0) r.AddComponent(u, new GuidedTarget { TargetId = FindNet(r, baseNet + i - 1) });
                    }
                    return;
                }
                for (int i = f % 10; i < perNode; i += 10) Move(r, baseNet + i, i + f);
            }, keyEvery: 0);
            using var manager = FederatedReplayManager.LoadGroup(new[] { P(1), P(2), P(3) });
            using var bin = NewTarget();
            var merger = new BinaryMerger(RecordedOwnership());

            manager.SetBaseWallTicks(Wall(0));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            merger.Merge(manager, bin);
            double fullMs = sw.Elapsed.TotalMilliseconds;
            int fullCopies = merger.LastComponentsCopied;
            Assert.Equal(9000 + Junk, bin.EntityCount);

            double total = 0; long copies = 0; int n = 0;
            for (int f = 1; f < frames; f++)
            {
                manager.StepForwardAll();
                sw.Restart();
                merger.Merge(manager, bin);
                double ms = sw.Elapsed.TotalMilliseconds;
                if (f >= 4) { total += ms; copies += merger.LastComponentsCopied; n++; }
            }
            double perFrame = total / n;
            _out.WriteLine($"MEASURED 9 000 entities (3 nodes x 3 000), 10% moving/frame: full pass {fullMs:F1} ms ({fullCopies} components) | incremental {perFrame:F2} ms/frame ({copies / n} components/frame, {merger.LastComponentsSkipped} skipped last frame)");
            Assert.True(perFrame < fullMs, $"incremental {perFrame:F2} ms is not cheaper than the full pass {fullMs:F2} ms");
            Assert.True(perFrame < 100, $"incremental {perFrame:F2} ms/frame");
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
