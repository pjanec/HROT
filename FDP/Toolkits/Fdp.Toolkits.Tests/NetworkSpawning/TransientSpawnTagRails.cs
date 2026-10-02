using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.NetworkSpawning.Tests.Helpers;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Toolkit.NetworkSpawning.Tests
{
    /// <summary>
    /// 🔴🔴🔴 <b><c>D2</c> / <c>R-140</c> — a THROWAWAY entity must never reach a saved scenario.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Node_Roles_And_Policies.md</c> §7.3 ·
    /// <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b. 🔒 The ruling: a passive node such as an
    /// IG creates only temporary entities — <i>"if IG crashes, its entities are gone, but no one cares,
    /// they were temporary anyway"</i>.</para>
    ///
    /// <para>📐 <b>Why this rail is the load-bearing one.</b> §7.3 measured that IG genuinely does not
    /// answer the cluster-wide save — and that this is <b>not sufficient</b>, because the entity
    /// replicates into every peer's world and <c>ProcessSpawn</c> materialises it unconditionally there.
    /// ⇒ the sketch is saved by the PEER. ⭐⭐ The rails in <c>CreateEntityRequestSystemTests</c> prove the
    /// flag reaches the published command; ⛔ only this one proves the flag <b>does something</b>.</para>
    ///
    /// <para>⭐ It runs the REAL <see cref="NetworkSpawningSystem"/> and the REAL
    /// <see cref="ScenarioSerializer"/>, so it pins that <c>ProcessSpawn</c> has no owner filter and that
    /// <see cref="ScenarioIgnoreTag"/> alone excludes the transient sketch here. ⚠ These entities are
    /// LOCALLY owned (<c>OwnerNodeId == LocalNodeId</c>), so they pass the ownership arm of the gate;
    /// <c>CollectSaveableEntities</c> ALSO gates on ownership now (<c>CE-275</c> ② — a non-owner drops a
    /// replicated entity), covered by the ownership rails in <c>ScenarioSerializerTests</c> and
    /// <c>AuthorityExtensionsTests</c>. 📄 <c>docs/DESIGN_Distributed_Scenario_Persistence.md</c> §6.</para>
    /// </summary>
    public class TransientSpawnTagRails
    {
        private const long TkbType     = 4242L;
        private const int  LocalNodeId = 1;

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterComponent<ScenarioIgnoreTag>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        private static void RunSpawn(EntityRepository repo, NetworkSpawningSystem system, SpawnEntityCommand cmd)
        {
            repo.Bus.PublishManaged(cmd);
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0f);
            ((EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer()).Playback(repo);
        }

        /// <summary>
        /// 🔴🔴 <b>Trap ㉗ — these two rails fail intermittently in the FULL suite and pass 2/2 in
        /// isolation, and SIX observed failures produced NO usable evidence because nobody ever
        /// captured the assertion state.</b> 📄 <c>RESUME_Occurrence_Storage.md</c> §5 ㉗.
        ///
        /// <para>📐 Measured <c>2026-09-20</c>: ~6 failures in ~27 full runs, parallelism ON and OFF
        /// alike, never reproducible on demand. ⛔ The leading suspect — that the process-global
        /// <c>ComponentTypeRegistry</c> could hand back a different id depending on which of the 26
        /// <c>Clear()</c> callers ran first — is <b>refuted</b>: <c>GetOrRegisterManaged</c> requires an
        /// explicit <c>[ComponentId]</c>, so <see cref="ScenarioIgnoreTag"/> is <b>always 200</b>.</para>
        ///
        /// <para>⭐⭐ <b>So the next red must diagnose itself.</b> ⚠ Both rails fail TOGETHER, which
        /// already narrows it to one mechanism — <i>the tag never landed</i> — and these fields say
        /// whether the id, the registration or the stamp is the part that moved. ⛔ A generic
        /// <c>Assert.True</c> message would send the next session round the same loop.</para>
        /// </summary>
        private static string Diagnosis(EntityRepository repo, int tagId, Entity keeper, Entity sketch)
            => $"[trap ㉗ diagnosis] tagId={tagId} (expected 200) · " +
               $"registeredOnRepo={repo.IsComponentTypeRegistered<ScenarioIgnoreTag>()} · " +
               $"keeperMask.IsSet={repo.GetComponentMask(keeper.Index).IsSet(tagId)} · " +
               $"sketchMask.IsSet={repo.GetComponentMask(sketch.Index).IsSet(tagId)} · " +
               $"keeperAlive={repo.IsAlive(keeper)} sketchAlive={repo.IsAlive(sketch)} · " +
               $"keeperIndex={keeper.Index} sketchIndex={sketch.Index}";

        private static SpawnEntityCommand Cmd(long networkId, bool isTransient) => new()
        {
            RequestId   = System.Guid.NewGuid(),
            NetworkId   = networkId,
            TkbType     = TkbType,
            OwnerNodeId = LocalNodeId,
            InitType    = ReliableInitType.None,
            IsTransient = isTransient,
        };

        /// <summary>
        /// ⭐⭐⭐ <b>The stamp: a transient spawn carries <see cref="ScenarioIgnoreTag"/>, a normal one
        /// does not — and BOTH materialise.</b>
        ///
        /// <para>⚠ The "both materialise" half is not padding: it pins that the flag is a SAVE filter and
        /// not an accidental spawn filter. ⛔ A change that made transient entities simply not spawn would
        /// also make the serializer assertion below pass.</para>
        /// </summary>
        [Fact]
        public void ProcessSpawn_StampsTheIgnoreTag_OnlyOnTheTransientEntity()
        {
            var repo = CreateWorld();
            var tkb  = new TkbDatabase();
            tkb.Register(new TkbTemplate("Sketchable", TkbType));
            var map    = new NetworkEntityMap();
            var system = new NetworkSpawningSystem(
                tkb, new EntityLifecycleModule(tkb, System.Array.Empty<int>()),
                map, new StubIdAllocator(startId: 900), LocalNodeId);

            RunSpawn(repo, system, Cmd(901, isTransient: false));
            RunSpawn(repo, system, Cmd(902, isTransient: true));

            Assert.True(map.TryGetEntity(901, out var keeper), "the normal entity did not materialise.");
            Assert.True(map.TryGetEntity(902, out var sketch),
                "the TRANSIENT entity did not materialise — the flag must filter the SAVE, never the spawn.");

            int tagId = ComponentTypeRegistry.GetId(typeof(ScenarioIgnoreTag));
            Assert.False(repo.GetComponentMask(keeper.Index).IsSet(tagId),
                "the NORMAL entity was tagged — ProcessSpawn is not reading cmd.IsTransient. " +
                Diagnosis(repo, tagId, keeper, sketch));
            Assert.True(repo.GetComponentMask(sketch.Index).IsSet(tagId),
                "the TRANSIENT entity was NOT tagged — ProcessSpawn dropped the stamp, so an IG sketch " +
                "would be written into the scenario by whichever node saves (R-140, §7.3). " +
                Diagnosis(repo, tagId, keeper, sketch));
        }

        /// <summary>
        /// 🔴🔴🔴 <b>THE END-TO-END CLAIM: the saved scenario contains the normal entity and NOT the
        /// sketch.</b>
        ///
        /// <para>⭐ The rail above proves the tag lands; this proves the serializer HONOURS it, closing the
        /// chain the design asserts. ⚠ <b>Non-vacuity is built in</b> — the keeper must be PRESENT, so a
        /// serializer that emitted nothing at all would redden rather than pass.</para>
        /// </summary>
        [Fact]
        public void ATransientEntity_IsAbsentFromTheSavedScenario_AndANormalOneIsPresent()
        {
            var repo = CreateWorld();
            var tkb  = new TkbDatabase();
            tkb.Register(new TkbTemplate("Sketchable", TkbType));
            var map    = new NetworkEntityMap();
            var system = new NetworkSpawningSystem(
                tkb, new EntityLifecycleModule(tkb, System.Array.Empty<int>()),
                map, new StubIdAllocator(startId: 900), LocalNodeId);

            RunSpawn(repo, system, Cmd(901, isTransient: false));
            RunSpawn(repo, system, Cmd(902, isTransient: true));

            var dom = new ScenarioSerializerBuilder("Hrot.Scenario").Build()
                .Serialize(repo, new ScenarioHeader("Hrot.Scenario"));

            var entities = dom["Entities"]!.AsObject();
            Assert.True(entities.Count == 1,
                $"expected exactly the keeper in the saved scenario, got {entities.Count} " +
                $"([{string.Join(", ", entities.Select(e => e.Key))}]). " +
                // ⚠ trap ㉗ — this rail reds TOGETHER with the stamp rail above, so the same
                //   diagnosis applies: a count of 2 means the tag never landed, 0 means the
                //   serializer emitted nothing and the assertion would have passed vacuously.
                Diagnosis(repo, ComponentTypeRegistry.GetId(typeof(ScenarioIgnoreTag)),
                          map.TryGetEntity(901, out var k) ? k : default,
                          map.TryGetEntity(902, out var s) ? s : default));
        }
    }
}
