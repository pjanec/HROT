using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.SimHost.Integration.Tests.Infrastructure;
using Xunit;

namespace Bagira.SimHost.Integration.Tests
{
    /// <summary>
    /// TASK-S6.1 — Entity Creation Flow Integration Test.
    ///
    /// Validates the full path from IOS → CreateEntityRequest → SimHost ECS → CreateEntityAck:
    ///   1. A mock IOS client publishes a <see cref="CreateEntityRequest"/> to the stub inbox.
    ///   2. The SimHost pipeline (CreateEntityRequestSystem → NetworkSpawningSystem → ELM)
    ///      processes the request in simulated ticks.
    ///   3. The mock client receives a matching <see cref="CreateEntityAck"/> with ErrorCode=0
    ///      and a valid new entity ID.
    ///   4. The ECS world contains a <see cref="TkbIdentity"/> with the correct
    ///      TkbType on the spawned entity.
    ///
    /// This test is DDS-free — all networking is replaced by in-process stubs defined in
    /// <see cref="SimHostInstance"/>.
    /// </summary>
    public sealed class EntityCreationFlowTests : IDisposable
    {
        private readonly SimHostInstance _host;
        private readonly MockIOSClient   _client;

        public EntityCreationFlowTests()
        {
            _host   = new SimHostInstance();
            _client = new MockIOSClient(_host);
        }

        public void Dispose() => _host.Dispose();

        // ── Tests ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// End-to-end flow: IOS client requests a Tank_M1Abrams, SimHost creates it,
        /// client receives ACK and can read back the TkbIdentity metadata.
        /// </summary>
        [Fact]
        public async Task FullFlow_IOSCreateTank_ReceivesAckAndSpawnRequestIsSet()
        {
            // ── Arrange ──────────────────────────────────────────────────────────────────
            var requestId = Guid.NewGuid();
            var request   = BuildTankRequest(requestId);

            // ── Act ───────────────────────────────────────────────────────────────────────
            // Send the request (enqueues into the stub source, identical to DDS publish).
            _client.SendCreateRequest(request);

            // Poll until ACK arrives (runs simulation ticks internally).
            var ack = await _client.WaitForAckAsync(requestId, timeoutMs: 3000);

            // ── Assert: ACK ───────────────────────────────────────────────────────────────
            Assert.NotNull(ack);
            Assert.Equal(requestId, ack!.Value.RequestId);
            Assert.Equal(0, ack.Value.ErrorCode);
            Assert.True(ack.Value.NewEntityId > 0,
                $"Expected a positive network entity ID, got {ack.Value.NewEntityId}.");

            // ── Assert: TkbIdentity component ──────────────────────────────────────────────────────────────────────
            // Allow a few more ticks for ELM lifecycle processing to complete.
            _host.RunForTicks(5);

            var tkbId = _client.ReadTkbIdentity(ack.Value.NewEntityId);
            Assert.NotNull(tkbId);
            Assert.Equal(TkbEntityTypes.Tank_M1Abrams, tkbId!.Value.TkbType);
        }

        /// <summary>
        /// Second entity gets a different (incremented) network ID.
        /// </summary>
        [Fact]
        public async Task TwoRequests_ProduceTwoDistinctEntityIds()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            _client.SendCreateRequest(BuildTankRequest(id1));
            var ack1 = await _client.WaitForAckAsync(id1, timeoutMs: 3000);

            _client.SendCreateRequest(BuildTankRequest(id2));
            var ack2 = await _client.WaitForAckAsync(id2, timeoutMs: 3000);

            Assert.NotNull(ack1);
            Assert.NotNull(ack2);
            Assert.Equal(0, ack1!.Value.ErrorCode);
            Assert.Equal(0, ack2!.Value.ErrorCode);
            Assert.NotEqual(ack1.Value.NewEntityId, ack2.Value.NewEntityId);
        }

        /// <summary>
        /// A request with TkbType=0 (no EntityMaster descriptor) is rejected with
        /// a non-zero ErrorCode.
        /// </summary>
        [Fact]
        public async Task MissingEntityMaster_ReturnsErrorAck()
        {
            var requestId = Guid.NewGuid();
            var emptyRequest = new CreateEntityRequest
            {
                RequestId          = requestId,
                Owner              = new Bagira.DDS.DM.NodeId { AppDomainId = 1, AppInstanceId = 99 },
                Flags              = 0,
                InitialDescriptors = new List<EntityDescriptorUnion>(),  // No EntityMaster
            };

            _client.SendCreateRequest(emptyRequest);
            var ack = await _client.WaitForAckAsync(requestId, timeoutMs: 3000);

            Assert.NotNull(ack);
            Assert.NotEqual(0, ack!.Value.ErrorCode);  // Must be an error code
        }

        // ── JSON attribute patching tests ────────────────────────────────────────────────

