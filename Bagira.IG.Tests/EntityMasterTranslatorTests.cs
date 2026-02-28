using Bagira.BDC.SSTD;
using Bagira.IG.Translators;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="EntityMasterTranslator"/> logic paths exercised
/// without a live DDS participant.
///
/// Passing <c>null</c> as the participant parameter activates test mode: the
/// translator's DDS reader is not constructed and <see cref="EntityMasterTranslator.PollIngress"/>
/// becomes a no-op. The business logic methods
/// <see cref="EntityMasterTranslator.ProcessSample"/> and
/// <see cref="EntityMasterTranslator.ProcessDispose"/> are called directly
/// via the <c>InternalsVisibleTo</c> attribute declared in Bagira.IG.csproj.
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

    private static (EntityRepository repo, NetworkEntityMap entityMap, FdpEventBus eventBus, EntityMasterTranslator translator)
        CreateFixture()
    {
        var repo       = new EntityRepository();
        var entityMap  = new NetworkEntityMap();
        var eventBus   = new FdpEventBus();
        // null participant = test mode (no DDS reader created)
        var translator = new EntityMasterTranslator(null, entityMap, eventBus);
        return (repo, entityMap, eventBus, translator);
    }

    // ── SpawnEntityCommand path (new entity) ─────────────────────────────────

    /// <summary>
    /// When a sample arrives for a network ID not yet in the entity map,
    /// ProcessSample must publish exactly one <see cref="SpawnEntityCommand"/>
    /// carrying the correct fields.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_PublishesSpawnEntityCommandWithCorrectFields()
    {
        var (repo, _, eventBus, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType, DisType = TestDisType };

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();

        translator.ProcessSample(in master, cmd, view);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<SpawnEntityCommand>();

        Assert.Single(commands);
        Assert.Equal(TestNetworkId,         commands[0].NetworkId);
        Assert.Equal(TestTkbType,           commands[0].TkbType);
        Assert.Equal(TestDisType,           commands[0].DisType);
        Assert.Equal(0,                     commands[0].OwnerNodeId); // ghost node — no local authority
        Assert.Equal(ReliableInitType.None, commands[0].InitType);
    }

    /// <summary>
    /// The <see cref="SpawnEntityCommand.InitialComponents"/> list must remain
    /// empty to avoid routing network descriptors into ECS components.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_SpawnCommandHasEmptyInitialComponents()
    {
        var (repo, _, eventBus, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();

        translator.ProcessSample(in master, cmd, view);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<SpawnEntityCommand>();

        Assert.Empty(commands[0].InitialComponents);
        Assert.Equal(TestTkbType, commands[0].TkbType);
    }

    // ── Component-update path (known entity) ─────────────────────────────────

    /// <summary>
    /// SC1 (TASK-IF004): A replicated entity must have <c>OwnerNodeId = 0</c>
    /// (FDP convention for \u201cremote / no local authority\u201d).  This prevents
    /// <c>NetworkSpawningSystem</c> from tagging the entity with
    /// <c>NetworkAuthority.HasAuthority = true</c>, which would cause
    /// <c>TransformSyncSystem</c> to skip dead-reckoning for all replicated entities.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_OwnerNodeIdIsZero_EnforcingGhostOwnership()
    {
        var (repo, _, eventBus, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        ISimulationView view = repo;
        IEntityCommandBuffer cmd = view.GetCommandBuffer();
        translator.ProcessSample(in master, cmd, view);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<SpawnEntityCommand>();

        Assert.Single(commands);
        // OwnerNodeId = 0 \u2192 remote ownership \u2192 HasAuthority = false in the spawning system.
        Assert.Equal(0, commands[0].OwnerNodeId);
        // Specifically must NOT use the local node ID, which would steal authority.
        Assert.NotEqual(IgNetworkConstants.LocalNodeId, commands[0].OwnerNodeId);
    }

    /// <summary>
    /// When the entity is already known, ProcessSample must NOT emit a new
    /// <see cref="SpawnEntityCommand"/> or apply any ECS component updates.</summary>
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
            TkbType  = TestTkbType,   // changed
            DisType  = TestDisType,   // changed
        };

        ISimulationView view = repo;
        var cmd = new RecordingCommandBuffer();
        translator.ProcessSample(in updatedMaster, cmd, view);

        eventBus.SwapBuffers();
        Assert.Empty(eventBus.ConsumeManaged<SpawnEntityCommand>());
        Assert.Empty(eventBus.ConsumeManaged<UpdateEntityCommand>());
        Assert.False(cmd.SetComponentCalled);
    }

    [Fact]
    public void ApplyToEntity_IsNoOp()
    {
        var (repo, _, _, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        var exception = Record.Exception(() =>
            translator.ApplyToEntity(new Entity(), master, repo));

        Assert.Null(exception);
        Assert.Throws<InvalidOperationException>(() => repo.GetComponentTable<EntityMaster>());
    }

    private sealed class RecordingCommandBuffer : IEntityCommandBuffer
    {
        public bool SetComponentCalled { get; private set; }

        public Entity CreateEntity() => new Entity();
        public void DestroyEntity(Entity entity) { }
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged { }
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
    /// events. This verifies the test-mode guard added to support unit testing.
    /// </summary>
    [Fact]
    public void PollIngress_WithNullParticipant_IsNoOpAndEmitsNoEvents()
    {
        var (repo, _, eventBus, translator) = CreateFixture();

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();
        translator.PollIngress(cmd, view);

        eventBus.SwapBuffers();
        Assert.Empty(eventBus.ConsumeManaged<SpawnEntityCommand>());
        Assert.Empty(eventBus.ConsumeManaged<DestroyEntityCommand>());
    }
}
