using System;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Interfaces;
using Hrot.Core.Network;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.SimHost.Integration.Tests.Infrastructure;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-160</c> — <b>the BEHAVIOURAL half of the <c>EntityCreationPack</c> coverage.</b>
    ///
    /// <para>📐 <b>The hole these close, measured <c>2026-09-02</c>.</b> The pack had <b>23</b> rails and
    /// every one was <i>construction-time</i> — <c>Build</c> returns non-null pieces, the list is
    /// non-empty, one instance reaches the ELM, placements land next to their anchor. ⛔ <b>Not one
    /// submitted a request and asserted an entity came out right.</b> Separately, <b>39</b> behavioural
    /// tests existed for the pack's constituent systems and <b>every one hand-assembled them</b>, so they
    /// proved the SYSTEMS work and never that the PACK composes them correctly.</para>
    ///
    /// <para>⭐⭐ <b>That seam was not hypothetical — it was already leaking.</b>
    /// <see cref="SimHostInstance"/> hand-rolled <b>FIVE</b> translators where
    /// <see cref="TkbTranslatorSet.Base"/> carries <b>SIX</b>; <c>PresentationTkbTranslator</c> was
    /// missing, so every test in this assembly projected a different component set than production and
    /// nothing could see it. The harness now composes through the pack, which makes the list correct by
    /// construction, and <see cref="TheHarnessProjectsExactlyProductionsTranslatorSet"/> is the rail that
    /// would have caught the drift.</para>
    ///
    /// <para>⚠ <b>Non-vacuity is asserted explicitly</b>, per <c>DESIGN_Subsystem_Composition_Unification.md</c>
    /// §7's rail-blindness family (<c>CE-049</c> / <c>CE-053</c> / <c>CE-064</c> — rails that passed
    /// because the thing they checked could not be reached). Every component assertion below is preceded
    /// by an <c>IsComponentTypeRegistered</c> guard that FAILS rather than skips, so a world that stops
    /// registering the component reddens the rail instead of silently satisfying it.</para>
    /// </summary>
    public sealed class EntityCreationPackFlowRails : IDisposable
    {
        private readonly SimHostInstance  _host;
        private readonly MockExConClient  _client;

        public EntityCreationPackFlowRails()
        {
            _host   = new SimHostInstance();
            _client = new MockExConClient(_host);
        }

        public void Dispose() => _host.Dispose();

        /// <summary>
        /// ⭐⭐⭐ <b>THE DRIFT RAIL.</b> The harness must project <i>exactly</i> production's translator
        /// set — same types, same order. ⛔ Order matters and is asserted: <c>CE-145</c> exists because
        /// <c>InfantryVehicleStateStripTkbTranslator</c> must run immediately after
        /// <c>VehicleKinematicsTkbTranslator</c>, and a set-equality assertion would have been blind to
        /// that. 📌 This rail fails on the pre-<c>CE-160</c> harness (5 types vs 6).
        /// </summary>
        [Fact]
        public void TheHarnessProjectsExactlyProductionsTranslatorSet()
        {
            var expected = TkbTranslatorSet.Base().Select(t => t.GetType()).ToList();
            var actual   = _host.Translators.Select(t => t.GetType()).ToList();

            Assert.NotEmpty(expected);                 // ⛔ guard: an empty Base() would pass vacuously
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE BEHAVIOURAL RAIL — an entity born through a PACK-BUILT pipeline carries the
        /// projection of the base translator set.</b>
        ///
        /// <para>📌 This is the assertion that did not exist. The pack's whole job is to wire the request
        /// system, the spawn system and the ELM over ONE translator list; the only way to prove it did
        /// that is to birth an entity through it and read the components back.</para>
        ///
        /// <para>⚠ <b>Scope, stated honestly — THREE of <c>Base()</c>'s six are observable here.</b>
        /// 📐 Measured: <c>SimHostInstance.RegisterBehaviorTemplate</c> adds exactly three descriptors
        /// (<c>TkbMasterDto</c>, <c>VehicleParametersDto</c>, <c>BehaviorProfileDto</c>), so
        /// <c>Perception</c>, <c>Combat</c> and <c>Presentation</c> are <b>in the list and inert in this
        /// world</b> — they have nothing to consume. ⛔ Do not read this rail as proving all six project:
        /// it proves the pack's ONE list reaches the spawn path and that every translator with a
        /// descriptor lands its components. ⭐ The other three are covered by the cluster suite, where the
        /// production catalogue is real.</para>
        /// </summary>
        [Fact]
        public async Task AnEntityBornThroughThePack_CarriesTheBaseTranslatorProjection()
        {
            var requestId = Guid.NewGuid();
            _client.SendCreateRequest(new EntityCreationRequest
            {
                RequestId          = requestId,
                OwnerAppInstanceId = SimHostInstance.LocalNodeId,
                TkbType            = TkbEntityTypes.Tank_M1Abrams,
                DisType            = 0x0100_0000_0000_0001UL,
            });

            var ack = await _client.WaitForAckAsync(requestId, timeoutMs: 3000);
            Assert.NotNull(ack);
            Assert.Equal(0, ack!.Value.StatusCode);

            _host.RunForTicks(5);

            Assert.True(_host.EntityMap.TryGetEntity(ack.Value.EntityId, out Entity entity),
                $"The pack ACKed entity {ack.Value.EntityId} but never put it in the entity map.");
            Assert.True(_host.World.IsAlive(entity), "The spawned entity is not alive.");

            // ⭐ One row per translator whose target component this world registers. The guard is an
            //   ASSERT, not a skip — see the class summary on rail-blindness.
            AssertProjected<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity, "the spawn path itself");
            AssertProjected<SimTransform>(entity, "SpatialCoreTkbTranslator");
            AssertProjected<CarKinem.Core.VehicleParams>(entity, "VehicleKinematicsTkbTranslator");
            AssertProjected<CarKinem.Core.VehicleState>(entity, "VehicleKinematicsTkbTranslator");
            AssertProjected<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity, "BehaviorTkbTranslator");

            // ⛔ NO ROW for PerceptionTkbTranslator, CombatTkbTranslator or PresentationTkbTranslator.
            //   📐 MEASURED 2026-09-02: RegisterBehaviorTemplate adds exactly THREE descriptors —
            //   TkbMasterDto, VehicleParametersDto, BehaviorProfileDto. The other three translators
            //   have nothing to consume here, so a row for any of them would assert something this
            //   harness cannot produce.
            //   ⭐ Both wrong rows were caught by this rail REDDENING as it was written, not by
            //   reasoning — which is the guard working. ⚠ Their coverage is the CLUSTER suite.
        }

        /// <summary>
        /// ⭐⭐ <b>The pack's ACK path is the one the host actually schedules.</b> The harness now throws at
        /// construction if any pack piece is unscheduled (<c>Unserviceable</c>), so this rail's job is the
        /// complement: prove the scheduled trio really does drive a request to a terminal ACK rather than
        /// merely existing. ⛔ Without it, <c>Unserviceable</c> could be satisfied by three objects that
        /// are never ticked.
        /// </summary>
        [Fact]
        public async Task ThePackTrio_DrivesARequestToATerminalAck()
        {
            var requestId = Guid.NewGuid();
            _client.SendCreateRequest(new EntityCreationRequest
            {
                RequestId          = requestId,
                OwnerAppInstanceId = SimHostInstance.LocalNodeId,
                TkbType            = TkbEntityTypes.Tank_M1Abrams,
                DisType            = 0x0100_0000_0000_0001UL,
            });

            var ack = await _client.WaitForAckAsync(requestId, timeoutMs: 3000);

            Assert.NotNull(ack);
            Assert.Equal(requestId, ack!.Value.RequestId);
            Assert.Equal(0, ack.Value.StatusCode);
            Assert.True(ack.Value.EntityId > 0,
                $"Expected a positive network id from the pack's allocator, got {ack.Value.EntityId}.");

            // ⭐⭐⭐ ACCEPTANCE ⑩ — "path 2 works end to end WITHOUT the arbiter."
            //   📄 DESIGN_Entity_Creation_Unification.md §6 ⑩ asks for exactly this shape: enqueue with
            //   OwnerAppInstanceId == localNodeId on a node with isDefaultProcessor FALSE, then assert the
            //   entity is materialised LOCALLY with its AuthorityMask stamped. ⛔ Its own wording warns
            //   that "a rail that runs on the arbiter proves nothing — it is the old path", and this
            //   harness is built with IsBroadcastArbiter: false precisely so the assertion means something.
            _host.RunForTicks(5);
            Assert.True(_host.EntityMap.TryGetEntity(ack.Value.EntityId, out Entity spawned),
                "Path 2: the entity was ACKed but never materialised locally.");
            Assert.True(_host.World.HasAuthority<SimTransform>(spawned),
                "Path 2: the creating node did not stamp authority over the entity it created, so " +
                "NetworkSpawningSystem's AuthorityMask never reached it.");
        }

        private void AssertProjected<T>(Entity entity, string owner) where T : unmanaged
        {
            Assert.True(_host.World.IsComponentTypeRegistered<T>(),
                $"{typeof(T).Name} is not registered in the harness world, so an assertion about " +
                $"{owner} projecting it could only ever pass vacuously. Register it, or drop the row.");
            Assert.True(_host.World.HasComponent<T>(entity),
                $"{owner} did not project {typeof(T).Name} onto an entity born through EntityCreationPack.");
        }
    }
}
