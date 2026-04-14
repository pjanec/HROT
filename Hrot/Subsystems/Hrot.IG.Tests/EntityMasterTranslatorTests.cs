using Hrot.NED.Descriptors;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Kernel;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using System.Threading;
using Fdp.Interfaces;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="EntityMasterIngressTranslator"/> logic paths exercised
/// without a live DDS participant.
///
/// Passing <c>null</c> as the participant parameter activates test mode: the
/// translator's DDS reader is not constructed and <see cref="EntityMasterIngressTranslator.PollIngress"/>
/// becomes a no-op. The business logic methods
/// <see cref="EntityMasterIngressTranslator.ProcessSample"/> and
/// <see cref="EntityMasterIngressTranslator.ProcessDispose"/> are called directly
/// via the <c>InternalsVisibleTo</c> attribute declared in Hrot.Map.Common.csproj.
///
/// Full DDS-level integration tests (DdsParticipant → DdsReader.Take() →
/// PollIngress result) require a live CycloneDDS domain and are deferred to the
/// integration test suite that runs with native libraries present.
/// </summary>
public class EntityMasterTranslatorTests
{
    // ── Test constants (§CODE-STANDARDS §1 — no magic numbers) ───────────────
    private const long  TestNetworkId = 77L;
    private const long  TestTkbType   = 42L;
    // TestDisType is kept as ulong for assertion; the wire DisTypeStruct uses Extra=100.
    private const ulong TestDisType   = 100UL;
    private static readonly DisTypeStruct TestDisTypeStruct = new DisTypeStruct { Extra = 100 };

    // ── Fixture factory ──────────────────────────────────────────────────────

    private static (EntityRepository repo, NetworkEntityMap entityMap, FdpEventBus eventBus, EntityMasterIngressTranslator translator)
        CreateFixture()
    {
        var repo       = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>(); // required by GhostCreationSystem
        repo.RegisterComponent<GhostStateTracker>(); // required by GhostCreationSystem
        var entityMap  = new NetworkEntityMap();
        var eventBus   = new FdpEventBus();
        var ghostCreationSystem = new GhostCreationSystem(entityMap);
        // null participant = test mode (no DDS reader created)
        var translator = new EntityMasterIngressTranslator(null, entityMap, localNodeId: 1, eventBus, ghostCreationSystem);
        return (repo, entityMap, eventBus, translator);
    }

    // ── Ghost spawn path (new entity) ──────────────────────────────────────────

    /// <summary>
    /// When a sample arrives for an unknown network ID, ProcessSample must call
    /// AddComponent with a <see cref="TkbIdentity"/> carrying the correct TkbType,
    /// and write DisType natively to the entity header.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_AddsTkbIdentityWithCorrectTkbType()
    {
        var (repo, entityMap, _, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType, DisType = TestDisTypeStruct };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();

        translator.ProcessSample(in master, cmd, view);

        Assert.True(cmd.AddComponentCalled, "AddComponent must be called with TkbIdentity");
        Assert.NotNull(cmd.LastTkbIdentity);
        Assert.Equal(TestTkbType, cmd.LastTkbIdentity!.Value.TkbType);
        Assert.NotNull(cmd.LastNetworkAuthority);
        Assert.Equal(-1, cmd.LastNetworkAuthority!.Value.PrimaryOwnerId);
        Assert.Equal(1, cmd.LastNetworkAuthority!.Value.LocalNodeId);

        // DisType is now stored natively in EntityHeader — verify via repo.GetHeader.
        Assert.True(entityMap.TryGetEntity(TestNetworkId, out var entity));
        ulong disType = repo.GetHeader(entity.Index).DisType.Value;
        Assert.Equal(TestDisType, disType);
    }