        /// <summary>
        /// Verifies the primary bug-fix: the IOS serialises <c>eForceIdentifier</c> as its raw
        /// integer value (e.g. <c>{"Affiliation":2}</c> for FORCE_OPPOSING).
        /// CreateEntityRequestSystem must route the integer through the JsonAttributeCompiler
        /// and produce an entity whose IgEntityData has ForceId.Hostile.
        /// </summary>
        [Fact]
        public async Task CreateEntity_WithAffiliationAsInteger_SetsForceIdHostile()
        {
            // Arrange — integer 2 = eForceIdentifier.FORCE_OPPOSING
            var reqId   = Guid.NewGuid();
            var request = BuildTankRequestWithJson(reqId, "{\"Affiliation\":2}");

            // Act
            _client.SendCreateRequest(request);
            var ack = await _client.WaitForAckAsync(reqId, timeoutMs: 3000);

            Assert.NotNull(ack);
            Assert.Equal(0, ack!.Value.ErrorCode);
            _host.RunForTicks(5);

            // Assert
            var igData = _client.ReadIgEntityData(ack.Value.NewEntityId);
            Assert.NotNull(igData);
            Assert.Equal(ForceId.Hostile, igData!.ForceId);
        }

        /// <summary>
        /// Verifies backward compatibility: legacy string-serialised affiliation
        /// (<c>{"Affiliation":"FORCE_FRIENDLY"}</c>) still resolves to ForceId.Friend.
        /// </summary>
        [Fact]
        public async Task CreateEntity_WithAffiliationAsString_SetsForceIdFriendly()
        {
            var reqId   = Guid.NewGuid();
            var request = BuildTankRequestWithJson(reqId, "{\"Affiliation\":\"FORCE_FRIENDLY\"}");

            _client.SendCreateRequest(request);
            var ack = await _client.WaitForAckAsync(reqId, timeoutMs: 3000);

            Assert.NotNull(ack);
            Assert.Equal(0, ack!.Value.ErrorCode);
            _host.RunForTicks(5);

            var igData = _client.ReadIgEntityData(ack.Value.NewEntityId);
            Assert.NotNull(igData);
            Assert.Equal(ForceId.Friend, igData!.ForceId);
        }

        /// <summary>
        /// Verifies that both the <c>Name</c> and <c>Affiliation</c> fields are patched
        /// in a single pass when both are present in <c>InitialAttributesJson</c>.
        /// </summary>
        [Fact]
        public async Task CreateEntity_WithNameAndAffiliationJson_PatchesBothFields()
        {
            var reqId   = Guid.NewGuid();
            var request = BuildTankRequestWithJson(
                reqId,
                "{\"Name\":\"Bravo-3\",\"Affiliation\":2}");

            _client.SendCreateRequest(request);
            var ack = await _client.WaitForAckAsync(reqId, timeoutMs: 3000);

            Assert.NotNull(ack);
            Assert.Equal(0, ack!.Value.ErrorCode);
            _host.RunForTicks(5);

            var igData = _client.ReadIgEntityData(ack.Value.NewEntityId);
            Assert.NotNull(igData);
            Assert.Equal("Bravo-3",       igData!.Name);
            Assert.Equal(ForceId.Hostile, igData.ForceId);
        }

        /// <summary>
        /// Verifies that a null <c>InitialAttributesJson</c> does not produce an error and
        /// leaves IgEntityData in its default-constructed state.
        /// </summary>
        [Fact]
        public async Task CreateEntity_WithNullJson_DoesNotThrowAndIgDataIsDefault()
        {
            var reqId   = Guid.NewGuid();
            var request = BuildTankRequestWithJson(reqId, initialAttributesJson: null);

            _client.SendCreateRequest(request);
            var ack = await _client.WaitForAckAsync(reqId, timeoutMs: 3000);

            Assert.NotNull(ack);
            Assert.Equal(0, ack!.Value.ErrorCode);
            _host.RunForTicks(5);

            // No IgEntityData patch was applied; the component is either absent or defaulted.
            var igData = _client.ReadIgEntityData(ack.Value.NewEntityId);
            if (igData != null)
                Assert.Equal(ForceId.Unknown, igData.ForceId);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────

        private static CreateEntityRequest BuildTankRequest(Guid requestId)
        {
            return BuildTankRequestWithJson(requestId, initialAttributesJson: null);
        }

        private static CreateEntityRequest BuildTankRequestWithJson(
            Guid    requestId,
            string? initialAttributesJson)
        {
            return new CreateEntityRequest
            {
                RequestId = requestId,
                Owner     = new Bagira.DDS.DM.NodeId { AppDomainId = 1, AppInstanceId = 1 },
                Flags     = 0,
                InitialDescriptors = new List<EntityDescriptorUnion>
                {
                    new EntityDescriptorUnion
                    {
                        _d           = EDescriptorType.dtEntityMaster,
                        EntityMaster = new EntityMaster
                        {
                            TkbType = TkbEntityTypes.Tank_M1Abrams,
                            DisType = 0x0100_0000_0000_0001UL
                        },
                    },
                },
                InitialAttributesJson = initialAttributesJson,
            };
        }
    }
}
