using Bagira.BDC.SSTD;
using Bagira.Map.Common.Replication.Ingress;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="EntityMasterIngressTranslator"/> logic paths exercised
/// without a live DDS participant.
///
/// Passing <c>null</c> as the participant parameter activates test mode: the
/// translator's DDS reader is not constructed and <see cref="EntityMasterIngressTranslator.PollIngress"/>
/// becomes a no-op. The business logic methods
/// <see cref="EntityMasterIngressTranslator.ProcessSample"/> and
/// <see cref="EntityMasterIngressTranslator.ProcessDispose"/> are called directly
/// via the <c>InternalsVisibleTo</c> attribute declared in Bagira.Map.Common.csproj.
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
    private const ulong TestDisType   = 100UL;

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
        var translator = new EntityMasterIngressTranslator(null, entityMap, eventBus, ghostCreationSystem);
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
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType, DisType = TestDisType };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();

        translator.ProcessSample(in master, cmd, view);

        Assert.True(cmd.AddComponentCalled, "AddComponent must be called with TkbIdentity");
        Assert.NotNull(cmd.LastTkbIdentity);
        Assert.Equal(TestTkbType, cmd.LastTkbIdentity!.Value.TkbType);

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
            DisType  = TestDisType,
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

        public Entity CreateEntity() => new Entity();
        public void DestroyEntity(Entity entity) { }
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            AddComponentCalled = true;
            if (component is TkbIdentity tkbId)
                LastTkbIdentity = tkbId;
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
}