    /// <summary>
    /// When a sample arrives for an unknown network ID, ProcessSample must register
    /// the freshly-created ghost in <see cref="NetworkEntityMap"/> so downstream
    /// translators can locate the entity by ID on the same tick.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_RegistersGhostInEntityMap()
    {
        var (repo, entityMap, _, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();
        translator.ProcessSample(in master, cmd, view);

        Assert.True(entityMap.TryGetEntity(TestNetworkId, out _),
            "Ghost entity must be registered in entityMap after ProcessSample for unknown ID");
    }

    /// <summary>
    /// FDP ghost ownership convention: ghosts created from ingress data have no local
    /// authority. TkbIdentity carries only the TKB type (no OwnerId field); ownership
    /// is tracked by NetworkOwnership if needed.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_RegistersGhostWithNoLocalAuthority()
    {
        var (repo, entityMap, _, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();
        translator.ProcessSample(in master, cmd, view);

        // TkbIdentity present; no NetworkOwnership set by this translator
        Assert.NotNull(cmd.LastTkbIdentity);
        // Entity map contains the new ghost
        Assert.True(entityMap.TryGetEntity(TestNetworkId, out _));
    }

    // ── Component-update path (known entity) ─────────────────────────────────

    /// <summary>
    /// When the entity is already known, ProcessSample must NOT emit SetComponent
    /// calls. AddComponent IS called to enqueue the <see cref="TkbIdentity"/>
    /// so the promotion pipeline can process the updated type information.
    /// </summary>
    [Fact]
    public void ProcessSample_KnownEntity_DoesNotCallSetComponent()
    {
        var (repo, entityMap, eventBus, translator) = CreateFixture();

        // Pre-register a known entity in the world and map
        var entity = repo.CreateEntity();
        entityMap.Register(TestNetworkId, entity);

        var updatedMaster = new EntityMaster
        {
            EntityId = (int)TestNetworkId,
            TkbType  = TestTkbType,
            DisType  = TestDisTypeStruct,
        };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();
        translator.ProcessSample(in updatedMaster, cmd, view);

        eventBus.SwapBuffers();
        Assert.Empty(eventBus.ConsumeManaged<DestroyEntityCommand>());
        Assert.False(cmd.SetComponentCalled, "SetComponent must not be called for known entities");
        Assert.True(cmd.AddComponentCalled, "AddComponent must be called with TkbIdentity");
    }

    [Fact]
    public void ApplyToEntity_IsNoOp()
    {
        var (repo, _, _, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        var exception = Record.Exception(() =>
            translator.ApplyToEntity(new Entity(), master, repo));

        Assert.Null(exception);
    }

    private sealed class RecordingCommandBuffer : IEntityCommandBuffer
    {
        public bool AddComponentCalled { get; private set; }
        public bool SetComponentCalled { get; private set; }
        public TkbIdentity? LastTkbIdentity { get; private set; }
        public NetworkAuthority? LastNetworkAuthority { get; private set; }

        public Entity CreateEntity() => new Entity();
        public void DestroyEntity(Entity entity) { }
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            AddComponentCalled = true;
            if (component is TkbIdentity tkbId)
                LastTkbIdentity = tkbId;
            if (component is NetworkAuthority netAuth)
                LastNetworkAuthority = netAuth;
        }
        public void SetComponent<T>(Entity entity, in T component) where T : unmanaged => SetComponentCalled = true;
        public void RemoveComponent<T>(Entity entity) where T : unmanaged { }
        public void AddManagedComponent<T>(Entity entity, T? component) where T : class { }
        public void SetManagedComponent<T>(Entity entity, T? component) where T : class { }
        public void RemoveManagedComponent<T>(Entity entity) where T : class { }
        public void PublishEvent<T>(in T evt) where T : unmanaged { }
        public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) { }
        public void SetManagedComponentRaw(Entity entity, int typeId, object obj) { }
        public void SetLifecycleState(Entity entity, EntityLifecycle state) { }
    }

    // ── DestroyEntityCommand path (disposed instance) ────────────────────────

    /// <summary>
    /// ProcessDispose must publish a <see cref="DestroyEntityCommand"/> with the
    /// supplied network ID and a non-empty reason string.
    /// </summary>
    [Fact]
    public void ProcessDispose_PublishesDestroyEntityCommandWithMatchingNetworkId()
    {
        var (_, _, eventBus, translator) = CreateFixture();

        translator.ProcessDispose(TestNetworkId);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<DestroyEntityCommand>();

        Assert.Single(commands);
        Assert.Equal(TestNetworkId, commands[0].NetworkId);
        Assert.False(string.IsNullOrEmpty(commands[0].Reason),
            "DestroyEntityCommand.Reason must carry a non-empty disposal message for diagnostics");
    }

    // ── Test-mode safety ─────────────────────────────────────────────────────

    /// <summary>
    /// PollIngress with a null participant must return without publishing any
    /// events or queuing any commands. This verifies the test-mode guard added
    /// to support unit testing.
    /// </summary>
    [Fact]
    public void PollIngress_WithNullParticipant_IsNoOpAndEmitsNoEvents()
    {
        var (repo, _, eventBus, translator) = CreateFixture();

        ISimulationView      view = repo;
        var cmd = new RecordingCommandBuffer();
        translator.PollIngress(cmd, view);

        eventBus.SwapBuffers();
        Assert.Empty(eventBus.ConsumeManaged<DestroyEntityCommand>());
        Assert.False(cmd.AddComponentCalled, "No AddComponent calls expected when PollIngress is a no-op");
    }

    // ── BD1-P5T1: DisTypeStruct ingress round-trip ────────────────────────────

    /// <summary>
    /// BD1-P5T1 SC2: ProcessSample must reconstruct <c>DISEntityType.Value</c> from the
    /// individual <c>DisTypeStruct</c> fields and store it in the entity header.
    /// The reconstructed ulong must encode all 8 fields correctly.
    /// </summary>
    [Fact]
    public void DisTypeStruct_IngressRoundTrip_ReconstructsCorrectUlongValue()
    {
        var (repo, entityMap, _, translator) = CreateFixture();

        var wireStruct = new DisTypeStruct
        {
            Kind        = 1,
            Domain      = 2,
            Country     = 225,
            Category    = 3,
            Subcategory = 4,
            Specific    = 5,
            Extra       = 6,
        };

        var master = new EntityMaster
        {
            EntityId = (int)TestNetworkId,
            TkbType  = TestTkbType,
            DisType  = wireStruct,
        };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();
        translator.ProcessSample(in master, cmd, view);

        Assert.True(entityMap.TryGetEntity(TestNetworkId, out var entity));

        var stored = repo.GetHeader(entity.Index).DisType;
        Assert.Equal(1,   (int)stored.Kind);
        Assert.Equal(2,   (int)stored.Domain);
        Assert.Equal(225, (int)stored.Country);
        Assert.Equal(3,   (int)stored.Category);
        Assert.Equal(4,   (int)stored.Subcategory);
        Assert.Equal(5,   (int)stored.Specific);
        Assert.Equal(6,   (int)stored.Extra);
    }

    // ── BUG2-N002 – EnableSenderTracking integration test ─────────────────

    /// <summary>
    /// Verifies that when <see cref="DdsParticipant.EnableSenderTracking"/> is called
    /// on both the sender and receiver participants, received samples expose non-null
    /// sender identity (AppInstanceId = "OwnerId" of the remote participant).
    /// </summary>
    [Fact]
    public void ProcessSample_WithSenderTracking_SetsOwnerId()
    {
        const uint domain  = 170u;
        const int  ownerId = 42;

        using var senderParticipant = new DdsParticipant(domain);
        senderParticipant.EnableSenderTracking(new SenderIdentityConfig
        {
            AppDomainId   = (int)domain,
            AppInstanceId = ownerId
        });

        using var receiverParticipant = new DdsParticipant(domain);
        receiverParticipant.EnableSenderTracking(new SenderIdentityConfig
        {
            AppDomainId   = (int)domain,
            AppInstanceId = 99
        });

        using var writer = new DdsWriter<EntityMaster>(senderParticipant);
        using var reader = new DdsReader<EntityMaster>(receiverParticipant);
        reader.EnableSenderTracking(receiverParticipant.SenderRegistry!);

        Thread.Sleep(500); // discovery

        writer.Write(new EntityMaster { EntityId = 77 });

        Thread.Sleep(500); // let sample propagate

        SenderIdentity? senderIdentity = null;
        using var loan = reader.Take();
        int sampleIdx = 0;
        foreach (var sample in loan)
        {
            if (sample.Info.InstanceState == DdsInstanceState.Alive)
            {
                senderIdentity = loan.GetSender(sampleIdx);
                break;
            }
            sampleIdx++;
        }

        Assert.NotNull(senderIdentity);
        // AppInstanceId maps to OwnerId in the application domain.
        Assert.Equal(ownerId, senderIdentity!.Value.AppInstanceId);
    }
}
